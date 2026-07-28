# Test plan: ESQ primitive comparisons guidance

## Lab evidence

From `C:\Projects\Workspaces\VirtualEntityGuidance`:

```powershell
dotnet test tests\UsrCodexVirtualEntity\UsrCodexVirtualEntity.Tests.csproj -c dev-n8 --no-restore
clio restart -e std-skill-n8
dotnet test tests\UsrCodexVirtualEntity.IntegrationTests\UsrCodexVirtualEntity.IntegrationTests.csproj `
  -c dev-n8 --no-build `
  --filter "(TestCategory=IntegerBoundaryComparisonsExampleHandler|TestCategory=TextPatternComparisonsExampleHandler|TestCategory=NegativeTextPatternComparisonsExampleHandler)"
```

Before the integration command, set `CREATIO_URL`, `CREATIO_IS_NETCORE`, and either
`CREATIO_ACCESS_TOKEN` or `CREATIO_USERNAME` plus `CREATIO_PASSWORD`; set
`CLIO_ENVIRONMENT_NAME=std-skill-n8`. Do not store credentials in the plan or test output.

Recorded result: 48/48 package tests and 6/6 focused live tests passed. The live set contains three exact
native-versus-ATF shape assertions and three direct ATF model-result assertions.

## clio validation

From the clio worktree:

```powershell
dotnet test clio.tests\clio.tests.csproj -c Release --filter "Category=Unit&Module=McpServer" --no-build
dotnet test clio.mcp.e2e\clio.mcp.e2e.csproj -c Release `
  --filter "(FullyQualifiedName~McpServer_ShouldReturnEsqFilterResources_WhenUrisAreRead|FullyQualifiedName~GuidanceGet_ShouldReturnEsqFilterChildGuides_WhenStableChildNamesAreRequested)"
```

Recorded result: the MCP module passed 2,429/2,429 tests on both net8.0 and net10.0. The focused MCP E2E set
passed 2/2 tests on both frameworks.

- Review the complete diff for correctness, maintainability, performance, and security before opening the PR.
