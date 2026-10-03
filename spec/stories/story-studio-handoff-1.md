# Studio handoff commands

Status: review
PRD: ../prd/prd-studio-handoff.md
ADR: ../adr/adr-studio-handoff.md

Implement independent checkout, deploy and status commands plus host MCP tools.
Use prebuilt images; resolve required inputs before writes; preserve foreign
resources and existing worktrees. Checkout must work without a cluster or valid
deployment inputs. Deploy must work without Git/source access. Document JSON
schema, examples, operator dependency and explicit validation boundaries.

Definition of done:
- [x] Contract, CLI, MCP and documentation implemented.
- [x] Unit and real Git integration checks pass.
- [ ] Operator contract and live installation checks pass.
- [x] Agentic review and Fable review addressed.
- [ ] Pull requests opened; status remains review until merged.

Installer/controller probes pass; full application login/Twin/Builder acceptance
is pending recipient model access and snapshot configuration validation. This
story remains review, not done. The operator and Clio PRs are drafts.
