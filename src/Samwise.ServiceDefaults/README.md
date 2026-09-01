# Samwise.ServiceDefaults

This Aspire shared project contains cross-cutting host defaults for health checks, service discovery, resilience dependencies, and OpenTelemetry.

## Main components

- `AddServiceDefaults` enables telemetry, the liveness check, and service discovery.
- `ConfigureOpenTelemetry` instruments ASP.NET Core, outgoing HTTP, and the .NET runtime, and enables OTLP export when an endpoint is configured.
- `MapDefaultEndpoints` is a reusable health-endpoint helper; `Samwise.Web` currently maps equivalent authenticated-policy-aware endpoints explicitly.

## Host flow

1. `Samwise.Web` calls `builder.AddServiceDefaults()` before application installers run.
2. Logging, traces, metrics, discovery, and the `self` liveness check are registered.
3. If `OTEL_EXPORTER_OTLP_ENDPOINT` is configured, telemetry is exported through OTLP.
4. Health endpoints exclude their own traffic from tracing noise.

## Boundary

Only operational host defaults belong here. Samwise authentication, workflow, database mapping, and UI behavior remain in their owning projects.
