# Clio development team

The human sets the outcome and governing architectural principles. TeamLead owns execution through independent review and QA. Architect is an escalation path, not a routine participant. TeamLead and Architect may consult Claude through Collab; workers and reviewers may not.

## Roles and invocation

Project-scoped agent definitions live in `.codex/agents/*.toml`. They inherit the model and set role-specific reasoning effort (see below). Open this checkout as the Codex project, then ask: **Have clio_teamlead implement <outcome> using the layer workers and continue until functional checks and independent agentic review pass.** Nothing is installed globally.

Prerequisites: Codex subagent support, the `agentic-code-review` skill for TeamLead, and the `claude-consultation` skill plus a working Collab Claude provider for consultations. These project roles do not install those dependencies. If a required skill or tool is unavailable, report the affected step as blocked and continue independent work; do not bypass Architect consultation or substitute Claude CLI.

The files use the [documented Codex custom-agent format](https://learn.chatgpt.com/docs/agent-configuration/subagents). Project configuration caps concurrent child threads at eight; TeamLead chooses fewer when sufficient. Subagents of the lead also consume capacity. The active conversation may have captured its available roles before these files existed, or be attached to another checkout. In that case, spawn the default role and explicitly read the relevant TOML's developer_instructions in the assigned checkout. Record that fallback honestly; do not claim automatic custom-role discovery was exercised. A fresh task opened in this checkout should discover the project definitions. Native Codex 0.152.1 configuration loading passed without warnings after correcting the invalid partial MCP overrides; evidence is in `artifacts/agent-team-config-validation.json`. This proves configuration loading, not a fresh custom-role dispatch.

| Role | Ownership |
|---|---|
| clio_teamlead | Plan, schedule, shared integration files (including Runtime, scripts and docs by default), verification and finding disposition |
| clio_primitives | External I/O and primitive tests; assigned runtime feature contracts |
| clio_composition | Workflows, metadata and controlled-response tests |
| clio_adapters | CLI/MCP/product surfaces and end-to-end tests |
| clio_core | Explicitly assigned infrastructure changes and lifetime tests |
| clio_architect | Architectural decisions with the human; Claude consultation through Collab |
| clio_reviewer | Independent correctness, security, performance, maintainability and test review |
| clio_intent | Independent acceptance and scope review |
| clio_kiss | Independent simplicity review |
| clio_qa | Early acceptance review and independent final behavioral verification |

These are roles, not a requirement to launch ten agents per task. Multiple workers can take independent modules in the same layer. Workers can read other layers but edit only their assigned files. Prompt ownership and restricted Claude use are collaboration rules, not a filesystem or tool security sandbox. Worker and reviewer roles explicitly prohibit Collab/Claude, including when using the fallback in an existing session; their inherited tools are not removed by these instructions. Standalone Codex role parsing rejects an incomplete MCP section containing only enabled=false, so we do not add invalid overrides or duplicate machine-specific transport credentials. Hard tool isolation would require a separately validated host configuration if it becomes a requirement.

## Execution protocol

1. TeamLead and independent QA establish acceptance criteria before coding. TeamLead writes one task record from `agent-task-template.md`, defining acceptance, contract, ownership and dependencies. Check dirty files before editing. Preserve the primary Clio checkout.
2. Agree on new primitive contracts before parallel implementation. TeamLead owns contract edits unless explicitly assigning them. Workers must not silently change that agreement.
3. Run independent work in parallel. Reserve shared registration, manifest, package and documentation files for one owner. Each assignment states the worker is not alone and must preserve others' edits.
4. TeamLead explicitly grants and releases one build slot at a time when outputs are shared, including reviewers running tests. Grant it only after referenced source dependencies are ready. Use bounded `dotnet -m:1` builds. Without a grant a worker reports files and tests not run, requests the slot and hands control back. Parallel independent file work is useful; concurrent rebuilds of the same DLLs are not.
5. Workers report ready for review with changed files, tests actually run, unresolved concerns and handoff dependencies. TeamLead assigns a nonauthor reviewer before accepting each delivery. The reviewer checks the task, bounded diff, architectural compliance and test evidence; worker fixes findings and reviewer confirms closure. Files appearing in a shared checkout does not mean the delivery is accepted. TeamLead integrates accepted work; incomplete work must not be declared complete.
6. Independent final QA verifies integrated acceptance and relevant failure paths. Use the agentic-code-review skill for an independent integrated review. Keep intent and KISS separate; one or more other reviewers cover correctness, security, performance, maintainability and testing. Reviewers cannot be the authors of the reviewed implementation.
7. TeamLead sends findings to their owners, reruns affected verification and obtains independent review of the corrections or proposed rejection. No unresolved Blocker/High finding can pass, and TeamLead cannot unilaterally downgrade one; every advisory finding needs an explicit evidence-backed disposition. Review is not a substitute for functional tests. Do not promise unlimited retries: when a genuine architecture decision or external blocker prevents progress, report it and continue independent tasks where possible.
8. Finish only when acceptance, required verification and review pass. Report artifacts and limitations. Commit, push, public release and live destructive operations require their own task authorization.

There is no scheduler service, polling daemon, external message bus or custom locking protocol. TeamLead uses Codex's existing subagent tools and a human-readable task record. Existing dependency guards enforce the references they test; remaining boundaries rely on review.

## Escalation

TeamLead may use Claude through the claude-consultation skill and Collab for difficult implementation or conflicting findings. It is optional advice, not permission to violate architecture. Workers report blockers to TeamLead.

Architect becomes involved when TeamLead needs an exception or cannot solve a task within architectural boundaries: layer dependencies, resident ABI, lifetime, coordination, trust or public guarantees. Architect consults Claude with actual files and evidence, then records approval of a bounded exception, rejection or a compliant alternative within the human-agreed principles. If unresolved or those governing principles/product intent must change, involve the human and pause dependent work. Ordinary feature extensions need no Architect. Silence is not approval.

## Effort and review stages

| Stage / role | Default effort | Responsibility |
|---|---|---|
| Worker | Low | Execute bounded assignment and focused tests |
| TeamLead | Medium | Plan, schedule, integrate and close findings |
| Early QA | Medium (explicit dispatch override) | Check acceptance and required evidence before coding |
| Worker review | Medium for straightforward tasks; high for complex/sensitive tasks | Independent review before delivery acceptance |
| Final QA and integrated reviewers | High | User behavior, cross-layer interactions and comprehensive review |
| Architect | High, only on escalation | Resolve architectural concerns with Claude |

Reviewer and QA role files default to high so a missing override does not weaken the final gate. TeamLead explicitly chooses medium for early QA and straightforward worker reviews. For default-role fallback, pass the corresponding reasoning effort at dispatch and read the role instructions; merely reading a TOML does not change an already-running agent's effort. Record actual settings. No cheaper model is selected automatically: lower effort is not a promise of a particular cost saving. Effort escalation for a hard bug is separate from architectural escalation.

## Test the revised protocol

Open a fresh task in this checkout and ask TeamLead to implement one bounded new feature under this protocol. Before coding, capture QA's acceptance review. Dispatch independent workers after agreeing contracts. Record each ready-for-review handoff, nonauthor verdict and corrections before acceptance. Finish with high-effort integrated QA and review on the final scope. Demonstrate at least one real entry-point test. Record actual effort, reviewer identities, any Claude consultation and why Architect was or was not needed. Do not manufacture an architectural exception to exercise the role.

The earlier pilot below predates this revised per-worker review/QA policy. It does not prove the new gates or fresh-session effort dispatch; those require the next trial.

## Pilot

The first pilot implements `verify-file`: compare a local file's SHA-256 with a caller-supplied digest. It exercises a new primitive, composition policy and generic CLI/MCP surfaces. It is new functionality, not a claimed Clio 8 parity port. TeamLead records real assignments, reviews, corrections and results in `docs/agent-runs/verify-file.md`. Run evidence proves this pilot only; it is not proof of reliable operation with 100 agents.
