<!--
Sync Impact Report:
- Version change: 0.0.0 → 1.0.0
- Modified principles: none (initial filled constitution)
- Added sections: Test-First Behavior; Code Quality; Security; Observability;
  Simplicity; Offline-First Architecture; Technical Constraints;
  Development Workflow; Governance
- Removed sections: none
- Follow-up TODOs: none
-->

# ContosoDashboard Constitution

## Core Principles

### I. Test-First Behavior (NON-NEGOTIABLE)

Define the expected behavior in executable tests before implementation for all new behavior
and bug fixes. Add focused unit tests for services and models, integration tests for authentication,
authorization, database access, and protected routes, and UI tests only where component behavior
cannot be validated by simpler coverage. Run the relevant test suite and review failures before
implementation; TDD red-to-green-to-refactor is the preferred cycle.

Rationale: Test-first work makes authorization, isolation, and dashboard behavior explicit and
prevents regressions in a training application that deliberately demonstrates both working and
known security patterns.

### II. Code Quality and Maintainability

Keep .NET code idiomatic, nullable-aware, asynchronous for I/O, and consistent with the existing
Models, Data, Services, Pages, and Shared separation. Use dependency injection for infrastructure,
keep page and component logic focused, and prevent unauthorized or cross-user data access in
service operations as well as UI routes. Public behavior and non-obvious invariants MUST be clear
from names and concise comments rather than duplicated or speculative abstractions.

Rationale: The repository is intended for learning; readable code is a feature of the training
experience, while consistent boundaries keep examples understandable.

### III. Security and Privacy

Protected pages MUST require authentication and enforce the least privilege needed for the
requested operation. Role checks, resource membership checks, and service-level authorization
MUST prevent IDOR and cross-user access. Apply secure defaults and existing security headers
without introducing secrets or production credentials into source code. Because authentication
is mock-only, user selection MUST remain clearly identified as unsuitable for production.

Rationale: Defense in depth is a central teaching goal of this repository and must not be weakened
for implementation convenience.

### IV. Observability and Diagnostics

Use structured logging for failures, authorization denials, database initialization errors, and
other state-changing operations. Log enough context to diagnose a request or operation without
logging passwords, cookies, tokens, or personal data. Development and review checks MUST provide
clear success or failure signals and actionable error output.

Rationale: Observable behavior helps learners distinguish authentication, authorization, data
access, and UI failures while protecting sensitive information.

### V. Simplicity and YAGNI

Prefer the smallest implementation that satisfies the approved behavior. Do not add frameworks,
services, configuration surfaces, or abstractions without a concrete repository need. Any
material complexity MUST be justified by testability, security, maintainability, or a documented
offline-to-cloud migration requirement.

Rationale: A simple, local, self-contained implementation is essential to the training and offline
goals of ContosoDashboard.

### VI. Offline-First and Infrastructure Adaptability

The application MUST remain usable locally without external services. Infrastructure dependencies
MUST use interfaces or equivalent replacement boundaries where swapping implementations is a stated
design goal, such as storage and cloud-backed services. Core task, project, notification, dashboard,
and profile behavior MUST remain independent of a specific infrastructure provider.

Rationale: The repository is designed to teach migration paths while remaining executable in an
offline training environment.

## Technical Constraints

The implementation SHALL use ASP.NET Core 8 or later, Blazor Server, Razor Pages, Entity Framework
Core, and SQL Server LocalDB unless a feature specification deliberately changes the target stack.
The checked project targets .NET 10 and SHALL remain buildable with the repository's declared SDK.
Bootstrap and Bootstrap Icons SHALL be retained unless a UI specification explicitly replaces them.
Mock authentication SHALL be used only for training; production identity, password, MFA, TLS,
audit, and compliance requirements SHALL be documented rather than silently implied.

## Development Workflow

Implementation work SHALL begin from an approved specification and implementation plan. Changes
MUST preserve existing behavior unless the specification requires otherwise. Pull requests and
feature reviews SHALL verify test coverage, authorization behavior, service-level isolation,
database compatibility, logging, and documentation affected by the change. A change MUST compile
and pass all relevant tests before it is accepted.

## Governance

This constitution supersedes informal project practices for repository-wide engineering decisions.
Amendments require a documented proposal describing the affected principle or section, a semantic
version rationale, migration guidance for existing features, and review approval. All specifications,
plans, tasks, implementations, and reviews MUST be evaluated against these rules.

- MAJOR: Backward-incompatible governance or principle removal/redefinition.
- MINOR: A new principle or section, or materially expanded governance guidance.
- PATCH: Clarification, correction, formatting, or other non-semantic refinement.

Compliance SHALL be reviewed during specification, implementation planning, code review, and
acceptance. Unresolved exceptions MUST be explicit, scoped, time-bounded, and recorded in the
feature artifacts rather than hidden in implementation decisions.

**Version**: 1.0.0 | **Ratified**: 2026-11-23 | **Last Amended**: 2026-11-23
