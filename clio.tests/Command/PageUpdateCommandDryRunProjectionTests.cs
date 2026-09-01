namespace Clio.Tests.Command;

using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

/// <summary>
/// What <c>update-page --dry-run</c> reports about an append merge (GitHub #1150). Before the fix the
/// dry-run branch returned before the merge ran, so it could not name a single thing the write would
/// change — and its body checks inspected the incoming fragment rather than the body that would be saved.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageUpdateCommandDryRunProjectionTests {

	private const string SelectQueryUrl = "http://test/DataService/json/SyncReply/SelectQuery";
	private const string GetSchemaUrl = "http://test/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema";
	private const string SaveSchemaUrl = "http://test/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema";
	private const string SchemaUId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
	private const string SchemaName = "Test_FormPage";

	private IApplicationClient _applicationClient;
	private PageUpdateCommand _command;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns(SelectQueryUrl);
		serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema").Returns(GetSchemaUrl);
		serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema").Returns(SaveSchemaUrl);
		_applicationClient.ExecutePostRequest(
				SelectQueryUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($$"""{"success": true, "rows": [{"UId": "{{SchemaUId}}"}]}""");
		_applicationClient.ExecutePostRequest(
				SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success": true}""");
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId(SchemaUId).Returns("test-pkg-uid");
		hierarchyClient.GetParentSchemas(SchemaUId, "test-pkg-uid").Returns([
			new PageDesignerHierarchySchema { UId = SchemaUId, Name = SchemaName, PackageUId = "test-pkg-uid" }
		]);
		_command = new PageUpdateCommand(
			_applicationClient, serviceUrlBuilder, Substitute.For<ILogger>(),
			Substitute.For<IPageBaselineGuard>(), hierarchyClient);
	}

	private static string WebBody(string viewConfigDiffInner) =>
		"define(\"" + SchemaName + "\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/" + viewConfigDiffInner + "/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	/// <summary>
	/// Stubs the server's stored body. The designer response embeds the body as a JSON string value, so it
	/// has to be escaped rather than interpolated raw.
	/// </summary>
	private void StubCurrentBody(string body) {
		string escaped = Newtonsoft.Json.JsonConvert.ToString(body);
		_applicationClient.ExecutePostRequest(
				GetSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($$"""{"success": true, "schema": {"body": {{escaped}}, "name": "{{SchemaName}}" } }""");
	}

	private static PageUpdateOptions AppendDryRun(string incomingViewConfigDiff) =>
		new() {
			SchemaName = SchemaName,
			Body = WebBody(incomingViewConfigDiff),
			Mode = "append",
			DryRun = true
		};

	[Test]
	[Description("An append dry run reports the projected operation counts instead of a bare success.")]
	public void TryUpdatePage_WhenAppendDryRun_ReportsTheProjectedCounts() {
		StubCurrentBody(WebBody("""
			[
				{"operation":"merge","name":"ContactRolesExpansionPanel","values":{"title":"Coverage"}},
				{"operation":"move","name":"ContactRolesExpansionPanel","parentName":"OverviewTab","propertyName":"items","index":3}
			]
			"""));
		PageUpdateOptions options = AppendDryRun(
			"""[{"operation":"insert","name":"UsrNewTab","parentName":"Tabs","propertyName":"items","index":1,"values":{"type":"crt.TabContainer"}}]""");

		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		result.Should().BeTrue(because: "the append is valid — projecting it must not turn a good dry run into a failure");
		response.DryRun.Should().BeTrue();
		response.AppendProjection.Should().NotBeNull(
			because: "GH-1150: a dry run that cannot say what the write will change is the defect");
		response.AppendProjection.CurrentOperationCount.Should().Be(2);
		response.AppendProjection.IncomingOperationCount.Should().Be(1);
		response.AppendProjection.ProjectedOperationCount.Should().Be(3,
			because: "the caller must be able to compare this against the count they expect");
		response.AppendProjection.DroppedOperationCount.Should().Be(0);
	}

	[Test]
	[Description("An append dry run warns before the write when an existing operation would be dropped.")]
	public void TryUpdatePage_WhenAppendDryRunWouldDropAnOperation_WarnsAndNamesIt() {
		StubCurrentBody(WebBody("""
			[
				{"operation":"merge","name":"UsrPanel","values":{"title":"First"}},
				{"operation":"merge","name":"UsrPanel","values":{"visible":true}}
			]
			"""));
		PageUpdateOptions options = AppendDryRun(
			"""[{"operation":"merge","name":"UsrPanel","values":{"title":"Incoming"}}]""");

		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		result.Should().BeTrue(because: "the loss is advisory, not a blocker — the caller decides");
		response.AppendProjection.DroppedOperationCount.Should().Be(1);
		response.Warnings.Should().NotBeNull(
			because: "the reporter asked to be told during dry-run that an existing operation would go");
		response.Warnings.Should().Contain(warning => warning.Contains("merge UsrPanel"),
			because: "the warning must name the operation, not just count it");
	}

	[Test]
	[Description("An append dry run runs the body detectors against the MERGED body, not the fragment alone.")]
	public void TryUpdatePage_WhenAppendDryRunFormsAnInertPairWithTheServerBody_WarnsBeforeTheWrite() {
		// The pair only exists in the merged body: the insert is the server's, the merge is the caller's.
		// Against the fragment alone — the pre-fix behaviour — there is nothing to see.
		StubCurrentBody(WebBody("""[{"operation":"insert","name":"UsrName","parentName":"Main","values":{"type":"crt.Input"}}]"""));
		PageUpdateOptions options = AppendDryRun(
			"""[{"operation":"merge","name":"UsrName","values":{"visible":false}}]""");

		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		result.Should().BeTrue();
		response.Warnings.Should().NotBeNull(
			because: "the merge is inert beside the server's insert, and the dry run can now see that");
		response.Warnings.Should().Contain(warning => warning.Contains("UsrName"),
			because: "the inert-operation warning must name the component whose transform will not run");
	}

	[Test]
	[Description("An append whose real save could not merge now fails the dry run with the same error.")]
	public void TryUpdatePage_WhenAppendDryRunCannotMerge_FailsInsteadOfReportingSuccess() {
		// A full-config current body cannot be appended to. Before the fix the dry run reported success and
		// the caller only learned this on the real save.
		StubCurrentBody("""
			{
				"viewConfig": { "type": "crt.FlexContainer" },
				"viewModelConfig": {},
				"modelConfig": {}
			}
			""");
		PageUpdateOptions options = AppendDryRun(
			"""[{"operation":"insert","name":"UsrNewTab","parentName":"Tabs","propertyName":"items","index":1,"values":{"type":"crt.TabContainer"}}]""");

		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		result.Should().BeFalse(
			because: "a dry run that passes an append the save would reject is exactly the false reassurance GH-1150 reported");
		response.Success.Should().BeFalse();
		response.Error.Should().NotBeNullOrWhiteSpace();
	}

	[Test]
	[Description("A replace-mode dry run keeps its previous shape: no projection, and no schema fetch.")]
	public void TryUpdatePage_WhenReplaceDryRun_ProjectsNothingAndDoesNotFetchTheSchemaBody() {
		// Replace writes the body verbatim, so there is nothing to project and no reason to pay for the
		// round trip. sync-pages pins replace and runs at volume, so this has to stay free.
		PageUpdateOptions options = new() {
			SchemaName = SchemaName,
			Body = WebBody("""[{"operation":"insert","name":"UsrName","parentName":"Main","values":{"type":"crt.Input"}}]"""),
			Mode = "replace",
			DryRun = true
		};

		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		result.Should().BeTrue();
		response.AppendProjection.Should().BeNull(
			because: "a verbatim write has no merge to project — reporting zeros would read as coverage");
		_applicationClient.DidNotReceive().ExecutePostRequest(
			GetSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("A real append save carries the same projection as the dry run.")]
	public void TryUpdatePage_WhenAppendSaves_CarriesTheProjectionToo() {
		StubCurrentBody(WebBody("""[{"operation":"merge","name":"UsrPanel","values":{"title":"Old"}}]"""));
		PageUpdateOptions options = new() {
			SchemaName = SchemaName,
			Body = WebBody("""[{"operation":"merge","name":"UsrPanel","values":{"title":"New"}}]"""),
			Mode = "append"
		};

		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		result.Should().BeTrue();
		response.DryRun.Should().BeFalse();
		response.AppendProjection.Should().NotBeNull(
			because: "the save is the only place a caller who skipped the dry run learns what the merge did");
		response.AppendProjection.ReplacedOperations.Should().Contain("merge UsrPanel");
	}
}
