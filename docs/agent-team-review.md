# Agent team setup review

## Scope

Project roles under `.codex/agents`, project agent concurrency settings, `AGENTS.md`, `agent-team.md` and `agent-task-template.md`. This review concerns the development operating model; the verify-file implementation has its own independent review in `agent-runs/verify-file.md`.

## Actual participants

- Root created and validated the role definitions and protocol.
- `pilot_teamlead` used the TeamLead role to freeze the feature contract, delegate three layer workers, integrate and verify the pilot, and run independent agentic review.
- `pilot_architect` used the Architect role and requested Claude review `rev_d2fa862c77ee4199` through Collab. No TeamLead or worker Claude consultation was requested.

The existing session used explicit role-file loading with default subagents. Automatic fresh custom-role dispatch was not exercised. Native Codex 0.152.1 successfully loaded the final project configuration with no startup warnings; the evidence is recorded under `artifacts/agent-team-config-validation.json` and `artifacts/agent-team-definition-check.json`.

## Findings and disposition

The first TOML syntax check passed but native Codex rejected partial disabled Collab MCP sections as invalid transport configuration. Root removed those sections. The team now explicitly describes Architect-only Collab use as policy, not enforced tool isolation. This avoids machine-specific duplicate connection settings and makes the proof boundary clear.

Claude's proposed P1 build-slot deadlock assumed that a running worker could not receive a grant. Architect checked the actual collaboration tools and rejected that premise. The useful correction was accepted: grant and release explicitly; without a grant workers report tests not run and hand control back. Builds must also wait for referenced source dependencies, a concrete lesson from the pilot's early missing Composition type.

Additional accepted corrections:

- Architect returns options/recommendations, not human approval; TeamLead relays unresolved decisions to the human through the parent task.
- Independent reviewers verify finding closure/rejection; TeamLead cannot downgrade a blocking finding to pass itself.
- Record original intent and steering, exact review scope and recheck later changes.
- TeamLead owns shared Runtime, scripts and documentation unless explicitly delegated.
- Dependency guard claims are limited to references actually tested.
- Required skills and Collab are external prerequisites; unavailable consultation is reported rather than bypassed.

Architect independently reread the corrected roles and protocol and returned **closure verification PASS**, with no remaining blocking findings or new ownership/approval conflicts. No second Claude review was used for these ordinary corrections. No new architectural decision required human approval.

## What this establishes

Valid project configuration, explicit role ownership, an independently reviewed execution protocol and one real multi-agent feature pilot. It does not establish filesystem/tool isolation, automatic recovery from every agent failure, a live custom-role reload, or scalability to 100 agents. The implementation and native configuration evidence are separate from reviewer judgment. The unrelated Codex doctor terminal check reports TERM=dumb in the noninteractive shell; the configuration check itself passes.
