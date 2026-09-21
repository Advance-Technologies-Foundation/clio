# Clio 10 contributor guidance

Read README.md, CONTRIBUTING.md and [architectural principles](docs/architectural-principles.md) before implementation. The principles define layer placement and the design checkpoints for changing a boundary. Do not restore legacy Clio 8 source/dependencies.

- Product starts adapters; adapters call Composition. Composition owns workflows/default wiring. Core owns environments, local coordination and versioned sessions. Primitives performs external I/O.
- Concrete SDK types stay out of contracts/workflows; Core has no concrete primitive reference.
- Workflows register independently through DI. Nested operations borrow the context and finish sequentially before parent returns.
- Minimal centralized dependencies, interface DI, public XML documentation.
- NUnit/FluentAssertions, AAA, descriptions and assertion explanations. Run affected tests; solution tests for shared changes; local install for packaging.
- Real maintenance needs an authorized target. Never claim readiness/exactly-once from HTTP acceptance.
- Keep docs aligned. CI redesign, host replacement and cross-process coordination are outside this slice. Complete-runtime acquisition and Composition/primitive activation are opt-in proofs; read docs/runtime-update-proof.md before changing them.

## Agentic development

Use the project roles in `.codex/agents/` and the protocol in `docs/agent-team.md` when the user requests team execution. TeamLead plans, delegates, monitors, integrates and continues until functional verification and independent agentic review pass. Layer workers own explicit files; only one agent builds shared outputs at a time. TeamLead and Architect may consult Claude through Collab; workers and reviewers may not. Architect handles architectural exceptions within human-agreed principles; unresolved conflicts or changes to those governing principles require the human. Worker deliveries require independent review before acceptance; final integrated QA and review remain mandatory. See `docs/agent-task-template.md` for the small task record. Agent instructions express ownership; they are not a sandbox.
