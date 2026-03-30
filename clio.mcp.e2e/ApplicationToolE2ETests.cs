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

	[Test]
	[Description("Starts the real clio MCP server, invokes application-get-list for the configured sandbox environment, and verifies that a structured installed-application list envelope is returned.")]
	[AllureFeature(ListToolName)]
	[AllureTag(ListToolName)]
	[AllureName("Application get list returns structured installed applications")]
	[AllureDescription("Uses the real clio MCP server to call application-get-list for the configured sandbox environment and verifies that the returned structured application list envelope contains usable id, name, and code fields.")]
	public async Task ApplicationGetList_Should_Return_Structured_Applications() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		TestConfiguration.EnsureSandboxIsConfigured(settings);
		await using ApplicationArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(2));

		// Act
		ApplicationListActResult actResult = await ActListAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			arrangeContext.EnvironmentName);

		// Assert
		actResult.CallResult.IsError.Should().NotBeTrue(
			because: $"a valid application-get-list request should return a structured MCP payload. Actual result: {DescribeCallResult(actResult.CallResult)}");
		actResult.Result.Success.Should().BeTrue(
			because: "successful list calls should return the core-style success envelope");
		actResult.Result.Error.Should().BeNullOrWhiteSpace(
			because: "successful list calls should not report an error payload");
		actResult.Result.Applications.Should().NotBeEmpty(
			because: "the sandbox environment should expose at least one installed application");
		actResult.Result.Applications.Should().Contain(application =>
				!string.IsNullOrWhiteSpace(application.Id)
				&& !string.IsNullOrWhiteSpace(application.Name)
				&& !string.IsNullOrWhiteSpace(application.Code),
			because: "the MCP tool should return usable application identifiers and names");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes application-get-info for an installed application from the sandbox environment, and verifies that a structured package/entity success envelope is returned.")]
	[AllureFeature(InfoToolName)]
	[AllureTag(InfoToolName)]
	[AllureName("Application get info returns structured package and entity metadata")]
	[AllureDescription("Uses the real clio MCP server to call application-get-list, reuses the first returned application code, and verifies that application-get-info returns a package identifier plus runtime entity metadata in the core response envelope.")]
	public async Task ApplicationGetInfo_Should_Return_Structured_Metadata() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		TestConfiguration.EnsureSandboxIsConfigured(settings);
		await using ApplicationArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(2));
		ApplicationListActResult listResult = await ActListAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			arrangeContext.EnvironmentName);
		string appCode = listResult.Result.Applications![0].Code;

		// Act
		ApplicationInfoActResult actResult = await ActInfoAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			arrangeContext.EnvironmentName,
			appId: null,
			appCode: appCode);

		// Assert
		actResult.CallResult.IsError.Should().NotBeTrue(
			because: $"a valid application-get-info request should return structured package and entity metadata. Actual result: {DescribeCallResult(actResult.CallResult)}");
		actResult.Result.Success.Should().BeTrue(
			because: "successful info calls should return the core-style success envelope");
		actResult.Result.ApplicationCode.Should().Be(appCode,
			because: "application-get-info should echo the installed application code that was used to resolve the target app");
		actResult.Result.ApplicationId.Should().NotBeNullOrWhiteSpace(
			because: "application-get-info should return the installed application identifier for follow-up targeting");
		actResult.Result.ApplicationName.Should().NotBeNullOrWhiteSpace(
			because: "application-get-info should return the installed application display name");
		actResult.Result.ApplicationVersion.Should().NotBeNullOrWhiteSpace(
			because: "application-get-info should return the installed application version");
		actResult.Result.PackageUId.Should().NotBeNullOrWhiteSpace(
			because: "the application info response should include the primary package identifier");
		actResult.Result.PackageName.Should().NotBeNullOrWhiteSpace(
			because: "the application info response should include the primary package name");
		actResult.Result.Entities.Should().NotBeNull(
			because: "the application info response should include the application entity collection");
		actResult.Result.Error.Should().BeNullOrWhiteSpace(
			because: "successful info calls should not include an error payload");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes application-get-info without identifiers, and verifies that a structured error envelope explains the exactly-one rule.")]
	[AllureFeature(InfoToolName)]
	[AllureTag(InfoToolName)]
	[AllureName("Application get info rejects missing identifiers")]
	[AllureDescription("Uses the real clio MCP server to call application-get-info without app-id or app-code and verifies that the tool returns a structured error envelope with clear exactly-one validation guidance.")]
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
			appId: null,
			appCode: null);

		// Assert
		result.Success.Should().BeFalse(
			because: "application-get-info should return a structured error envelope when neither identifier is provided");
		result.Error.Should().MatchRegex("(?is)(exactly one|app-id|app-code)",
			because: "the failure should explain the exact-one identifier rule");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes application-get-info with both identifiers, and verifies that a structured error envelope explains the exactly-one rule.")]
	[AllureFeature(InfoToolName)]
	[AllureTag(InfoToolName)]
	[AllureName("Application get info rejects both identifiers")]
	[AllureDescription("Uses the real clio MCP server to call application-get-info with both app-id and app-code and verifies that the tool returns a structured error envelope with clear exactly-one validation guidance.")]
	public async Task ApplicationGetInfo_Should_Reject_Both_Identifiers() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		TestConfiguration.EnsureSandboxIsConfigured(settings);
		await using ApplicationArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(2));
		ApplicationListActResult listResult = await ActListAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			arrangeContext.EnvironmentName);
		ApplicationListItemEnvelope application = listResult.Result.Applications![0];

		// Act
		ApplicationContextResponseEnvelope result = await ActInfoFailureAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			arrangeContext.EnvironmentName,
			appId: application.Id,
			appCode: application.Code);

		// Assert
		result.Success.Should().BeFalse(
			because: "application-get-info should return a structured error envelope when both identifiers are provided");
		result.Error.Should().MatchRegex("(?is)(exactly one|app-id|app-code)",
			because: "the failure should explain the exact-one identifier rule");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes application-get-info with a bad application code, and verifies that a structured error envelope reports the lookup failure.")]
	[AllureFeature(InfoToolName)]
	[AllureTag(InfoToolName)]
	[AllureName("Application get info reports unknown application failures")]
	[AllureDescription("Uses the real clio MCP server to call application-get-info with an unknown application code and verifies that the tool returns a structured error envelope mentioning that the application was not found.")]
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
			appId: null,
			appCode: invalidAppCode);

		// Assert
		result.Success.Should().BeFalse(
			because: "application-get-info should return a structured error envelope when the requested installed application does not exist");
		result.Error.Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidAppCode)}|application.*not.*found|not found)",
			because: "the failure should tell a human that the requested application could not be resolved");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes application-create for a configured sandbox environment, and verifies that the created application is returned in the structured metadata envelope.")]
	[AllureFeature(CreateToolName)]
	[AllureTag(CreateToolName)]
	[AllureName("Application create returns structured metadata")]
	[AllureDescription("Uses the real clio MCP server to call application-create for a configured sandbox environment and verifies that the response contains the same structured metadata shape as application-get-info.")]
	public async Task ApplicationCreate_Should_Return_Structured_Metadata() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E:AllowDestructiveMcpTests=true to run application-create end-to-end tests.");
		}

		TestConfiguration.EnsureSandboxIsConfigured(settings);
		await using ApplicationArrangeContext arrangeContext = await ArrangeAsync(settings, TimeSpan.FromMinutes(10));
		string suffix = Guid.NewGuid().ToString("N")[..8];
		string applicationCode = $"UsrCodex{suffix}";
		string applicationName = $"Codex E2E {suffix}";

		if (string.IsNullOrWhiteSpace(settings.Sandbox.ApplicationTemplateCode) ||
			string.IsNullOrWhiteSpace(settings.Sandbox.ApplicationIconId) ||
			string.IsNullOrWhiteSpace(settings.Sandbox.ApplicationIconBackground)) {
			Assert.Ignore("Configure McpE2E:Sandbox:ApplicationTemplateCode, ApplicationIconId, and ApplicationIconBackground to run application-create success E2E.");
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
			because: $"a valid application-create request should return structured application metadata. Actual result: {DescribeCallResult(actResult.CallResult)}");
		actResult.Result.Success.Should().BeTrue(
			because: "successful create calls should return the core-style success envelope");
		actResult.Result.PackageUId.Should().NotBeNullOrWhiteSpace(
			because: "successful application-create calls should return the created application's primary package identifier");
		actResult.Result.PackageName.Should().NotBeNullOrWhiteSpace(
			because: "successful application-create calls should return the created application's primary package name");
		actResult.Result.CanonicalMainEntityName.Should().Be(applicationCode,
			because: "application-create should surface the canonical main entity explicitly for MCP clients");
		actResult.Result.ApplicationCode.Should().Be(applicationCode,
			because: "application-create should return the created installed application code in the same envelope shape as application-get-info");
		actResult.Result.ApplicationName.Should().Be(applicationName,
			because: "application-create should return the created installed application display name");
		actResult.Result.ApplicationId.Should().NotBeNullOrWhiteSpace(
			because: "application-create should return the created installed application identifier");
		ApplicationEntityEnvelope? canonicalMainEntity = actResult.Result.Entities?
			.FirstOrDefault(entity => string.Equals(entity.Name, applicationCode, StringComparison.OrdinalIgnoreCase));
		canonicalMainEntity.Should().NotBeNull(
			because: "successful application-create calls should include the canonical main entity payload");
		canonicalMainEntity!.Caption.Should().Be(applicationName,
			because: "the canonical main entity caption should reflect the requested application name instead of the generic template fallback");
		actResult.Result.Error.Should().BeNullOrWhiteSpace(
			because: "successful create calls should not include an error payload");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes application-create with an invalid environment, and verifies that a structured error envelope reports the failure.")]
	[AllureFeature(CreateToolName)]
	[AllureTag(CreateToolName)]
	[AllureName("Application create reports invalid environment failures")]
	[AllureDescription("Uses the real clio MCP server to call application-create with an unknown environment name and verifies that the tool returns a structured error envelope mentioning the missing environment.")]
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
			because: "application-create should return a structured error envelope when the requested environment is not registered");
		result.Error.Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found)",
			because: "the failure should tell a human that the requested environment is not registered");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes application-create with an invalid template payload, and verifies that a structured error envelope reports the failure.")]
	[AllureFeature(CreateToolName)]
	[AllureTag(CreateToolName)]
	[AllureName("Application create reports invalid template failures")]
	[AllureDescription("Uses the real clio MCP server to call application-create with a clearly invalid template code and verifies that the create request returns a structured error envelope with readable diagnostics.")]
	public async Task ApplicationCreate_Should_Report_Invalid_Template_Failure() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E:AllowDestructiveMcpTests=true to run application-create invalid template end-to-end tests.");
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
			because: "application-create should return a structured error envelope when the supplied template code is invalid");
		result.Error.Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidTemplateCode)}|template|dependency|failed)",
			because: "the failure should include readable diagnostics for the invalid create payload");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes application-create with malformed optional-template-data-json, and verifies that a structured error envelope is returned before any create side effect is attempted.")]
	[AllureFeature(CreateToolName)]
	[AllureTag(CreateToolName)]
	[AllureName("Application create rejects malformed template JSON")]
	[AllureDescription("Uses the real clio MCP server to call application-create with malformed optional-template-data-json and verifies that the tool returns readable validation diagnostics in a structured error envelope.")]
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
			because: "application-create should return a structured error envelope when optional-template-data-json is malformed");
		result.Error.Should().MatchRegex(
			"(?is)(optional-template-data-json|invalid)",
			because: "the failure should explain that the JSON payload is invalid");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes application-create with icon-id set to auto, and verifies that automatic icon resolution still produces structured metadata.")]
	[AllureFeature(CreateToolName)]
	[AllureTag(CreateToolName)]
	[AllureName("Application create resolves auto icon ids")]
	[AllureDescription("Uses the real clio MCP server to call application-create with icon-id='auto' and verifies that automatic icon resolution still produces structured application metadata.")]
	public async Task ApplicationCreate_Should_Support_Auto_IconId() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E:AllowDestructiveMcpTests=true to run application-create auto icon end-to-end tests.");
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
	[Description("Advertises application-delete in the MCP tool list so callers can discover the uninstall tool.")]
	[AllureFeature(DeleteToolName)]
	[AllureTag(DeleteToolName)]
	[AllureName("Application delete tool is advertised by the MCP server")]
	[AllureDescription("Starts the real clio MCP server and verifies that application-delete appears in the advertised tool manifest.")]
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
			because: "application-delete must be advertised so MCP callers can discover the uninstall tool");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes application-delete with an unknown environment, and verifies that the failure remains human-readable.")]
	[AllureFeature(DeleteToolName)]
	[AllureTag(DeleteToolName)]
	[AllureName("Application delete reports invalid environment failures")]
	[AllureDescription("Uses the real clio MCP server to call application-delete with an unknown environment and verifies that the tool returns a structured readable error envelope.")]
	public async Task ApplicationDelete_Should_Report_Invalid_Environment_Failure() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		string invalidEnvironmentName = $"missing-application-delete-env-{Guid.NewGuid():N}";

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
			because: $"structured application-delete failures should be returned in the payload instead of as MCP invocation errors. Actual result: {DescribeCallResult(callResult)}");
		response.Success.Should().BeFalse(
			because: "application-delete should fail when the requested environment does not exist");
		response.Error.Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found)",
			because: "the failure should explain that the requested environment is missing");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes application-delete without environment-name or explicit connection args, and verifies that the failure explains the missing target.")]
	[AllureFeature(DeleteToolName)]
	[AllureTag(DeleteToolName)]
	[AllureName("Application delete rejects missing execution target")]
	[AllureDescription("Uses the real clio MCP server to call application-delete without environment-name or URI credentials and verifies that the tool returns readable resolver diagnostics.")]
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
			because: $"structured application-delete failures should be returned in the payload instead of as MCP invocation errors. Actual result: {DescribeCallResult(callResult)}");
		response.Success.Should().BeFalse(
			because: "application-delete should fail when the call does not identify any execution target");
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
		ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(
			settings,
			["ping-app", "-e", environmentName]);
		return result.ExitCode == 0;
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
		string? appId,
		string? appCode) {
		CallToolResult callResult = await CallInfoAsync(session, cancellationToken, environmentName, appId, appCode);
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
		string? appId,
		string? appCode) {
		CallToolResult callResult = await CallInfoAsync(session, cancellationToken, environmentName, appId, appCode);
		callResult.IsError.Should().NotBeTrue(
			because: $"structured application-get-info failures should be returned in the payload instead of as MCP invocation errors. Actual result: {DescribeCallResult(callResult)}");
		return ApplicationResultParser.ExtractInfo(callResult);
	}

	[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters",
		Justification = "E2E helper parameters intentionally mirror the application-create MCP request shape.")]
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
		Justification = "E2E helper parameters intentionally mirror the application-create MCP request shape.")]
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
			because: $"structured application-create failures should be returned in the payload instead of as MCP invocation errors. Actual result: {DescribeCallResult(callResult)}");
		return ApplicationResultParser.ExtractInfo(callResult);
	}

	private static async Task<CallToolResult> CallListAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		string environmentName) {
		IList<McpClientTool> tools = await session.ListToolsAsync(cancellationToken);
		tools.Select(tool => tool.Name).Should().Contain(ListToolName,
			because: "the application-get-list MCP tool must be advertised before the end-to-end call can be executed");

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
		string? appId,
		string? appCode) {
		IList<McpClientTool> tools = await session.ListToolsAsync(cancellationToken);
		tools.Select(tool => tool.Name).Should().Contain(InfoToolName,
			because: "the application-get-info MCP tool must be advertised before the end-to-end call can be executed");

		Dictionary<string, object?> args = new() {
			["environment-name"] = environmentName
		};
		if (!string.IsNullOrWhiteSpace(appId)) {
			args["app-id"] = appId;
		}

		if (!string.IsNullOrWhiteSpace(appCode)) {
			args["app-code"] = appCode;
		}

		return await session.CallToolAsync(
			InfoToolName,
			new Dictionary<string, object?> {
				["args"] = args
			},
			cancellationToken);
	}

	[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters",
		Justification = "E2E helper parameters intentionally mirror the application-create MCP request shape.")]
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
			because: "the application-create MCP tool must be advertised before the end-to-end call can be executed");

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

	[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters",
		Justification = "E2E helper parameters intentionally mirror the application-create MCP request shape.")]
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

	private static string DescribeCallResult(CallToolResult callResult) {
		return JsonSerializer.Serialize(new {
			callResult.IsError,
			StructuredContent = callResult.StructuredContent,
			Content = callResult.Content
		});
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
