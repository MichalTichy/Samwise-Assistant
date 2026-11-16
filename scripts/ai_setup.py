#!/usr/bin/env python3
"""Small, dependency-free client for the guided setup in docs/ai-setup.md.

Secrets stay in an ignored env file. This client never invokes a shell and never
prints management responses containing newly issued credentials.
"""
import argparse
import json
import os
from pathlib import Path
import re
import secrets
import sys
import tempfile
import urllib.error
import urllib.parse
import urllib.request


class SetupError(Exception):
    pass


def read_env(path):
    values = {}
    if path.exists():
        for line in path.read_text(encoding="utf-8-sig").splitlines():
            match = re.match(r"^([A-Za-z_][A-Za-z0-9_]*)=(.*)$", line)
            if match:
                value = match[2]
                if len(value) >= 2 and value[0] == value[-1] and value[0] in "\"'":
                    value = value[1:-1]
                values[match[1]] = value
    return values


def update_env(path, changes):
    """Preserve unrelated settings/comments; replace atomically with private mode."""
    path.parent.mkdir(parents=True, exist_ok=True)
    lines = path.read_text(encoding="utf-8-sig").splitlines() if path.exists() else []
    remaining = dict(changes)
    output = []
    for line in lines:
        name = line.split("=", 1)[0]
        if name in changes:
            if name in remaining:
                output.append(f"{name}={remaining.pop(name)}")
        else:
            output.append(line)
    output.extend(f"{name}={value}" for name, value in remaining.items())
    for name, value in changes.items():
        if any(c in str(value) for c in "\r\n"):
            raise SetupError(f"Multiline environment value is unsupported: {name}")
    fd, temp = tempfile.mkstemp(dir=path.parent, prefix=".setup-")
    try:
        with os.fdopen(fd, "w", encoding="utf-8", newline="\n") as stream:
            stream.write("\n".join(output) + "\n")
        os.replace(temp, path)
        os.chmod(path, 0o600)
    finally:
        if os.path.exists(temp):
            os.unlink(temp)


def usable(value):
    return bool(value and not re.search(r"(?:^|[=;])replace-[^;]*", value, re.IGNORECASE))


def prepare(args):
    path = Path(args.env).resolve()
    existing = read_env(path)
    changes = {}
    if not path.exists():
        example = Path(__file__).resolve().parents[1] / ".env.example"
        update_env(path, read_env(example))
        existing = read_env(path)
    for name in ("SAMWISE_MCP_API_KEY",
                 "SAMWISE_MANUAL_INPUT_API_KEY", "SAMWISE_SETUP_API_KEY",
                 "CODEX_GATEWAY_SETUP_API_KEY", "CODEX_GATEWAY_ADMIN_PASSWORD"):
        if not usable(existing.get(name)):
            changes[name] = secrets.token_hex(32)
    if args.owner:
        # Explicit legacy provisioning remains available for unattended deployments.
        changes["SAMWISE_OWNER_EMAIL"] = args.owner
        if not usable(existing.get("SAMWISE_OWNER_INITIAL_PASSWORD")):
            changes["SAMWISE_OWNER_INITIAL_PASSWORD"] = secrets.token_hex(32)
    elif not usable(existing.get("SAMWISE_OWNER_EMAIL")) and not usable(existing.get("SAMWISE_OWNER_REGISTRATION_TOKEN")):
        changes["SAMWISE_OWNER_REGISTRATION_TOKEN"] = secrets.token_hex(32)
    if args.gateway_source:
        source = Path(args.gateway_source).resolve()
        if not (source / "CodexGateway.slnx").exists():
            raise SetupError("Gateway source must point to its separate repository checkout.")
        changes["CODEX_GATEWAY_SOURCE"] = source.as_posix()
        image_prefix = args.deployment_id or "samwise-local"
        for variable, component, default in (
                ("CODEX_GATEWAY_IMAGE", "gateway", "registry.tichymichal.net/codex-gateway:latest"),
                ("CODEX_GATEWAY_RUNNER_IMAGE", "runner", "registry.tichymichal.net/codex-gateway-runner:latest"),
                ("SAMWISE_IMAGE", "web", "registry.tichymichal.net/samwise-assistant:latest")):
            if not existing.get(variable) or existing[variable] == default:
                changes[variable] = f"{image_prefix}-{component}:setup"
    if args.local_database:
        for name in ("SAMWISE_CONNECTION_STRING", "CODEX_GATEWAY_CONNECTION_STRING"):
            if usable(existing.get(name)) and "Host=postgres;" not in existing[name]:
                raise SetupError("Existing external database settings detected; omit --local-database.")
        password = existing.get("SAMWISE_DB_PASSWORD") or secrets.token_hex(32)
        changes["SAMWISE_DB_PASSWORD"] = password
        for name, database in (("SAMWISE_CONNECTION_STRING", "samwise"),
                               ("CODEX_GATEWAY_CONNECTION_STRING", "codex_gateway")):
            changes[name] = f"Host=postgres;Port=5432;Database={database};Username=samwise;Password={password}"
    if args.deployment_id:
        if not re.fullmatch(r"[a-z][a-z0-9-]{0,39}", args.deployment_id):
            raise SetupError("Deployment ID must be a short lowercase name.")
        for suffix in ("DATA_VOLUME", "AUTH_VOLUME", "RUNNER_NETWORK", "BACKEND_NETWORK"):
            name = "CODEX_GATEWAY_" + suffix
            target = f"{args.deployment_id}-{suffix.lower().replace('_', '-')}"
            if usable(existing.get(name)) and existing[name].startswith(args.deployment_id + "-"):
                continue
            if name in existing and existing[name] not in ("codex-gateway-data", "codex-gateway-auth", "codex-gateway-runner", "codex-gateway-backend"):
                raise SetupError(f"Refusing to change existing {name}.")
            changes[name] = target
    update_env(path, changes)
    print(f"Prepared {path.name}; credentials preserved/generated without displaying them.")
    print("Keep this file private. On Windows restrict its ACL to the deployment owner.")


