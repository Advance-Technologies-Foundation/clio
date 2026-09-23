# Review: issue 1641 (2026-09-23)

Intent: allow ordinary comments while preserving required Creatio marker pairs and source text.

Parallel agentic review covered quality, security, performance, tests, edge cases, intent, and KISS. Pre-PR and final full-diff passes found no actionable code defects. KISS reported a stale SPEC live-save phrase; a fresh read confirmed the current SPEC already describes Windows MCP plus mocked save-payload validation, with no live save claim.

The production change uses existing JsonDocumentOptions; no custom parser, new dependency, or lifecycle machinery. Preserve the runtime/validation distinction: Mutagen proves file delivery, Windows stdio MCP proves validation behavior, and unit tests prove the SaveSchema payload. No registered-page rendering or live SaveSchema result was exercised.

Validation: dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=Command|Module=McpServer)" --verbosity minimal --logger "trx;LogFileName=issue-1641.trx" passed: 10,473 passed, 15 skipped, 0 failed. Real patched Windows stdio MCP passed all three valid fixtures and rejected the missing-marker fixture. Claude review rev_13567fa1a9b4499b is pending.

Claude review rev_13567fa1a9b4499b completed with no material correctness/security defects. Accepted its documentation clarification: replace saves preserve comments, but append re-serializes merged sections and can discard ordinary comments (pre-existing behavior). Added the clarification. No code change or repeated test/review was needed for this documentation-only correction.

PR review correction: the Codex bot correctly required durable MCP E2E coverage beyond the manual Windows probe. Added PageCommentValidationE2ETests: real external stdio processes exercise baseline and line/block comments through validate-page, update-page and sync-pages. Missing opening/closing markers are rejected by validate-page/sync-pages offline gates; update-page checks markers after environment resolution, so its negative marker coverage uses the resolved command and asserts no SaveSchema call. Unique unregistered environments prevent writes during wire tests. No production code changed. Focused E2E passed on .NET 8 and .NET 10 (3 tests / 21 tool calls per framework). Incremental testing/correctness review covers this test-only follow-up; the earlier Claude verdict remains applicable to production commit e1af5a702.
