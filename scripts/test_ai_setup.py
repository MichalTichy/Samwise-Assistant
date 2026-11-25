import argparse
import contextlib
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import MagicMock, patch

import ai_setup


class SetupClientTests(unittest.TestCase):
    def test_clean_install_and_rerun_preserve_credentials(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / ".env"
            args = argparse.Namespace(env=str(path), owner="owner@example.com", local_database=True,
                                      deployment_id="isolated", gateway_source=None)
            with contextlib.redirect_stdout(io.StringIO()) as output:
                ai_setup.prepare(args)
                initial = path.read_bytes()
                ai_setup.prepare(args)
            self.assertEqual(initial, path.read_bytes())
            values = ai_setup.read_env(path)
            self.assertIn("Host=postgres;", values["SAMWISE_CONNECTION_STRING"])
            self.assertNotEqual(values["SAMWISE_SETUP_API_KEY"], values["CODEX_GATEWAY_SETUP_API_KEY"])
            self.assertNotIn(values["SAMWISE_SETUP_API_KEY"], output.getvalue())

    def test_web_registration_keeps_owner_credentials_in_browser_and_preserves_token(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / ".env"
            args = argparse.Namespace(env=str(path), owner=None, local_database=True,
                                      deployment_id="registration", gateway_source=None)
            with contextlib.redirect_stdout(io.StringIO()) as output:
                ai_setup.prepare(args)
                before = path.read_bytes()
                ai_setup.prepare(args)
            values = ai_setup.read_env(path)
            self.assertEqual(before, path.read_bytes())
            self.assertFalse(values["SAMWISE_OWNER_EMAIL"])
            self.assertFalse(values["SAMWISE_OWNER_INITIAL_PASSWORD"])
            token = values["SAMWISE_OWNER_REGISTRATION_TOKEN"]
            self.assertGreaterEqual(len(token), 32)
            self.assertNotIn(token, output.getvalue())
            link = Path(directory) / "installation.txt"
            with contextlib.redirect_stdout(io.StringIO()) as output:
                ai_setup.registration_link(argparse.Namespace(env=str(path), samwise="http://localhost:18080", output=str(link)))
            self.assertEqual(f"http://localhost:18080/register?token={token}\n", link.read_text())
            self.assertNotIn(token, output.getvalue())

    def test_registration_link_rejects_remote_http_and_env_overwrite(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / ".env"
            ai_setup.update_env(path, {"SAMWISE_OWNER_REGISTRATION_TOKEN": "a" * 64})
            before = path.read_bytes()
            for url, target in [("http://remote.example", str(Path(directory) / "link")), ("https://samwise.example", str(path))]:
                with self.assertRaises(ai_setup.SetupError):
                    ai_setup.registration_link(argparse.Namespace(env=str(path), samwise=url, output=target))
            self.assertEqual(before, path.read_bytes())

    def test_existing_external_database_is_not_replaced(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / ".env"
            path.write_text("# Keep this\nSAMWISE_CONNECTION_STRING=Host=existing;Password=valid\n", encoding="utf-8")
            before = path.read_bytes()
            args = argparse.Namespace(env=str(path), owner=None, local_database=True, deployment_id=None, gateway_source=None)
            with self.assertRaises(ai_setup.SetupError):
                ai_setup.prepare(args)
            self.assertEqual(before, path.read_bytes())

    def test_source_build_uses_isolated_tags_and_preserves_custom_tags_on_resume(self):
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "gateway"
            source.mkdir()
            (source / "CodexGateway.slnx").touch()
            path = Path(directory) / ".env"
            args = argparse.Namespace(env=str(path), owner=None, local_database=True,
                                      deployment_id="isolated", gateway_source=str(source))
            with contextlib.redirect_stdout(io.StringIO()):
                ai_setup.prepare(args)
                self.assertEqual("isolated-web:setup", ai_setup.read_env(path)["SAMWISE_IMAGE"])
                ai_setup.update_env(path, {"SAMWISE_IMAGE": "custom-web:trial"})
                ai_setup.prepare(args)
            self.assertEqual("custom-web:trial", ai_setup.read_env(path)["SAMWISE_IMAGE"])

    def test_private_env_updates_preserve_comments_and_unrelated_values(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / ".env"
            path.write_text("# Private\nOTHER='not altered'\nSECRET=old\n", encoding="utf-8")
            ai_setup.update_env(path, {"SECRET": "new"})
            self.assertEqual("# Private\nOTHER='not altered'\nSECRET=new\n", path.read_text())
            with self.assertRaises(ai_setup.SetupError):
                ai_setup.update_env(path, {"SECRET": "bad\ninjection"})
            self.assertEqual("new", ai_setup.read_env(path)["SECRET"])

    def test_management_refuses_remote_plain_http_and_absolute_routes(self):
        for base, route in (("http://example.com", "/state"), ("https://example.com", "//elsewhere/state"),
                            ("https://user:password@example.com", "/state")):
            with self.assertRaises(ai_setup.SetupError):
                ai_setup.request(base, route, "private")

    def test_provision_resumes_missing_access_without_overwriting_grants(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / ".env"
            ai_setup.update_env(path, {f"SAMWISE_{p}_API_KEY": f"saved-{p}" for p in ("INTAKE", "PLANNING", "EXECUTION")})
            calls = []
            def api(route, method="GET", body=None):
                calls.append((route, method, body))
                if route == "/api-keys":
                    return {"created": False, "key": None}
                if route == "/state":
                    return {"projects": [
                        {"id": "samwise-planning", "apiKeyAccess": []},
                        {"id": "samwise-execution", "apiKeyAccess": [{"apiKeyId": "samwise-execution", "mcpServers": [{"serverId": "existing"}]}]},
                    ]}
                return {}
            args = argparse.Namespace(env=str(path), automatic_approval=False)
            with patch.object(ai_setup, "client", return_value=api), contextlib.redirect_stdout(io.StringIO()):
                ai_setup.provision(args)
            writes = [c for c in calls if "/access/" in c[0]]
            self.assertEqual(1, len(writes))
            self.assertIn("samwise-planning/access/samwise-planning", writes[0][0])
            self.assertEqual("samwise-planning", writes[0][2]["apiKeyId"])

    def test_login_status_preserves_current_challenge_but_omits_stale_codes(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / ".env"
            ai_setup.update_env(path, {"CODEX_GATEWAY_SETUP_API_KEY": "saved-setup-key"})
            args = argparse.Namespace(env=str(path), gateway="http://localhost:5050")
            api = ai_setup.client(args, "gateway")
            for status in ("Pending", "Completed", "Failed"):
                original = {"account": {"authenticated": status == "Completed"},
                            "login": {"status": status, "userCode": "demo-code"}}
                with patch.object(ai_setup, "request", return_value=original):
                    result = api("/codex")
                self.assertEqual(status == "Pending", "userCode" in result["login"])
                self.assertEqual(original["account"], result["account"])
                self.assertIn("userCode", original["login"])

    def test_submission_does_not_claim_acceptance_for_an_unexpected_success_status(self):
        response = MagicMock()
        response.status = 200
        response.read.return_value = b'{"inputEventId":"demo-input"}'
        response.__enter__.return_value = response
        opener = MagicMock()
        opener.open.return_value = response
        with patch.object(ai_setup.urllib.request, "build_opener", return_value=opener):
            with self.assertRaisesRegex(ai_setup.SetupError, "inspect saved state before retrying"):
                ai_setup.request("http://localhost:8080", "/api/manual-input", "private", "POST", {}, expected_status=202)
            response.status = 202
            result = ai_setup.request("http://localhost:8080", "/api/manual-input", "private", "POST", {}, expected_status=202)
            self.assertEqual("demo-input", result["inputEventId"])

    def test_redaction_keeps_grants_inspectable_without_exposing_nested_credentials(self):
        state = {"apiKeys": [{"id": "planning", "name": "Planning", "key": "private-value"}],
                 "projects": [{"apiKeyAccess": [{"apiKeyId": "planning",
                     "mcpServers": [{"serverId": "calendar", "enabledTools": ["list_events"]}],
                     "apiToken": "private-value"}]}],
                 "apiKey": "private-value", "password": "private-value", "clientSecret": "private-value",
                 "apiKeyConfigured": True, "clientSecretConfigured": False}
        result = ai_setup.redact(state)
        self.assertNotIn("private-value", json.dumps(result))
        self.assertEqual("planning", result["apiKeys"][0]["id"])
        access = result["projects"][0]["apiKeyAccess"][0]
        self.assertEqual("planning", access["apiKeyId"])
        self.assertEqual(["list_events"], access["mcpServers"][0]["enabledTools"])
        self.assertTrue(result["apiKeyConfigured"])
        self.assertFalse(result["clientSecretConfigured"])
        self.assertEqual("[redacted]", ai_setup.redact({"apiKeys": "private-value"})["apiKeys"])

    def test_missing_existing_secret_stops_without_rotating_any_key(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / ".env"
            args = argparse.Namespace(env=str(path), automatic_approval=False)
            calls = []
            def api(route, method="GET", body=None):
                calls.append(route)
                return {"created": False, "key": None}
            with patch.object(ai_setup, "client", return_value=api), self.assertRaises(ai_setup.SetupError):
                ai_setup.provision(args)
            self.assertEqual(["/api-keys"], calls)

    def test_verification_accepts_isolated_native_shell_but_rejects_external_intake_tools(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / ".env"
            ai_setup.update_env(path, {f"SAMWISE_{p}_API_KEY": f"saved-{p}" for p in ("INTAKE", "PLANNING", "EXECUTION")})
            args = argparse.Namespace(env=str(path), gateway="http://localhost:5050")
            def api(route, method="GET", body=None):
                if route == "/status":
                    return {"stages": [{"stage": stage, "model": "supported", "reasoningEffort": "medium", "endpointConfigured": True}
                                       for stage in ("Intake", "Planning", "Execution")]}
                return [{"id": "supported", "supportedReasoningEfforts": ["medium"]}]
            shell = {"data": [{"id": "codex-built-in", "tools": [{"name": "shell"}]}]}
            with patch.object(ai_setup, "client", return_value=api), patch.object(ai_setup, "request", return_value=shell), contextlib.redirect_stdout(io.StringIO()):
                ai_setup.verify(args)
            external = {"data": [{"id": "calendar", "tools": [{"name": "create_event"}]}]}
            with patch.object(ai_setup, "client", return_value=api), patch.object(ai_setup, "request", return_value=external), self.assertRaises(ai_setup.SetupError):
                ai_setup.verify(args)


if __name__ == "__main__":
    unittest.main()