def request(base, route, key, method="GET", body=None, auth_header="X-Setup-Key", expected_status=None):
    parsed = urllib.parse.urlparse(base)
    if parsed.scheme not in ("http", "https") or not parsed.hostname or parsed.username or parsed.password:
        raise SetupError("Use an absolute HTTP(S) URL without embedded credentials.")
    if parsed.scheme == "http" and parsed.hostname not in ("localhost", "127.0.0.1", "::1"):
        raise SetupError("Use HTTPS for remote management; tunnel remote HTTP through localhost.")
    if not route.startswith("/") or route.startswith("//") or ".." in route:
        raise SetupError("API path must be relative to its management prefix.")
    headers = {auth_header: key, "Accept": "application/json"}
    data = None
    if body is not None:
        data = json.dumps(body).encode()
        headers["Content-Type"] = "application/json"
    req = urllib.request.Request(base.rstrip("/") + route, data=data, headers=headers, method=method)
    # A redirect could leak the setup credential to another origin.
    class NoRedirect(urllib.request.HTTPRedirectHandler):
        def redirect_request(self, *unused):
            return None
    try:
        with urllib.request.build_opener(NoRedirect).open(req, timeout=120) as response:
            if expected_status is not None and response.status != expected_status:
                raise SetupError(f"{method} {route} returned HTTP {response.status}, expected {expected_status}; inspect saved state before retrying.")
            content = response.read()
            return json.loads(content) if content else {}
    except urllib.error.HTTPError as error:
        # Do not echo response bodies: errors from integrations can contain secrets.
        raise SetupError(f"{method} {route} returned HTTP {error.code}; inspect server logs locally.") from None
    except urllib.error.URLError:
        raise SetupError(f"Cannot reach {parsed.hostname}; check URL, health, and deployment.") from None


def client(args, service):
    values = read_env(Path(args.env))
    name = "CODEX_GATEWAY_SETUP_API_KEY" if service == "gateway" else "SAMWISE_SETUP_API_KEY"
    if not usable(values.get(name)):
        raise SetupError(f"{name} is missing. Enable temporary setup access first.")
    base = args.gateway.rstrip("/") + "/admin/setup" if service == "gateway" else args.samwise.rstrip("/") + "/api/setup"
    def invoke(route, method="GET", body=None):
        result = request(base, route, values[name], method, body)
        if service == "gateway" and route == "/codex" and method == "GET":
            login = result.get("login")
            if login and login.get("status") != "Pending":
                # Completed/expired challenges are not instructions to sign in again.
                result = {**result, "login": {k: v for k, v in login.items() if k != "userCode"}}
        return result
    return invoke


