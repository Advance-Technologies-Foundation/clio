# clio.mcp.e2e

This project is the end-to-end test suite for the `clio` MCP server.
It uses the official [`ModelContextProtocol`][Official MCP C# SDK documentation] client library to talk to a real `clio mcp-server` child process over stdio.

This suite is intentionally different from `clio.tests` MCP unit tests:
- unit tests validate MCP argument mapping and command-resolution behavior in-process
- `clio.mcp.e2e` validates the real server process, stdio transport, MCP discovery, and tool execution end to end

## Skill to use

For MCP test work in this project, explicitly use the `test-mcp-tool` skill.

## Decisions

- The server under test must be started as an external process. Do not replace this with in-process invocation.
- Destructive MCP tests require explicit opt-in via `McpE2E:AllowDestructiveMcpTests=true`.
- Destructive tests must target a dedicated sandbox only. Never point them at a developer default environment.
- Configuration comes from `appsettings.json` with environment-variable overrides.
- The sandbox `EnvironmentName` is the source of truth; resolve `EnvironmentPath` from the registered `clio` environment and read `ConnectionStrings.config` to obtain Redis and database connection strings.
- Credentials-based MCP tests should also resolve the registered environment URL, login, password, and `IsNetCore` flag from the real `clio` settings file instead of duplicating secrets in test configuration.
- The first destructive test targets `clear-redis` and must prove the Redis side effect, not only a successful MCP response.
- Reusable harness and infrastructure code belongs in dedicated support folders, not inline in test classes.
- When the sandbox is not configured and destructive opt-in is disabled, tests should be skipped with an explicit reason.
- When destructive opt-in is enabled but required sandbox settings are missing, tests should fail fast with actionable diagnostics.

## Configuration contract

Use the `McpE2E` section with this shape:

- `McpE2E:AllowDestructiveMcpTests`
- `McpE2E:ClioProcessPath` optional override for the `clio` process or `clio.dll`
- `McpE2E:Sandbox:EnvironmentName`
- `McpE2E:Sandbox:SeedKeyPrefix`

Environment variables should use the standard double-underscore form, for example:

- `McpE2E__AllowDestructiveMcpTests`
- `McpE2E__Sandbox__EnvironmentName`

## Test structure

- All tests follow explicit AAA comments: `Arrange`, `Act`, `Assert`.
- Assertions must use `FluentAssertions`, and every assertion must include a `because` explanation.
- Every test method must include `[Description("...")]`.
- Decorate every MCP test with `AllureTag` naming the tool under test plus human-readable `AllureName` and `AllureDescription`.
- Prefer `[AllureFeature("<clio-command-name>")]` when the MCP tool maps directly to a `clio` command, for example `[AllureFeature("clear-redis-db")]`.
- Split long tests into explicit Allure step methods with `[AllureStep]` for `Arrange`, `Act`, and `Assert`.
- Do not collapse multiple assertions into a single Allure assert step; expose each important assertion as its own `[AllureStep]` with `AllureDescription` so reports stay readable.
- Successful command-path assertions should verify that MCP output includes at least one `Info` log message.
- Failed command-path assertions should verify that MCP output includes at least one `Error` log message when execution output is available.
- Add negative MCP tests for important failure modes such as invalid environment names and assert both failure diagnostics and absence of unintended side effects.
- Cover all meaningful MCP argument combinations, especially optional arguments that are not marked `[Required]`; use unit tests for mapping combinations and E2E tests for the critical real-world paths.
- Invalid-environment MCP calls may surface as a top-level MCP tool invocation error instead of the underlying command text; tests should accept that contract while still asserting human-readable diagnostics and no sandbox mutation.
- For `clear-redis-by-credentials`, an invalid URL is the reliable negative runtime case on the current sandbox; invalid credentials may not fail deterministically in the same way.
- Prefer one feature-focused fixture per MCP tool or workflow.
- Shared process startup, configuration loading, Redis helpers, and MCP client helpers should live in support folders.
- Keep tests safe by generating unique resource names per run to avoid collisions across repeated executions.

## Manual execution

- Allure Report 3 must be installed before using the local E2E reporting flow.
- Install Allure 3 globally with `npm install -g allure`.
- Verify the installation with `allure --version`.
- The official Allure 3 install reference is [Allure Report 3 install](https://allurereport.org/docs/v3/install/#install-allure-report-3).
- The repository includes [`run-e2e-tests.ps1`](C:\Projects\clio\clio.mcp.e2e\run-e2e-tests.ps1) for the common manual workflow.
- From [`C:\Projects\clio\clio.mcp.e2e`](C:\Projects\clio\clio.mcp.e2e), that script:
  - clears the existing `allure-report` output
  - clears `bin\Debug\net10.0`
  - runs `dotnet test .\clio.mcp.e2e.csproj`
  - runs `allure generate`
  - runs `allure serve`
- If `allure` is not on `PATH` after global installation, the Allure docs note that the npm global install directory must be added to `PATH`.

This project uses [Allure NUnit] for human-readable reports, but correctness and deterministic diagnostics come first.

[//]: # (## Docs and references)
[Official MCP C# SDK documentation]: https://devblogs.microsoft.com/dotnet/release-v10-of-the-official-mcp-csharp-sdk/
[Demo repository]: https://github.com/mikekistler/mcp-whats-new
[Allure NUnit]: https://allurereport.org/docs/nunit/
