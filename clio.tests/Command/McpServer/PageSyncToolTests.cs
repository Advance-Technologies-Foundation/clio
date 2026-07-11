using System;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NSubstitute.Core;
using Clio.UserEnvironment;
using McpServerLib = ModelContextProtocol.Server;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class PageSyncToolTests {

	[Test]
	[Category("Unit")]
	[Description("Advertises a stable MCP tool name for sync-pages")]
	public void PageSyncTool_Should_Advertise_Stable_Tool_Name() {
		// Arrange

		// Act
		string toolName = PageSyncTool.ToolName;

		// Assert
		toolName.Should().Be("sync-pages",
			because: "the sync-pages MCP tool identifier must remain stable for callers");
	}

	[Test]
	[Category("Unit")]
	[Description("Marks sync-pages as destructive and not read-only")]
	public void PageSyncTool_Should_Advertise_Safety_Metadata() {
		// Arrange
		var method = typeof(PageSyncTool).GetMethod(nameof(PageSyncTool.SyncPages))!;
		var attribute = method
			.GetCustomAttributes(typeof(ModelContextProtocol.Server.McpServerToolAttribute), false)
			.Cast<ModelContextProtocol.Server.McpServerToolAttribute>()
			.Single();

		// Act
		bool readOnly = attribute.ReadOnly;
		bool destructive = attribute.Destructive;

		// Assert
		readOnly.Should().BeFalse(
			because: "sync-pages mutates remote page schemas and should not be marked read-only");
		destructive.Should().BeTrue(
			because: "sync-pages modifies remote page schemas and should warn clients");
	}

	[Test]
	[Category("Unit")]
	[Description("Successfully updates a single page when Creatio responds with success")]
	public async Task SyncPages_Should_Succeed_For_Valid_Page() {
		// Arrange
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog, Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(new MockFileSystem()));
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrTodo_FormPage", ValidPageBody)],
			Validate: false,
			SkipSampling: true);

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Success.Should().BeTrue(
			because: "the page update should succeed when Creatio returns success");
		response.Pages.Should().HaveCount(1,
			because: "one page was requested");
		response.Pages[0].SchemaName.Should().Be("UsrTodo_FormPage",
			because: "the result should reference the requested page");
		response.Pages[0].BodyLength.Should().BeGreaterThan(0,
			because: "the saved page should report a non-zero body length");
	}

	[Test]
	[Category("Unit")]
	[Description("Scopes the registry-driven chart-widget batch validation to the platform version resolved from the target environment")]
	public async Task SyncPages_ShouldScopeChartCatalogToResolvedVersion_WhenEnvironmentResolvesVersion() {
		// Arrange
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		// The target environment resolves to platform version 8.2.1 through the SAME
		// IToolCommandResolver the tool uses for command resolution (Pattern-A) — no
		// direct ISettingsRepository dependency anymore.
		commandResolver.Resolve<EnvironmentSettings>(Arg.Any<EnvironmentOptions>()).Returns(new EnvironmentSettings());
		IPlatformVersionResolver resolver = Substitute.For<IPlatformVersionResolver>();
		resolver.ResolveAsync(Arg.Any<CancellationToken>())
			.Returns(new PlatformVersionResolution("8.2.1", VersionResolutionSource.Environment));
		IPlatformVersionResolverFactory resolverFactory = Substitute.For<IPlatformVersionResolverFactory>();
		resolverFactory.Create(Arg.Any<EnvironmentSettings>()).Returns(resolver);
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), Substitute.For<IMobileComponentInfoCatalog>(),
			webCatalog, Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(new MockFileSystem()),
			resolverFactory);
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrTodo_FormPage", ValidPageBody)],
			Validate: true,
			SkipSampling: true);

		// Act
		await tool.SyncPages(args, null);

		// Assert — the merged chart type definitions are resolved once per batch on the async entry,
		// so the catalog must be loaded against the version resolved from the environment, not 'latest'.
		string requestedVersion = (string)webCatalog.ReceivedCalls()
			.Single(c => c.GetMethodInfo().Name == nameof(IComponentInfoCatalog.LoadAsync))
			.GetArguments()[0];
		requestedVersion.Should().Be("8.2.1",
			because: "sync-pages must scope its chart-widget batch validation to the version resolved from the target environment");
	}

	[Test]
	[Category("Unit")]
	[Description("Updates multiple pages in a single call")]
	public async Task SyncPages_Should_Process_Multiple_Pages() {
		// Arrange
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog, Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(new MockFileSystem()));
		PageSyncArgs args = new(
			"dev",
			[
				new PageSyncPageInput("UsrTodo_FormPage", ValidPageBody),
				new PageSyncPageInput("UsrTodo_ListPage", ValidPageBody)
			],
			Validate: false,
			SkipSampling: true);

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Success.Should().BeTrue(
			because: "both pages should be updated successfully");
		response.Pages.Should().HaveCount(2,
			because: "two pages were requested");
		response.Pages[0].SchemaName.Should().Be("UsrTodo_FormPage",
			because: "pages should be processed in order");
		response.Pages[1].SchemaName.Should().Be("UsrTodo_ListPage",
			because: "the second page should also be processed");
	}

	[Test]
	[Category("Unit")]
	[Description("Continues processing remaining pages when one page fails")]
	public async Task SyncPages_Should_Continue_On_Failure() {
		// Arrange
		PageUpdateCommand updateCommand = CreatePageUpdateCommandWithFailureForSchema("UsrBroken_FormPage");
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog, Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(new MockFileSystem()));
		PageSyncArgs args = new(
			"dev",
			[
				new PageSyncPageInput("UsrBroken_FormPage", ValidPageBody),
				new PageSyncPageInput("UsrWorking_ListPage", ValidPageBody)
			],
			Validate: false,
			SkipSampling: true);

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Success.Should().BeFalse(
			because: "one page failed so overall success should be false");
		response.Pages.Should().HaveCount(2,
			because: "both pages should be processed even when the first fails");
		response.Pages[0].Success.Should().BeFalse(
			because: "the first page should report failure");
		response.Pages[1].Success.Should().BeTrue(
			because: "the second page should succeed independently");
	}

	[Test]
	[Category("Unit")]
	[Description("Client-side validation rejects a page body whose schema markers are missing — the syntax check passes (the body parses as JS) so the test exercises the markers validator on its own, not the upstream Acornima gate")]
	public async Task SyncPages_Should_Reject_Invalid_Page_Body_When_Validation_Enabled() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog, Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(new MockFileSystem()));
		// Body parses as valid JavaScript so the upstream PageBodySyntaxValidator
		// gate (ENG-89796) passes; the markers validator then catches the missing
		// SCHEMA_* envelope and reports the failure.
		string bodyWithMissingMarkers = "define('BadPage', [], function() { return {}; });";
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrBad_FormPage", bodyWithMissingMarkers)],
			Validate: true,
			SkipSampling: true);

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Success.Should().BeFalse(
			because: "client-side validation should reject a body with missing markers");
		response.Pages[0].Success.Should().BeFalse(
			because: "the page should fail validation");
		response.Pages[0].Validation.Should().NotBeNull(
			because: "validation results should be included when validation is enabled");
		response.Pages[0].Validation!.MarkersOk.Should().BeFalse(
			because: "the body is missing required schema markers");
		response.Pages[0].Error.Should().Contain("validation failed",
			because: "the error should describe the validation failure");
	}

	[Test]
	[Category("Unit")]
	[Description("A body with a JavaScript syntax error fails fast BEFORE the markers/sampling chain AND no remote save call is made — proves the deterministic gate short-circuits before TryUpdatePage by asserting ReceivedCalls on the IApplicationClient substitute is empty")]
	public async Task SyncPages_Should_FailFast_WhenBodyHasJavaScriptSyntaxError() {
		// Arrange — wire a real PageUpdateCommand so the IApplicationClient
		// substitute behind it can confirm no remote save call was made.
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommandWithClient(out IApplicationClient applicationClient);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(updateCommand);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog, Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(new MockFileSystem()));
		// `define('BadPage', {})}` has a stray closing brace at the end → SyntaxError.
		// The PageBodySyntaxValidator must surface this before the markers validator
		// runs and no SaveSchema request should ever leave the process.
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrBad_FormPage", "define('BadPage', {})}")],
			Validate: false,
			SkipSampling: true);

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Success.Should().BeFalse(
			because: "a syntactically broken body must NEVER be persisted — this is the deterministic floor of the syntax gate");
		response.Pages[0].Success.Should().BeFalse(
			because: "the per-page result must mirror the overall failure");
		response.Pages[0].Error.Should().Contain("JavaScript syntax error",
			because: "the failure message must name the actual class of problem so the operator does not chase a phantom marker/sampling issue");
		response.Pages[0].Error.Should().Contain("NOT sent to Creatio",
			because: "the operator must know the broken body did not reach the server (and therefore did not corrupt a saved page) without having to read the code");
		applicationClient.ReceivedCalls().Should().BeEmpty(
			because: "the syntax-gate short-circuit must run BEFORE PageUpdateCommand.TryUpdatePage — no SaveSchema (or any other) request must have been issued for this batch");
	}

	[Test]
	[Category("Unit")]
	[Description("Batch with two pages sharing the same schema-name: a broken first body must not corrupt the second body's pre-pass results — the gates are keyed by input index, not by SchemaName, so last-write-wins on a Dictionary cannot blow away the AST/findings of the first page.")]
	public async Task SyncPages_Should_Not_CrossContaminate_When_Batch_Contains_Duplicate_SchemaName() {
		// Arrange
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommandWithClient(out IApplicationClient applicationClient);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(updateCommand);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog, Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(new MockFileSystem()));
		PageSyncArgs args = new(
			"dev",
			[
				new PageSyncPageInput("UsrPage_FormPage", "define('Dup', {})}"),
				new PageSyncPageInput("UsrPage_FormPage", ValidPageBody)
			],
			Validate: false,
			SkipSampling: true);

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Pages.Should().HaveCount(2,
			because: "both inputs must produce an independent per-page result, even when they share a schema-name");
		response.Pages[0].Success.Should().BeFalse(
			because: "the first entry's body is syntactically broken and must surface its own deterministic failure regardless of what the duplicate-named sibling looks like");
		response.Pages[0].Error.Should().Contain("JavaScript syntax error",
			because: "index-keyed pre-pass guarantees the first entry's diagnosis is not overwritten by the second entry's AST or vice versa");
		response.Pages[1].Success.Should().BeTrue(
			because: "the second entry's valid body must still proceed to save, independent of the first entry's failure");
		int saveSchemaCalls = applicationClient.ReceivedCalls()
			.Count(c => c.GetArguments().FirstOrDefault() is string url && url.Contains("SaveSchema"));
		saveSchemaCalls.Should().Be(1,
			because: "exactly one SaveSchema round-trip must happen — for the valid second entry only");
	}

	[Test]
	[Category("Unit")]
	[Description("AC4: when the body passes the deterministic syntax + lint pre-pass and the caller did not opt out via skip-sampling, the LLM semantic-review (sampling) MUST be invoked with the schema name, body, and resources — proves that the new gates did not displace sampling on the canonical happy path.")]
	public async Task SyncPages_Should_Invoke_Sampling_For_Valid_Body() {
		// Arrange
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommandWithClient(out IApplicationClient applicationClient);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(updateCommand);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		// Wire a recording sampling service. Returning `null` keeps the
		// downstream `samplingReview is { Ok: false ... }` check inert so we
		// observe invocation without forcing a sampling-block outcome.
		IPageBodySamplingService samplingService = Substitute.For<IPageBodySamplingService>();
		samplingService
			.TrySamplingReviewAsync(Arg.Any<McpServerLib.McpServer>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
			.Returns((PageSamplingReview)null);
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog, samplingService, new PageBaselineGuard(new MockFileSystem()));
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrValid_FormPage", ValidPageBody, Resources: "{\"caption\":\"Hello\"}")],
			Validate: true,
			SkipSampling: false);

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Success.Should().BeTrue(
			because: "the body passes every deterministic gate so the sync must complete successfully when no sampling issues are surfaced");
		await samplingService.Received(1).TrySamplingReviewAsync(
			Arg.Any<McpServerLib.McpServer>(),
			Arg.Is<string>(name => name == "UsrValid_FormPage"),
			Arg.Is<string>(body => body == ValidPageBody),
			Arg.Is<string?>(resources => resources == "{\"caption\":\"Hello\"}"),
			Arg.Any<CancellationToken>());
	}

	[Test]
	[Category("Unit")]
	[Description("AC4 negative path: when the body fails the deterministic syntax pre-pass, sampling is NOT invoked — proves the gates short-circuit BEFORE LLM tokens are spent on a doomed body.")]
	public async Task SyncPages_Should_NotInvoke_Sampling_When_Syntax_Fails() {
		// Arrange
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommandWithClient(out _);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(updateCommand);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		IPageBodySamplingService samplingService = Substitute.For<IPageBodySamplingService>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog, samplingService, new PageBaselineGuard(new MockFileSystem()));
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrBad_FormPage", "define('BadPage', {})}")],
			Validate: true,
			SkipSampling: false);

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Pages[0].Success.Should().BeFalse(
			because: "the syntax gate must reject the body");
		await samplingService.DidNotReceive().TrySamplingReviewAsync(
			Arg.Any<McpServerLib.McpServer>(),
			Arg.Any<string>(),
			Arg.Any<string>(),
			Arg.Any<string?>(),
			Arg.Any<CancellationToken>());
	}

	[Test]
	[Category("Unit")]
	[Description("Mixed batch: a syntactically broken page is rejected and a valid page is saved in the same call — exactly one save round-trip happens for the valid page, not one per page or none at all. Pins per-page fail-fast semantics that no other test currently covers.")]
	public async Task SyncPages_Should_Save_Only_Valid_Page_When_Batch_Contains_One_Broken_And_One_Valid() {
		// Arrange
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommandWithClient(out IApplicationClient applicationClient);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(updateCommand);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog, Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(new MockFileSystem()));
		PageSyncArgs args = new(
			"dev",
			[
				new PageSyncPageInput("UsrBad_FormPage", "define('BadPage', {})}"),
				new PageSyncPageInput("UsrGood_FormPage", ValidPageBody)
			],
			Validate: false,
			SkipSampling: true);

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Pages.Should().HaveCount(2,
			because: "both inputs must produce a per-page result, with order preserved");
		response.Pages[0].Success.Should().BeFalse(
			because: "the broken page must fail fast on the syntax gate");
		response.Pages[0].Error.Should().Contain("JavaScript syntax error",
			because: "the broken page's failure must name the deterministic problem class");
		response.Pages[1].Success.Should().BeTrue(
			because: "fail-fast is per-page; one broken sibling must not block the valid one from being saved");
		int saveSchemaCalls = applicationClient.ReceivedCalls()
			.Count(c => c.GetArguments().FirstOrDefault() is string url && url.Contains("SaveSchema"));
		saveSchemaCalls.Should().Be(1,
			because: "exactly one SaveSchema round-trip must happen — for the valid page only; the broken page must not be sent");
	}

	[Test]
	[Category("Unit")]
	[Description("Skips client-side validation when validate is false")]
	public async Task SyncPages_Should_Skip_Validation_When_Disabled() {
		// Arrange
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog, Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(new MockFileSystem()));
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrPage", ValidPageBody)],
			Validate: false,
			SkipSampling: true);

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Success.Should().BeTrue(
			because: "validation is disabled so the page should be updated directly");
		response.Pages[0].Validation.Should().BeNull(
			because: "no validation results should be present when validation is skipped");
	}

	[Test]
	[Category("Unit")]
	[Description("Allows JavaScript handlers when validation is enabled")]
	public async Task SyncPages_Should_Accept_JavaScript_Handler_Content_When_Validation_Is_Enabled() {
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog, Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(new MockFileSystem()));
		string bodyWithHandler = ValidPageBody.Replace(
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/",
			"/**SCHEMA_HANDLERS*/[{ request: \"crt.HandleViewModelInitRequest\", handler: async (request, next) => { await next?.handle(request); } }]/**SCHEMA_HANDLERS*/");
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrTodo_FormPage", bodyWithHandler)],
			Validate: true,
			SkipSampling: true);

		PageSyncResponse response = await tool.SyncPages(args, null);

		response.Success.Should().BeTrue(
			because: "handler markers may contain JavaScript and should not fail content validation");
		response.Pages[0].Success.Should().BeTrue(
			because: "the page body remains valid when handlers contain executable JavaScript");
		response.Pages[0].Validation.Should().NotBeNull(
			because: "validation details should still be returned when validation is enabled");
		response.Pages[0].Validation!.ContentOk.Should().BeTrue(
			because: "content validation should ignore handler JavaScript blocks");
	}

	[Test]
	[Category("Unit")]
	[Description("Allows JavaScript converters and validators when validation is enabled")]
	public void SyncPages_Should_Accept_JavaScript_Converters_And_Validators_When_Validation_Is_Enabled() {
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog, Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(new MockFileSystem()));
		string bodyWithConverterAndValidator = ValidPageBody
			.Replace(
				"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/",
				"/**SCHEMA_CONVERTERS*/{ \"usr.ToUpperCase\": function(value) { return value?.toUpperCase() ?? \"\"; } }/**SCHEMA_CONVERTERS*/")
			.Replace(
				"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/",
				"/**SCHEMA_VALIDATORS*/{ \"usr.ValidateFieldValue\": { \"validator\": function(config) { return function(control) { return control.value !== config.invalidName ? null : { \"usr.ValidateFieldValue\": { message: config.message } }; }; }, \"params\": [{ \"name\": \"invalidName\" }, { \"name\": \"message\" }], \"async\": false } }/**SCHEMA_VALIDATORS*/");
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrTodo_FormPage", bodyWithConverterAndValidator)],
			Validate: true);

		PageSyncResponse response = tool.SyncPages(args, null).Result;

		response.Success.Should().BeTrue(
			because: "sync-pages should not reject function-based converter and validator sections as JSON errors");
		response.Pages[0].Success.Should().BeTrue(
			because: "the page body remains valid when converter and validator markers contain JavaScript functions");
		response.Pages[0].Validation.Should().NotBeNull(
			because: "validation details should still be present when validation is enabled");
		response.Pages[0].Validation!.ContentOk.Should().BeTrue(
			because: "content validation should treat converters and validators as JavaScript object sections");
	}

	[Test]
	[Category("Unit")]
	[Description("Client-side validation rejects a field insert whose binding attribute is not declared in viewModelConfigDiff and whose label resource is neither registered in 'resources' nor auto-provided by a DS-bound attribute. Without rejection, the saved control would have no data source and a blank caption.")]
	public async Task SyncPages_Should_Reject_InsertedFields_Without_Matching_ViewModelOrResource() {
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog, Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(new MockFileSystem()));
		string bodyWithUndeclaredBindings = "define('TestPage', /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
			"function(/**SCHEMA_ARGS*//**SCHEMA_ARGS*/) { return { " +
			"/**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"insert\",\"name\":\"UsrStatus\",\"values\":{\"type\":\"crt.ComboBox\",\"label\":\"$Resources.Strings.PDS_UsrStatus\",\"control\":\"$PDS_UsrStatus\"}}]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			"/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
			"/**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
			"/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
			"/**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
			"/**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrTodo_FormPage", bodyWithUndeclaredBindings)],
			Validate: true,
			SkipSampling: true);

		PageSyncResponse response = await tool.SyncPages(args, null);

		response.Success.Should().BeFalse(
			because: "sync-pages must surface insert operations that would render fields with no data source and a blank caption");
	}

	[Test]
	[Category("Unit")]
	[Description("Client-side validation accepts a merge operation that targets a parent-provided field control without local viewModelConfigDiff declarations — the inserted-field contract applies only to operation:\"insert\", not to operation:\"merge\".")]
	public async Task SyncPages_Should_Accept_MergeAgainstParentProvidedControl() {
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog, Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(new MockFileSystem()));
		string bodyWithParentMerge = "define('TestPage', /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
			"function(/**SCHEMA_ARGS*//**SCHEMA_ARGS*/) { return { " +
			"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"merge\",\"name\":\"UsrStatus\",\"values\":{\"type\":\"crt.ComboBox\",\"label\":\"$Resources.Strings.PDS_UsrStatus\",\"control\":\"$PDS_UsrStatus\"}}]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
			"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
			"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
			"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
			"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrTodo_FormPage", bodyWithParentMerge)],
			Validate: true,
			SkipSampling: true);

		PageSyncResponse response = await tool.SyncPages(args, null);

		response.Success.Should().BeTrue(
			because: "merge operations target existing parent-provided controls whose binding attribute and resource may legitimately live in the parent schema, so the inserted-field contract does NOT apply");
	}

	[Test]
	[Category("Unit")]
	[Description("Client-side validation surfaces warnings for explicit custom field caption resources when the field uses a declared view-model attribute")]
	public async Task SyncPages_Should_Surface_FieldCaptionWarnings_When_ExplicitResources_Are_Provided() {
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog, Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(new MockFileSystem()));
		string bodyWithExplicitFieldCaption = "define('TestPage', /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
			"function(/**SCHEMA_ARGS*//**SCHEMA_ARGS*/) { return { " +
			"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"insert\",\"name\":\"UsrStatus\",\"values\":{\"type\":\"crt.ComboBox\",\"label\":\"#ResourceString(UsrStatus_caption)#\",\"control\":\"$UsrStatus\"}}]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[{\"operation\":\"merge\",\"path\":[],\"values\":{\"attributes\":{\"UsrStatus\":{\"modelConfig\":{\"path\":\"PDS.UsrStatus\"}}}}}]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
			"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
			"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
			"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
			"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrTodo_FormPage", bodyWithExplicitFieldCaption, "{\"UsrStatus_caption\":\"Status\"}")],
			Validate: true,
			SkipSampling: true);

		PageSyncResponse response = await tool.SyncPages(args, null);

		response.Success.Should().BeTrue(
			because: "explicit resources keep the custom field caption pattern non-blocking");
		response.Pages[0].Validation.Should().NotBeNull(
			because: "validation details should still be returned on successful guarded saves");
		response.Pages[0].Validation!.Warnings.Should().ContainSingle(warning => warning.Contains("UsrStatus_caption"),
			because: "sync-pages should surface the softer caption guidance as a warning");
	}

	[Test]
	[Category("Unit")]
	[Description("Passes page resources through sync-pages and returns the registered resource count from update-page.")]
	public async Task SyncPages_Should_Surface_Registered_Resources() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(Arg.Any<string>())
			.Returns(callInfo => "http://test" + callInfo.Arg<string>());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SelectQuery")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new JObject {
				["success"] = true,
				["rows"] = new JArray { new JObject { ["UId"] = "resource-page-uid" } }
			}.ToString());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("GetSchema")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new JObject {
				["success"] = true,
				["schema"] = new JObject {
					["body"] = "original",
					["localizableStrings"] = new JArray()
				}
			}.ToString());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SaveSchema")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new JObject { ["success"] = true }.ToString());
		PageUpdateCommand updateCommand = new(applicationClient, serviceUrlBuilder, Substitute.For<ILogger>(), Substitute.For<IPageBaselineGuard>(), CreateHierarchyClientFor("resource-page-uid"));
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog, Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(new MockFileSystem()));
		string bodyWithResource = "define('TestPage', /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
			"function(/**SCHEMA_ARGS*//**SCHEMA_ARGS*/) { return { " +
			"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[{ values: { caption: \"#ResourceString(UsrTitle)#\" } }]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
			"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/{}/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
			"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/{}/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
			"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
			"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
			"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrTodo_FormPage", bodyWithResource, "{\"UsrTitle\":\"Title\"}")],
			Validate: false,
			SkipSampling: true);

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Success.Should().BeTrue(
			because: "sync-pages should forward resources into update-page and keep the successful response");
		response.Pages[0].ResourcesRegistered.Should().Be(1,
			because: "the sync-pages response should preserve the number of resources registered by update-page");
	}

	[Test]
	[Category("Unit")]
	[Description("Serializes sync-pages request and response resource fields using the documented MCP names.")]
	public void PageSync_Should_Serialize_Resource_Fields() {
		// Arrange
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrTodo_FormPage", ValidPageBody, "{\"UsrTitle\":\"Title\"}")],
			Validate: true,
			Verify: false);
		PageSyncResponse response = new() {
			Success = true,
			Pages = [
				new PageSyncPageResult {
					SchemaName = "UsrTodo_FormPage",
					Success = true,
					ResourcesRegistered = 1,
					Page = new PageMetadataInfo {
						SchemaName = "UsrTodo_FormPage",
						SchemaUId = "test-uid",
						PackageName = "UsrPkg",
						PackageUId = "test-package-uid",
						ParentSchemaName = "BaseModulePage"
					},
					VerifiedBodyFile = "/workspace/.clio-pages/UsrTodo_FormPage/body.js"
				}
			]
		};

		// Act
		string serializedArgs = System.Text.Json.JsonSerializer.Serialize(args);
		string serializedResponse = System.Text.Json.JsonSerializer.Serialize(response);

		// Assert
		serializedArgs.Should().Contain("\"resources\":\"{\\u0022UsrTitle\\u0022:\\u0022Title\\u0022}\"",
			because: "sync-pages should include the optional page resources payload when it is provided");
		serializedResponse.Should().Contain("\"resources-registered\":1",
			because: "sync-pages should serialize the registered-resource count using the documented MCP field name");
		serializedResponse.Should().Contain("\"page\":{",
			because: "sync-pages should serialize read-back page metadata when it is present");
		serializedResponse.Should().Contain("\"verified-body-file\":",
			because: "sync-pages should serialize the verified body file path using the documented MCP field name");
	}

	[Test]
	[Category("Unit")]
	[Description("Verifies page content after save when verify is true")]
	public async Task SyncPages_Should_Verify_After_Save_When_Enabled() {
		// Arrange
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommand();
		PageGetCommand getCommand = CreateSuccessfulPageGetCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		commandResolver.Resolve<PageGetCommand>(Arg.Any<PageGetOptions>())
			.Returns(getCommand);
		MockFileSystem mockFs = new();
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, mockFs, mobileCatalog, webCatalog, Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(mockFs));
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrTodo_FormPage", ValidPageBody)],
			Validate: false,
			Verify: true,
			SkipSampling: true);

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Success.Should().BeTrue(
			because: "both save and verification should succeed");
		response.Pages[0].Success.Should().BeTrue(
			because: "the page was saved and verified successfully");
		response.Pages[0].Page.Should().NotBeNull(
			because: "verify=true should surface page metadata from the read-back response");
		response.Pages[0].Page!.SchemaName.Should().Be("UsrTodo_FormPage",
			because: "the verified page metadata should identify the saved schema");
		response.Pages[0].Page.SchemaUId.Should().Be("test-uid",
			because: "the read-back page metadata should preserve schema identity");
		response.Pages[0].Page.PackageName.Should().Be("UsrPkg",
			because: "the read-back page metadata should preserve package ownership");
		response.Pages[0].Page.ParentSchemaName.Should().Be("BaseModulePage",
			because: "the read-back page metadata should preserve the parent schema");
		response.Pages[0].VerifiedBodyFile.Should().NotBeNullOrWhiteSpace(
			because: "verify=true should write verified body to disk and return the file path");
		response.Pages[0].VerifiedBodyFile.Should().EndWith("body.js",
			because: "the verified body file must be named body.js");
		mockFs.File.ReadAllText(response.Pages[0].VerifiedBodyFile).Should().Be(ValidPageBody,
			because: "the file written to disk must contain the verified body from the read-back get-page");
	}

	[Test]
	[Category("Unit")]
	[Description("Reports error when verification fails after successful save")]
	public async Task SyncPages_Should_Report_Error_When_Verification_Fails() {
		// Arrange
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommand();
		PageGetCommand getCommand = CreateFailingPageGetCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		commandResolver.Resolve<PageGetCommand>(Arg.Any<PageGetOptions>())
			.Returns(getCommand);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog, Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(new MockFileSystem()));
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrTodo_FormPage", ValidPageBody)],
			Validate: false,
			Verify: true,
			SkipSampling: true);

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Success.Should().BeFalse(
			because: "verification failure should result in overall failure");
		response.Pages[0].Error.Should().Contain("verification failed",
			because: "the error should indicate that verification failed after save");
	}

	[Test]
	[Category("Unit")]
	[Description("Propagates the command's insert->merge downgrade warning onto the per-page sync result (locks AppendCommandWarnings).")]
	public async Task SyncPages_Should_Surface_InsertDowngradeWarning_PerPage() {
		// Arrange — the stored schema inserts UsrName; the incoming body downgrades it to a merge.
		PageUpdateCommand updateCommand = CreatePageUpdateCommandWithInsertPriorBody();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog, Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(new MockFileSystem()));
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrTodo_FormPage", MergeUsrNameBody)],
			Validate: false,
			SkipSampling: true);

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Pages[0].Success.Should().BeTrue(
			because: "the downgrade is advisory and must not fail the per-page save");
		response.Pages[0].Validation.Should().NotBeNull(
			because: "AppendCommandWarnings must attach the command warning even when client-side validation is skipped");
		response.Pages[0].Validation!.Warnings.Should().ContainSingle(w => w.Contains("UsrName") && w.Contains("merge"),
			because: "the per-page result must propagate the command's insert->merge downgrade warning");
	}

	// ENG-89796: every page-body fixture must be syntactically valid JavaScript.
	// Earlier fixtures dropped the object-literal property keys (`viewConfigDiff:`,
	// `handlers:`, …) before each SCHEMA_* marker pair because the brace-counter
	// "syntax" check let them through. Acornima parses these fixtures for real
	// now, so the keys are required.
	private const string InsertUsrNamePriorBody = "define('TestPage', /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
		"function(/**SCHEMA_ARGS*//**SCHEMA_ARGS*/) { return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"insert\",\"name\":\"UsrName\",\"values\":{\"type\":\"crt.Input\"}}]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	private const string MergeUsrNameBody = "define('TestPage', /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
		"function(/**SCHEMA_ARGS*//**SCHEMA_ARGS*/) { return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"merge\",\"name\":\"UsrName\",\"values\":{\"label\":\"X\"}}]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	private static PageUpdateCommand CreatePageUpdateCommandWithInsertPriorBody() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(Arg.Any<string>())
			.Returns(callInfo => "http://test" + callInfo.Arg<string>());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SelectQuery")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new JObject {
				["success"] = true,
				["rows"] = new JArray { new JObject { ["UId"] = "test-uid", ["SchemaType"] = 9 } }
			}.ToString());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("GetSchema")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new JObject {
				["success"] = true,
				["schema"] = new JObject { ["body"] = InsertUsrNamePriorBody }
			}.ToString());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SaveSchema")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new JObject { ["success"] = true }.ToString());
		return new PageUpdateCommand(applicationClient, serviceUrlBuilder, Substitute.For<ILogger>(), Substitute.For<IPageBaselineGuard>(), CreateHierarchyClientFor("test-uid"));
	}

	private const string ValidPageBody = "define('TestPage', /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
		"function(/**SCHEMA_ARGS*//**SCHEMA_ARGS*/) { return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/{}/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/{}/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	private static IPageDesignerHierarchyClient CreateHierarchyClientFor(string schemaUId, string packageUId = "test-pkg-uid") {
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId(schemaUId).Returns(packageUId);
		hierarchyClient.GetParentSchemas(schemaUId, packageUId).Returns([
			new PageDesignerHierarchySchema { UId = schemaUId, PackageUId = packageUId }
		]);
		return hierarchyClient;
	}

	private static PageUpdateCommand CreateSuccessfulPageUpdateCommand(int schemaType = 9) =>
		CreateSuccessfulPageUpdateCommandWithClient(out _, schemaType);

	private static PageUpdateCommand CreateSuccessfulPageUpdateCommandWithClient(out IApplicationClient applicationClient, int schemaType = 9) {
		applicationClient = Substitute.For<IApplicationClient>();
		return ConfigureSuccessfulPageUpdateCommand(applicationClient, schemaType);
	}

	private static PageUpdateCommand ConfigureSuccessfulPageUpdateCommand(IApplicationClient applicationClient, int schemaType) {
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(Arg.Any<string>())
			.Returns(callInfo => "http://test" + callInfo.Arg<string>());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SelectQuery")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new JObject {
				["success"] = true,
				["rows"] = new JArray {
					new JObject {
						["UId"] = "test-uid",
						["SchemaType"] = schemaType
					}
				}
			}.ToString());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("GetSchema")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new JObject {
				["success"] = true,
				["schema"] = new JObject { ["body"] = "original" }
			}.ToString());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SaveSchema")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new JObject { ["success"] = true }.ToString());
		return new PageUpdateCommand(applicationClient, serviceUrlBuilder, Substitute.For<ILogger>(), Substitute.For<IPageBaselineGuard>(), CreateHierarchyClientFor("test-uid"));
	}

	private static PageUpdateCommand CreatePageUpdateCommandWithFailureForSchema(string failSchemaName) {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(Arg.Any<string>())
			.Returns(callInfo => "http://test" + callInfo.Arg<string>());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SelectQuery")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(callInfo => {
				string body = callInfo.ArgAt<string>(1);
				bool isFailing = body.Contains(failSchemaName);
				return new JObject {
					["success"] = !isFailing,
					["rows"] = isFailing
						? new JArray()
						: new JArray { new JObject { ["UId"] = "test-uid" } }
				}.ToString();
			});
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("GetSchema")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new JObject {
				["success"] = true,
				["schema"] = new JObject { ["body"] = "original" }
			}.ToString());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SaveSchema")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new JObject { ["success"] = true }.ToString());
		return new PageUpdateCommand(applicationClient, serviceUrlBuilder, Substitute.For<ILogger>(), Substitute.For<IPageBaselineGuard>(), CreateHierarchyClientFor("test-uid"));
	}

	private static PageGetCommand CreateSuccessfulPageGetCommand() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(Arg.Any<string>())
			.Returns(callInfo => "http://test" + callInfo.Arg<string>());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SelectQuery")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new JObject {
				["success"] = true,
				["rows"] = new JArray {
					new JObject {
						["Name"] = "UsrTodo_FormPage",
						["UId"] = "test-uid",
						["PackageUId"] = "test-package-uid",
						["PackageName"] = "UsrPkg",
						["ParentSchemaName"] = "BaseModulePage",
						["SchemaType"] = 9
					}
				}
			}.ToString());
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId("test-uid").Returns("test-package-uid");
		hierarchyClient.GetParentSchemas("test-uid", "test-package-uid")
			.Returns([
				new PageDesignerHierarchySchema {
					UId = "test-uid",
					Name = "UsrTodo_FormPage",
					PackageUId = "test-package-uid",
					PackageName = "UsrPkg",
					SchemaVersion = 1,
					Body = ValidPageBody
				}
			]);
		return new PageGetCommand(
			applicationClient,
			serviceUrlBuilder,
			Substitute.For<ILogger>(),
			hierarchyClient,
			new PageSchemaBodyParser(),
			new PageBundleBuilder(new PageJsonDiffApplier(), new PageJsonPathDiffApplier()),
			CreatePassthroughPageFileWriter());
	}

	private static PageGetCommand CreateFailingPageGetCommand() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(Arg.Any<string>())
			.Returns(callInfo => "http://test" + callInfo.Arg<string>());
		applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new JObject { ["success"] = false }.ToString());
		return new PageGetCommand(
			applicationClient,
			serviceUrlBuilder,
			Substitute.For<ILogger>(),
			Substitute.For<IPageDesignerHierarchyClient>(),
			new PageSchemaBodyParser(),
			new PageBundleBuilder(new PageJsonDiffApplier(), new PageJsonPathDiffApplier()),
			CreatePassthroughPageFileWriter());
	}

	private static IPageFileWriter CreatePassthroughPageFileWriter() {
		IPageFileWriter writer = Substitute.For<IPageFileWriter>();
		writer.WritePageFiles(
				Arg.Any<PageGetResponse>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
			.Returns(callInfo => callInfo.Arg<PageGetResponse>());
		return writer;
	}

	[Test]
	[Category("Unit")]
	[Description("SyncPages succeeds for a mobile page body (plain JSON), skips AMD marker validation, and DOES issue a real SaveSchema round-trip — proves the mobile bypass reaches the persist step instead of silently passing because of weak NOT-CONTAIN assertions.")]
	public async Task SyncPages_Should_Succeed_For_Valid_Mobile_Json_Body() {
		// Arrange
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommandWithClient(out IApplicationClient applicationClient, schemaType: 10);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog, Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(new MockFileSystem()));
		string mobileBody = """
			{
			  "viewConfigDiff": [],
			  "viewModelConfigDiff": [],
			  "modelConfigDiff": []
			}
			""";
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrMobile_FormPage", mobileBody)],
			Validate: true,
			SkipSampling: true);

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Success.Should().BeTrue(
			because: "the mobile body is a valid JSON page; the sync must end in a success state, not just avoid producing certain error strings");
		response.Pages[0].Success.Should().BeTrue(
			because: "the per-page result must mirror the overall success — a NOT-contain assertion alone leaves room for the page to fail for unrelated reasons");
		response.Pages[0].Error.Should().BeNull(
			because: "a fully-successful mobile sync should not surface any per-page error message");
		int saveSchemaCalls = applicationClient.ReceivedCalls()
			.Count(c => c.GetArguments().FirstOrDefault() is string url && url.Contains("SaveSchema"));
		saveSchemaCalls.Should().Be(1,
			because: "the mobile bypass must reach PageUpdateCommand.TryUpdatePage and issue exactly one SaveSchema round-trip — this is the structural proof that the mobile path is not silently short-circuited");
	}

	[Test]
	[Category("Unit")]
	[Description("SyncPages with validate:true rejects a mobile body that contains 'converters' section.")]
	public async Task SyncPages_Should_Reject_Mobile_Body_With_Converters_When_Validate_Is_True() {
		// Arrange
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog, Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(new MockFileSystem()));
		string mobileBodyWithConverters = """
			{
			  "viewConfigDiff": [],
			  "converters": {}
			}
			""";
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput("UsrMobile_FormPage", mobileBodyWithConverters)],
			Validate: true,
			SkipSampling: true);

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Pages[0].Success.Should().BeFalse(
			because: "a mobile body containing 'converters' must be rejected during validation");
		response.Pages[0].Error.Should().Contain("converters",
			because: "the error should describe the disallowed key that caused validation to fail");
	}

	// --- ENG-93347 Story 12: sync-pages passthrough-tool-parity tests ---
	//
	// These four tests pin the specific regression this story fixes: ResolvePlatformVersionAsync used to
	// short-circuit to `null` on a blank environment-name BEFORE ever calling the resolver, so the version
	// probe silently degraded to 'latest' without ever consulting the credential context. The fix routes the
	// probe's settings lookup through IToolCommandResolver.Resolve<EnvironmentSettings> (Pattern-A) instead
	// of a direct ISettingsRepository.GetEnvironment call, which is also what fixes the AC-04 ordering bug
	// (the resolver's passthrough/mixed-input rejection now runs BEFORE any named-tenant settings lookup,
	// because that rejection lives inside the same Resolve<T> call the probe now uses).

	[Test]
	[Category("Unit")]
	[Description("AC-01/AC-02 regression guard: a header-only passthrough call (blank/null environment-name) must REACH the platform-version resolver through IToolCommandResolver.Resolve<EnvironmentSettings> and resolve against the header tenant — not merely return a version. Before this fix, ResolvePlatformVersionAsync returned null on a blank name without ever invoking the resolver; asserting only 'a version came back' would not catch that regression, so this test asserts the resolver call was actually received.")]
	public async Task SyncPages_ShouldReachVersionProbeThroughResolver_WhenEnvironmentNameIsBlank() {
		// Arrange
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		// Simulates ToolCommandResolver.Resolve<T>'s passthrough branch: with no explicit
		// environment-name and an authorized HTTP credential-passthrough header active, the
		// resolver resolves ephemeral settings from the header's credential context.
		commandResolver.Resolve<EnvironmentSettings>(Arg.Any<EnvironmentOptions>())
			.Returns(new EnvironmentSettings());
		IPlatformVersionResolver resolver = Substitute.For<IPlatformVersionResolver>();
		resolver.ResolveAsync(Arg.Any<CancellationToken>())
			.Returns(new PlatformVersionResolution("8.2.1", VersionResolutionSource.Environment));
		IPlatformVersionResolverFactory resolverFactory = Substitute.For<IPlatformVersionResolverFactory>();
		resolverFactory.Create(Arg.Any<EnvironmentSettings>()).Returns(resolver);
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog,
			Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(new MockFileSystem()), resolverFactory);
		PageSyncArgs args = new(
			null,
			[new PageSyncPageInput("UsrTodo_FormPage", ValidPageBody)],
			Validate: true,
			SkipSampling: true);

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert — the resolver call is the discriminator: the old blank-name guard returned null
		// WITHOUT ever calling Resolve<EnvironmentSettings>, so this Received() assertion fails against
		// the pre-fix code even though a "version" (null -> latest) would still come back.
		commandResolver.Received(1).Resolve<EnvironmentSettings>(Arg.Any<EnvironmentOptions>());
		response.Success.Should().BeTrue(
			because: "the header-selected tenant resolves settings successfully so the batch save must proceed normally");
		string requestedVersion = (string)webCatalog.ReceivedCalls()
			.Single(c => c.GetMethodInfo().Name == nameof(IComponentInfoCatalog.LoadAsync))
			.GetArguments()[0];
		requestedVersion.Should().Be("8.2.1",
			because: "the probe must resolve against the header tenant's platform version, not silently fall back to latest without ever consulting the credential context");
	}

	[Test]
	[Category("Unit")]
	[Description("AC-04 mixed input (header AND an explicit environment-name naming a different registered environment): the version probe's settings lookup is rejected by the resolver's passthrough guard BEFORE any named-tenant lookup can run, and fails SOFT to 'latest' without ever touching the named environment's stored credentials — proved structurally by asserting the resolver-factory's Create(settings) (which would start an authenticated probe against a resolved environment) was never reached.")]
	public async Task SyncPages_ShouldFailSoftWithoutBypassingResolver_WhenVersionProbeResolverRejectsMixedInput() {
		// Arrange
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		// Simulates ToolCommandResolver.Resolve<T> under authorized HTTP credential passthrough: an
		// explicit environment-name alongside a passthrough header is rejected via HasExplicitCredentialArgs
		// BEFORE ResolveSettingsAndKey ever attempts a named-tenant lookup (ToolCommandResolver.cs:111-118).
		commandResolver.Resolve<EnvironmentSettings>(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw new EnvironmentResolutionException(
				"Explicit credential or environment arguments (uri/login/password/client-id/client-secret/environment) "
				+ "are not accepted when credential passthrough is enabled over HTTP. Supply the target environment "
				+ "and credentials via the X-Integration-Credentials header, not tool arguments."));
		IPlatformVersionResolverFactory resolverFactory = Substitute.For<IPlatformVersionResolverFactory>();
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog,
			Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(new MockFileSystem()), resolverFactory);
		PageSyncArgs args = new(
			"dev-named-registered-env",
			[new PageSyncPageInput("UsrTodo_FormPage", ValidPageBody)],
			Validate: true,
			SkipSampling: true);

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Success.Should().BeTrue(
			because: "the resolver's mixed-input rejection during the version probe must fail SOFT to 'latest' and never block or crash the page save itself");
		commandResolver.Received(1).Resolve<EnvironmentSettings>(
			Arg.Is<EnvironmentOptions>(o => o.Environment == "dev-named-registered-env"));
		resolverFactory.DidNotReceive().Create(Arg.Any<EnvironmentSettings>());
	}

	[Test]
	[Category("Unit")]
	[Description("AC-03/FR-05a: environment-name must be schema-optional so an authorized passthrough call is not rejected at pre-tool MCP schema binding — asserts the property carries no [Required] attribute while its documented kebab-case JSON contract name is unchanged.")]
	public void PageSyncArgs_EnvironmentName_ShouldBeSchemaOptional_ToSupportPassthrough() {
		// Arrange
		System.Reflection.PropertyInfo? property = typeof(PageSyncArgs).GetProperty(nameof(PageSyncArgs.EnvironmentName));

		// Act
		bool hasRequiredAttribute = property!
			.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.RequiredAttribute), inherit: false)
			.Length > 0;
		string? jsonName = property
			.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonPropertyNameAttribute), inherit: false)
			.Cast<System.Text.Json.Serialization.JsonPropertyNameAttribute>()
			.SingleOrDefault()?.Name;

		// Assert
		hasRequiredAttribute.Should().BeFalse(
			because: "environment-name must be schema-optional so an authorized HTTP passthrough call is not rejected at pre-tool MCP binding before the tool (and the resolver's own conditional requiredness) ever runs");
		jsonName.Should().Be("environment-name",
			because: "relaxing requiredness must not rename the kebab-case MCP argument contract");
	}

	[Test]
	[Category("Unit")]
	[Description("AC-05 non-passthrough regression guard: when the target environment is unresolvable on the stdio/registered-environment path (no active credential-passthrough context), IToolCommandResolver.Resolve<PageUpdateCommand> still throws EnvironmentResolutionException exactly as before this story — sync-pages surfaces the (redacted) message as a per-page error rather than crashing or silently succeeding. Pins that relaxing [Required] on environment-name removed only pre-tool schema rejection, not the resolver's runtime requiredness enforcement.")]
	public async Task SyncPages_WhenEnvironmentIsUnresolvable_SurfacesEnvironmentResolutionExceptionPerPage() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(_ => throw new EnvironmentResolutionException(
				"Either a configured environment name or an explicit URI is required for MCP command execution. "
				+ "Prefer a registered environment name; use explicit URI credentials only as a bootstrap or emergency fallback."));
		IMobileComponentInfoCatalog mobileCatalog = Substitute.For<IMobileComponentInfoCatalog>();
		IComponentInfoCatalog webCatalog = Substitute.For<IComponentInfoCatalog>();
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), mobileCatalog, webCatalog,
			Substitute.For<IPageBodySamplingService>(), new PageBaselineGuard(new MockFileSystem()));
		PageSyncArgs args = new(
			null,
			[new PageSyncPageInput("UsrTodo_FormPage", ValidPageBody)],
			Validate: false,
			SkipSampling: true);

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Success.Should().BeFalse(
			because: "an unresolvable environment on the non-passthrough path must fail the batch, matching the pre-change baseline");
		response.Pages[0].Error.Should().Contain("environment name or an explicit URI is required",
			because: "the resolver's existing EnvironmentResolutionException throw is the mechanism that still enforces requiredness on stdio/registered-environment paths — AC-05 must not regress this");
	}
}
