# Story: resolve the uninstall warning sandbox by URI

Status: done

Issue: #893

As a Clio maintainer, I want the locked-profile MCP E2E to resolve its IIS application pool from the
registered sandbox URI so the master TeamCity suite validates uninstall warnings without requiring
unrelated `EnvironmentPath` metadata.

## Definition of done

- The fixture resolves the environment URI through the fresh clio executable.
- IIS URI matching is strict, deterministic, and covered by no-environment E2E harness tests.
- TeamCity's explicit application-pool parameter supports public URL routing that differs from local
  IIS topology without weakening fail-closed target validation.
- The existing destructive warning scenario passes against an explicitly opted-in disposable stand.
- Documentation, MCP production surface, and ClioRing compatibility are reviewed and recorded.
- Final comprehensive agentic review has no unresolved Blocker or High findings.
