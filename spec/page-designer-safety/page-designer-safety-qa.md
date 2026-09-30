# Issue #1697 validation

## Real Creatio validation

Exclusive disposable environment: `clio-issue-1697`, Creatio 10.0.0.858 Studio,
.NET 8, PostgreSQL. Created for this issue, with its own application/package and page.
The user approved this version because it matches the original report.

1. Created `UsrIssue1697_FormPage` in the issue-owned application package.
2. Saved a factory constant and function plus a handler calling that function.
3. Opened the real Interface Designer and pressed Save.
4. Read the persisted body: both declarations disappeared, but the handler call remained.
5. Created `UsrIssue1697Logic`, exported `label()`, and added its dependency and argument
   in `SCHEMA_DEPS` and `SCHEMA_ARGS`. The page handler populated a label attribute.
6. Reloaded the Designer and saved again. Readback retained the dependency, helper call,
   and label. The actual running form displayed **MODULE SURVIVED DESIGNER SAVE**.
7. Real stdio MCP `update-page` and `sync-pages` rejected both uninitialized helpers and
   initialized factory helpers. Independent `get-page` readbacks were byte-identical to
   the original stored body after each rejected non-dry-run save.

This proves the reported web-page boundary on this version. It does not claim that all
Creatio versions preserve every marker identically or that mobile pages use this AMD rule.
Before/after bodies and a screenshot are retained in the local issue evidence directory.

## Automated checks

```text
dotnet test clio.tests/clio.tests.csproj --filter "TestCategory=Unit&Module=McpServer"
5778 passed; 2 platform-specific tests skipped; 0 failed.

dotnet test clio.mcp.e2e/clio.mcp.e2e.csproj -f net10.0 --no-build --filter "FullyQualifiedName~PageValidateToolE2ETests|FullyQualifiedName~When_HelperIsUnsafe"
50 passed; 0 skipped; 0 failed. Explicit sandbox and destructive-test opt-in used.
```

MCP descriptions, CLI help, command docs and index updated. `get-guidance` references
now identify an MCP tool rather than a nonexistent standalone CLI verb.

ClioRing compatibility reviewed, no Ring-consumed contract changed: searched
`clio-ring/ClioRing.Ipc`, `clio-ring/ClioRing`, and
`clio-ring/ClioRing.Desktop/actions.json` for the three affected page tool names.
