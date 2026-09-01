# Container deployment

`compose.yaml` runs Samwise together with Codex CLI API Gateway and the Gateway's isolated Codex runner. It connects both applications to an existing PostgreSQL instance; it does not start or manage PostgreSQL.

## First deployment

1. Copy `.env.example` to `.env` beside `compose.yaml`.
2. Create separate `samwise` and `codex_gateway` databases on the existing PostgreSQL instance, then configure both connection strings. The applications create their own schemas and tables. When PostgreSQL runs on the Docker host, use `host.docker.internal` rather than `localhost`.
3. Replace the owner credentials and Gateway admin password. Placeholder Samwise project keys are sufficient for the first Gateway startup. Each Gateway API URL must be absolute and end in `/v1/`.
4. Pull and start the stack:

   ```bash
   docker compose pull
   docker compose up -d
   ```

5. Open `http://<host>:5050/admin`, sign in to the Gateway, complete Codex device authentication, and create the `samwise-planning` and `samwise-execution` projects and their keys.
6. Put the real intake, planning, and execution keys in `.env`, then recreate Samwise with `docker compose up -d web`.
7. Open Samwise on its configured host and port, sign in, and change the initial password.
8. Remove `SAMWISE_OWNER_INITIAL_PASSWORD` from `.env` after the owner has a password. An empty value is valid on subsequent starts.

Samwise listens on plain HTTP port `8080` inside its container and the Gateway listens on `8080` inside its container. Their default published host ports are `8080` and `5050`. Terminate HTTPS in the host reverse proxy. `ASPNETCORE_FORWARDEDHEADERS_ENABLED` lets Samwise honor the proxy's forwarded scheme, while production authentication cookies remain HTTPS-only.

## Persistent state

The Compose stack creates three named volumes:

- `data-protection-keys` keeps login cookies valid across container replacement.
- `codex-gateway-data` is the workspace volume mounted into isolated Gateway runner containers.
- `codex-gateway-auth` stores the Codex authentication used by the Gateway.

Back up these volumes and the two PostgreSQL databases. Losing only the data-protection volume signs the owner out but does not remove work items or credentials.

## Updating

The default images are `registry.tichymichal.net/samwise-assistant:latest`, `registry.tichymichal.net/codex-gateway:latest`, and `registry.tichymichal.net/codex-gateway-runner:latest`. For reproducible deployment, set all three image variables in `.env` to their published immutable commit tags, then run:

```bash
docker compose pull
docker compose up -d
```

## Building and publishing

Build from the repository root. Replace `<commit>` with the Git commit being published:

```bash
docker build --tag registry.tichymichal.net/samwise-assistant:<commit> --tag registry.tichymichal.net/samwise-assistant:latest .
docker push registry.tichymichal.net/samwise-assistant:<commit>
docker push registry.tichymichal.net/samwise-assistant:latest
```

The Gateway repository builds two targets. Publish its `gateway` target as `registry.tichymichal.net/codex-gateway:<commit>` and its `runner` target as `registry.tichymichal.net/codex-gateway-runner:<commit>`, plus the corresponding `latest` tags. The one-shot `gateway-runner-image` Compose service ensures Docker pulls the exact runner image before the Gateway starts.

```bash
docker build --target gateway --tag registry.tichymichal.net/codex-gateway:<commit> --tag registry.tichymichal.net/codex-gateway:latest .
docker build --target runner --tag registry.tichymichal.net/codex-gateway-runner:<commit> --tag registry.tichymichal.net/codex-gateway-runner:latest .
docker push registry.tichymichal.net/codex-gateway:<commit>
docker push registry.tichymichal.net/codex-gateway:latest
docker push registry.tichymichal.net/codex-gateway-runner:<commit>
docker push registry.tichymichal.net/codex-gateway-runner:latest
```
