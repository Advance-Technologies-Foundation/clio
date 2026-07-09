using System.Text.RegularExpressions;
using System.Threading;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

[TestFixture]
[AllureNUnit]
[AllureFeature(SchemaCreateTool.ToolName)]
[NonParallelizable]
public sealed class SchemaCreateToolE2ETests : McpContractFixtureBase {
	private const string ToolName = SchemaCreateTool.ToolName;
	private const string PackageName = "Custom";

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Exposes create-schema via the get-tool-contract compact index on the lazy tool surface.")]
	[AllureTag(ToolName)]
	[AllureName("create-schema is discoverable on the lazy surface")]
	public async Task SchemaCreateTool_Should_Be_Listed_By_MCP_Server() {
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		IReadOnlyCollection<string> toolNames =
			await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);

		toolNames.Should().Contain(ToolName,
			because: "create-schema must be discoverable via the get-tool-contract compact index so MCP callers can find the C# schema creation tool even though it is not resident in tools/list");
	}

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Reports readable failures when create-schema is called with an invalid environment name.")]
	[AllureTag(ToolName)]
	[AllureName("create-schema reports invalid environment failures")]
	public async Task SchemaCreateTool_Should_Report_Invalid_Environment_Failure() {
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-create-schema-env-{Guid.NewGuid():N}";

		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = "UsrMissingHelper",
					["package-name"] = "Custom",
					["environment-name"] = invalidEnvironmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		SourceCodeSchemaCreateResponse response =
			EntitySchemaStructuredResultParser.Extract<SourceCodeSchemaCreateResponse>(callResult);

		callResult.IsError.Should().NotBeTrue(
			because: "structured create-schema failures should stay inside the tool response envelope");
		response.Success.Should().BeFalse();
		response.Error.Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found)");
	}

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Rejects malformed schema-name via create-schema before any remote calls.")]
	[AllureTag(ToolName)]
	[AllureName("create-schema rejects malformed schema-name")]
	public async Task SchemaCreateTool_Should_Reject_Invalid_Schema_Name() {
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = "1BadName",
					["package-name"] = "Custom",
					["environment-name"] = $"noop-{Guid.NewGuid():N}"
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		SourceCodeSchemaCreateResponse response =
			EntitySchemaStructuredResultParser.Extract<SourceCodeSchemaCreateResponse>(callResult);

		callResult.IsError.Should().NotBeTrue();
		response.Success.Should().BeFalse();
		response.Error.Should().Contain("schema-name must start with a letter");
	}

	[Category("McpE2E.Sandbox")]
	[Test]
	[Description("Creates a C# source-code schema and verifies it exists in the environment.")]
	[AllureTag(ToolName)]
	[AllureName("create-schema creates a real source-code schema on the remote environment")]
	public async Task SchemaCreateTool_Should_Create_Real_Schema() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(5));
		string schemaName = $"UsrE2EHelper{Guid.NewGuid():N}".Substring(0, 35);

		CallToolResult createResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = schemaName,
					["package-name"] = PackageName,
					["caption"] = "E2E test helper",
					["environment-name"] = environmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		SourceCodeSchemaCreateResponse createResponse =
			EntitySchemaStructuredResultParser.Extract<SourceCodeSchemaCreateResponse>(createResult);

		createResult.IsError.Should().NotBeTrue();
		createResponse.Success.Should().BeTrue(
			because: $"create-schema must succeed for a fresh schema name '{schemaName}' in the editable '{PackageName}' package. Error: {createResponse.Error}");
		createResponse.SchemaName.Should().Be(schemaName);
		createResponse.SchemaUId.Should().NotBeNullOrWhiteSpace();
		createResponse.PackageName.Should().Be(PackageName);
		createResponse.Caption.Should().Be("E2E test helper");
	}

	[Category("McpE2E.Sandbox")]
	[Test]
	[Description("create-schema returns a duplicate-name error when the schema already exists.")]
	[AllureTag(ToolName)]
	[AllureName("create-schema rejects duplicate schema names")]
	public async Task SchemaCreateTool_Should_Report_Duplicate_Schema_Name() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(5));
		string schemaName = $"UsrE2EDupHelper{Guid.NewGuid():N}".Substring(0, 35);

		CallToolResult first = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = schemaName,
					["package-name"] = PackageName,
					["environment-name"] = environmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		EntitySchemaStructuredResultParser.Extract<SourceCodeSchemaCreateResponse>(first)
			.Success.Should().BeTrue(because: "arrange-step create-schema must succeed before the duplicate-name assertion");

		CallToolResult duplicateResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = schemaName,
					["package-name"] = PackageName,
					["environment-name"] = environmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		SourceCodeSchemaCreateResponse duplicateResponse =
			EntitySchemaStructuredResultParser.Extract<SourceCodeSchemaCreateResponse>(duplicateResult);

		duplicateResult.IsError.Should().NotBeTrue();
		duplicateResponse.Success.Should().BeFalse();
		duplicateResponse.Error.Should().Contain(schemaName).And.Contain("already exists");
	}

	private static async Task<string> ResolveReachableEnvironmentAsync(McpE2ESettings settings) {
		string? configuredEnvironmentName = settings.Sandbox.EnvironmentName;
		if (!string.IsNullOrWhiteSpace(configuredEnvironmentName) &&
			await CanReachEnvironmentAsync(settings, configuredEnvironmentName)) {
			return configuredEnvironmentName;
		}

		const string fallbackEnvironmentName = "d2";
		if (await CanReachEnvironmentAsync(settings, fallbackEnvironmentName)) {
			return fallbackEnvironmentName;
		}

		Assert.Ignore(
			$"create-schema MCP E2E requires a reachable environment. Configured sandbox environment '{configuredEnvironmentName}' was not reachable, and fallback environment '{fallbackEnvironmentName}' was also unavailable.");
		return string.Empty;
	}

	private static async Task<bool> CanReachEnvironmentAsync(McpE2ESettings settings, string environmentName) {
		using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
		try {
			ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(
				settings,
				["ping-app", "-e", environmentName],
				cancellationToken: cts.Token);
			return result.ExitCode == 0;
		} catch (OperationCanceledException) {
			return false;
		}
	}

}