def provision(args):
    path = Path(args.env)
    values = read_env(path)
    api = client(args, "gateway")
    profiles = [("INTAKE", None), ("PLANNING", "samwise-planning"), ("EXECUTION", "samwise-execution")]
    if args.automatic_approval:
        profiles += [("AUTO_APPROVAL", "samwise-auto-approval"), ("AUTO_EXECUTION", "samwise-auto-execution")]
    selected = getattr(args, "profiles", None)
    if selected:
        profiles = [(p, project) for p, project in profiles if p in selected]
        if len(profiles) != len(set(selected)):
            raise SetupError("Automatic profiles require --automatic-approval; no profiles were changed.")
    for profile, project in profiles:
        key_id = "samwise-" + profile.lower().replace("_", "-")
        variable = f"SAMWISE_{profile}_API_KEY"
        result = api("/api-keys", "POST", {"id": key_id, "name": key_id})
        if result.get("created"):
            secret = result.get("key")
            if not secret:
                raise SetupError("Gateway did not return a new key; incompatible setup API.")
            update_env(path, {variable: secret})  # Persist each issued key immediately.
            values[variable] = secret
        elif not usable(values.get(variable)):
            raise SetupError(f"{key_id} already exists but its secret is unavailable. Recover the existing secret or explicitly create a new key; no key was rotated.")
        if project:
            api("/projects/" + project, "PUT", {"name": project})
            # Recover a partial run without replacing configured tool grants.
            state = api("/state")
            current = next((p for p in state["projects"] if p["id"] == project), None)
            if not current:
                raise SetupError(f"Gateway did not persist project {project}.")
            if not any(a["apiKeyId"] == key_id for a in current.get("apiKeyAccess", [])):
                api(f"/projects/{project}/access/{key_id}", "PUT", {"apiKeyId": key_id, "mcpServers": [], "webSearchMode": 0})
        print(f"Ready: {key_id}; credentials saved privately.")
    print("Recreate Samwise to load its Gateway keys, then configure integrations and models.")


def verify(args):
    """Exercise the deployed consumer's actual credentials, not just local files."""
    api = client(args, "samwise")
    status = api("/status")
    configured = {s["stage"]: s for s in status["stages"]}
    values = read_env(Path(args.env))
    for stage, prefix, project in (("Intake", "INTAKE", None),
                                    ("Planning", "PLANNING", "samwise-planning"),
                                    ("Execution", "EXECUTION", "samwise-execution")):
        settings = configured.get(stage, {})
        if not settings.get("endpointConfigured") or not settings.get("model"):
            raise SetupError(f"{stage} is not fully configured.")
        models = api(f"/agents/{stage}/models")  # Calls from inside deployed Samwise.
        if not any(m["id"] == settings["model"] and settings["reasoningEffort"] in m["supportedReasoningEfforts"] for m in models):
            raise SetupError(f"{stage} model/effort is unavailable through its deployed credentials.")
        secret = values.get(f"SAMWISE_{prefix}_API_KEY")
        if not usable(secret):
            raise SetupError(f"The saved {stage} credential is missing.")
        base_url = values.get(f"SAMWISE_{prefix}_BASE_URL", "/v1/" if not project else f"/p/{project}/v1/")
        path = urllib.parse.urlparse(base_url).path.rstrip("/") + "/tools"
        catalog = request(args.gateway, path, "Bearer " + secret, auth_header="Authorization")
        tools = [f"{s['id']}/{t['name']}" for s in catalog.get("data", []) for t in s.get("tools", [])]
        if stage == "Intake" and any(tool != "codex-built-in/shell" for tool in tools):
            raise SetupError("Intake exposes external or unexpected native capabilities; review its grants.")
        print(f"PASS {stage}: actual Samwise model access; {len(tools)} granted tools.")
        if tools:
            print("  " + ", ".join(tools))
    print("Connection checks passed. Still run actionable/informational examples and an approved reversible execution.")


def submit(args):
    values = read_env(Path(args.env))
    key = values.get("SAMWISE_MANUAL_INPUT_API_KEY")
    if not usable(key):
        raise SetupError("Manual input key is missing.")
    content = Path(args.text_file).read_text(encoding="utf-8-sig")
    result = request(args.samwise, "/api/manual-input", key, "POST",
                     {"text": content, "title": "Setup verification", "note": "Owner-selected onboarding test"},
                     auth_header="X-Samwise-Input-Key", expected_status=202)
    print("HTTP 202 Accepted.")
    print(json.dumps(result, indent=2))
    print("Accepted is not complete: inspect input-events/work-items until classification and planning finish.")


def redact(value):
    """Hide credential values while retaining setup IDs, grants and readiness."""
    if isinstance(value, dict):
        result = {}
        for key, item in value.items():
            name = key.lower()
            metadata = name in ("apikeyid", "apikeyids") or (
                name in ("apikeys", "apikeyaccess") and isinstance(item, (dict, list)))
            secret = name == "key" or any(part in name for part in ("password", "secret", "token", "apikey"))
            result[key] = "[redacted]" if secret and not metadata and not isinstance(item, bool) else redact(item)
        return result
    if isinstance(value, list):
        return [redact(item) for item in value]
    return value


