# /// script
# requires-python = ">=3.11"
# dependencies = ["mcp==1.28.0"]
# ///
"""Temporary stdio MCP administration bridge. Run with uv run scripts/setup_mcp.py.

Connect this to the assistant helping the owner, never to Samwise's agent projects.
Uses the official MCP SDK; management credentials are read from an ignored file.
"""
import argparse
import contextlib
import io
import json
from pathlib import Path
from typing import Literal

from mcp.server.fastmcp import FastMCP

import ai_setup


def create_server(args):
    server = FastMCP("Samwise setup")

    @server.tool()
    def setup_status() -> dict:
        """Read Samwise readiness and Gateway configuration, never credential values."""
        return {
            "samwise": ai_setup.client(args, "samwise")("/status"),
            "gateway": ai_setup.client(args, "gateway")("/state"),
        }

    @server.tool()
    def provision_gateway_profiles(automatic_approval: bool = False,
                                   profiles: list[Literal["INTAKE", "PLANNING", "EXECUTION", "AUTO_APPROVAL", "AUTO_EXECUTION"]] | None = None) -> str:
        """Create missing Samwise Gateway keys/projects and save secrets privately.

        Does not rotate existing keys or grant MCP tools. Recreate Samwise after
        this operation so its deployment loads the issued credentials.
        """
        operation = argparse.Namespace(**vars(args), automatic_approval=automatic_approval, profiles=profiles)
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            ai_setup.provision(operation)
        return output.getvalue()

    @server.tool()
    def manage_setup(service: Literal["samwise", "gateway"], method: Literal["GET", "POST", "PUT"],
                     path: str, body: dict | None = None) -> dict | list:
        """Read or configure an onboarding resource using its documented management path.

        Examples: samwise GET /agents; gateway GET /codex; samwise POST /playbooks;
        gateway PUT /projects/samwise-planning/access/samwise-planning.
        For provider credentials use {"$env":"VARIABLE_NAME"} references.
        Read the current configuration before replacing it. Review exact tool
        grants with the owner; never grant unknown newly discovered tools.
        """
        if service == "gateway" and path.startswith("/api-keys") and method != "GET":
            raise ai_setup.SetupError("Use provision_gateway_profiles to save issued secrets privately.")
        values = ai_setup.read_env(Path(args.env))
        def resolve(value):
            if isinstance(value, dict):
                if set(value) == {"$env"}:
                    name = value["$env"]
                    if not values.get(name):
                        raise ai_setup.SetupError(f"Missing private credential: {name}")
                    return values[name]
                return {k: resolve(v) for k, v in value.items()}
            if isinstance(value, list):
                return [resolve(v) for v in value]
            return value
        return ai_setup.client(args, service)(path, method, resolve(body))

    @server.resource("samwise-setup://guide")
    def setup_guide() -> str:
        """The complete guided onboarding workflow."""
        return (Path(__file__).resolve().parents[1] / "docs/ai-setup.md").read_text(encoding="utf-8")

    return server


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--env", default=".env")
    parser.add_argument("--gateway", default="http://localhost:5050")
    parser.add_argument("--samwise", default="http://localhost:8080")
    create_server(parser.parse_args()).run(transport="stdio")
