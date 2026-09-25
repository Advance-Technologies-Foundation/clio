# MCP E2E, guidance and knowledge record

**Jira**: [ENG-95244](https://creatio.atlassian.net/browse/ENG-95244) (work package C — the rescope is not yet agreed with the reporter)
**Spec**: [spec-descriptor-strict-keys.md](../prd/spec-descriptor-strict-keys.md) ·
**ADR**: [adr-descriptor-strict-keys.md](../adr/adr-descriptor-strict-keys.md) ·
**Test plan**: [tp-descriptor-strict-keys.md](../test-plans/tp-descriptor-strict-keys.md)
Status: review

As a reviewer I want the refusal proven over the real MCP server and documented once.

Acceptance:
- [x] `clio.mcp.e2e` covers create, modify and as-new-version with an unknown key, asserting refusal or
      warning by the stand's installed vs bundled version. TC-I-01..TC-I-03.
- [x] Guidance (clio-knowledge, draft PR with a `libraryVersion` bump) states the refusal; tool descriptions
      do not grow (payload-budget ceiling).
- [x] `docs/knowledge/` record of the measured silent drop and of why the key set lives in clio.
