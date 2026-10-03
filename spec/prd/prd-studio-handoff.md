# Portable AI Studio handoff

Status: approved for implementation by the user on 2026-10-03.

## Outcome

A recipient uses Clio and an explicit handoff package to run AI Studio in local
Rancher Desktop without accessing Omen, compiling application images, or knowing
the producer's machine. Independently, the recipient can check out matching
sources as pinned Git worktrees. Both commands must work without the other.

## Contract

- `clio studio deploy --profile <json> --context rancher-desktop` uses the
  existing operator bootstrap and a compatible operator's Studio controller.
- `clio studio checkout --profile <json> --directory <path>` requires Git and
  repository access, but no Kubernetes, Docker, operator or deployment settings.
- `clio studio status` reports cluster progress independently of either operation.
- Images come from explicitly declared registries, pinned by digest. The current
  accepted registry is registry.krylov.cloud. Deployment definitions travel in
  the package; deployment must not clone repositories to obtain charts.
- Missing required inputs are returned together as structured, credential-free
  diagnostics. The calling agent supplies them through an inputs file. Explicitly
  generated local secrets are stable across retries.
- Source entries declare HTTPS/SSH Git URL, branch and exact commit. A metarepo is
  an ordinary source entry; the package contains the complete resolved source
  list, including additional repositories. No hidden source-discovery step.
- Reuse compatible shared dependencies only when requested with verifiable
  requirements. Do not substitute PG18 for PG16 or collapse Redis policies.
- Reruns resume the same installation and preserve its data and unrelated CRM.

## Acceptance

1. Missing-input validation occurs before deployment mutation and leaks no values.
2. Checkout proves exact commits, preserves existing/dirty paths, and can rerun.
3. Deploy without any source checkout creates Studio through the operator.
4. Deployment status distinguishes pending, blocked, failed and ready; completed
   migration/initialization Jobs are not rerun during steady-state reconciliation.
5. Foreign resources and namespaces are never adopted or replaced silently.
6. One real handoff installs Identity, Studio, Twin and Builder; login, Twin reply
   and an agent build/deploy/reply are exercised. Any unmet check is reported.
7. Host MCP tools reuse the commands and preserve their independence.

## Exclusions

CRM-to-Studio wiring, offline distribution, HA/production installation, automatic
handoff generation, release-discipline enforcement, automatic shared database
major upgrades, and copying private developer state. The first handoff initializes
a fresh organization with explicit demo content.

## Dependencies

Clio PR #1735 supplies the experimental operator bootstrap and host-runtime guards.
The Studio feature is delivered as a dependent PR plus an operator PR. The
producer is responsible for building/publishing deployment artifacts; consumers
pull them. Source-image provenance inconsistencies are reported, not repaired.
