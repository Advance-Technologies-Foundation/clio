namespace Clio.Tests.Command;

using System.Collections.Generic;
using System.Linq;
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
	/// Stubs the server's stored body. The designer response embeds it as a JSON string value, so it has to
	/// be escaped rather than interpolated raw.
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

	/// <summary>
	/// Asserts the call wrote nothing. The invariant that matters most here: the dry run now routes through
	/// the same <c>TryLoadSchemaForSave</c>/<c>TryResolveBodyToWrite</c> helpers as the save, so falling
	/// through to <c>TrySaveSchema</c> is the one regression that would be unrecoverable in production.
	/// </summary>
	private void AssertNothingWasSaved() =>
		_applicationClient.DidNotReceive().ExecutePostRequest(
			SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());

	[Test]
	[Description("An append dry run reports the projected operation counts instead of a bare success, and writes nothing.")]
	public void TryUpdatePage_WhenAppendDryRun_ReportsTheProjectedCounts() {
		// Arrange
		StubCurrentBody(WebBody("""
			[
				{"operation":"merge","name":"ContactRolesExpansionPanel","values":{"title":"Coverage"}},
				{"operation":"move","name":"ContactRolesExpansionPanel","parentName":"OverviewTab","propertyName":"items","index":3}
			]
			"""));
		PageUpdateOptions options = AppendDryRun(
			"""[{"operation":"insert","name":"UsrNewTab","parentName":"Tabs","propertyName":"items","index":1,"values":{"type":"crt.TabContainer"}}]""");

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(
			because: "the append is valid, and projecting it must not turn a good dry run into a failure");
		response.DryRun.Should().BeTrue(because: "the caller asked for validation only");
		response.AppendProjection.Should().NotBeNull(
			because: "GH-1150: a dry run that cannot say what the write will change is the defect");
		response.AppendProjection.CurrentOperationCount.Should().Be(2,
			because: "the counts must come from the server's stored body, which only a real fetch provides");
		response.AppendProjection.IncomingOperationCount.Should().Be(1,
			because: "the fragment carries one operation");
		response.AppendProjection.ProjectedOperationCount.Should().Be(3,
			because: "the caller must be able to compare this against the count they expect");
		response.AppendProjection.DroppedOperationCount.Should().Be(0,
			because: "nothing in the current body shares an identity the fragment supersedes twice");
		AssertNothingWasSaved();
	}

	[Test]
	[Description("An append dry run warns before the write when an existing operation would be dropped.")]
	public void TryUpdatePage_WhenAppendDryRunWouldDropAnOperation_WarnsAndNamesIt() {
		// Arrange
		StubCurrentBody(WebBody("""
			[
				{"operation":"merge","name":"UsrPanel","values":{"title":"First"}},
				{"operation":"merge","name":"UsrPanel","values":{"visible":true}}
			]
			"""));
		PageUpdateOptions options = AppendDryRun(
			"""[{"operation":"merge","name":"UsrPanel","values":{"title":"Incoming"}}]""");

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "the loss is advisory, not a blocker: the caller decides");
		response.AppendProjection.DroppedOperationCount.Should().Be(1,
			because: "one further current entry of the superseded identity is not carried over");
		response.Warnings.Should().NotBeNull(
			because: "the reporter asked to be told during a dry run that an existing operation would go");
		response.Warnings.Should().Contain(warning => warning.Contains("Append drops 1 existing"),
			because: "the warning must state the loss in the server-body channel, not merely count it");
		response.Warnings.Should().Contain(warning => warning.Contains("merge UsrPanel"),
			because: "the warning must name the operation so the caller knows what to fold");
		AssertNothingWasSaved();
	}

	[Test]
	[Description("GH-1150 review: a dry run warns when the caller's own fragment supersedes one of its own operations.")]
	public void TryUpdatePage_WhenFragmentSupersedesItsOwnOperation_WarnsOnTheCallerSideChannel() {
		// Arrange — the caller's own loss, distinct from a server-body drop, and reported separately because
		// the fix is theirs to make.
		StubCurrentBody(WebBody("[]"));
		PageUpdateOptions options = AppendDryRun("""
			[
				{"operation":"merge","name":"UsrPanel","values":{"title":"A"}},
				{"operation":"merge","name":"UsrPanel","values":{"visible":true}}
			]
			""");

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "the collapse is advisory, like every other projected loss");
		response.AppendProjection.CollapsedIncomingOperationCount.Should().Be(1,
			because: "the fragment names one identity twice, so its earlier entry never reaches the body");
		response.AppendProjection.DroppedOperationCount.Should().Be(0,
			because: "keeping the channels separate is what tells the caller whose fragment to correct");
		response.Warnings.Should().Contain(warning => warning.Contains("Your fragment carries 1"),
			because: "the warning must address the caller's own body rather than blaming the server's");
		AssertNothingWasSaved();
	}

	[Test]
	[Description("GH-1150 review: a dry run warns when the merged viewConfigDiff cannot be written back at all.")]
	public void TryUpdatePage_WhenCurrentBodyHasNoViewConfigDiffSection_WarnsThatEveryOperationIsDiscarded() {
		// Arrange — a current body with no SCHEMA_VIEW_CONFIG_DIFF marker pair. The merged array is silently
		// discarded, so a projection that reported the counts alone would confirm a write that loses
		// everything: strictly worse than the bare success it replaced.
		StubCurrentBody(
			"define(\"" + SchemaName + "\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
			"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
			"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
			"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/ }; });");
		PageUpdateOptions options = AppendDryRun(
			"""[{"operation":"insert","name":"UsrName","parentName":"Main","values":{"type":"crt.Input"}}]""");

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "the merge itself succeeds; what fails is writing the section back");
		response.AppendProjection.ViewConfigDiffApplied.Should().BeFalse(
			because: "the caller must be able to see that the counts describe an array the write discards");
		response.Warnings.Should().Contain(warning => warning.Contains("no SCHEMA_VIEW_CONFIG_DIFF marker pair"),
			because: "a flag alone is easy to miss; the loss has to be stated in the warnings the caller reads");
		response.Warnings.Should().Contain(warning => warning.Contains("--mode replace"),
			because: "the warning must name the way out, since this is not fixable by editing the fragment");
		AssertNothingWasSaved();
	}

	[Test]
	[Description("An append dry run runs the inert-operation detector against the MERGED body, not the fragment alone.")]
	public void TryUpdatePage_WhenAppendDryRunFormsAnInertPairWithTheServerBody_WarnsBeforeTheWrite() {
		// Arrange — the pair exists only in the merged body: the insert is the server's, the merge is the
		// caller's. Against the fragment alone, which is the pre-fix behaviour, there is nothing to see.
		StubCurrentBody(WebBody("""[{"operation":"insert","name":"UsrName","parentName":"Main","values":{"type":"crt.Input"}}]"""));
		PageUpdateOptions options = AppendDryRun(
			"""[{"operation":"merge","name":"UsrName","values":{"visible":false}}]""");

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "an inert operation is advisory, not a blocker");
		response.Warnings.Should().NotBeNull(
			because: "the merge is inert beside the server's insert, and the dry run can now see that");
		response.Warnings.Should().Contain(
			warning => warning.Contains("whole operation groups in a fixed order") && warning.Contains("UsrName"),
			because: "asserting the inert detector's own wording proves WHICH detector fired, not merely that some warning mentions the component");
		AssertNothingWasSaved();
	}

	[Test]
	[Description("An append whose real save could not merge fails the dry run with the same error, marked as a dry run.")]
	public void TryUpdatePage_WhenAppendDryRunCannotMerge_FailsInsteadOfReportingSuccess() {
		// Arrange — a full-config current body cannot be appended to. Before the fix the dry run reported
		// success and the caller only learned this on the real save.
		StubCurrentBody("""
			{
				"viewConfig": { "type": "crt.FlexContainer" },
				"viewModelConfig": {},
				"modelConfig": {}
			}
			""");
		PageUpdateOptions options = AppendDryRun(
			"""[{"operation":"insert","name":"UsrNewTab","parentName":"Tabs","propertyName":"items","index":1,"values":{"type":"crt.TabContainer"}}]""");

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeFalse(
			because: "a dry run that passes an append the save would reject is exactly the false reassurance GH-1150 reported");
		response.Success.Should().BeFalse(because: "the envelope must agree with the return value");
		response.Error.Should().StartWith(PageBodyMerger.MobileCurrentFullConfigNotSupportedMessage,
			because: "AC5 is 'fails with the SAME error' — asserting only that some error exists would also pass if the stub stopped matching and the catch-all fired instead");
		response.Error.Should().Contain("docs://mcp/guides/page-modification",
			because: "the dry run must carry the same corrective pointer the real save appends, not a bare rejection");
		response.DryRun.Should().BeTrue(
			because: "without this the failure is byte-identical to a failed real save and the caller cannot tell whether anything was written");
		response.SchemaName.Should().Be(SchemaName,
			because: "the failure envelope should identify the schema like every successful one does");
		response.AppendProjection.Should().BeNull(
			because: "no merge completed, so there is nothing to project and a stale projection would mislead");
		AssertNothingWasSaved();
	}

	[Test]
	[Description("A dry run whose loss exceeds the naming cap states the remainder rather than truncating silently.")]
	public void TryUpdatePage_WhenAppendDryRunDropsMoreThanTheNamingCap_WarningStatesTheRemainder() {
		// Arrange — 30 components each carrying the same identity twice in the CURRENT body, with a fragment
		// that supersedes all 30: 30 drops against a 25-entry naming cap.
		List<string> currentEntries = [];
		List<string> incomingEntries = [];
		for (int i = 0; i < 30; i++) {
			currentEntries.Add("{\"operation\":\"merge\",\"name\":\"UsrPanel" + i + "\",\"values\":{\"title\":\"A\"}}");
			currentEntries.Add("{\"operation\":\"merge\",\"name\":\"UsrPanel" + i + "\",\"values\":{\"visible\":true}}");
			incomingEntries.Add("{\"operation\":\"merge\",\"name\":\"UsrPanel" + i + "\",\"values\":{\"title\":\"B\"}}");
		}
		StubCurrentBody(WebBody("[" + string.Join(",", currentEntries) + "]"));
		PageUpdateOptions options = AppendDryRun("[" + string.Join(",", incomingEntries) + "]");

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "a large advisory loss is still advisory");
		response.AppendProjection.DroppedOperationCount.Should().Be(30,
			because: "the count is exact even though the named list is capped");
		response.Warnings.Should().Contain(warning => warning.Contains("(+5 more)"),
			because: "a truncated list must name its remainder or it reads as the whole story");
		response.Warnings.Should().Contain(warning => warning.Contains("Append drops 30 existing"),
			because: "the exact scale belongs in the sentence the caller reads, not only in the projection");
		AssertNothingWasSaved();
	}

	[Test]
	[Description("An append against an empty stored body writes the fragment verbatim and reports no projection.")]
	public void TryUpdatePage_WhenAppendDryRunAgainstAnEmptyServerBody_ReportsNoProjection() {
		// Arrange — the merger is never invoked when the stored body is blank, so there is no merge to
		// project. Pinning it because the contract text is read as 'append implies a projection'.
		StubCurrentBody(string.Empty);
		PageUpdateOptions options = AppendDryRun(
			"""[{"operation":"insert","name":"UsrName","parentName":"Main","values":{"type":"crt.Input"}}]""");

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "an append onto an empty body is a verbatim write, not a failure");
		response.DryRun.Should().BeTrue(because: "the caller asked for validation only");
		response.AppendProjection.Should().BeNull(
			because: "no merge ran, and a zeroed projection would read as coverage of a merge that never happened — the documented contract is absence");
		AssertNothingWasSaved();
	}

	[Test]
	[Description("A replace-mode dry run keeps its previous shape: no projection, and no schema fetch.")]
	public void TryUpdatePage_WhenReplaceDryRun_ProjectsNothingAndDoesNotFetchTheSchemaBody() {
		// Arrange — replace writes the body verbatim, so there is nothing to project and no reason to pay for
		// the round trip. sync-pages pins replace and runs at volume, so this has to stay free.
		PageUpdateOptions options = new() {
			SchemaName = SchemaName,
			Body = WebBody("""[{"operation":"insert","name":"UsrName","parentName":"Main","values":{"type":"crt.Input"}}]"""),
			Mode = "replace",
			DryRun = true
		};

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "a valid replace body validates cleanly");
		response.AppendProjection.Should().BeNull(
			because: "a verbatim write has no merge to project, and reporting zeros would read as coverage");
		_applicationClient.DidNotReceive().ExecutePostRequest(
			GetSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		AssertNothingWasSaved();
	}

	[Test]
	[Description("A real append save carries the same projection as the dry run.")]
	public void TryUpdatePage_WhenAppendSaves_CarriesTheProjectionToo() {
		// Arrange
		StubCurrentBody(WebBody("""[{"operation":"merge","name":"UsrPanel","values":{"title":"Old"}}]"""));
		PageUpdateOptions options = new() {
			SchemaName = SchemaName,
			Body = WebBody("""[{"operation":"merge","name":"UsrPanel","values":{"title":"New"}}]"""),
			Mode = "append"
		};

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "the append is valid and the save is stubbed to succeed");
		response.DryRun.Should().BeFalse(because: "this call was a real save");
		response.AppendProjection.Should().NotBeNull(
			because: "the save is the only place a caller who skipped the dry run learns what the merge did");
		response.AppendProjection.ReplacedOperations.Should().Contain("merge UsrPanel",
			because: "the same labels must appear on both paths, or the dry run stops predicting the save");
		_applicationClient.Received().ExecutePostRequest(
			SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("An append dry run and the equivalent real save produce the same warnings for the same input.")]
	public void TryUpdatePage_WhenAppendDryRunAndSaveSeeTheSameInput_ProduceTheSameWarnings() {
		// Arrange — the PR's central claim is that a dry run predicts the save. Two call sites assemble the
		// warning set independently, so nothing but this test stops them drifting apart.
		string currentBody = WebBody("""
			[
				{"operation":"merge","name":"UsrPanel","values":{"title":"First"}},
				{"operation":"merge","name":"UsrPanel","values":{"visible":true}}
			]
			""");
		string incoming = """[{"operation":"merge","name":"UsrPanel","values":{"title":"Incoming"}}]""";
		StubCurrentBody(currentBody);

		// Act
		_command.TryUpdatePage(AppendDryRun(incoming), out PageUpdateResponse dryRunResponse);
		_command.TryUpdatePage(
			new PageUpdateOptions { SchemaName = SchemaName, Body = WebBody(incoming), Mode = "append" },
			out PageUpdateResponse saveResponse);

		// Assert
		dryRunResponse.Warnings.Should().NotBeNull(because: "this input loses an operation on both paths");
		saveResponse.Warnings.Should().NotBeNull(because: "the save reports the same loss");
		saveResponse.Warnings.Should().BeEquivalentTo(dryRunResponse.Warnings,
			because: "a dry run whose warnings differ from the save's is the false-reassurance defect GH-1150 reported, one layer up");
		saveResponse.AppendProjection.Should().BeEquivalentTo(dryRunResponse.AppendProjection,
			because: "the projection is a by-product of the one merge, so both paths must report it identically");
	}
}
