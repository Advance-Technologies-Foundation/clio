using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for application MCP tools.
/// </summary>
[TestFixture]
[AllureNUnit]
[NonParallelizable]
public sealed class ApplicationToolE2ETests {
	private const string ListToolName = ApplicationGetListTool.ApplicationGetListToolName;
	private const string InfoToolName = ApplicationGetInfoTool.ApplicationGetInfoToolName;
	private const string CreateToolName = ApplicationCreateTool.ApplicationCreateToolName;
	private const string DeleteToolName = ApplicationDeleteTool.ToolName;
	private const string SchemaSyncToolName = SchemaSyncTool.ToolName;


	[Test]
	[Description("Starts the real clio MCP server, invokes list-apps, and verifies structured installed application items when the environment exposes at least one installed application.")]
	[AllureFeature(ListToolName)]
	[AllureTag(ListToolName)]
	[AllureName("Application get list returns structured installed applications")]
	[AllureDescription("Uses the real clio MCP server to call list-apps for the configured environment, ignores when no installed applications are available, and otherwise verifies that the first installed application exposes the expected structured selectors and metadata fields.")]
	public async Task ApplicationGetList_Should_Return_Structured_Applications() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		TestConfiguration.EnsureSandboxIsConfigured(settings);
		await using ApplicationArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(2));

		// Act
		ApplicationListActResult listResult = await ActListAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			arrangeContext.EnvironmentName);
		ApplicationListItemEnvelope installedApplication = GetInstalledApplicationOrIgnore(listResult.Result);

		// Assert
		listResult.CallResult.IsError.Should().NotBeTrue(
			because: $"a valid list-apps request should return a structured MCP payload instead of a transport-level error. Actual result: {DescribeCallResult(listResult.CallResult)}");
		listResult.Result.Success.Should().BeTrue(
			because: "list-apps should succeed before a discovered installed application can be validated");
		installedApplication.Id.Should().NotBeNullOrWhiteSpace(
			because: "installed application list items should expose an id for follow-up MCP targeting");
		installedApplication.Code.Should().NotBeNullOrWhiteSpace(
			because: "installed application list items should expose a code for human-readable targeting and diagnostics");
		installedApplication.Name.Should().NotBeNullOrWhiteSpace(
			because: "installed application list items should expose a display name");
		installedApplication.Version.Should().NotBeNullOrWhiteSpace(
			because: "installed application list items should expose a version string in the structured list envelope");
	}

	[Test]
	[Description("Starts the real clio MCP server, discovers an installed application through list-apps, and verifies that get-app-info returns structured metadata for that application.")]
	[AllureFeature(InfoToolName)]
	[AllureTag(InfoToolName)]
	[AllureName("Application get info returns structured package and entity metadata")]
	[AllureDescription("Uses the real clio MCP server to discover an installed application via list-apps, ignores when none exist, and otherwise verifies that get-app-info returns the expected structured metadata envelope for that application.")]
	public async Task ApplicationGetInfo_Should_Return_Structured_Metadata() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		TestConfiguration.EnsureSandboxIsConfigured(settings);
		await using ApplicationArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(2));
		ApplicationListActResult listResult = await ActListAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			arrangeContext.EnvironmentName);
		ApplicationListItemEnvelope installedApplication = GetInstalledApplicationOrIgnore(listResult.Result);

		// Act
		ApplicationInfoActResult infoResult = await ActInfoAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			arrangeContext.EnvironmentName,
			id: installedApplication.Id,
			code: null);

		// Assert
		infoResult.CallResult.IsError.Should().NotBeTrue(
			because: $"a valid get-app-info request should return structured metadata instead of a transport-level error. Actual result: {DescribeCallResult(infoResult.CallResult)}");
		infoResult.Result.Success.Should().BeTrue(
			because: "get-app-info should succeed for a discovered installed application");
		infoResult.Result.ApplicationId.Should().Be(installedApplication.Id,
			because: "get-app-info should resolve the same installed application selected from list-apps");
		infoResult.Result.ApplicationCode.Should().Be(installedApplication.Code,
			because: "get-app-info should preserve the installed application code from list-apps");
		infoResult.Result.ApplicationName.Should().Be(installedApplication.Name,
			because: "get-app-info should preserve the installed application name from list-apps");
		infoResult.Result.PackageName.Should().NotBeNullOrWhiteSpace(
			because: "get-app-info should expose the owning package name in the structured metadata envelope");
		infoResult.Result.Pages.Should().NotBeNull(
			because: "get-app-info should include page summaries in the structured metadata envelope even when the list is empty");
		infoResult.Result.Entities.Should().NotBeNull(
			because: "get-app-info should include entity summaries in the structured metadata envelope even when the list is empty");
		infoResult.Result.Error.Should().BeNullOrWhiteSpace(
			because: "successful get-app-info calls should not include an error payload");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes list-apps, and verifies that the structured response remains valid whether the environment has zero installed apps or many.")]
	[AllureFeature(ListToolName)]
	[AllureTag(ListToolName)]
	[AllureName("Application list returns a valid structured contract for empty and non-empty environments")]
	[AllureDescription("Uses the real clio MCP server to call list-apps for the configured sandbox environment and verifies that the response succeeds, always includes the applications collection, and exposes non-empty id, name, and code fields for every returned item.")]
	public async Task ApplicationGetList_Should_Return_A_Valid_Structured_Contract_For_Empty_And_NonEmpty_Environments() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		TestConfiguration.EnsureSandboxIsConfigured(settings);
		await using ApplicationArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(2));

		// Act
		ApplicationListActResult listResult = await ActListAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			arrangeContext.EnvironmentName);

		// Assert
		listResult.CallResult.IsError.Should().NotBeTrue(
			because: $"a valid list-apps request should return a structured MCP payload instead of a transport-level error. Actual result: {DescribeCallResult(listResult.CallResult)}");
		listResult.Result.Success.Should().BeTrue(
			because: "list-apps should succeed whether the target environment currently has zero installed apps or many");
		listResult.Result.Applications.Should().NotBeNull(
			because: "list-apps should always include the applications collection so MCP clients can handle empty and populated environments uniformly");
		listResult.Result.Applications.Should().OnlyContain(application =>
				!string.IsNullOrWhiteSpace(application.Id)
				&& !string.IsNullOrWhiteSpace(application.Name)
				&& !string.IsNullOrWhiteSpace(application.Code),
			because: "every returned application item should expose the selectors that MCP clients need for follow-up targeting");
		listResult.Result.Error.Should().BeNullOrWhiteSpace(
			because: "successful list-apps calls should not include an error payload");
	}

	[Test]
	[Description("Starts the real clio MCP server, discovers an installed application through list-apps, and verifies that its id can be reused with get-app-info.")]
	[AllureFeature(ListToolName)]
	[AllureFeature(InfoToolName)]
	[AllureTag(ListToolName)]
	[AllureTag(InfoToolName)]
	[AllureName("Application list returns ids usable by get-app-info when applications exist")]
	[AllureDescription("Uses the real clio MCP server to discover an installed application via list-apps, ignores when none exist, and otherwise verifies that the returned application id is accepted by get-app-info and resolves the same application.")]
	public async Task ApplicationGetList_Should_Return_Ids_Usable_By_GetAppInfo_When_Applications_Exist() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		TestConfiguration.EnsureSandboxIsConfigured(settings);
		await using ApplicationArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(2));
		ApplicationListActResult listResult = await ActListAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			arrangeContext.EnvironmentName);
		ApplicationListItemEnvelope installedApplication = GetInstalledApplicationOrIgnore(listResult.Result);

		// Act
		ApplicationInfoActResult infoResult = await ActInfoAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			arrangeContext.EnvironmentName,
			id: installedApplication.Id,
			code: null);

		// Assert
		infoResult.Result.Success.Should().BeTrue(
			because: "a discovered list-apps identifier should be reusable as a valid get-app-info selector");
		infoResult.Result.ApplicationId.Should().Be(installedApplication.Id,
			because: "get-app-info should resolve the same installed application id that list-apps returned");
		infoResult.Result.ApplicationCode.Should().Be(installedApplication.Code,
			because: "get-app-info should resolve the same installed application code that list-apps returned");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes get-app-info without identifiers, and verifies that a structured error envelope explains the exactly-one rule.")]
	[AllureFeature(InfoToolName)]
	[AllureTag(InfoToolName)]
	[AllureName("Application get info rejects missing identifiers")]
	[AllureDescription("Uses the real clio MCP server to call get-app-info without id or code and verifies that the tool returns a structured error envelope with clear exactly-one validation guidance.")]
	public async Task ApplicationGetInfo_Should_Reject_Missing_Identifiers() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		TestConfiguration.EnsureSandboxIsConfigured(settings);
		await using ApplicationArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(2));

		// Act
		ApplicationContextResponseEnvelope result = await ActInfoFailureAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			arrangeContext.EnvironmentName,
			id: null,
			code: null);

		// Assert
		result.Success.Should().BeFalse(
			because: "get-app-info should return a structured error envelope when neither identifier is provided");
		result.Error.Should().MatchRegex("(?is)(exactly one|id or code)",
			because: "the failure should explain the exact-one identifier rule with the canonical selector names");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes get-app-info with both identifiers, and verifies that a structured error envelope explains the exactly-one rule.")]
	[AllureFeature(InfoToolName)]
	[AllureTag(InfoToolName)]
	[AllureName("Application get info rejects both identifiers")]
	[AllureDescription("Uses the real clio MCP server to call get-app-info with both id and code and verifies that the tool returns a structured error envelope with clear exactly-one validation guidance.")]
	public async Task ApplicationGetInfo_Should_Reject_Both_Identifiers() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		TestConfiguration.EnsureSandboxIsConfigured(settings);
		await using ApplicationArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(2));

		// Act
		ApplicationContextResponseEnvelope result = await ActInfoFailureAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			arrangeContext.EnvironmentName,
			id: "11111111-1111-1111-1111-111111111111",
			code: "UsrAnyApp");

		// Assert
		result.Success.Should().BeFalse(
			because: "get-app-info should return a structured error envelope when both identifiers are provided");
		result.Error.Should().MatchRegex("(?is)(exactly one|id or code)",
			because: "the failure should explain the exact-one identifier rule with the canonical selector names");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes get-app-info with a bad application code, and verifies that a structured error envelope reports the lookup failure.")]
	[AllureFeature(InfoToolName)]
	[AllureTag(InfoToolName)]
	[AllureName("Application get info reports unknown application failures")]
	[AllureDescription("Uses the real clio MCP server to call get-app-info with an unknown application code and verifies that the tool returns a structured error envelope mentioning that the application was not found.")]
	public async Task ApplicationGetInfo_Should_Report_Unknown_Application_Failure() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		TestConfiguration.EnsureSandboxIsConfigured(settings);
		await using ApplicationArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(2));
		string invalidAppCode = $"missing-app-{Guid.NewGuid():N}";

		// Act
		ApplicationContextResponseEnvelope result = await ActInfoFailureAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			arrangeContext.EnvironmentName,
			id: null,
			code: invalidAppCode);

		// Assert
		result.Success.Should().BeFalse(
			because: "get-app-info should return a structured error envelope when the requested installed application does not exist");
		result.Error.Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidAppCode)}|application.*not.*found|not found)",
			because: "the failure should tell a human that the requested application could not be resolved");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes create-app for a configured sandbox environment, and verifies that the created application is returned in the structured metadata envelope.")]
	[AllureFeature(CreateToolName)]
	[AllureTag(CreateToolName)]
	[AllureName("Application create returns structured metadata")]
	[AllureDescription("Uses the real clio MCP server to call create-app for a configured sandbox environment and verifies that the response contains the same structured metadata shape as get-app-info.")]
	public async Task ApplicationCreate_Should_Return_Structured_Metadata() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E:AllowDestructiveMcpTests=true to run create-app end-to-end tests.");
		}

		TestConfiguration.EnsureSandboxIsConfigured(settings);
		await using ApplicationArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(10));
		string suffix = Guid.NewGuid().ToString("N")[..8];
		string applicationCode = $"UsrCodex{suffix}";
		string applicationName = $"Codex E2E {suffix}";

		if (string.IsNullOrWhiteSpace(settings.Sandbox.ApplicationTemplateCode) ||
			string.IsNullOrWhiteSpace(settings.Sandbox.ApplicationIconId) ||
			string.IsNullOrWhiteSpace(settings.Sandbox.ApplicationIconBackground)) {
			Assert.Ignore("Configure McpE2E:Sandbox:ApplicationTemplateCode, ApplicationIconId, and ApplicationIconBackground to run create-app success E2E.");
		}

		// Act
		ApplicationInfoActResult actResult = await ActCreateAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			arrangeContext.EnvironmentName,
			applicationName,
			applicationCode,
			description: null,
			settings.Sandbox.ApplicationTemplateCode!,
			settings.Sandbox.ApplicationIconId!,
			settings.Sandbox.ApplicationIconBackground!,
			optionalTemplateDataJson: null);

		// Assert
		actResult.CallResult.IsError.Should().NotBeTrue(
			because: $"a valid create-app request should return structured application metadata. Actual result: {DescribeCallResult(actResult.CallResult)}");
		actResult.Result.Success.Should().BeTrue(
			because: "successful create calls should return the core-style success envelope");
		actResult.Result.PackageUId.Should().NotBeNullOrWhiteSpace(
			because: "successful create-app calls should return the created application's primary package identifier");
		actResult.Result.PackageName.Should().NotBeNullOrWhiteSpace(
			because: "successful create-app calls should return the created application's primary package name");
		actResult.Result.CanonicalMainEntityName.Should().Be(applicationCode,
			because: "create-app should surface the canonical main entity explicitly for MCP clients");
		actResult.Result.ApplicationCode.Should().Be(applicationCode,
			because: "create-app should return the created installed application code in the same envelope shape as get-app-info");
		actResult.Result.ApplicationName.Should().Be(applicationName,
			because: "create-app should return the created installed application display name");
		actResult.Result.ApplicationId.Should().NotBeNullOrWhiteSpace(
			because: "create-app should return the created installed application identifier");
		actResult.Result.Pages.Should().NotBeNull(
			because: "create-app should return the primary-package page summaries together with entity context");
		actResult.Result.DataForge.Should().NotBeNull(
			because: "create-app should return Data Forge diagnostics together with the created application metadata");
		actResult.Result.DataForge!.Used.Should().BeTrue(
			because: "create-app should always report that the internal Data Forge enrichment stage ran");
		actResult.Result.DataForge.Coverage.Should().NotBeNull(
			because: "create-app should expose Data Forge coverage flags even when the enrichment stage is degraded");
		ApplicationEntityEnvelope? canonicalMainEntity = actResult.Result.Entities?
			.FirstOrDefault(entity => string.Equals(entity.Name, applicationCode, StringComparison.OrdinalIgnoreCase));
		canonicalMainEntity.Should().NotBeNull(
			because: "successful create-app calls should include the canonical main entity payload");
		canonicalMainEntity!.Caption.Should().Be(applicationName,
			because: "the canonical main entity caption should reflect the requested application name instead of the generic template fallback");
		actResult.Result.Error.Should().BeNullOrWhiteSpace(
			because: "successful create calls should not include an error payload");
	}

	[Test]
	[Description("Creates an application, mutates the canonical main entity through sync-schemas, and verifies get-app-info still returns the application display name instead of Base object.")]
	[AllureFeature(CreateToolName)]
	[AllureFeature(SchemaSyncToolName)]
	[AllureFeature(InfoToolName)]
	[AllureTag(CreateToolName)]
	[AllureTag(SchemaSyncToolName)]
	[AllureTag(InfoToolName)]
	[AllureName("Application get info keeps canonical main entity caption after sync-schemas")]
	[AllureDescription("Uses the real clio MCP server to create an application, applies a minimal sync-schemas update-entity mutation to the canonical main entity, then verifies get-app-info still returns the installed application display name instead of the generic Base object fallback.")]
	public async Task ApplicationGetInfo_Should_Keep_Canonical_Main_Entity_Caption_After_SchemaSync() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E:AllowDestructiveMcpTests=true to run application/sync-schemas regression E2E tests.");
		}

		TestConfiguration.EnsureSandboxIsConfigured(settings);
		await using ApplicationArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(10));
		string suffix = Guid.NewGuid().ToString("N")[..8];
		string applicationCode = $"UsrCodex{suffix}";
		string applicationName = $"Codex E2E {suffix}";
		string addedColumnName = $"UsrStatus{suffix[..4]}";

		if (string.IsNullOrWhiteSpace(settings.Sandbox.ApplicationTemplateCode) ||
			string.IsNullOrWhiteSpace(settings.Sandbox.ApplicationIconId) ||
			string.IsNullOrWhiteSpace(settings.Sandbox.ApplicationIconBackground)) {
			Assert.Ignore("Configure McpE2E:Sandbox:ApplicationTemplateCode, ApplicationIconId, and ApplicationIconBackground to run application/sync-schemas regression E2E.");
		}

		ApplicationInfoActResult createResult = await ActCreateAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			arrangeContext.EnvironmentName,
			applicationName,
			applicationCode,
			description: null,
			settings.Sandbox.ApplicationTemplateCode!,
			settings.Sandbox.ApplicationIconId!,
			settings.Sandbox.ApplicationIconBackground!,
			optionalTemplateDataJson: null);

		// Act
		CallToolResult schemaSyncCallResult = await CallSchemaSyncUpdateCanonicalMainEntityAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			arrangeContext.EnvironmentName,
			createResult.Result.PackageName!,
			applicationCode,
			addedColumnName);
		JsonElement schemaSyncResponse = ExtractSchemaSyncResponse(schemaSyncCallResult);
		ApplicationInfoActResult infoResult = await ActInfoAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			arrangeContext.EnvironmentName,
			id: null,
			code: applicationCode);
		ApplicationEntityEnvelope? canonicalMainEntity = infoResult.Result.Entities?
			.FirstOrDefault(entity => string.Equals(entity.Name, applicationCode, StringComparison.OrdinalIgnoreCase));

		// Assert
		createResult.Result.Success.Should().BeTrue(
			because: "the regression scenario requires a successfully created application before sync-schemas mutates the canonical main entity");
		schemaSyncCallResult.IsError.Should().NotBeTrue(
			because: $"sync-schemas should return a structured payload for the canonical-main-entity regression scenario. Actual result: {DescribeCallResult(schemaSyncCallResult)}");
		schemaSyncResponse.GetProperty("success").GetBoolean().Should().BeTrue(
			because: "the minimal sync-schemas update should succeed before get-app-info readback is validated");
		canonicalMainEntity.Should().NotBeNull(
			because: "get-app-info should continue to return the canonical main entity after sync-schemas mutations");
		canonicalMainEntity!.Caption.Should().Be(applicationName,
			because: "the canonical main entity should keep the installed application display name instead of degrading to Base object after sync-schemas");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes create-app with an invalid environment, and verifies that a structured error envelope reports the failure.")]
	[AllureFeature(CreateToolName)]
	[AllureTag(CreateToolName)]
	[AllureName("Application create reports invalid environment failures")]
	[AllureDescription("Uses the real clio MCP server to call create-app with an unknown environment name and verifies that the tool returns a structured error envelope mentioning the missing environment.")]
	public async Task ApplicationCreate_Should_Report_Invalid_Environment_Failure() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		await using ApplicationArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-app-create-env-{Guid.NewGuid():N}";

		// Act
		ApplicationContextResponseEnvelope result = await ActCreateFailureAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			invalidEnvironmentName,
			"Codex Invalid Env",
			$"UsrInvalid{Guid.NewGuid():N}"[..14],
			description: null,
			"AppFreedomUI",
			"11111111-1111-1111-1111-111111111111",
			"#FFFFFF",
			optionalTemplateDataJson: null);

		// Assert
		result.Success.Should().BeFalse(
			because: "create-app should return a structured error envelope when the requested environment is not registered");
		result.Error.Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found)",
			because: "the failure should tell a human that the requested environment is not registered");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes create-app with forbidden localization-map fields, and verifies that validation rejects the request before any create side effect is attempted.")]
	[AllureFeature(CreateToolName)]
	[AllureTag(CreateToolName)]
	[AllureName("Application create rejects localization map fields")]
	[AllureDescription("Uses the real clio MCP server to call create-app with forbidden localization-map fields and verifies that the tool returns a structured validation error instead of attempting app creation.")]
	public async Task ApplicationCreate_Should_Reject_Localization_Map_Fields() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		await using ApplicationArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));
		string suffix = Guid.NewGuid().ToString("N")[..8];
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);
		tools.Select(tool => tool.Name).Should().Contain(CreateToolName,
			because: "the create-app MCP tool must be advertised before the end-to-end call can be executed");

		Dictionary<string, object?> args = BuildCreateArgs(
			environmentName: arrangeContext.EnvironmentName,
			name: $"Codex Invalid Localization {suffix}",
			code: $"UsrBadLoc{suffix}",
			description: null,
			templateCode: "AppFreedomUI",
			iconId: "11111111-1111-1111-1111-111111111111",
			iconBackground: "#FFFFFF",
			optionalTemplateDataJson: null);
		args["title-localizations"] = new Dictionary<string, object?> {
			["en-US"] = "Forbidden caption"
		};

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			CreateToolName,
			new Dictionary<string, object?> {
				["args"] = args
			},
			arrangeContext.CancellationTokenSource.Token);
		callResult.IsError.Should().NotBeTrue(
			because: $"structured create-app validation failures should be returned in the payload instead of as MCP invocation errors. Actual result: {DescribeCallResult(callResult)}");
		ApplicationContextResponseEnvelope result = ApplicationResultParser.ExtractInfo(callResult);

		// Assert
		result.Success.Should().BeFalse(
			because: "create-app should reject forbidden localization maps before attempting a create");
		result.Error.Should().MatchRegex("(?is)(scalar-only|locali[sz]ation|title-localizations)",
			because: "the failure should explain that localization maps are forbidden on create-app");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes create-app with an invalid template payload, and verifies that a structured error envelope reports the failure.")]
	[AllureFeature(CreateToolName)]
	[AllureTag(CreateToolName)]
	[AllureName("Application create reports invalid template failures")]
	[AllureDescription("Uses the real clio MCP server to call create-app with a clearly invalid template code and verifies that the create request returns a structured error envelope with readable diagnostics.")]
	public async Task ApplicationCreate_Should_Report_Invalid_Template_Failure() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E:AllowDestructiveMcpTests=true to run create-app invalid template end-to-end tests.");
		}

		TestConfiguration.EnsureSandboxIsConfigured(settings);
		await using ApplicationArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));
		string invalidTemplateCode = $"MissingTemplate{Guid.NewGuid():N}";

		// Act
		ApplicationContextResponseEnvelope result = await ActCreateFailureAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			arrangeContext.EnvironmentName,
			"Codex Invalid Template",
			$"UsrBad{Guid.NewGuid():N}"[..14],
			description: null,
			invalidTemplateCode,
			settings.Sandbox.ApplicationIconId ?? "11111111-1111-1111-1111-111111111111",
			settings.Sandbox.ApplicationIconBackground ?? "#FFFFFF",
			optionalTemplateDataJson: null);

		// Assert
		result.Success.Should().BeFalse(
			because: "create-app should return a structured error envelope when the supplied template code is invalid");
		result.Error.Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidTemplateCode)}|template|dependency|failed)",
			because: "the failure should include readable diagnostics for the invalid create payload");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes create-app with malformed optional-template-data-json, and verifies that a structured error envelope is returned before any create side effect is attempted.")]
	[AllureFeature(CreateToolName)]
	[AllureTag(CreateToolName)]
	[AllureName("Application create rejects malformed template JSON")]
	[AllureDescription("Uses the real clio MCP server to call create-app with malformed optional-template-data-json and verifies that the tool returns readable validation diagnostics in a structured error envelope.")]
	public async Task ApplicationCreate_Should_Reject_Malformed_OptionalTemplateDataJson() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		TestConfiguration.EnsureSandboxIsConfigured(settings);
		await using ApplicationArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));
		string suffix = Guid.NewGuid().ToString("N")[..8];

		// Act
		ApplicationContextResponseEnvelope result = await ActCreateFailureAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			arrangeContext.EnvironmentName,
			name: $"Codex Invalid Json {suffix}",
			code: $"UsrBadJson{suffix}",
			description: null,
			templateCode: settings.Sandbox.ApplicationTemplateCode ?? "AppFreedomUI",
			iconId: settings.Sandbox.ApplicationIconId ?? "11111111-1111-1111-1111-111111111111",
			iconBackground: settings.Sandbox.ApplicationIconBackground ?? "#FFFFFF",
			optionalTemplateDataJson: "{not-json");

		// Assert
		result.Success.Should().BeFalse(
			because: "create-app should return a structured error envelope when optional-template-data-json is malformed");
		result.Error.Should().MatchRegex(
			"(?is)(optional-template-data-json|invalid)",
			because: "the failure should explain that the JSON payload is invalid");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes create-app with icon-id set to auto, and verifies that automatic icon resolution still produces structured metadata.")]
	[AllureFeature(CreateToolName)]
	[AllureTag(CreateToolName)]
	[AllureName("Application create resolves auto icon ids")]
	[AllureDescription("Uses the real clio MCP server to call create-app with icon-id='auto' and verifies that automatic icon resolution still produces structured application metadata.")]
	public async Task ApplicationCreate_Should_Support_Auto_IconId() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E:AllowDestructiveMcpTests=true to run create-app auto icon end-to-end tests.");
		}

		TestConfiguration.EnsureSandboxIsConfigured(settings);
		await using ApplicationArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(10));
		string suffix = Guid.NewGuid().ToString("N")[..8];

		// Act
		ApplicationInfoActResult actResult = await ActCreateAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			arrangeContext.EnvironmentName,
			name: $"Codex Auto Icon {suffix}",
			code: $"UsrAutoIcon{suffix}",
			description: null,
			templateCode: settings.Sandbox.ApplicationTemplateCode ?? "AppFreedomUI",
			iconId: "auto",
			iconBackground: settings.Sandbox.ApplicationIconBackground ?? "#FFFFFF",
			optionalTemplateDataJson: null);

		// Assert
		actResult.CallResult.IsError.Should().NotBeTrue(
			because: $"icon-id='auto' should resolve a usable icon before CreateApp is called. Actual result: {DescribeCallResult(actResult.CallResult)}");
		actResult.Result.Success.Should().BeTrue(
			because: "auto icon resolution should still return the normal success envelope");
		actResult.Result.PackageName.Should().NotBeNullOrWhiteSpace(
			because: "the auto-icon create flow should still return structured application metadata");
	}

	[Test]
	[Description("Advertises delete-app in the MCP tool list so callers can discover the uninstall tool.")]
	[AllureFeature(DeleteToolName)]
	[AllureTag(DeleteToolName)]
	[AllureName("Application delete tool is advertised by the MCP server")]
	[AllureDescription("Starts the real clio MCP server and verifies that delete-app appears in the advertised tool manifest.")]
	public async Task ApplicationDelete_Should_Be_Listed_By_Mcp_Server() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);

		// Act
		IList<McpClientTool> tools = await session.ListToolsAsync(cancellationTokenSource.Token);
		IEnumerable<string> toolNames = tools.Select(tool => tool.Name);

		// Assert
		toolNames.Should().Contain(DeleteToolName,
			because: "delete-app must be advertised so MCP callers can discover the uninstall tool");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes delete-app with an unknown environment, and verifies that the failure remains human-readable.")]
	[AllureFeature(DeleteToolName)]
	[AllureTag(DeleteToolName)]
	[AllureName("Application delete reports invalid environment failures")]
	[AllureDescription("Uses the real clio MCP server to call delete-app with an unknown environment and verifies that the tool returns a structured readable error envelope.")]
	public async Task ApplicationDelete_Should_Report_Invalid_Environment_Failure() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		string invalidEnvironmentName = $"missing-delete-app-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await session.CallToolAsync(
			DeleteToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["app-name"] = "11111111-1111-1111-1111-111111111111"
				}
			},
			cancellationTokenSource.Token);
		ApplicationDeleteResponseEnvelope response = ApplicationResultParser.ExtractDelete(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: $"structured delete-app failures should be returned in the payload instead of as MCP invocation errors. Actual result: {DescribeCallResult(callResult)}");
		response.Success.Should().BeFalse(
			because: "delete-app should fail when the requested environment does not exist");
		response.Error.Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found)",
			because: "the failure should explain that the requested environment is missing");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes delete-app without environment-name or explicit connection args, and verifies that the failure explains the missing target.")]
	[AllureFeature(DeleteToolName)]
	[AllureTag(DeleteToolName)]
	[AllureName("Application delete rejects missing execution target")]
	[AllureDescription("Uses the real clio MCP server to call delete-app without environment-name or URI credentials and verifies that the tool returns readable resolver diagnostics.")]
	public async Task ApplicationDelete_Should_Reject_Missing_Execution_Target() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);

		// Act
		CallToolResult callResult = await session.CallToolAsync(
			DeleteToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["app-name"] = "11111111-1111-1111-1111-111111111111"
				}
			},
			cancellationTokenSource.Token);
		ApplicationDeleteResponseEnvelope response = ApplicationResultParser.ExtractDelete(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: $"structured delete-app failures should be returned in the payload instead of as MCP invocation errors. Actual result: {DescribeCallResult(callResult)}");
		response.Success.Should().BeFalse(
			because: "delete-app should fail when the call does not identify any execution target");
		response.Error.Should().Contain("Either a configured environment name or an explicit URI is required",
			because: "the failure should explain that the MCP request needs an environment-name or explicit URI");
	}

	private static async Task<ApplicationArrangeContext> ArrangeAsync(McpE2ESettings settings, TimeSpan timeout) {
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		CancellationTokenSource cancellationTokenSource = new(timeout);
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ApplicationArrangeContext(environmentName, session, cancellationTokenSource);
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
			$"application MCP E2E requires a reachable environment. Configured sandbox environment '{configuredEnvironmentName}' was not reachable, and fallback environment '{fallbackEnvironmentName}' was also unavailable.");
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

	private static async Task<ApplicationListActResult> ActListAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		string environmentName) {
		CallToolResult callResult = await CallListAsync(session, cancellationToken, environmentName);
		ApplicationListResponseEnvelope result = ApplicationResultParser.ExtractList(callResult);
		return new ApplicationListActResult(callResult, result);
	}

	private static async Task<ApplicationInfoActResult> ActInfoAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		string environmentName,
		string? id,
		string? code) {
		CallToolResult callResult = await CallInfoAsync(session, cancellationToken, environmentName, id, code);
		ApplicationContextResponseEnvelope result;
		try {
			result = ApplicationResultParser.ExtractInfo(callResult);
		}
		catch (InvalidOperationException exception) {
			throw new InvalidOperationException(
				$"{exception.Message} Raw result: {DescribeCallResult(callResult)}",
				exception);
		}
		return new ApplicationInfoActResult(callResult, result);
	}

	private static async Task<ApplicationContextResponseEnvelope> ActInfoFailureAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		string environmentName,
		string? id,
		string? code) {
		CallToolResult callResult = await CallInfoAsync(session, cancellationToken, environmentName, id, code);
		callResult.IsError.Should().NotBeTrue(
			because: $"structured get-app-info failures should be returned in the payload instead of as MCP invocation errors. Actual result: {DescribeCallResult(callResult)}");
		return ApplicationResultParser.ExtractInfo(callResult);
	}

	[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters",
		Justification = "E2E helper parameters intentionally mirror the create-app MCP request shape.")]
	private static async Task<ApplicationInfoActResult> ActCreateAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		string environmentName,
		string? name,
		string? code,
		string? description,
		string templateCode,
		string? iconId,
		string? iconBackground,
		string? optionalTemplateDataJson) {
		CallToolResult callResult = await CallCreateAsync(
			session,
			cancellationToken,
			environmentName,
			name,
			code,
			description,
			templateCode,
			iconId,
			iconBackground,
			optionalTemplateDataJson);
		ApplicationContextResponseEnvelope result;
		try {
			result = ApplicationResultParser.ExtractInfo(callResult);
		}
		catch (InvalidOperationException exception) {
			throw new InvalidOperationException(
				$"{exception.Message} Raw result: {DescribeCallResult(callResult)}",
				exception);
		}
		return new ApplicationInfoActResult(callResult, result);
	}

	[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters",
		Justification = "E2E helper parameters intentionally mirror the create-app MCP request shape.")]
	private static async Task<ApplicationContextResponseEnvelope> ActCreateFailureAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		string environmentName,
		string? name,
		string? code,
		string? description,
		string templateCode,
		string? iconId,
		string? iconBackground,
		string? optionalTemplateDataJson) {
		CallToolResult callResult = await CallCreateAsync(
			session,
			cancellationToken,
			environmentName,
			name,
			code,
			description,
			templateCode,
			iconId,
			iconBackground,
			optionalTemplateDataJson);
		callResult.IsError.Should().NotBeTrue(
			because: $"structured create-app failures should be returned in the payload instead of as MCP invocation errors. Actual result: {DescribeCallResult(callResult)}");
		return ApplicationResultParser.ExtractInfo(callResult);
	}

	private static async Task<CallToolResult> CallListAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		string environmentName) {
		IList<McpClientTool> tools = await session.ListToolsAsync(cancellationToken);
		tools.Select(tool => tool.Name).Should().Contain(ListToolName,
			because: "the list-apps MCP tool must be advertised before the end-to-end call can be executed");

		return await session.CallToolAsync(
			ListToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName
				}
			},
			cancellationToken);
	}

	private static async Task<CallToolResult> CallInfoAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		string environmentName,
		string? id,
		string? code) {
		IList<McpClientTool> tools = await session.ListToolsAsync(cancellationToken);
		tools.Select(tool => tool.Name).Should().Contain(InfoToolName,
			because: "the get-app-info MCP tool must be advertised before the end-to-end call can be executed");

		Dictionary<string, object?> args = new() {
			["environment-name"] = environmentName
		};
		if (!string.IsNullOrWhiteSpace(id)) {
			args["id"] = id;
		}

		if (!string.IsNullOrWhiteSpace(code)) {
			args["code"] = code;
		}

		return await session.CallToolAsync(
			InfoToolName,
			new Dictionary<string, object?> {
				["args"] = args
			},
			cancellationToken);
	}

	[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters",
		Justification = "E2E helper parameters intentionally mirror the create-app MCP request shape.")]
	private static async Task<CallToolResult> CallCreateAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		string environmentName,
		string? name,
		string? code,
		string? description,
		string templateCode,
		string? iconId,
		string? iconBackground,
		string? optionalTemplateDataJson) {
		IList<McpClientTool> tools = await session.ListToolsAsync(cancellationToken);
		tools.Select(tool => tool.Name).Should().Contain(CreateToolName,
			because: "the create-app MCP tool must be advertised before the end-to-end call can be executed");

		return await session.CallToolAsync(
			CreateToolName,
			new Dictionary<string, object?> {
				["args"] = BuildCreateArgs(
					environmentName,
					name,
					code,
					description,
					templateCode,
					iconId,
					iconBackground,
					optionalTemplateDataJson)
			},
			cancellationToken);
	}

	private static async Task<CallToolResult> CallSchemaSyncUpdateCanonicalMainEntityAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		string environmentName,
		string packageName,
		string schemaName,
		string addedColumnName) {
		IList<McpClientTool> tools = await session.ListToolsAsync(cancellationToken);
		tools.Select(tool => tool.Name).Should().Contain(SchemaSyncToolName,
			because: "the sync-schemas MCP tool must be advertised before the canonical-main-entity regression scenario can be executed");

		return await session.CallToolAsync(
			SchemaSyncToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["package-name"] = packageName,
					["operations"] = new object?[] {
						new Dictionary<string, object?> {
							["type"] = "update-entity",
							["schema-name"] = schemaName,
							["update-operations"] = new object?[] {
								new Dictionary<string, object?> {
									["action"] = "modify",
									["column-name"] = "UsrName",
									["title-localizations"] = BuildLocalizations("Title")
								},
								new Dictionary<string, object?> {
									["action"] = "add",
									["column-name"] = addedColumnName,
									["type"] = "Text",
									["title-localizations"] = BuildLocalizations("Status")
								}
							}
						}
					}
				}
			},
			cancellationToken);
	}

	[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters",
		Justification = "E2E helper parameters intentionally mirror the create-app MCP request shape.")]
	private static Dictionary<string, object?> BuildCreateArgs(
		string environmentName,
		string? name,
		string? code,
		string? description,
		string templateCode,
		string? iconId,
		string? iconBackground,
		string? optionalTemplateDataJson) {
		Dictionary<string, object?> args = new() {
			["environment-name"] = environmentName,
			["name"] = name,
			["code"] = code,
			["template-code"] = templateCode,
			["icon-background"] = iconBackground
		};
		if (!string.IsNullOrWhiteSpace(description)) {
			args["description"] = description;
		}

		if (!string.IsNullOrWhiteSpace(iconId)) {
			args["icon-id"] = iconId;
		}

		if (!string.IsNullOrWhiteSpace(optionalTemplateDataJson)) {
			args["optional-template-data-json"] = optionalTemplateDataJson;
		}

		return args;
	}

	private static Dictionary<string, object?> BuildLocalizations(string value) {
		return new Dictionary<string, object?> {
			["en-US"] = value
		};
	}

	private static JsonElement ExtractSchemaSyncResponse(CallToolResult callResult) {
		if (TryExtractSchemaSyncResponse(callResult.StructuredContent, out JsonElement structuredPayload)) {
			return structuredPayload;
		}

		if (TryExtractSchemaSyncResponse(callResult.Content, out JsonElement contentPayload)) {
			return contentPayload;
		}

		throw new InvalidOperationException("Could not parse SchemaSyncResponse MCP result.");
	}

	private static bool TryExtractSchemaSyncResponse(object? value, out JsonElement payload) {
		if (value is null) {
			payload = default;
			return false;
		}

		JsonElement element = JsonSerializer.SerializeToElement(value);
		if (TryExtractSchemaSyncPayloadElement(element, out payload)) {
			return true;
		}

		if (element.ValueKind == JsonValueKind.Array) {
			foreach (JsonElement item in element.EnumerateArray()) {
				if (TryGetTextPayload(item, out string? textPayload) &&
					!string.IsNullOrWhiteSpace(textPayload) &&
					TryParseJson(textPayload, out JsonElement parsedPayload) &&
					TryExtractSchemaSyncPayloadElement(parsedPayload, out payload)) {
					return true;
				}
			}
		}

		if (element.ValueKind == JsonValueKind.String) {
			string? textPayload = element.GetString();
			if (!string.IsNullOrWhiteSpace(textPayload) &&
				TryParseJson(textPayload, out JsonElement parsedPayload) &&
				TryExtractSchemaSyncPayloadElement(parsedPayload, out payload)) {
				return true;
			}
		}

		payload = default;
		return false;
	}

	private static bool TryExtractSchemaSyncPayloadElement(JsonElement element, out JsonElement payload) {
		if (element.ValueKind == JsonValueKind.Object &&
			element.TryGetProperty("success", out JsonElement successElement) &&
			successElement.ValueKind is JsonValueKind.True or JsonValueKind.False &&
			element.TryGetProperty("results", out JsonElement resultsElement) &&
			resultsElement.ValueKind == JsonValueKind.Array) {
			payload = element;
			return true;
		}

		payload = default;
		return false;
	}

	private static bool TryGetTextPayload(JsonElement element, out string? textPayload) {
		textPayload = null;
		if (element.ValueKind != JsonValueKind.Object) {
			return false;
		}

		if (element.TryGetProperty("text", out JsonElement textElement) &&
			textElement.ValueKind == JsonValueKind.String) {
			textPayload = textElement.GetString();
			return true;
		}

		return false;
	}

	private static bool TryParseJson(string value, out JsonElement element) {
		try {
			element = JsonSerializer.SerializeToElement(JsonSerializer.Deserialize<JsonElement>(value));
			return true;
		}
		catch (JsonException) {
			element = default;
			return false;
		}
	}

	private static string DescribeCallResult(CallToolResult callResult) {
		return JsonSerializer.Serialize(new {
			callResult.IsError,
			StructuredContent = callResult.StructuredContent,
			Content = callResult.Content
		});
	}

	private static ApplicationListItemEnvelope GetInstalledApplicationOrIgnore(ApplicationListResponseEnvelope listResponse) {
		ApplicationListItemEnvelope? installedApplication = listResponse.Applications?.FirstOrDefault();
		if (installedApplication is not null) {
			return installedApplication;
		}

		Assert.Ignore("TODO: add predefined installed application data to the E2E environment.");
		return null!;
	}

	private sealed record ApplicationArrangeContext(
		string EnvironmentName,
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}

	private sealed record ApplicationListActResult(
		CallToolResult CallResult,
		ApplicationListResponseEnvelope Result);

	private sealed record ApplicationInfoActResult(
		CallToolResult CallResult,
		ApplicationContextResponseEnvelope Result);
}
