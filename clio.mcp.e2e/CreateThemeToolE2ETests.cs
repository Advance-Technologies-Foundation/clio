using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for the create-theme MCP tool. Actually creating a theme requires a live Creatio
/// environment with branding licensing and the CanManageThemes operation, so the hermetic CI-safe assertions
/// are that the real clio MCP server advertises create-theme and rejects a camelCase alias with a structured
/// rename hint; the live behavior is covered by <see cref="ThemingSandboxE2ETests"/>.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("create-theme")]
[NonParallelizable]
public sealed class CreateThemeToolE2ETests : McpContractFixtureBase {
	[Test]
	[AllureTag(CreateThemeTool.ToolName)]
	[AllureName("create-theme tool is discoverable on the lazy surface")]
	[Description("Starts the real clio MCP server and verifies create-theme is discoverable via the get-tool-contract compact index on the lazy tool surface.")]
	public async Task CreateTheme_Should_Be_Discoverable_On_Lazy_Surface() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(CreateThemeTool.ToolName,
			because: "the create-theme MCP tool must be discoverable on the lazy surface (get-tool-contract compact index) even though it is not resident in tools/list");
	}

	[Test]
	[AllureTag(CreateThemeTool.ToolName)]
	[AllureName("create-theme and build-theme advertise the full brand-parameter surface over the wire")]
	[Description("Fetches the create-theme and build-theme contracts from the real clio MCP server and asserts the advertised parameter set — every brand property (including the seven now declared on the shared ThemeBrandArgs base record) reaches the schema the model actually sees, css-content is no longer schema-required on create-theme, and each tool keeps its own required set. Guards the ThemeBrandArgs extraction: a base-record property that the schema generator failed to walk would silently disappear from the advertised contract while every in-process unit test still passed.")]
	public async Task ThemeTools_Should_Advertise_All_Brand_Parameters_Over_The_Wire() {
		// Arrange
		string[] brandParameters = [
			"primary", "secondary", "accent", "success", "error", "heading-font", "body-font", "font-weights"
		];
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		ToolContractDefinition createContract = await GetToolContractAsync(context, CreateThemeTool.ToolName);
		ToolContractDefinition buildContract = await GetToolContractAsync(context, BuildThemeTool.ToolName);

		// Assert
		string[] createParameters = createContract.InputSchema.Properties.Select(field => field.Name).ToArray();
		string[] buildParameters = buildContract.InputSchema.Properties.Select(field => field.Name).ToArray();
		createParameters.Should().Contain(brandParameters,
			because: "every brand parameter — including the seven inherited from ThemeBrandArgs — must reach the advertised create-theme schema");
		createParameters.Should().Contain("css-content",
			because: "the inline CSS source stays advertised alongside the brand mode");
		createContract.InputSchema.Required.Should().BeEquivalentTo(["environment-name"],
			because: "css-content must not be schema-required once the brand mode is an alternative CSS source");
		buildParameters.Should().Contain(brandParameters,
			because: "build-theme inherits the same shared brand properties and must keep advertising them");
		buildParameters.Should().Contain("version",
			because: "build-theme targets a version the caller names, so version stays part of its surface");
		createParameters.Should().NotContain("version",
			because: "create-theme writes to a named environment, so the template always follows that environment's own version — an override could only build CSS that does not match where the theme lands");
		buildContract.InputSchema.Required.Should().BeEquivalentTo(["primary"],
			because: "build-theme keeps its own required set — the shared base record must not change it");
		string[] sharedBrandParameters = ["secondary", "accent", "success", "error", "heading-font", "body-font", "font-weights"];
		foreach (string shared in sharedBrandParameters) {
			string createDescription = createContract.InputSchema.Properties.Single(field => field.Name == shared).Description;
			string buildDescription = buildContract.InputSchema.Properties.Single(field => field.Name == shared).Description;
			createDescription.Should().NotBeNullOrWhiteSpace(
				because: $"the inherited '{shared}' parameter must keep its [Description] in the advertised contract");
			buildDescription.Should().Be(createDescription,
				because: $"'{shared}' is declared once on ThemeBrandArgs, so both tools must advertise the identical description — a divergence means the shared declaration stopped flowing through");
		}
	}

	[Test]
	[AllureTag(CreateThemeTool.ToolName)]
	[AllureName("create-theme rejects a camelCase alias with a structured rename hint over the wire")]
	[Description("Calls create-theme through the real clio MCP server with a camelCase environmentName field and verifies the structured rename hint — proving the args wrapper binds and unknown keys reach the ExtensionData bag through the real MCP serializer, without a live Creatio environment.")]
	public async Task CreateTheme_Should_Return_RenameHint_When_CamelCase_Alias_Is_Passed_Over_The_Wire() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			CreateThemeTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environmentName"] = "docker_fix2"
				}
			},
			context.CancellationTokenSource.Token);
		CreateThemeResult result = EntitySchemaStructuredResultParser.Extract<CreateThemeResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an argument mistake must surface as a structured in-tool failure, not an MCP protocol error");
		result.Success.Should().BeFalse(
			because: "a camelCase alias must be rejected, not silently dropped");
		result.Error.Should().Contain("'environmentName' -> 'environment-name'",
			because: "the failure must tell the caller the exact rename that fixes the call");
	}

	[Test]
	[AllureTag(CreateThemeTool.ToolName)]
	[AllureName("create-theme binds the args wrapper and returns a structured validation failure")]
	[Description("Calls create-theme through the real clio MCP server with an empty args object and verifies the structured { success=false, error } result names environment-name — proving the args wrapper binds without a live Creatio environment.")]
	public async Task CreateTheme_Should_Return_Structured_Validation_Failure_When_Args_Are_Empty() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			CreateThemeTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?>()
			},
			context.CancellationTokenSource.Token);
		CreateThemeResult result = EntitySchemaStructuredResultParser.Extract<CreateThemeResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an argument mistake must surface as a structured in-tool failure, not an MCP protocol error");
		result.Success.Should().BeFalse(
			because: "a missing environment name is an expected, caller-actionable validation error");
		result.Error.Should().Contain("environment-name is required",
			because: "the failure must name the exact kebab-case field the caller has to add");
	}

	[Test]
	[AllureTag(CreateThemeTool.ToolName)]
	[AllureName("create-theme rejects css-content combined with the brand parameters over the wire")]
	[Description("Calls create-theme through the real clio MCP server with css-content AND the brand parameters (including the typed font-weights int array) and verifies the structured theme-css-source-conflict failure — proving the brand-mode args bind through the real MCP serializer, without a live Creatio environment (ENG-93989).")]
	public async Task CreateTheme_Should_Return_SourceConflict_When_CssContentAndBrandParametersArePassedOverTheWire() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			CreateThemeTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = "docker_fix2",
					["css-content"] = ".ocean-theme{color:#003366}",
					["primary"] = "#004fd6",
					["heading-font"] = "Poppins",
					["font-weights"] = new[] { 400, 600 }
				}
			},
			context.CancellationTokenSource.Token);
		CreateThemeResult result = EntitySchemaStructuredResultParser.Extract<CreateThemeResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an argument mistake must surface as a structured in-tool failure, not an MCP protocol error");
		result.Success.Should().BeFalse(
			because: "css-content and the brand parameters are mutually exclusive CSS sources");
		result.Error.Should().Contain("theme-css-source-conflict",
			because: "the stable kebab-case error code must travel in the message so the caller can branch on it");
		result.Warnings.Should().BeNull(
			because: "the guard fired before any build, so the parsed result must carry no advisories (the key-omission wire shape itself is pinned by CreateThemeResult_ShouldOmitWarningsKey_WhenThereAreNoAdvisories)");
	}

	[Test]
	[AllureTag(CreateThemeTool.ToolName)]
	[AllureName("create-theme without any CSS source names both sources in the structured failure over the wire")]
	[Description("Calls create-theme through the real clio MCP server with an environment name but neither css-content nor primary and verifies the structured theme-css-source-missing failure names both accepted CSS sources — the wire-level contract of the brand mode's exactly-one-source rule (ENG-93989).")]
	public async Task CreateTheme_Should_Return_SourceMissing_When_NoCssSourceIsPassedOverTheWire() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			CreateThemeTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = "docker_fix2",
					["caption"] = "Ocean"
				}
			},
			context.CancellationTokenSource.Token);
		CreateThemeResult result = EntitySchemaStructuredResultParser.Extract<CreateThemeResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an argument mistake must surface as a structured in-tool failure, not an MCP protocol error");
		result.Success.Should().BeFalse(
			because: "a create request needs exactly one CSS source");
		result.Error.Should().Contain("theme-css-source-missing",
			because: "the stable kebab-case error code must travel in the message so the caller can branch on it");
		result.Error.Should().Contain("css-content",
			because: "the failure must name the inline source the caller can provide");
		result.Error.Should().Contain("primary",
			because: "the failure must name the brand-mode source the caller can provide");
		result.Warnings.Should().BeNull(
			because: "the guard fired before any build, so the parsed result must carry no advisories (the key-omission wire shape itself is pinned by CreateThemeResult_ShouldOmitWarningsKey_WhenThereAreNoAdvisories)");
	}

	private static async Task<ToolContractDefinition> GetToolContractAsync(ArrangeContext context, string toolName) {
		CallToolResult result = await context.Session.CallToolAsync(
			ToolContractGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> { ["tool-names"] = new[] { toolName } }
			},
			context.CancellationTokenSource.Token);
		ToolContractGetResponse response = EntitySchemaStructuredResultParser.Extract<ToolContractGetResponse>(result);
		response.Success.Should().BeTrue(
			because: $"the {toolName} contract fetch must succeed before its schema can be asserted (error: {response.Error?.Message})");
		return response.Tools.Should().ContainSingle(
			because: "one requested tool name must expand to exactly one contract").Subject;
	}
}
