namespace Clio.Tests.Command;

using System;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

/// <summary>
/// ENG-94418 (R4): the page-schema-hierarchy READ-failure paths of <c>get-page</c>
/// (<see cref="PageGetCommand.TryGetPage"/>) and <c>update-page</c>
/// (<see cref="PageUpdateCommand"/>'s <c>TryGetHierarchy</c>) surface an actionable phantom-cache
/// recovery hint when the server error carries the empty-IN() SqlException signature
/// (<c>Incorrect syntax near ')'</c>) or the hierarchy comes back empty — both are symptoms of the
/// Creatio schema-manager cache holding a phantom for a section whose concurrent creation was
/// abandoned. The hint escalates to "Restart Creatio" as the guaranteed recovery. Additive only: a
/// generic hierarchy failure is unaffected, and the existing save-path AppendActionableHint branches
/// are unchanged.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageHierarchyRecoveryHintTests {

	private const string SelectQueryUrl = "http://test/DataService/json/SyncReply/SelectQuery";
	private const string GetSchemaUrl = "http://test/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema";
	private const string SaveSchemaUrl = "http://test/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema";
	private const string SchemaName = "UsrOrders_FormPage";
	private const string SchemaUId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
	private const string DesignPackageUId = "design-pkg-uid";

	// The empty-IN() SqlException the Creatio server emits when the schema-manager cache holds a phantom
	// for a section whose concurrent creation was abandoned: the parent set is empty, so the server builds
	// `... IN ()` and SQL Server rejects it near the ')'.
	private const string EmptyInServerError =
		"Failed to load page schema hierarchy: SqlException: Incorrect syntax near ')'.";

	private const string ValidBody =
		"define(\"UsrOrders_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private ILogger _logger;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_logger = Substitute.For<ILogger>();
		_serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns(SelectQueryUrl);
		_serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema").Returns(GetSchemaUrl);
		_serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema").Returns(SaveSchemaUrl);
	}

	// ---- update-page (PageUpdateCommand.TryGetHierarchy) ----

	[Test]
	[Description("update-page surfaces the phantom-cache recovery hint (escalating to a guaranteed Restart Creatio) when the hierarchy read fails with the empty-IN() SqlException signature.")]
	public void TryUpdatePage_ShouldAppendPhantomCacheRecoveryHint_WhenHierarchyReadFailsWithEmptyInSignature() {
		// Arrange
		StubNameMetadata();
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId(SchemaUId).Returns(DesignPackageUId);
		hierarchyClient.GetParentSchemas(SchemaUId, DesignPackageUId)
			.Returns(_ => throw new InvalidOperationException(EmptyInServerError));
		PageUpdateCommand command = CreateUpdateCommand(hierarchyClient);

		// Act
		bool result = command.TryUpdatePage(CreateUpdateOptions(), out PageUpdateResponse response);

		// Assert
		result.Should().BeFalse(because: "a poisoned-cache hierarchy read must fail the update");
		AssertPhantomCacheHint(response.Error);
		response.Error.Should().Contain("Incorrect syntax near ')'",
			because: "the original server error must be preserved alongside the appended hint");
	}

	[Test]
	[Description("update-page surfaces the phantom-cache recovery hint when the hierarchy read returns an empty hierarchy (the other poisoned-cache symptom).")]
	public void TryUpdatePage_ShouldAppendPhantomCacheRecoveryHint_WhenHierarchyIsEmpty() {
		// Arrange
		StubNameMetadata();
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId(SchemaUId).Returns(DesignPackageUId);
		hierarchyClient.GetParentSchemas(SchemaUId, DesignPackageUId).Returns([]);
		PageUpdateCommand command = CreateUpdateCommand(hierarchyClient);

		// Act
		bool result = command.TryUpdatePage(CreateUpdateOptions(), out PageUpdateResponse response);

		// Assert
		result.Should().BeFalse(because: "an empty hierarchy cannot resolve an editable schema, so the update fails");
		response.Error.Should().Contain("hierarchy is empty",
			because: "the original empty-hierarchy message must be preserved alongside the appended hint");
		AssertPhantomCacheHint(response.Error);
	}

	[Test]
	[Description("Additive only: a GENERIC hierarchy read failure (no empty-IN() signature, non-empty message) is left unchanged — no phantom-cache hint is appended, so the hint stays scoped to the poisoned-cache symptoms.")]
	public void TryUpdatePage_ShouldNotAppendHint_WhenHierarchyReadFailsWithUnrelatedError() {
		// Arrange
		StubNameMetadata();
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId(SchemaUId).Returns(DesignPackageUId);
		hierarchyClient.GetParentSchemas(SchemaUId, DesignPackageUId)
			.Returns(_ => throw new InvalidOperationException("Failed to load page schema hierarchy: 503 Service Unavailable"));
		PageUpdateCommand command = CreateUpdateCommand(hierarchyClient);

		// Act
		bool result = command.TryUpdatePage(CreateUpdateOptions(), out PageUpdateResponse response);

		// Assert
		result.Should().BeFalse(because: "the hierarchy read still failed");
		response.Error.Should().Contain("503 Service Unavailable",
			because: "the original unrelated error must be surfaced");
		response.Error.Should().NotContain("ENG-94418",
			because: "the phantom-cache recovery hint must NOT fire for a generic hierarchy failure — it is scoped to the poisoned-cache signatures");
	}

	[Test]
	[Description("Existing save-path hints unchanged: an 'Item with name not found' SaveSchema error still yields the original AppendActionableHint save-path phantom hint and does NOT pick up the new hierarchy-read recovery hint.")]
	public void TryUpdatePage_ShouldKeepExistingSaveHintUnchanged_WhenSaveFailsWithItemNotFound() {
		// Arrange — full happy path up to SaveSchema, which returns the 'Item with name ... not found' error.
		StubNameMetadata();
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId(SchemaUId).Returns(DesignPackageUId);
		hierarchyClient.GetParentSchemas(SchemaUId, DesignPackageUId).Returns([
			new PageDesignerHierarchySchema { UId = SchemaUId, Name = SchemaName, PackageUId = DesignPackageUId }
		]);
		_applicationClient.ExecutePostRequest(GetSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($$"""{"success": true, "schema": {"body": "old body", "name": "{{SchemaName}}" } }""");
		_applicationClient.ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success": false, "errorInfo": {"message": "Item with name 'UsrOrders_FormPage' not found."}}""");
		PageUpdateCommand command = CreateUpdateCommand(hierarchyClient);

		// Act
		bool result = command.TryUpdatePage(CreateUpdateOptions(), out PageUpdateResponse response);

		// Assert
		result.Should().BeFalse(because: "the SaveSchema call reported a failure");
		response.Error.Should().Contain("stale phantom replacing schema from an earlier failed save",
			because: "the existing save-path AppendActionableHint branch must remain byte-for-byte unchanged");
		response.Error.Should().NotContain("ENG-94418",
			because: "the new hierarchy-read recovery hint must not leak into the unrelated save-error path");
	}

	// ---- get-page (PageGetCommand.TryGetPage) ----

	[Test]
	[Description("get-page surfaces the phantom-cache recovery hint when the hierarchy read fails with the empty-IN() SqlException signature.")]
	public void TryGetPage_ShouldAppendPhantomCacheRecoveryHint_WhenHierarchyReadFailsWithEmptyInSignature() {
		// Arrange
		StubGetPageMetadata();
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId(SchemaUId).Returns(DesignPackageUId);
		hierarchyClient.GetParentSchemas(SchemaUId, DesignPackageUId)
			.Returns(_ => throw new InvalidOperationException(EmptyInServerError));
		PageGetCommand command = CreateGetCommand(hierarchyClient);

		// Act
		bool result = command.TryGetPage(new PageGetOptions { SchemaName = SchemaName }, out PageGetResponse response);

		// Assert
		result.Should().BeFalse(because: "a poisoned-cache hierarchy read must fail the get-page");
		response.Error.Should().Contain("Incorrect syntax near ')'",
			because: "the original server error must be preserved alongside the appended hint");
		AssertPhantomCacheHint(response.Error);
	}

	[Test]
	[Description("get-page surfaces the phantom-cache recovery hint when the hierarchy read returns an empty hierarchy.")]
	public void TryGetPage_ShouldAppendPhantomCacheRecoveryHint_WhenHierarchyIsEmpty() {
		// Arrange
		StubGetPageMetadata();
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId(SchemaUId).Returns(DesignPackageUId);
		hierarchyClient.GetParentSchemas(SchemaUId, DesignPackageUId).Returns([]);
		PageGetCommand command = CreateGetCommand(hierarchyClient);

		// Act
		bool result = command.TryGetPage(new PageGetOptions { SchemaName = SchemaName }, out PageGetResponse response);

		// Assert
		result.Should().BeFalse(because: "an empty hierarchy cannot be read into a page bundle");
		response.Error.Should().Contain("hierarchy is empty",
			because: "the original empty-hierarchy message must be preserved alongside the appended hint");
		AssertPhantomCacheHint(response.Error);
	}

	// ---- helpers ----

	private static void AssertPhantomCacheHint(string error) {
		error.Should().Contain("ENG-94418",
			because: "the appended hint must attribute the failure to the abandoned-concurrent-create cache phantom (ENG-94418)");
		error.Should().Contain("Restart Creatio",
			because: "the hint must escalate to Restart Creatio as the guaranteed recovery");
	}

	private void StubNameMetadata() =>
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($$"""{"success": true, "rows": [{"UId": "{{SchemaUId}}"}]}""");

	private void StubGetPageMetadata() =>
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($$"""{"success":true,"rows":[{"Name":"{{SchemaName}}","UId":"{{SchemaUId}}","PackageName":"UsrPkg","PackageUId":"{{DesignPackageUId}}","ParentSchemaName":"BasePage"}]}""");

	private PageUpdateCommand CreateUpdateCommand(IPageDesignerHierarchyClient hierarchyClient) =>
		new(_applicationClient, _serviceUrlBuilder, _logger, Substitute.For<IPageBaselineGuard>(), hierarchyClient);

	private PageGetCommand CreateGetCommand(IPageDesignerHierarchyClient hierarchyClient) =>
		new(_applicationClient, _serviceUrlBuilder, _logger, hierarchyClient,
			new PageSchemaBodyParser(),
			new PageBundleBuilder(new PageJsonDiffApplier(), new PageJsonPathDiffApplier()),
			Substitute.For<IPageFileWriter>());

	private static PageUpdateOptions CreateUpdateOptions() =>
		new() { SchemaName = SchemaName, Body = ValidBody };
}
