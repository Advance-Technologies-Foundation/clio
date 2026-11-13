using System.Collections.Generic;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using Newtonsoft.Json;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageGetCommandPackageChoiceTests {
	private const string SchemaName = "Test_FormPage";
	private const string SelectQueryUrl = "http://test/DataService/json/SyncReply/SelectQuery";
	private const string DesignPackage = "pkg-1";

	private const string Body =
		"define(\"Test_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	private static readonly PagePackageChoice RequiredChoice = new() {
		Required = true,
		DefaultTargetPackageUId = DesignPackage,
		Layers = [
			new PagePackageLayer {
				PackageName = "Custom", PackageUId = "custom-pkg", SchemaUId = "custom-schema", HierarchyLevel = 2,
				PackageLocked = false, Changes = ["merge UsrSerialNumber"]
			}
		]
	};

	[Test]
	[Description("get-page returns the inspector's verdict as page.packageChoice, read for the page's chain and design package, so the agent learns before writing that it must ask which package to change.")]
	public void TryGetPage_Should_Return_The_Package_Choice_In_Page_Metadata() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urls = Substitute.For<IServiceUrlBuilder>();
		urls.Build("/DataService/json/SyncReply/SelectQuery").Returns(SelectQueryUrl);
		applicationClient.ExecutePostRequest(
				Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($$"""{"success":true,"rows":[{"Name":"{{SchemaName}}","UId":"uid-1","PackageName":"UsrPkg","PackageUId":"{{DesignPackage}}","ParentSchemaName":"BaseTemplate"}]}""");
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId("uid-1").Returns(DesignPackage);
		hierarchyClient.GetParentSchemas(Arg.Any<string>(), Arg.Any<string>()).Returns([
			new PageDesignerHierarchySchema {
				UId = "uid-1", Name = SchemaName, PackageUId = DesignPackage, PackageName = "UsrPkg", SchemaVersion = 1, Body = Body
			},
			new PageDesignerHierarchySchema {
				UId = "uid-2", Name = "BaseTemplate", PackageUId = "pkg-2", PackageName = "CrtPkg", SchemaVersion = 1, Body = Body
			}
		]);
		IPageLayerInspector inspector = Substitute.For<IPageLayerInspector>();
		inspector.Inspect(SchemaName, Arg.Any<IReadOnlyList<PageDesignerHierarchySchema>>(), DesignPackage)
			.Returns(RequiredChoice);
		PageGetCommand command = new(applicationClient, urls, Substitute.For<ILogger>(), hierarchyClient,
			new PageSchemaBodyParser(),
			new PageBundleBuilder(() => new JsonDiffApplier(), () => new JsonPathDiffApplier()),
			Substitute.For<IPageFileWriter>(), inspector);

		// Act
		bool ok = command.TryGetPage(new PageGetOptions { SchemaName = SchemaName, Environment = "dev" },
			out PageGetResponse response);

		// Assert
		ok.Should().BeTrue(because: $"the page reads successfully: {response.Error}");
		response.Page.PackageChoice.Should().BeSameAs(RequiredChoice,
			because: "get-page must hand the inspector's verdict to the agent unchanged");
		string json = JsonConvert.SerializeObject(response.Page);
		json.Should().Contain("\"packageChoice\":{\"required\":true", because: "the verdict travels as page.packageChoice")
			.And.Contain("\"defaultTargetPackageUId\":\"pkg-1\"", because: "the default target is named")
			.And.Contain("custom-pkg", because: "every layer the user can choose is listed");
	}
}
