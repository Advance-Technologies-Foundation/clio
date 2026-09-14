using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Tool-level coverage for the ONE blocking check the removed LLM sampling review used to make
/// (ENG-98526): an inserted widget caption bound to a <c>$Resources.Strings.&lt;Key&gt;</c> that will not
/// resolve at runtime. The replacement is deterministic and lives in
/// <see cref="SchemaValidationService.ValidateInsertedWidgetCaptionsRegistered"/>, invoked from
/// <c>PageUpdateCommand.TryUpdatePage</c>. <c>SchemaValidationServiceTests</c> covers the validator
/// itself; what was NOT covered anywhere, and what the sampling removal makes load-bearing, is that the
/// validator is actually reached from both write tools and refuses the save before any SaveSchema call —
/// including the argument plumbing that carries <c>resources</c> from the tool argument to the command.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class PageWriteCaptionResourceGateTests {

	private const string SchemaName = "UsrCaptionGate_FormPage";
	private const string CaptionKey = "IndicatorWidget_captiongate_title";

	// A web body whose viewConfigDiff INSERTS a widget with a caption bound to CaptionKey. The key is neither
	// data-source bound (the platform would auto-provide it) nor `Usr`-prefixed (clio would auto-derive a
	// caption for it), so it MUST be registered explicitly or the caption renders the raw binding text.
	private const string BodyWithUnregisteredCaption =
		"define('UsrCaptionGate_FormPage', /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, " +
		"function(/**SCHEMA_ARGS*//**SCHEMA_ARGS*/) { return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"insert\",\"name\":\"UsrCaptionGateCard\"," +
		"\"values\":{\"type\":\"crt.Card\",\"title\":\"$Resources.Strings." + CaptionKey + "\"}}]" +
		"/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/{}/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/{}/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	private const string RegisteringResources = "{\"" + CaptionKey + "\": \"Caption gate\"}";

	private const string UnregisteredCaptionErrorFragment = "unregistered localizable strings";

	[Test]
	[Description("update-page refuses a body whose inserted widget caption binds to a localizable key that is neither registered through the 'resources' argument nor data-source bound, and refuses it BEFORE any SaveSchema round-trip. This is the check the removed sampling review used to make; it must now be proven at the tool level, not only at the validator level.")]
	public void UpdatePage_Should_Refuse_Unregistered_Caption_Before_Saving() {
		// Arrange
		PageUpdateCommand command = CreateSuccessfulPageUpdateCommand(out IApplicationClient applicationClient);
		PageUpdateTool tool = CreateUpdateTool(command);
		PageUpdateArgs args = new(SchemaName, BodyWithUnregisteredCaption);

		// Act
		PageUpdateResponse response = tool.UpdatePage(args).Result;

		// Assert
		response.Success.Should().BeFalse(
			because: "an unresolvable caption binding renders the raw '$Resources.Strings.<Key>' text at runtime, so the save must be refused");
		response.Error.Should().Contain(UnregisteredCaptionErrorFragment,
			because: "the agent-facing error must name the actual defect class so the caller registers the key instead of chasing a different rejection");
		CountSaveSchemaCalls(applicationClient).Should().Be(0,
			because: "the gate must refuse before the write reaches Creatio, not after a persisted body has to be undone");
	}

	[Test]
	[Description("update-page accepts the same body once the caption key is supplied through the 'resources' argument — proving both that the gate is satisfiable and that the tool actually plumbs its 'resources' argument into the command options (the plumbing formerly asserted only by the deleted sampling-invocation test).")]
	public void UpdatePage_Should_Accept_Caption_When_Resources_Register_The_Key() {
		// Arrange
		PageUpdateCommand command = CreateSuccessfulPageUpdateCommand(out IApplicationClient applicationClient);
		PageUpdateTool tool = CreateUpdateTool(command);
		PageUpdateArgs args = new(SchemaName, BodyWithUnregisteredCaption, RegisteringResources);

		// Act
		PageUpdateResponse response = tool.UpdatePage(args).Result;

		// Assert
		response.Success.Should().BeTrue(
			because: "the caption key is registered through the 'resources' argument, so the binding resolves and the save must proceed");
		CountSaveSchemaCalls(applicationClient).Should().Be(1,
			because: "a satisfied gate must let exactly one save round-trip through");
	}

	[Test]
	[Description("sync-pages applies the same caption-resource gate per page: the offending page fails with the deterministic error and no SaveSchema round-trip happens for it. sync-pages routes through the same PageUpdateCommand, so this pins that the batch path did not lose the gate when sampling was removed.")]
	public async Task SyncPages_Should_Refuse_Unregistered_Caption_Before_Saving() {
		// Arrange
		PageUpdateCommand command = CreateSuccessfulPageUpdateCommand(out IApplicationClient applicationClient);
		PageSyncTool tool = CreateSyncTool(command);
		PageSyncArgs args = new("dev", [new PageSyncPageInput(SchemaName, BodyWithUnregisteredCaption)], Validate: true);

		// Act
		PageSyncResponse response = await tool.SyncPages(args);

		// Assert
		response.Pages[0].Success.Should().BeFalse(
			because: "the per-page result must carry the same refusal update-page gives for the same body");
		response.Pages[0].Error.Should().Contain(UnregisteredCaptionErrorFragment,
			because: "the batch path must surface the deterministic diagnosis, not a generic failure");
		CountSaveSchemaCalls(applicationClient).Should().Be(0,
			because: "no page in the batch may reach Creatio once its caption binding cannot resolve");
	}

	[Test]
	[Description("sync-pages accepts the same page once its caption key is registered through the per-page 'resources' payload — the batch counterpart of the update-page acceptance case, and the proof that per-page resources reach the command.")]
	public async Task SyncPages_Should_Accept_Caption_When_Resources_Register_The_Key() {
		// Arrange
		PageUpdateCommand command = CreateSuccessfulPageUpdateCommand(out IApplicationClient applicationClient);
		PageSyncTool tool = CreateSyncTool(command);
		PageSyncArgs args = new(
			"dev",
			[new PageSyncPageInput(SchemaName, BodyWithUnregisteredCaption, Resources: RegisteringResources)],
			Validate: true);

		// Act
		PageSyncResponse response = await tool.SyncPages(args);

		// Assert
		response.Pages[0].Success.Should().BeTrue(
			because: "the per-page resources register the caption key, so the binding resolves and the page must save");
		CountSaveSchemaCalls(applicationClient).Should().Be(1,
			because: "exactly one save round-trip must happen for the single valid page");
	}

	private static PageUpdateTool CreateUpdateTool(PageUpdateCommand command) {
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(command);
		return new PageUpdateTool(
			command,
			Substitute.For<ILogger>(),
			commandResolver,
			Substitute.For<IMobileComponentInfoCatalog>(),
			Substitute.For<IComponentInfoCatalog>(),
			new PageBaselineGuard(new MockFileSystem()));
	}

	private static PageSyncTool CreateSyncTool(PageUpdateCommand command) {
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(command);
		return new PageSyncTool(
			commandResolver,
			new MockFileSystem(),
			Substitute.For<IMobileComponentInfoCatalog>(),
			Substitute.For<IComponentInfoCatalog>(),
			new PageBaselineGuard(new MockFileSystem()));
	}

	private static int CountSaveSchemaCalls(IApplicationClient applicationClient) =>
		applicationClient.ReceivedCalls()
			.Count(c => c.GetArguments().FirstOrDefault() is string url && url.Contains("SaveSchema"));

	private static PageUpdateCommand CreateSuccessfulPageUpdateCommand(out IApplicationClient applicationClient) {
		applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(Arg.Any<string>()).Returns(callInfo => "http://test" + callInfo.Arg<string>());
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
				["schema"] = new JObject { ["body"] = "original" }
			}.ToString());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SaveSchema")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new JObject { ["success"] = true }.ToString());
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId("test-uid").Returns("test-pkg-uid");
		hierarchyClient.GetParentSchemas("test-uid", "test-pkg-uid").Returns([
			new PageDesignerHierarchySchema { UId = "test-uid", PackageUId = "test-pkg-uid" }
		]);
		return new PageUpdateCommand(
			applicationClient, serviceUrlBuilder, Substitute.For<ILogger>(),
			Substitute.For<IPageBaselineGuard>(), hierarchyClient);
	}
}
