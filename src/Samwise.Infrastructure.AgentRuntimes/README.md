# Samwise.Infrastructure.AgentRuntimes

This project implements the application-layer `IAgentRuntime` port through the standard OpenAI Chat Completions connector and Microsoft Agent Framework. The configured endpoint may be the Codex CLI API Gateway, but this project does not use Gateway-specific contracts.

## Main components

- `OpenAiAgentRuntime` sends structured Intake, Planning, AutomaticApproval, Execution, and AutoApprovedExecution requests through the official OpenAI client adapted to `IChatClient`, then maps strict wire results back to logic models.
- `AgentEndpointOptions` contains provider-neutral per-stage base URLs, API keys, and shared request timeout. The two automatic profiles are optional until enabled through playbooks.
- `AgentRuntimesInstaller` binds `AgentEndpoints` and registers the OpenAI runtime in every environment.

## Runtime flow

1. A logic service calls one of the structured `IAgentRuntime` operations.
2. `OpenAiAgentRuntime` selects the matching OpenAI-compatible endpoint and sends the already rendered prompt through a stateless `ChatClientAgent`.
3. Strict snake-case wire models are deserialized with unknown members rejected.
4. The adapter converts the Intake decision and its zero or more Work Item changes, plan steps with reviewable tool-call parameters, and execution outputs into application-layer records. Planning and execution both accept `awaiting_external_event` for unfinished outcomes gated by an external approval, response, or owner action. Execution's strict disposition schema includes it; it is distinct from success, failure, and uncertain side effects.
5. It creates an `AgentReceipt` containing the OpenAI Chat Completions protocol identity, model, completion ID, and raw response for invocation auditing.

## Test doubles

The production project does not contain a demo provider. Tests that need controlled agent behavior define scenario-specific doubles inside `Samwise.Tests`, so test support cannot accidentally be selected by the web application.

## Boundary

This project translates between application and standard OpenAI transport models. It does not own prompts, tool/model catalog discovery, workflow ordering, approval rules, domain transitions, or persistence. Gateway-only discovery belongs to `Samwise.Infrastructure.CodexGateway`.