def call(args):
    if args.service == "gateway" and args.path.startswith("/api-keys") and args.method != "GET":
        raise SetupError("Use provision for key creation so secrets are saved without display.")
    body = json.loads(Path(args.body).read_text(encoding="utf-8-sig")) if args.body else None
    # Optional env references let private provider tokens stay out of JSON plans.
    values = read_env(Path(args.env)) | dict(os.environ)
    def resolve(value):
        if isinstance(value, dict):
            if set(value) == {"$env"}:
                name = value["$env"]
                if not values.get(name):
                    raise SetupError(f"Missing credential reference: {name}")
                return values[name]
            return {k: resolve(v) for k, v in value.items()}
        if isinstance(value, list):
            return [resolve(v) for v in value]
        return value
    result = client(args, args.service)(args.path, args.method, resolve(body))
    print(json.dumps(redact(result), indent=2, ensure_ascii=False))


def registration_link(args):
    token = read_env(Path(args.env)).get("SAMWISE_OWNER_REGISTRATION_TOKEN", "")
    if not usable(token) or len(token) < 32:
        raise SetupError("No web registration token is configured. Existing owners use /login; new installations use prepare without --owner.")
    base = urllib.parse.urlparse(args.samwise)
    if (base.scheme not in ("http", "https") or not base.hostname or base.username or base.password
            or base.query or base.fragment or base.path not in ("", "/")):
        raise SetupError("Use the Samwise origin URL without credentials, path, query or fragment.")
    if base.scheme == "http" and base.hostname not in ("localhost", "127.0.0.1", "::1"):
        raise SetupError("Open registration through HTTPS or a localhost tunnel.")
    target = Path(args.output).resolve()
    if target == Path(args.env).resolve():
        raise SetupError("Registration link output must not replace the deployment environment file.")
    target.parent.mkdir(parents=True, exist_ok=True)
    fd, temp = tempfile.mkstemp(dir=target.parent, prefix=".registration-")
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as stream:
            stream.write(args.samwise.rstrip("/") + "/register?" + urllib.parse.urlencode({"token": token}) + "\n")
        os.replace(temp, target)
        os.chmod(target, 0o600)
    finally:
        if os.path.exists(temp):
            os.unlink(temp)
    print(f"Private installation link saved to {target}. Open it for the owner; do not request their email or password in chat.")
    print("The link stops working after the first owner account is created. Confirm completion through owner sign-in.")


def main():
    # Windows redirected stdout may otherwise fail *after* a successful mutation
    # when printing a Czech playbook, misleading the assistant into retrying it.
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--env", default=".env", help="Private deployment environment file")
    parser.add_argument("--gateway", default="http://localhost:5050")
    parser.add_argument("--samwise", default="http://localhost:8080")
    commands = parser.add_subparsers(dest="command", required=True)
    prep = commands.add_parser("prepare", help="Generate missing secrets without overwriting working credentials")
    prep.add_argument("--owner", help="Legacy provisioning only; omit for owner-created web registration")
    prep.add_argument("--local-database", action="store_true")
    prep.add_argument("--deployment-id")
    prep.add_argument("--gateway-source", help="Build local images from this separate Gateway checkout")
    link = commands.add_parser("registration-link", help="Write the private one-time owner registration URL to a protected file")
    link.add_argument("--output", default=".data/setup/owner-registration-link.txt")
    prov = commands.add_parser("provision", help="Create Gateway profiles and privately wire their keys")
    prov.add_argument("--automatic-approval", action="store_true")
    prov.add_argument("--profiles", nargs="+", choices=("INTAKE", "PLANNING", "EXECUTION", "AUTO_APPROVAL", "AUTO_EXECUTION"),
                      help="Provision only missing standard profiles; preserve custom existing profiles")
    commands.add_parser("verify", help="Check the deployed Samwise stage credentials and model/tool catalogs")
    sample = commands.add_parser("submit", help="Submit one owner-selected verification input")
    sample.add_argument("--text-file", required=True)
    req = commands.add_parser("call", help="Call a management operation; JSON bodies may use {$env: NAME}")
    req.add_argument("service", choices=("gateway", "samwise"))
    req.add_argument("method", choices=("GET", "POST", "PUT", "DELETE"))
    req.add_argument("path")
    req.add_argument("--body", help="JSON file, not inline credentials")
    args = parser.parse_args()
    try:
        {"prepare": prepare, "provision": provision, "call": call, "verify": verify, "submit": submit, "registration-link": registration_link}[args.command](args)
    except (SetupError, ValueError, OSError) as error:
        # JSON parse and filesystem errors are user-correctable. No tracebacks with local data.
        print(f"Setup stopped: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
