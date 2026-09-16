using System.Text.RegularExpressions;
using System.Threading;
using Allure.Net.Commons;
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

	[Category("McpE2E.Sandbox")]
	[TestCase(false, false)]
	[TestCase(true, false)]
	[TestCase(true, true)]
	[Description("Persists multi-KB inline and file bodies through clio-run, including file precedence, and reads them back in a new MCP session.")]
	[AllureTag(ToolName)]
	[AllureName("create-schema preserves supplied source in a fresh session")]
	[AllureDescription("Reads back exact multi-KB Unicode source after creation through the real MCP process.")]
	public async Task CreateSchema_ShouldPersistBody_WhenBodyIsSupplied(bool useFile, bool includeInlineBody) {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using var context = Arrange(TimeSpan.FromMinutes(5));
		string schemaName = $"UsrBody{Guid.NewGuid():N}";
		string body = $"// Unicode: Grüße Україна\r\nnamespace Terrasoft.Configuration {{ public class {schemaName} {{ }} }}\r\n/*{new string('x', 12000)}*/\r\n";
		string bodyFile = Path.Combine(Path.GetTempPath(), $"{schemaName}.cs");
		Dictionary<string, object?> args = new() {
			["schema-name"] = schemaName,
			["package-name"] = PackageName,
			["environment-name"] = environmentName
		};
		try {
			if (useFile) {
				await File.WriteAllTextAsync(bodyFile, body);
				args["body-file"] = bodyFile;
				if (includeInlineBody) {
					args["body"] = "// The file must take precedence.";
				}
			} else {
				args["body"] = body;
			}
			// Act
			CallToolResult result = await AllureApi.Step("Create schema with source", () => context.Session.CallToolAsync(ToolName,
				new Dictionary<string, object?> { ["args"] = args }, context.CancellationTokenSource.Token));
			SourceCodeSchemaCreateResponse created = EntitySchemaStructuredResultParser.Extract<SourceCodeSchemaCreateResponse>(result);
			await using McpServerSession reopened = await McpServerSession.StartAsync(settings, context.CancellationTokenSource.Token);
			CallToolResult readResult = await AllureApi.Step("Read persisted source in a fresh session", () => reopened.CallToolAsync(GetSchemaTool.ToolName,
				new Dictionary<string, object?> { ["args"] = new Dictionary<string, object?> {
					["schema-name"] = schemaName, ["environment-name"] = environmentName
				} }, context.CancellationTokenSource.Token));
			GetSourceCodeSchemaResponse read = EntitySchemaStructuredResultParser.Extract<GetSourceCodeSchemaResponse>(readResult);
			// Assert
			AllureApi.Step("Creation has no protocol error", () => result.IsError.Should().NotBeTrue(because: "creation must return a normal tool result"));
			AllureApi.Step("Creation succeeds", () => created.Success.Should().BeTrue(because: $"the supplied body must be saved: {created.Error}"));
			AllureApi.Step("Fresh-session read succeeds", () => read.Success.Should().BeTrue(because: $"the new schema must be readable from a new session: {read.Error}"));
			AllureApi.Step("Source matches exactly", () => read.Body.Should().Be(body, because: "creation must persist the exact supplied C# body, including Unicode and line endings"));
			AllureApi.Step("Source length matches", () => read.BodyLength.Should().Be(body.Length, because: "readback length must describe the persisted body"));
		} finally {
			File.Delete(bodyFile);
		}
	}

	[Category("McpE2E.Sandbox")]
	[TestCase("missing-file")]
	[TestCase("empty-file")]
	[TestCase("empty-body")]
	[Description("Rejects invalid source inputs and confirms no schema was created.")]
	[AllureTag(ToolName)]
	[AllureName("create-schema rejects invalid content before creation")]
	[AllureDescription("Uses the configured disposable environment to prove invalid source does not create an empty schema.")]
	public async Task CreateSchema_ShouldNotCreateSchema_WhenBodyInputIsInvalid(string input) {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await using var context = Arrange(TimeSpan.FromMinutes(5));
		string schemaName = $"UsrBadBody{Guid.NewGuid():N}";
		string bodyFile = Path.Combine(Path.GetTempPath(), $"{schemaName}.cs");
		Dictionary<string, object?> args = new() {
			["schema-name"] = schemaName, ["package-name"] = PackageName, ["environment-name"] = environmentName,
			["body"] = input == "empty-body" ? " \r\n" : "// Do not use this fallback when the file is invalid."
		};
		if (input != "empty-body") {
			args["body-file"] = bodyFile;
		}
		try {
			if (input == "empty-file") {
				await File.WriteAllTextAsync(bodyFile, " \r\n");
			}
			// Act
			CallToolResult result = await AllureApi.Step("Attempt creation with invalid source", () => context.Session.CallToolAsync(ToolName,
				new Dictionary<string, object?> { ["args"] = args }, context.CancellationTokenSource.Token));
			SourceCodeSchemaCreateResponse created = EntitySchemaStructuredResultParser.Extract<SourceCodeSchemaCreateResponse>(result);
			CallToolResult readResult = await AllureApi.Step("Check that no schema was created", () => context.Session.CallToolAsync(GetSchemaTool.ToolName,
				new Dictionary<string, object?> { ["args"] = new Dictionary<string, object?> {
					["schema-name"] = schemaName, ["environment-name"] = environmentName
				} }, context.CancellationTokenSource.Token));
			GetSourceCodeSchemaResponse read = EntitySchemaStructuredResultParser.Extract<GetSourceCodeSchemaResponse>(readResult);
			// Assert
			AllureApi.Step("Creation is rejected", () => created.Success.Should().BeFalse(because: "invalid supplied source must not be silently discarded"));
			AllureApi.Step("Rejection explains invalid content", () => created.Error.Should().Contain(input == "missing-file" ? "body-file not found" : "must not be empty",
				because: "the failure must explain how to correct the source input"));
			AllureApi.Step("Readback does not find a schema", () => read.Success.Should().BeFalse(because: "invalid input must not create a schema"));
			AllureApi.Step("Readback confirms absence", () => read.Error.Should().Contain("not found", because: "a missing schema proves absence rather than a transport failure"));
		} finally {
			File.Delete(bodyFile);
		}
	}

	private static async Task<string> ResolveReachableEnvironmentAsync(McpE2ESettings settings) {
		// The opt-in is consulted BEFORE the environment is resolved, so neither a clio process nor the
		// stand is reached while it is off. Every caller is a sandbox test that creates a real schema on
		// the environment, which is exactly what this switch authorizes.
		if (!DestructiveStandAuthorization.IsAuthorized(true, settings.AllowDestructiveMcpTests)) {
			Assert.Ignore(DestructiveStandAuthorization.MissingOptInMessage);
		}
		// Configured-only: the opt-in authorizes writes to the disposable stand named in settings, never
		// to a fallback environment that merely answers.
		return await ReachableSandboxEnvironment.ResolveConfiguredOrIgnoreAsync(
			settings,
			$"create-schema MCP E2E requires the configured sandbox environment '{settings.Sandbox.EnvironmentName}' to be set and reachable.");
	}

}
