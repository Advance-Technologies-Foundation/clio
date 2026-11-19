namespace Clio.Tests.Command;

using System.Collections.Generic;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageUpdateCommandPackageChoiceTests {
	private const string SelectQueryUrl = "http://test/DataService/json/SyncReply/SelectQuery";
	private const string GetSchemaUrl = "http://test/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema";
	private const string SaveSchemaUrl = "http://test/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema";
	private const string SchemaName = "Test_FormPage";
	private const string BaseUId = "11111111-1111-1111-1111-111111111111";
	private const string LeafUId = "22222222-2222-2222-2222-222222222222";
	private const string DesignPackage = "design-pkg-uid";
	private const string CustomPackage = "custom-pkg-uid";
	private const string CustomSchemaUId = "33333333-3333-3333-3333-333333333333";
	private const string LeafAndBaseRows =
		$$"""[{"UId": "{{LeafUId}}", "ExtendParent": true}, {"UId": "{{BaseUId}}", "ExtendParent": false}]""";
	private const string BaseOnlyRows = $$"""[{"UId": "{{BaseUId}}", "ExtendParent": false}]""";

	private const string ValidBody =
		"define(\"Test_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	private const string AppendFragment =
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"merge\",\"name\":\"UsrSerialNumber\","
		+ "\"values\":{\"visible\":true}}]/**SCHEMA_VIEW_CONFIG_DIFF*/";

	private static readonly PagePackageChoice RequiredChoice = new() {
		Required = true,
		DefaultTargetPackageUId = DesignPackage,
		Layers = [
			new PagePackageLayer {
				PackageName = "Custom", PackageUId = "custom-pkg-uid", SchemaUId = "custom-schema", HierarchyLevel = 6,
				PackageLocked = false, Changes = ["merge UsrSerialNumber"]
			},
			new PagePackageLayer {
				PackageName = "UsrProj", PackageUId = DesignPackage, SchemaUId = LeafUId, HierarchyLevel = 7,
				PackageLocked = false, IsDefaultTarget = true
			}
		]
	};

	private IApplicationClient _applicationClient;
	private IPageDesignerHierarchyClient _hierarchyClient;
	private IPageLayerInspector _inspector;
	private List<PageDesignerHierarchySchema> _chain;

	/// <summary>
	/// The page name lookup (it asks for <c>ExtendParent</c>) returns these variant rows; every other
	/// <c>SelectQuery</c> keeps the default stub.
	/// </summary>
	private void StubNameLookup(string rowsJson) =>
		_applicationClient.ExecutePostRequest(SelectQueryUrl,
				Arg.Is<string>(query => query.Contains(ClassicEntitySchemaQuery.ExtendParentColumn)),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($$"""{"success": true, "rows": {{rowsJson}}}""");

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		// The page name resolves to the LEAF variant; the chain read from it then names the base as the root
		// variant, so the check must re-read the chain from the base.
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($$"""{"success": true, "rows": [{"UId": "{{LeafUId}}"}]}""");
		_applicationClient.ExecutePostRequest(GetSchemaUrl, Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($$"""{"success": true, "schema": {"body": "old body", "name": "{{SchemaName}}" } }""");
		_applicationClient.ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success": true}""");
		_hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		_hierarchyClient.GetDesignPackageUId(Arg.Any<string>()).Returns(DesignPackage);
		_chain = [
			new PageDesignerHierarchySchema { UId = LeafUId, Name = SchemaName, PackageUId = DesignPackage },
			new PageDesignerHierarchySchema { UId = BaseUId, Name = SchemaName, PackageUId = "app-pkg-uid" }
		];
		_hierarchyClient.GetParentSchemas(Arg.Any<string>(), Arg.Any<string>()).Returns(_chain);
		_inspector = Substitute.For<IPageLayerInspector>();
		_inspector.Inspect(Arg.Any<string>(), Arg.Any<IReadOnlyList<PageDesignerHierarchySchema>>(), Arg.Any<string>())
			.Returns(RequiredChoice);
	}

	private PageUpdateCommand BuildCommand(IPageLayerInspector inspector) =>
		BuildCommand(inspector, _hierarchyClient);

	private PageUpdateCommand BuildCommand(IPageLayerInspector inspector, IPageDesignerHierarchyClient hierarchyClient) {
		IServiceUrlBuilder urls = Substitute.For<IServiceUrlBuilder>();
		urls.Build("/DataService/json/SyncReply/SelectQuery").Returns(SelectQueryUrl);
		urls.Build(ServiceUrlBuilder.KnownRoute.GetClientUnitDesignerSchema).Returns(GetSchemaUrl);
		urls.Build(ServiceUrlBuilder.KnownRoute.SaveClientUnitDesignerSchema).Returns(SaveSchemaUrl);
		return new PageUpdateCommand(_applicationClient, urls, Substitute.For<ILogger>(),
			Substitute.For<IPageBaselineGuard>(), new PersistedResourceKeyReader(), hierarchyClient,
			viewConfigApplierFactory: () => Substitute.For<IJsonDiffApplier>(), pageLayerInspector: inspector);
	}

	private static PageUpdateOptions Options(bool enforce, string targetPackageUId = null, string mode = null) =>
		new() {
			SchemaName = SchemaName,
			Body = ValidBody,
			EnforcePackageChoice = enforce,
			TargetPackageUId = targetPackageUId,
			Mode = mode
		};

	/// <summary>
	/// Custom holds its own replacing schema of the page: the in-package lookup for Custom finds it, and the
	/// designer service returns it with a valid body.
	/// </summary>
	private void StubCustomLayer() {
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Is<string>(query => query.Contains(CustomPackage)),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($$"""{"success": true, "rows": [{"UId": "{{CustomSchemaUId}}"}]}""");
		Newtonsoft.Json.Linq.JObject schema = new() {
			["uId"] = CustomSchemaUId,
			["name"] = SchemaName,
			["body"] = ValidBody
		};
		_applicationClient.ExecutePostRequest(GetSchemaUrl, Arg.Is<string>(request => request.Contains(CustomSchemaUId)),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(new Newtonsoft.Json.Linq.JObject { ["success"] = true, ["schema"] = schema }.ToString());
	}

	private void AssertNotSaved() =>
		_applicationClient.DidNotReceive().ExecutePostRequest(
			SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());

	[Test]
	[Description("A write that names no target is refused with package-choice-required, the layer list and no save, when another editable package also customizes the page.")]
	public void TryUpdatePage_Should_Refuse_Write_Without_Target_When_Choice_Is_Required() {
		// Arrange
		PageUpdateCommand sut = BuildCommand(_inspector);

		// Act
		bool ok = sut.TryUpdatePage(Options(enforce: true), out PageUpdateResponse response);

		// Assert
		ok.Should().BeFalse(because: "the user has not chosen which package to change");
		response.Error.Should().StartWith("package-choice-required",
			because: "the agent needs a stable marker to recognise the refusal");
		response.Error.Should().Contain("Custom (packageUId custom-pkg-uid, level 6, unlocked; changes: merge UsrSerialNumber)",
			because: "the refusal must carry everything the question to the user needs");
		response.Error.Should().Contain("default target", because: "the default target is named so the user can keep it");
		response.PackageChoice.Should().BeSameAs(RequiredChoice, because: "the layers are also returned as structured data");
		AssertNotSaved();
	}

	[Test]
	[Description("force lifts only the checksum conflict check and the replace refusal: a write that names no target is still refused with package-choice-required when the page needs a package choice.")]
	public void TryUpdatePage_Should_Refuse_Write_Without_Target_When_Choice_Is_Required_Even_With_Force() {
		// Arrange
		PageUpdateCommand sut = BuildCommand(_inspector);
		PageUpdateOptions options = Options(enforce: true);
		options.Force = true;

		// Act
		bool ok = sut.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		ok.Should().BeFalse(because: "force is not the user's choice of a package");
		response.Error.Should().StartWith("package-choice-required",
			because: "force must not bypass the package choice");
		response.PackageChoice.Should().BeSameAs(RequiredChoice, because: "the layers are still returned for the question");
		AssertNotSaved();
	}

	[Test]
	[Description("The CLI update-page verb turns the package-choice check on itself: Execute with no target exits non-zero with package-choice-required and saves nothing.")]
	public void Execute_Should_Refuse_Write_Without_Target_When_Choice_Is_Required() {
		// Arrange
		PageUpdateCommand sut = BuildCommand(_inspector);
		PageUpdateOptions options = new() { SchemaName = SchemaName, Body = ValidBody };

		// Act
		int exitCode = sut.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "the CLI verb must refuse a write into one of several editable packages");
		options.EnforcePackageChoice.Should().BeTrue(because: "Execute is the only place that enables the check for the CLI verb");
		_inspector.Received(1).Inspect(SchemaName, Arg.Any<IReadOnlyList<PageDesignerHierarchySchema>>(), DesignPackage);
		AssertNotSaved();
	}

	[Test]
	[Description("The CLI update-page verb saves when the caller named the target package, so the refusal has a way forward.")]
	public void Execute_Should_Save_When_Target_Package_Is_Named() {
		// Arrange
		PageUpdateCommand sut = BuildCommand(_inspector);
		PageUpdateOptions options = new() { SchemaName = SchemaName, Body = ValidBody, TargetPackageUId = DesignPackage };

		// Act
		int exitCode = sut.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a named target is the user's choice");
		_inspector.DidNotReceiveWithAnyArgs().Inspect(default, default, default);
		_applicationClient.Received().ExecutePostRequest(
			SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("The check reads the chain from the root variant of the page, as get-page does, when the name resolved to a replacing variant.")]
	public void TryUpdatePage_Should_Inspect_The_Chain_Read_From_The_Root_Variant() {
		// Arrange
		PageUpdateCommand sut = BuildCommand(_inspector);

		// Act
		sut.TryUpdatePage(Options(enforce: true), out _);

		// Assert
		_hierarchyClient.Received().GetParentSchemas(BaseUId, DesignPackage);
		_inspector.Received(1).Inspect(SchemaName, Arg.Any<IReadOnlyList<PageDesignerHierarchySchema>>(), DesignPackage);
	}

	[TestCase(LeafAndBaseRows, TestName = "lookup lands on a replacing variant")]
	[TestCase(BaseOnlyRows, TestName = "lookup lands on the root")]
	[Description("When the name lookup marks the root variant, an enforced write reads the chain once, from the root, checks that same chain and resolves the design package from the lookup row.")]
	public void TryUpdatePage_Should_Read_The_Chain_Once_From_The_Root_When_The_Lookup_Marks_It(string rowsJson) {
		// Arrange
		StubNameLookup(rowsJson);
		PageUpdateCommand sut = BuildCommand(_inspector);

		// Act
		sut.TryUpdatePage(Options(enforce: true), out _);

		// Assert
		_hierarchyClient.ReceivedWithAnyArgs(1).GetParentSchemas(default, default);
		_hierarchyClient.Received(1).GetParentSchemas(BaseUId, DesignPackage);
		_inspector.Received(1).Inspect(SchemaName,
			Arg.Is<IReadOnlyList<PageDesignerHierarchySchema>>(chain => ReferenceEquals(chain, _chain)), DesignPackage);
		// A capped lookup would return one unordered variant and lose the root row on a real stand.
		_applicationClient.Received(1).ExecutePostRequest(SelectQueryUrl,
			Arg.Is<string>(query => query.Contains(ClassicEntitySchemaQuery.ExtendParentColumn)
				&& (int)Newtonsoft.Json.Linq.JObject.Parse(query)["rowCount"] == PageSchemaMetadataHelper.AllRows),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("The design package is still resolved from the row the name lookup returned, not from the root variant.")]
	public void TryUpdatePage_Should_Resolve_The_Design_Package_From_The_Lookup_Row() {
		// Arrange
		StubNameLookup(LeafAndBaseRows);
		PageUpdateCommand sut = BuildCommand(_inspector);

		// Act
		sut.TryUpdatePage(Options(enforce: true), out _);

		// Assert
		_hierarchyClient.Received(1).GetDesignPackageUId(LeafUId);
	}

	[Test]
	[Description("When no lookup row is marked as the root variant, the write reads from the row it got and the check re-reads from the root it finds in that chain, as before.")]
	public void TryUpdatePage_Should_Fall_Back_To_Two_Reads_When_No_Row_Is_Marked_As_The_Root() {
		// Arrange
		StubNameLookup($$"""[{"UId": "{{LeafUId}}", "ExtendParent": true}]""");
		PageUpdateCommand sut = BuildCommand(_inspector);

		// Act
		sut.TryUpdatePage(Options(enforce: true), out _);

		// Assert
		_hierarchyClient.ReceivedWithAnyArgs(2).GetParentSchemas(default, default);
		_hierarchyClient.Received(1).GetParentSchemas(LeafUId, DesignPackage);
		_hierarchyClient.Received(1).GetParentSchemas(BaseUId, DesignPackage);
	}

	[Test]
	[Description("The write target is resolved from the chain read from the root variant, not from the variant the name lookup returned.")]
	public void TryUpdatePage_Should_Resolve_The_Target_From_The_Root_Read_Chain() {
		// Arrange
		const string TopUId = "44444444-4444-4444-4444-444444444444";
		StubNameLookup(LeafAndBaseRows);
		_hierarchyClient.GetParentSchemas(BaseUId, DesignPackage).Returns([
			new PageDesignerHierarchySchema { UId = TopUId, Name = SchemaName, PackageUId = DesignPackage },
			new PageDesignerHierarchySchema { UId = BaseUId, Name = SchemaName, PackageUId = "app-pkg-uid" }
		]);
		PageUpdateCommand sut = BuildCommand(_inspector);

		// Act
		bool ok = sut.TryUpdatePage(Options(enforce: false), out PageUpdateResponse response);

		// Assert
		ok.Should().BeTrue(because: $"the write is not enforced and the target exists: {response.Error}");
		_hierarchyClient.DidNotReceive().GetParentSchemas(LeafUId, Arg.Any<string>());
		_applicationClient.Received(1).ExecutePostRequest(GetSchemaUrl,
			Arg.Is<string>(request => request.Contains(TopUId)), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		_applicationClient.Received(1).ExecutePostRequest(
			SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("When the chain cannot be re-read from the root variant the write is refused with the hierarchy failure, never checked against a chain that can miss layers.")]
	public void TryUpdatePage_Should_Refuse_When_The_Root_Variant_Chain_Cannot_Be_Read() {
		// Arrange
		_hierarchyClient.GetParentSchemas(BaseUId, DesignPackage)
			.Returns(_ => throw new System.InvalidOperationException("designer service unavailable"));
		PageUpdateCommand sut = BuildCommand(_inspector);

		// Act
		bool ok = sut.TryUpdatePage(Options(enforce: true), out PageUpdateResponse response);

		// Assert
		ok.Should().BeFalse(because: "the check fails closed");
		response.Error.Should().Contain("Failed to load hierarchy for 'Test_FormPage' (package-choice check)",
			because: "the caller must learn which step failed");
		response.Error.Should().Contain("designer service unavailable", because: "the cause is kept");
		_inspector.DidNotReceiveWithAnyArgs().Inspect(default, default, default);
		AssertNotSaved();
	}

	[Test]
	[Description("A write with target-package-uid is the user's choice: the package-choice check does not run. A replace into the default package, as here, passes; a replace into a non-default package is checked against the default package and refused (see the package-choice-replace-refused tests).")]
	public void TryUpdatePage_Should_Skip_Check_When_Target_Package_Is_Named() {
		// Arrange
		PageUpdateCommand sut = BuildCommand(_inspector);

		// Act
		bool ok = sut.TryUpdatePage(Options(enforce: true, targetPackageUId: DesignPackage), out PageUpdateResponse response);

		// Assert
		ok.Should().BeTrue(because: $"the save named its target explicitly: {response.Error}");
		_inspector.DidNotReceiveWithAnyArgs().Inspect(default, default, default);
	}

	[Test]
	[Description("A write with target-schema-uid names the exact schema to change, so no package-choice check runs.")]
	public void TryUpdatePage_Should_Skip_Check_When_Target_Schema_Is_Named() {
		// Arrange
		PageUpdateCommand sut = BuildCommand(_inspector);
		PageUpdateOptions options = Options(enforce: true);
		options.TargetSchemaUId = LeafUId;

		// Act
		sut.TryUpdatePage(options, out _);

		// Assert
		_inspector.DidNotReceiveWithAnyArgs().Inspect(default, default, default);
	}

	[Test]
	[Description("Composite tools that do not set EnforcePackageChoice write as before.")]
	public void TryUpdatePage_Should_Skip_Check_When_Not_Enforced() {
		// Arrange
		PageUpdateCommand sut = BuildCommand(_inspector);

		// Act
		bool ok = sut.TryUpdatePage(Options(enforce: false), out PageUpdateResponse response);

		// Assert
		ok.Should().BeTrue(because: $"the check is opt-in per entry point: {response.Error}");
		_inspector.DidNotReceiveWithAnyArgs().Inspect(default, default, default);
	}

	[Test]
	[Description("A page customized only in the default target saves as before.")]
	public void TryUpdatePage_Should_Save_When_No_Choice_Is_Required() {
		// Arrange
		_inspector.Inspect(Arg.Any<string>(), Arg.Any<IReadOnlyList<PageDesignerHierarchySchema>>(), Arg.Any<string>())
			.Returns(new PagePackageChoice { DefaultTargetPackageUId = DesignPackage });
		PageUpdateCommand sut = BuildCommand(_inspector);

		// Act
		bool ok = sut.TryUpdatePage(Options(enforce: true), out PageUpdateResponse response);

		// Assert
		ok.Should().BeTrue(because: $"nothing to choose: {response.Error}");
		_applicationClient.Received().ExecutePostRequest(
			SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("An append into a chosen non-default layer (Custom) saves into Custom's own replacing schema, not the design package's and not a new replacing schema.")]
	public void TryUpdatePage_Should_Append_Into_The_Chosen_Custom_Layer() {
		// Arrange
		StubCustomLayer();
		PageUpdateCommand sut = BuildCommand(_inspector);
		PageUpdateOptions options = Options(enforce: true, targetPackageUId: CustomPackage, mode: "append");
		options.Body = AppendFragment;

		// Act
		bool ok = sut.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		ok.Should().BeTrue(because: $"an append keeps Custom's own changes, so the chosen layer may take it: {response.Error}");
		_applicationClient.Received().ExecutePostRequest(GetSchemaUrl,
			Arg.Is<string>(request => request.Contains(CustomSchemaUId)), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		_applicationClient.Received(1).ExecutePostRequest(SaveSchemaUrl,
			Arg.Is<string>(request => request.Contains(CustomSchemaUId)), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		_applicationClient.DidNotReceive().ExecutePostRequest(SaveSchemaUrl,
			Arg.Is<string>(request => request.Contains(LeafUId)), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		_inspector.DidNotReceiveWithAnyArgs().Inspect(default, default, default);
	}

	[Test]
	[Description("A replace into a chosen non-default layer (Custom) is refused with package-choice-replace-refused and saves nothing: get-page returned the default target's body, and saving it verbatim would erase Custom's own changes.")]
	public void TryUpdatePage_Should_Refuse_Replace_Into_A_Non_Default_Layer() {
		// Arrange
		StubCustomLayer();
		PageUpdateCommand sut = BuildCommand(_inspector);

		// Act
		bool ok = sut.TryUpdatePage(Options(enforce: true, targetPackageUId: CustomPackage), out PageUpdateResponse response);

		// Assert
		ok.Should().BeFalse(because: "replace mode is the default and would overwrite Custom's layer");
		response.Error.Should().StartWith(PageUpdateCommand.PackageChoiceReplaceRefusedMarker,
			because: "the agent needs a stable marker to recognise the refusal");
		response.Error.Should().Contain("mode append", because: "the refusal names the safe way forward");
		AssertNotSaved();
	}

	[TestCase(false)]
	[TestCase(true)]
	[Description("A replace into a package with no page layer preserves explicit targeting without requiring force, including dry runs.")]
	public void TryUpdatePage_ShouldAllowReplace_WhenTargetHasNoLayer(bool dryRun) {
		// Arrange
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Is<string>(query => query.Contains(CustomPackage)),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success": true, "rows": []}""");
		PageUpdateCommand sut = BuildCommand(_inspector);
		PageUpdateOptions options = Options(enforce: true, targetPackageUId: CustomPackage);
		options.DryRun = dryRun;

		// Act
		bool ok = sut.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		ok.Should().BeTrue(because: $"a new layer has no existing body to overwrite: {response.Error}");
		if (dryRun) {
			AssertNotSaved();
		} else {
			_applicationClient.Received(1).ExecutePostRequest(SaveSchemaUrl,
				Arg.Is<string>(request => request.Contains(CustomPackage)), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		}
	}

	[Test]
	[Description("force lets a replace into a chosen non-default layer through, once the user confirmed replacing that layer's body.")]
	public void TryUpdatePage_Should_Allow_Replace_Into_A_Non_Default_Layer_With_Force() {
		// Arrange
		StubCustomLayer();
		PageUpdateCommand sut = BuildCommand(_inspector);
		PageUpdateOptions options = Options(enforce: true, targetPackageUId: CustomPackage);
		options.Force = true;

		// Act
		bool ok = sut.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		ok.Should().BeTrue(because: $"force is the explicit opt-in: {response.Error}");
		_applicationClient.Received(1).ExecutePostRequest(SaveSchemaUrl,
			Arg.Is<string>(request => request.Contains(CustomSchemaUId)), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("A replace with target-package-uid fails closed when the default design package cannot be resolved, since the target cannot be confirmed as the default one.")]
	public void TryUpdatePage_Should_Refuse_Replace_When_The_Default_Package_Cannot_Be_Resolved() {
		// Arrange
		_hierarchyClient.GetDesignPackageUId(Arg.Any<string>())
			.Returns(_ => throw new System.InvalidOperationException("designer service unavailable"));
		PageUpdateCommand sut = BuildCommand(_inspector);

		// Act
		bool ok = sut.TryUpdatePage(Options(enforce: true, targetPackageUId: DesignPackage), out PageUpdateResponse response);

		// Assert
		ok.Should().BeFalse(because: "the check fails closed");
		response.Error.Should().StartWith(PageUpdateCommand.PackageChoiceReplaceRefusedMarker)
			.And.Contain("designer service unavailable", because: "the cause is kept");
		AssertNotSaved();
	}

	[Test]
	[Description("Composite tools that do not set EnforcePackageChoice keep the target-package-uid escape hatch in replace mode.")]
	public void TryUpdatePage_Should_Allow_Replace_Into_A_Non_Default_Layer_When_Not_Enforced() {
		// Arrange
		StubCustomLayer();
		PageUpdateCommand sut = BuildCommand(_inspector);

		// Act
		bool ok = sut.TryUpdatePage(Options(enforce: false, targetPackageUId: CustomPackage), out PageUpdateResponse response);

		// Assert
		ok.Should().BeTrue(because: $"the check is opt-in per entry point: {response.Error}");
	}

	[Test]
	[Description("Without a designer hierarchy client the write has no resolution chain (ResolutionHierarchy is null), so the check is skipped rather than run against nothing.")]
	public void TryUpdatePage_Should_Skip_Check_When_There_Is_No_Resolution_Hierarchy() {
		// Arrange
		PageUpdateCommand sut = BuildCommand(_inspector, hierarchyClient: null);

		// Act
		bool ok = sut.TryUpdatePage(Options(enforce: true), out PageUpdateResponse response);

		// Assert
		ok.Should().BeTrue(because: $"there is no chain to inspect: {response.Error}");
		_inspector.DidNotReceiveWithAnyArgs().Inspect(default, default, default);
		_applicationClient.Received().ExecutePostRequest(
			SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("The refusal names a default target that holds no layer yet by its package name, not only by GUID.")]
	public void DescribePackageChoice_Should_Name_A_Default_Target_Without_A_Layer() {
		// Arrange
		PagePackageChoice choice = new() {
			Required = true,
			DefaultTargetPackageUId = DesignPackage,
			DefaultTargetPackageName = "UsrProj",
			Layers = [RequiredChoice.Layers[0]]
		};

		// Act
		string error = PageUpdateCommand.DescribePackageChoice(SchemaName, choice);

		// Assert
		error.Should().Contain($"The default target UsrProj (packageUId {DesignPackage}) holds no replacing layer",
			because: "the user recognises a package by its name");
	}
}
