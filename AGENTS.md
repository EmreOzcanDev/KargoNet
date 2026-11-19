<!-- GSD:project-start source:PROJECT.md -->

## Project

**KargoNet**

KargoNet is a shipment tracking microservices system built on .NET 10. It consists of a Shipment service and a Tracking service that exchange shipment events over RabbitMQ (with hand-built retry and dead-letter queues), backed by PostgreSQL and a Redis cache. A customer-facing ASP.NET Core MVC portal lets customers create shipments and track them by tracking number. An Angular operator screen provides live shipment status updates over SignalR. Everything runs with Docker Compose.

**Core Value:** Reliable shipment status propagation: a status change made in the operator UI must reach the customer portal and the operator's live view without loss, even when RabbitMQ or individual consumers are temporarily down.

### Constraints

- **Tech stack**: .NET 10, EF Core + Npgsql, RabbitMQ.Client 7.x (async), PostgreSQL 17, Redis 7, Angular (latest via npx), SignalR — fixed by plan
- **Messaging**: Hand-built retry/DLQ (3 retries, 5s TTL) — no middleware abstraction
- **Auth**: None — no login, no API keys
- **Deployment**: Single Docker Compose stack — no Kubernetes, no cloud provider
- **Toolchain**: .NET 10 SDK and Docker must be installed as part of the first phase

<!-- GSD:project-end -->

<!-- GSD:stack-start source:STACK.md -->

## Technology Stack

Technology stack not yet documented. Will populate after codebase mapping or first phase.
<!-- GSD:stack-end -->

<!-- GSD:conventions-start source:CONVENTIONS.md -->

## Conventions

Conventions not yet established. Will populate as patterns emerge during development.
<!-- GSD:conventions-end -->

<!-- GSD:architecture-start source:ARCHITECTURE.md -->

## Architecture

Architecture not yet mapped. Follow existing patterns found in the codebase.
<!-- GSD:architecture-end -->

<!-- GSD:skills-start source:skills/ -->

## Project Skills

No project skills found. Add skills to any of: `.claude/skills/`, `.agents/skills/`, `.cursor/skills/`, `.github/skills/`, or `.codex/skills/` with a `SKILL.md` index file.
<!-- GSD:skills-end -->

<!-- GSD:workflow-start source:GSD defaults -->

## GSD Workflow Enforcement

Before using Edit, Write, or other file-changing tools, start work through a GSD command so planning artifacts and execution context stay in sync.

Use these entry points:
- `/gsd-fast` for a trivial task inline, with no subagents and no PLAN.md
- `/gsd-quick` for small fixes, doc updates, and ad-hoc tasks
- `/gsd-debug` for investigation and bug fixing
- `/gsd-execute-phase` for planned phase work

Do not make direct repo edits outside a GSD workflow unless the user explicitly asks to bypass it.
<!-- GSD:workflow-end -->

<!-- GSD:profile-start -->

## Developer Profile

> Profile not yet configured. Run `/gsd-profile-user` to generate your developer profile.
> This section is managed by `generate-claude-profile` -- do not edit manually.
<!-- GSD:profile-end -->
