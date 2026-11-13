using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public class PageLayerInspectorTests {
	private const string PageName = "UsrOrders_FormPage";
	private const string AppPackage = "app-pkg";
	private const string CustomPackage = "custom-pkg";
	private const string ProjectPackage = "proj-pkg";
	private const string VendorPackage = "vendor-pkg";

	private const string MergeSerialNumberBody =
		"define(\"UsrOrders_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ {\n"
		+ "\treturn {\n\t\tviewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"merge\",\"name\":\"UsrSerialNumber\","
		+ "\"values\":{\"labelPosition\":\"above\"}}]/**SCHEMA_VIEW_CONFIG_DIFF*/,\n"
		+ "\t\tviewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,\n"
		+ "\t\tmodelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,\n"
		+ "\t\thandlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,\n"
		+ "\t\tconverters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,\n"
		+ "\t\tvalidators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/\n\t};\n});";

	private static PageDesignerHierarchySchema Schema(string name, string packageUId, string body = null) =>
		new() {
			UId = name + "@" + packageUId,
			Name = name,
			PackageName = packageUId + "-name",
			PackageUId = packageUId,
			SchemaType = 9,
			Body = body
		};

	/// <summary>
	/// The QA chain of ENG-105680: two platform templates, the base page in the app package (level 2), replacing
	/// layers in Custom (level 3) and the project package (level 4, the default target). Effective-first order.
	/// </summary>
	private static List<PageDesignerHierarchySchema> QaChain() =>
		[
			Schema(PageName, ProjectPackage, MergeSerialNumberBody),
			Schema(PageName, CustomPackage, MergeSerialNumberBody),
			Schema(PageName, AppPackage),
			Schema("PageWithTabsFreedomTemplate", VendorPackage),
			Schema("BaseTemplate", VendorPackage)
		];

	private static string LockRows(params (string packageUId, int installType)[] rows) =>
		"{\"success\":true,\"rows\":["
		+ string.Join(",", rows.Select(row => $"{{\"UId\":\"{row.packageUId}\",\"InstallType\":{row.installType}}}"))
		+ "]}";

	private static (PageLayerInspector sut, IApplicationClient client) BuildSut(string lockAnswer) {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>()).Returns(lockAnswer);
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(Arg.Any<string>()).Returns(callInfo => callInfo.Arg<string>());
		return (new PageLayerInspector(client, urlBuilder, new PageSchemaBodyParser()), client);
	}

	[Test]
	[Description("A page with an unlocked replacing layer outside the default target requires a package choice and lists every replacing layer with what it changes; the base page is not a layer.")]
	public void Inspect_Should_Require_Choice_When_Another_Unlocked_Layer_Exists() {
		// Arrange
		(PageLayerInspector sut, _) = BuildSut(LockRows((CustomPackage, 0), (ProjectPackage, 0)));

		// Act
		PagePackageChoice choice = sut.Inspect(PageName, QaChain(), ProjectPackage);

		// Assert
		choice.Required.Should().BeTrue(because: "Custom is unlocked and is not the default target");
		choice.Layers.Select(layer => layer.PackageUId).Should().Equal([CustomPackage, ProjectPackage],
			because: "the replacing layers are listed lowest first and the base page in the app package is not one");
		choice.Layers.Select(layer => layer.HierarchyLevel).Should().Equal([3, 4],
			because: "levels count from the root template, as get-page-hierarchy reports them");
		choice.Layers.Single(layer => layer.IsDefaultTarget).PackageUId.Should().Be(ProjectPackage,
			because: "the default target is marked so the user can keep it");
		choice.Layers[0].Changes.Should().Equal(["merge UsrSerialNumber"],
			because: "the user chooses by what each layer already changes");
	}

	[Test]
	[Description("The base page is never offered even when its package is unlocked and is not the default target: the base is the lowest schema with the page name, not the chain root at level 0.")]
	public void Inspect_Should_Not_Treat_The_Base_Page_As_A_Layer() {
		// Arrange
		(PageLayerInspector sut, IApplicationClient client) = BuildSut(LockRows((ProjectPackage, 0)));
		List<PageDesignerHierarchySchema> chain = [
			Schema(PageName, ProjectPackage, MergeSerialNumberBody),
			Schema(PageName, AppPackage),
			Schema("BaseTemplate", VendorPackage)
		];

		// Act
		PagePackageChoice choice = sut.Inspect(PageName, chain, ProjectPackage);

		// Assert
		choice.Required.Should().BeFalse(
			because: "the only replacing layer is in the default target; the unlocked app package holds the base page");
		choice.Layers.Should().BeEmpty(because: "a page with nothing to choose carries no layer list");
		client.DidNotReceiveWithAnyArgs().ExecutePostRequest(default, default);
	}

	[Test]
	[Description("When only locked vendor layers sit outside the default target there is no choice to make, and the layer list is omitted.")]
	public void Inspect_Should_Not_Require_Choice_When_Other_Layers_Are_Locked() {
		// Arrange
		(PageLayerInspector sut, _) = BuildSut(LockRows((CustomPackage, 1), (ProjectPackage, 0)));

		// Act
		PagePackageChoice choice = sut.Inspect(PageName, QaChain(), ProjectPackage);

		// Assert
		choice.Required.Should().BeFalse(because: "a locked package cannot receive the change");
		choice.Layers.Should().BeEmpty(because: "listing locked vendor layers would cost every get-page tokens for no decision");
		choice.DefaultTargetPackageUId.Should().Be(ProjectPackage, because: "the default target is always reported");
	}

	[Test]
	[Description("A lock state that cannot be read requires a package choice and carries the reason, so an unknown layer is never written past silently.")]
	public void Inspect_Should_Require_Choice_When_Lock_State_Is_Unknown() {
		// Arrange
		(PageLayerInspector sut, IApplicationClient client) = BuildSut("unused");
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns(_ => throw new System.Net.Http.HttpRequestException("connection reset"));

		// Act
		PagePackageChoice choice = sut.Inspect(PageName, QaChain(), ProjectPackage);

		// Assert
		choice.Required.Should().BeTrue(because: "an unknown lock state may hide an editable package");
		choice.Layers.Should().OnlyContain(layer => layer.PackageLocked == null,
			because: "no lock state was read");
		choice.Warning.Should().Contain("connection reset", because: "the caller must learn why the lock state is unknown");
	}

	[Test]
	[Description("When the default target holds no layer yet and another unlocked package does, the choice is required and the default target is not listed as a layer.")]
	public void Inspect_Should_Require_Choice_When_Default_Target_Has_No_Layer_Yet() {
		// Arrange
		(PageLayerInspector sut, _) = BuildSut(LockRows((CustomPackage, 0)));
		List<PageDesignerHierarchySchema> chain = [
			Schema(PageName, CustomPackage, MergeSerialNumberBody),
			Schema(PageName, AppPackage),
			Schema("BaseTemplate", VendorPackage)
		];

		// Act
		PagePackageChoice choice = sut.Inspect(PageName, chain, ProjectPackage);

		// Assert
		choice.Required.Should().BeTrue(because: "the write would create a new layer while Custom already customizes the page");
		choice.Layers.Should().ContainSingle(because: "only Custom holds a layer").Which.IsDefaultTarget.Should().BeFalse(
			because: "the default target has no layer of its own yet");
	}

	[Test]
	[Description("A page with no replacing layer above its base needs no choice and no lock read.")]
	public void Inspect_Should_Not_Require_Choice_For_A_Page_Without_Replacing_Layers() {
		// Arrange
		(PageLayerInspector sut, IApplicationClient client) = BuildSut(LockRows());
		List<PageDesignerHierarchySchema> chain = [Schema(PageName, AppPackage), Schema("BaseTemplate", VendorPackage)];

		// Act
		PagePackageChoice choice = sut.Inspect(PageName, chain, AppPackage);

		// Assert
		choice.Required.Should().BeFalse(because: "there is only the base page");
		client.DidNotReceiveWithAnyArgs().ExecutePostRequest(default, default);
	}

	[Test]
	[Description("The change list of a layer is capped and the rest is counted, so a heavily customized layer cannot flood the response.")]
	public void Inspect_Should_Cap_The_Change_List_Per_Layer() {
		// Arrange
		(PageLayerInspector sut, _) = BuildSut(LockRows((CustomPackage, 0), (ProjectPackage, 0)));
		string ops = string.Join(",", Enumerable.Range(1, PageLayerInspector.MaxChangesPerLayer + 3)
			.Select(i => $"{{\"operation\":\"merge\",\"name\":\"Field{i}\",\"values\":{{}}}}"));
		string body = MergeSerialNumberBody.Replace(
			"[{\"operation\":\"merge\",\"name\":\"UsrSerialNumber\",\"values\":{\"labelPosition\":\"above\"}}]", $"[{ops}]");
		List<PageDesignerHierarchySchema> chain = QaChain();
		chain[1] = Schema(PageName, CustomPackage, body);

		// Act
		PagePackageChoice choice = sut.Inspect(PageName, chain, ProjectPackage);

		// Assert
		PagePackageLayer custom = choice.Layers.Single(layer => layer.PackageUId == CustomPackage);
		custom.Changes.Should().HaveCount(PageLayerInspector.MaxChangesPerLayer, because: "the list is capped");
		custom.MoreChanges.Should().Be(3, because: "the changes left out are counted");
	}
}
