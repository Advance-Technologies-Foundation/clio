using System.IO.Abstractions.TestingHelpers;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Unit tests for the write-path layout-guidance gate enforced by <see cref="PageUpdateTool"/>
/// (update-page) and <see cref="PageSyncTool"/> (sync-pages): a body that adds or lays out
/// <c>crt.*</c> view components is rejected unless the <c>ui-page-layout</c> guidance was fetched
/// this session or the call is forced. The thin <c>ui-guidelines</c> index does NOT satisfy the
/// gate.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class PageLayoutGuidanceGateTests {

	private const string CompositionBody =
		"define('TestPage', /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, "
		+ "function(/**SCHEMA_ARGS*//**SCHEMA_ARGS*/) { return { "
		+ "viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"insert\",\"name\":\"UsrName\",\"values\":{\"type\":\"crt.Input\"}}]/**SCHEMA_VIEW_CONFIG_DIFF*/, "
		+ "viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, "
		+ "modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, "
		+ "handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, "
		+ "converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, "
		+ "validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	private const string NonCompositionBody =
		"define('TestPage', /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, "
		+ "function(/**SCHEMA_ARGS*//**SCHEMA_ARGS*/) { return { "
		+ "viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"merge\",\"name\":\"UsrName\",\"values\":{\"label\":\"X\"}}]/**SCHEMA_VIEW_CONFIG_DIFF*/, "
		+ "viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, "
		+ "modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, "
		+ "handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, "
		+ "converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, "
		+ "validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	// ----- update-page -----

	private static PageUpdateTool BuildUpdateTool(IGuidanceAccessLedger ledger) {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		PageUpdateCommand command = new(applicationClient, serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>());
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(command);
		return new PageUpdateTool(
			command, logger, commandResolver,
			Substitute.For<IMobileComponentInfoCatalog>(),
			Substitute.For<IComponentInfoCatalog>(),
			Substitute.For<IPageBodySamplingService>(),
			new PageBaselineGuard(new MockFileSystem()),
			ledger,
			new PageLayoutCompositionDetector());
	}

	private static IGuidanceAccessLedger LedgerWith(params string[] fetched) {
		var ledger = new GuidanceAccessLedger();
		foreach (string name in fetched) {
			ledger.Record(name);
		}
		return ledger;
	}

	[Test]
	[Description("update-page rejects a layout-composing body when ui-page-layout was not fetched and not forced.")]
	public async Task UpdatePage_Should_Reject_When_Composition_And_Guidance_Not_Fetched() {
		// Arrange
		PageUpdateTool tool = BuildUpdateTool(LedgerWith());
		PageUpdateArgs args = new("UsrTodo_FormPage", CompositionBody, null, null, null, null, null, null);

		// Act
		PageUpdateResponse response = await tool.UpdatePage(args, null);

		// Assert
		response.Success.Should().BeFalse(
			because: "a crt.* layout-composing body must be blocked until ui-page-layout guidance is read");
		response.Error.Should().Contain("ui-page-layout",
			because: "the rejection must name the get-guidance call the agent has to make");
		response.Error.Should().Contain("force",
			because: "the rejection must tell the agent force overrides the gate");
	}

	[Test]
	[Description("update-page allows a layout-composing body once ui-page-layout was fetched this session.")]
	public async Task UpdatePage_Should_Allow_When_Guidance_Was_Fetched() {
		// Arrange
		PageUpdateTool tool = BuildUpdateTool(LedgerWith("ui-page-layout"));
		PageUpdateArgs args = new("UsrTodo_FormPage", CompositionBody, null, null, null, null, null, null);

		// Act
		PageUpdateResponse response = await tool.UpdatePage(args, null);

		// Assert
		response.Error.Should().NotContain("Layout guidance required",
			because: "fetching ui-page-layout satisfies the gate so the layout-guidance rejection must not fire");
	}

	[Test]
	[Description("update-page allows a layout-composing body when force=true even if ui-page-layout was not fetched.")]
	public async Task UpdatePage_Should_Allow_When_Forced() {
		// Arrange
		PageUpdateTool tool = BuildUpdateTool(LedgerWith());
		PageUpdateArgs args = new("UsrTodo_FormPage", CompositionBody, null, null, null, null, null, null, Force: true);

		// Act
		PageUpdateResponse response = await tool.UpdatePage(args, null);

		// Assert
		response.Error.Should().NotContain("Layout guidance required",
			because: "force:true overrides the layout-guidance gate, mirroring the checksum-conflict override");
	}

	[Test]
	[Description("update-page does not fire the gate for a non-composition (merge-only) body.")]
	public async Task UpdatePage_Should_Not_Fire_Gate_For_Non_Composition_Body() {
		// Arrange
		PageUpdateTool tool = BuildUpdateTool(LedgerWith());
		PageUpdateArgs args = new("UsrTodo_FormPage", NonCompositionBody, null, null, null, null, null, null);

		// Act
		PageUpdateResponse response = await tool.UpdatePage(args, null);

		// Assert
		response.Error.Should().NotContain("Layout guidance required",
			because: "a merge-only body adds no components, so the layout-guidance gate must not fire");
	}

	[Test]
	[Description("update-page still rejects when only the ui-guidelines index was fetched, not the ui-page-layout leaf.")]
	public async Task UpdatePage_Should_Reject_When_Only_Index_Was_Fetched() {
		// Arrange — reading only the thin index is exactly the failure the gate fixes.
		PageUpdateTool tool = BuildUpdateTool(LedgerWith("ui-guidelines"));
		PageUpdateArgs args = new("UsrTodo_FormPage", CompositionBody, null, null, null, null, null, null);

		// Act
		PageUpdateResponse response = await tool.UpdatePage(args, null);

		// Assert
		response.Success.Should().BeFalse(
			because: "the ui-guidelines index does not carry the layout mechanics, so it must not satisfy the gate");
		response.Error.Should().Contain("ui-page-layout",
			because: "the rejection must still direct the agent to the ui-page-layout leaf");
	}

	// ----- sync-pages -----

	private static PageSyncTool BuildSyncTool(IGuidanceAccessLedger ledger) {
		PageUpdateCommand updateCommand = CreateSuccessfulPageUpdateCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>())
			.Returns(updateCommand);
		return new PageSyncTool(
			commandResolver,
			new MockFileSystem(),
			Substitute.For<IMobileComponentInfoCatalog>(),
			Substitute.For<IComponentInfoCatalog>(),
			Substitute.For<IPageBodySamplingService>(),
			new PageBaselineGuard(new MockFileSystem()),
			ledger,
			new PageLayoutCompositionDetector());
	}

	private static PageUpdateCommand CreateSuccessfulPageUpdateCommand() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(Arg.Any<string>())
			.Returns(callInfo => "http://test" + callInfo.Arg<string>());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SelectQuery")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new Newtonsoft.Json.Linq.JObject {
				["success"] = true,
				["rows"] = new Newtonsoft.Json.Linq.JArray {
					new Newtonsoft.Json.Linq.JObject { ["UId"] = "test-uid", ["SchemaType"] = 9 }
				}
			}.ToString());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("GetSchema")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new Newtonsoft.Json.Linq.JObject {
				["success"] = true,
				["schema"] = new Newtonsoft.Json.Linq.JObject { ["body"] = "original" }
			}.ToString());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SaveSchema")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new Newtonsoft.Json.Linq.JObject { ["success"] = true }.ToString());
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId("test-uid").Returns("test-pkg-uid");
		hierarchyClient.GetParentSchemas("test-uid", "test-pkg-uid").Returns([
			new PageDesignerHierarchySchema { UId = "test-uid", PackageUId = "test-pkg-uid" }
		]);
		return new PageUpdateCommand(applicationClient, serviceUrlBuilder, Substitute.For<ILogger>(),
			Substitute.For<IPageBaselineGuard>(), hierarchyClient);
	}

	[Test]
	[Description("sync-pages rejects a layout-composing page per-page when ui-page-layout was not fetched and not forced.")]
	public async Task SyncPages_Should_Reject_When_Composition_And_Guidance_Not_Fetched() {
		// Arrange
		PageSyncTool tool = BuildSyncTool(LedgerWith());
		PageSyncArgs args = new("dev",
			[new PageSyncPageInput("UsrTodo_FormPage", CompositionBody)],
			Validate: false,
			SkipSampling: true);

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Pages.Should().ContainSingle(because: "one page was submitted");
		response.Pages[0].Success.Should().BeFalse(
			because: "a crt.* layout-composing page must be blocked until ui-page-layout guidance is read");
		response.Pages[0].Error.Should().Contain("ui-page-layout",
			because: "the per-page rejection must name the get-guidance call the agent has to make");
		response.Pages[0].Error.Should().Contain("force",
			because: "the per-page rejection must tell the agent force overrides the gate");
	}

	[Test]
	[Description("sync-pages allows a layout-composing page once ui-page-layout was fetched this session.")]
	public async Task SyncPages_Should_Allow_When_Guidance_Was_Fetched() {
		// Arrange
		PageSyncTool tool = BuildSyncTool(LedgerWith("ui-page-layout"));
		PageSyncArgs args = new("dev",
			[new PageSyncPageInput("UsrTodo_FormPage", CompositionBody)],
			Validate: false,
			SkipSampling: true);

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Pages[0].Success.Should().BeTrue(
			because: "fetching ui-page-layout satisfies the gate so the save proceeds");
		response.Pages[0].Error.Should().BeNull(
			because: "a satisfied gate produces no per-page error");
	}

	[Test]
	[Description("sync-pages allows a layout-composing page when the per-page force=true even if ui-page-layout was not fetched.")]
	public async Task SyncPages_Should_Allow_When_Page_Is_Forced() {
		// Arrange
		PageSyncTool tool = BuildSyncTool(LedgerWith());
		PageSyncArgs args = new("dev",
			[new PageSyncPageInput("UsrTodo_FormPage", CompositionBody, Force: true)],
			Validate: false,
			SkipSampling: true);

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Pages[0].Success.Should().BeTrue(
			because: "the per-page force:true overrides the layout-guidance gate so the save proceeds");
	}

	[Test]
	[Description("sync-pages does not fire the gate for a non-composition (merge-only) page.")]
	public async Task SyncPages_Should_Not_Fire_Gate_For_Non_Composition_Page() {
		// Arrange
		PageSyncTool tool = BuildSyncTool(LedgerWith());
		PageSyncArgs args = new("dev",
			[new PageSyncPageInput("UsrTodo_FormPage", NonCompositionBody)],
			Validate: false,
			SkipSampling: true);

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Pages[0].Success.Should().BeTrue(
			because: "a merge-only page adds no components, so the layout-guidance gate must not fire");
		response.Pages[0].Error.Should().BeNull(
			because: "a non-composition page is not blocked by the layout-guidance gate");
	}

	[Test]
	[Description("sync-pages still rejects when only the ui-guidelines index was fetched, not the ui-page-layout leaf.")]
	public async Task SyncPages_Should_Reject_When_Only_Index_Was_Fetched() {
		// Arrange — reading only the thin index is exactly the failure the gate fixes.
		PageSyncTool tool = BuildSyncTool(LedgerWith("ui-guidelines"));
		PageSyncArgs args = new("dev",
			[new PageSyncPageInput("UsrTodo_FormPage", CompositionBody)],
			Validate: false,
			SkipSampling: true);

		// Act
		PageSyncResponse response = await tool.SyncPages(args, null);

		// Assert
		response.Pages[0].Success.Should().BeFalse(
			because: "the ui-guidelines index does not carry the layout mechanics, so it must not satisfy the gate");
		response.Pages[0].Error.Should().Contain("ui-page-layout",
			because: "the per-page rejection must still direct the agent to the ui-page-layout leaf");
	}
}
