using System.Collections.Generic;
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
/// ENG-105680: update-page and sync-pages are the MCP entry points that must enforce the package choice. A
/// regression that drops <see cref="PageUpdateOptions.EnforcePackageChoice"/> from either one would silently
/// let the agent write past an editable layer without asking.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class PagePackageChoiceToolTests {
	private const string SchemaUId = "test-uid";
	private const string DesignPackage = "test-pkg-uid";
	private const string RefusedPage = "UsrRefused_FormPage";
	private const string SavedPage = "UsrSaved_FormPage";

	private const string Body =
		"define(\"UsrPage_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	[Test]
	[Description("update-page builds its command options with EnforcePackageChoice on, so the MCP write path runs the package-choice check.")]
	public void PageUpdateTool_BuildOptions_Should_Enforce_The_Package_Choice() {
		// Arrange
		PageUpdateArgs args = new("UsrPage_FormPage", Body: Body);

		// Act
		PageUpdateOptions options = PageUpdateTool.BuildOptions(args);

		// Assert
		options.EnforcePackageChoice.Should().BeTrue(
			because: "update-page is the main MCP write path and must refuse a write that names no package when the choice is required");
	}

	[Test]
	[Description("sync-pages refuses only the page another editable package also customizes, with package-choice-required naming update-page as the route, and still saves the other pages of the batch.")]
	public async Task PageSyncTool_Should_Refuse_Only_The_Page_That_Needs_A_Package_Choice() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IPageLayerInspector inspector = Substitute.For<IPageLayerInspector>();
		inspector.Inspect(Arg.Any<string>(), Arg.Any<IReadOnlyList<PageDesignerHierarchySchema>>(), Arg.Any<string>())
			.Returns(new PagePackageChoice { DefaultTargetPackageUId = DesignPackage });
		inspector.Inspect(RefusedPage, Arg.Any<IReadOnlyList<PageDesignerHierarchySchema>>(), Arg.Any<string>())
			.Returns(new PagePackageChoice {
				Required = true,
				DefaultTargetPackageUId = DesignPackage,
				Layers = [
					new PagePackageLayer {
						PackageName = "Custom", PackageUId = "custom-pkg", SchemaUId = "custom-schema", HierarchyLevel = 2,
						PackageLocked = false
					}
				]
			});
		PageUpdateCommand updateCommand = CreateUpdateCommand(applicationClient, inspector);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(updateCommand);
		PageSyncTool tool = new(commandResolver, new MockFileSystem(), Substitute.For<IMobileComponentInfoCatalog>(),
			Substitute.For<IComponentInfoCatalog>(), new PageBaselineGuard(new MockFileSystem()),
			new PersistedResourceKeyReader(), new PageDataSourceReferenceValidator(new PageSchemaBodyParser()));
		PageSyncArgs args = new("dev",
			[new PageSyncPageInput(RefusedPage, Body), new PageSyncPageInput(SavedPage, Body)],
			Validate: false);

		// Act
		PageSyncResponse response = await tool.SyncPages(args);

		// Assert
		PageSyncPageResult refused = response.Pages.Single(page => page.SchemaName == RefusedPage);
		PageSyncPageResult saved = response.Pages.Single(page => page.SchemaName == SavedPage);
		refused.Success.Should().BeFalse(because: "sync-pages cannot name a target package, so the page is refused");
		refused.Error.Should().StartWith("package-choice-required",
			because: "the agent recognizes the refusal by its stable marker")
			.And.Contain("update-page with target-package-uid",
				because: "sync-pages has no target input, so the refusal must route the write to update-page");
		saved.Success.Should().BeTrue(because: $"a page with nothing to choose saves as before: {saved.Error}");
		applicationClient.Received(1).ExecutePostRequest(
			Arg.Is<string>(url => url.Contains("SaveSchema")),
			Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	private static PageUpdateCommand CreateUpdateCommand(IApplicationClient applicationClient,
		IPageLayerInspector inspector) {
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(Arg.Any<string>())
			.Returns(callInfo => "http://test" + callInfo.Arg<string>());
		serviceUrlBuilder.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>())
			.Returns(ci => serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoutes[ci.Arg<ServiceUrlBuilder.KnownRoute>()]));
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SelectQuery")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new JObject {
				["success"] = true,
				["rows"] = new JArray { new JObject { ["UId"] = SchemaUId, ["SchemaType"] = 9 } }
			}.ToString());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("GetSchema")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new JObject { ["success"] = true, ["schema"] = new JObject { ["body"] = "original" } }.ToString());
		applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SaveSchema")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new JObject { ["success"] = true }.ToString());
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId(SchemaUId).Returns(DesignPackage);
		hierarchyClient.GetParentSchemas(SchemaUId, DesignPackage).Returns([
			new PageDesignerHierarchySchema { UId = SchemaUId, PackageUId = DesignPackage }
		]);
		return new PageUpdateCommand(applicationClient, serviceUrlBuilder, Substitute.For<ILogger>(),
			Substitute.For<IPageBaselineGuard>(), new PersistedResourceKeyReader(), hierarchyClient,
			viewConfigApplierFactory: () => Substitute.For<IJsonDiffApplier>(), pageLayerInspector: inspector);
	}
}
