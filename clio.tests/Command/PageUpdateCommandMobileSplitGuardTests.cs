namespace Clio.Tests.Command;

using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

/// <summary>
/// Guards against the web→mobile split defect: when a mobile page's base schema (just created by
/// create-page) lives in a package that is not the design package, update-page would otherwise mint a
/// REPLACING schema in the design package and leave the base empty — which crashes the Creatio Mobile app.
/// The command must fail-closed for that mobile case, while leaving web replacing writes untouched and the
/// target-schema-uid escape hatch working.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageUpdateCommandMobileSplitGuardTests
{
	private const string SelectQueryUrl = "http://test/DataService/json/SyncReply/SelectQuery";
	private const string GetSchemaUrl = "http://test/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema";
	private const string SaveSchemaUrl = "http://test/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema";
	private const string SchemaUId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
	private const string SchemaName = "UsrCases_MobileFormPage";
	private const int WebSchemaType = 9;
	private const int MobileSchemaType = 10;

	private const string MobileBody =
		"{\"viewConfigDiff\":[],\"viewModelConfigDiff\":[],\"modelConfigDiff\":[]}";

	private const string WebBody =
		"define(\"UsrCases_MobileFormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns(SelectQueryUrl);
		_serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema").Returns(GetSchemaUrl);
		_serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema").Returns(SaveSchemaUrl);
		// Name → UId lookup.
		_applicationClient.ExecutePostRequest(
				SelectQueryUrl, Arg.Is<string>(b => !b.Contains("byUId") && !b.Contains("byPackage")),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($$"""{"success": true, "rows": [{"UId": "{{SchemaUId}}"}]}""");
		// No replacing schema exists yet in the design package → resolution takes the create-replacing branch.
		_applicationClient.ExecutePostRequest(
				SelectQueryUrl, Arg.Is<string>(b => b.Contains("byPackage")),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success": true, "rows": []}""");
		_applicationClient.ExecutePostRequest(
				GetSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($$"""{"success": true, "schema": {"body": "old body", "name": "{{SchemaName}}" } }""");
		_applicationClient.ExecutePostRequest(
				SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success": true}""");
	}

	// head lives in "home-pkg" while the design package is "design-pkg" → a mismatch that drives the
	// create-replacing branch. schemaType and body of the head are the discriminators the guard checks.
	private IPageDesignerHierarchyClient HierarchyClient(int headSchemaType, string headBody) {
		IPageDesignerHierarchyClient client = Substitute.For<IPageDesignerHierarchyClient>();
		const string designPackageUId = "design-pkg";
		client.GetDesignPackageUId(SchemaUId).Returns(designPackageUId);
		client.GetParentSchemas(SchemaUId, designPackageUId).Returns([
			new PageDesignerHierarchySchema {
				UId = SchemaUId, Name = SchemaName, PackageUId = "home-pkg",
				PackageName = "CrtCustomer360Mobile", SchemaType = headSchemaType, Body = headBody
			}
		]);
		return client;
	}

	private PageUpdateCommand Command(int headSchemaType, string headBody) =>
		new(_applicationClient, _serviceUrlBuilder, Substitute.For<ILogger>(),
			Substitute.For<IPageBaselineGuard>(), HierarchyClient(headSchemaType, headBody));

	private static PageUpdateOptions Options(string body, string targetSchemaUId = null) =>
		new() { SchemaName = SchemaName, Body = body, TargetSchemaUId = targetSchemaUId };

	[Test]
	[Description("A mobile create-replacing write against a freshly-created (empty-body) base must fail-closed with an actionable target-schema-uid message, and MUST NOT save.")]
	public void TryUpdatePage_MobileCreateReplacingWithEmptyBase_FailsClosed() {
		PageUpdateCommand command = Command(MobileSchemaType, headBody: "");

		bool result = command.TryUpdatePage(Options(MobileBody), out PageUpdateResponse response);

		result.Should().BeFalse();
		response.Success.Should().BeFalse();
		response.Error.Should().Contain("target-schema-uid=" + SchemaUId)
			.And.Contain("CrtCustomer360Mobile");
		_applicationClient.DidNotReceive().ExecutePostRequest(
			SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("A WEB create-replacing write is NOT blocked (replacing schemas are legitimate across apps for web pages) — regression guard.")]
	public void TryUpdatePage_WebCreateReplacing_IsNotBlocked() {
		PageUpdateCommand command = Command(WebSchemaType, headBody: "");

		bool result = command.TryUpdatePage(Options(WebBody), out PageUpdateResponse response);

		result.Should().BeTrue();
		response.Error.Should().BeNullOrEmpty();
		_applicationClient.Received().ExecutePostRequest(
			SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("A mobile create-replacing write against a NON-empty base (a real platform page being replaced) is NOT blocked — the guard is scoped to the empty-base split.")]
	public void TryUpdatePage_MobileCreateReplacingWithNonEmptyBase_IsNotBlocked() {
		PageUpdateCommand command = Command(MobileSchemaType, headBody: MobileBody);

		bool result = command.TryUpdatePage(Options(MobileBody), out PageUpdateResponse response);

		result.Should().BeTrue();
		response.Error.Should().BeNullOrEmpty();
		_applicationClient.Received().ExecutePostRequest(
			SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("With target-schema-uid the guard is bypassed entirely (hierarchy resolution is skipped): the body is written into the given schema.")]
	public void TryUpdatePage_MobileWithTargetSchemaUid_WritesInPlace() {
		PageUpdateCommand command = Command(MobileSchemaType, headBody: "");

		bool result = command.TryUpdatePage(Options(MobileBody, targetSchemaUId: SchemaUId), out PageUpdateResponse response);

		result.Should().BeTrue();
		response.Error.Should().BeNullOrEmpty();
		_applicationClient.Received().ExecutePostRequest(
			SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}
}
