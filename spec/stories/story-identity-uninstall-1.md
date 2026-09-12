# Optional local identity lifecycle

Status: done
Issue: #1433

Implement the accepted identity-uninstall ADR as one cohesive change: settings/schema,
deployment recording, standalone and combined cleanup, CLI/MCP/docs and tests.

Definition of done:
- [x] Optional attachment is persisted and shown empty when absent.
- [x] Deploy, no-app and partial deployment preserve cleanup authority.
- [x] Standalone and combined cleanup satisfy the approved acceptance criteria.
- [x] Unit, integration, MCP E2E and disposable IIS verification pass.
- [x] Ring compatibility tests/harness and NativeAOT publish pass.
- [x] Documentation/guidance and required reviews are complete.

Merge and local worktree cleanup are tracked by the linked GitHub issue and pull request.
