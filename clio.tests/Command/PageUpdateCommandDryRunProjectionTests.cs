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
			Substitute.For<IPageBaselineGuard>(), Substitute.For<IPersistedResourceKeyReader>(),
			hierarchyClient);
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

	/// <summary>
	/// Stubs the server's stored body together with its registered <c>localizableStrings</c>. This is the
	/// only shape that distinguishes the save's authoritative caption gate from the fragment-scoped check it
	/// replaced: with an empty registration set both consult the same data and agree by accident.
	/// </summary>
	private void StubCurrentBodyWithRegisteredString(string body, string resourceKey) {
		string escaped = Newtonsoft.Json.JsonConvert.ToString(body);
		_applicationClient.ExecutePostRequest(
				GetSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($$"""
				{"success": true, "schema": {"body": {{escaped}}, "name": "{{SchemaName}}",
				 "localizableStrings": [{"name": "{{resourceKey}}", "value": "Already registered"}] } }
				""");
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
	public void TryUpdatePage_ShouldReportTheProjectedCounts_WhenAppendDryRun() {
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
	public void TryUpdatePage_ShouldWarnAndNameTheOperation_WhenAppendDryRunWouldDropOne() {
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
		response.Warnings.Should().Contain(warning => warning.Contains("'UsrPanel'"),
			because: "the caller cannot act on a warning that does not name the component");
		response.Warnings.Should().Contain(warning => warning.Contains("get-page"),
			because: "the warning must say how to recover, not merely that something happened");
		response.AppendProjection.DroppedOperations.Should().Contain("merge UsrPanel",
			because: "the structured label is the machine-readable view of the same loss the sentence describes");
		AssertNothingWasSaved();
	}

	[Test]
	[Description("GH-1150 review: a dry run COUNTS an operation the caller's own fragment supersedes, without warning about it.")]
	public void TryUpdatePage_ShouldCountItWithoutWarning_WhenFragmentSupersedesItsOwnOperation() {
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
		response.Warnings.Should().BeNull(
			because: "the collapse is reported as DATA only: the fragment is the caller's own and they can read it, so a warning about their own input would be noise (the rule the superseded-drop warning was set with). Nothing else about this input warns, so the list is absent entirely");
		AssertNothingWasSaved();
	}

	[Test]
	[Description("GH-1150 review: a dry run warns when the merged viewConfigDiff cannot be written back at all.")]
	public void TryUpdatePage_ShouldWarnThatEveryOperationIsDiscarded_WhenCurrentBodyHasNoViewConfigDiffSection() {
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
	public void TryUpdatePage_ShouldWarnBeforeTheWrite_WhenAppendDryRunFormsAnInertPairWithTheServerBody() {
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
	public void TryUpdatePage_ShouldFailWithTheSaveError_WhenAppendDryRunCannotMerge() {
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
	[Description("A dry run whose loss exceeds the naming cap keeps the count exact and truncates only the labels.")]
	public void TryUpdatePage_ShouldKeepTheCountExact_WhenAppendDryRunDropsMoreThanTheNamingCap() {
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
			because: "the count is exact and unbounded even though the named list is capped");
		response.AppendProjection.DroppedOperations.Should().HaveCount(25,
			because: "only the labels are capped, so a pathological body cannot bury the response while the count still reports the true scale");
		response.Warnings.Should().HaveCount(30,
			because: "the merge emits one actionable sentence per dropped IDENTITY, and all 30 identities are distinct here");
		// The REASON the sentences must stay uncapped, pinned rather than left to a comment: a component past
		// the naming cap is named in the warnings and NOWHERE else, so capping both would leave the response
		// reporting 30 drops while five of those components appear nowhere in it. Review has already proposed
		// capping this for symmetry with droppedOperations once.
		response.AppendProjection.DroppedOperations.Should().NotContain(label => label.Contains("UsrPanel29"),
			because: "the label list is truncated, so it is not where a component past the cap can be recovered");
		response.Warnings.Should().Contain(warning => warning.Contains("UsrPanel29"),
			because: "the sentences are the ONLY place a component past the naming cap is still named");
		AssertNothingWasSaved();
	}

	[Test]
	[Description("An append against an empty stored body writes the fragment verbatim and reports no projection.")]
	public void TryUpdatePage_ShouldReportNoProjection_WhenAppendDryRunRunsAgainstAnEmptyServerBody() {
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
	[Description("An append dry run reports the SAVE's caption gate, on the same body, as a warning rather than a refusal.")]
	public void TryUpdatePage_ShouldReportTheSaveCaptionGateAsAWarning_WhenAppendDryRun() {
		// Arrange — an inserted widget whose caption binds an unregistered, NON-Usr, non-DS-bound localizable
		// key. The Usr prefix matters: those are auto-provided and would pass. The save REFUSES this shape;
		// before the gate was unified the dry run ran a weaker, fragment-scoped variant and the two could
		// disagree in both directions.
		StubCurrentBody(WebBody("[]"));
		const string insertWithUnregisteredCaption =
			"""[{"operation":"insert","name":"ProbeLabel","parentName":"MainContainer","propertyName":"items","index":0,"values":{"type":"crt.Label","caption":"#ResourceString(ProbeLabel_caption)#"}}]""";

		// Act
		bool dryRunResult = _command.TryUpdatePage(AppendDryRun(insertWithUnregisteredCaption), out PageUpdateResponse dryRunResponse);
		bool saveResult = _command.TryUpdatePage(
			new PageUpdateOptions { SchemaName = SchemaName, Body = WebBody(insertWithUnregisteredCaption), Mode = "append" },
			out PageUpdateResponse saveResponse);

		// Assert
		saveResult.Should().BeFalse(because: "the save's caption gate blocks an unregistered inserted caption");
		saveResponse.Error.Should().Contain("unregistered localizable strings",
			because: "this test is only meaningful if the save really does refuse this body");
		dryRunResult.Should().BeTrue(
			because: "a dry run reports what would happen rather than refusing - severity is the only difference between the paths");
		dryRunResponse.Warnings.Should().Contain(warning => warning.Contains("unregistered localizable strings"),
			because: "the preview must state exactly what the save would refuse, in the save's own words; a dry run that stays silent here is the false reassurance this ticket exists to remove");
		AssertNothingWasSaved();
	}

	[Test]
	[Description("A replace-mode dry run keeps its previous shape: no projection, and no schema fetch.")]
	public void TryUpdatePage_ShouldProjectNothing_WhenReplaceDryRun() {
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
	public void TryUpdatePage_ShouldCarryTheProjectionToo_WhenAppendSaves() {
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
	public void TryUpdatePage_ShouldProduceTheSameWarnings_WhenAppendDryRunAndSaveSeeTheSameInput() {
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
	[Test]
	[Description("A dry run that fails validation BEFORE the mode is branched on still reports dryRun: true.")]
	public void TryUpdatePage_ShouldStillReportTheFailureAsADryRun_WhenValidationFailsBeforeTheModeBranch() {
		// Arrange - a body with no marker pairs at all. Marker-integrity validation runs upstream of the
		// dry-run branch, so this failure never reaches TryCompleteDryRun and was returning dryRun: false:
		// byte-identical to a failed real save, which is exactly the distinction GH-1150 set out to establish.
		PageUpdateOptions options = new() {
			SchemaName = SchemaName,
			Body = """[{"operation":"merge","name":"UsrPanel","values":{"title":"New"}}]""",
			Mode = "replace",
			DryRun = true
		};

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeFalse(because: "a body with no marker pairs cannot be written");
		response.DryRun.Should().BeTrue(
			because: "every failure exit of a dry run must say so, not only the ones downstream of the mode branch");
		response.SchemaName.Should().Be(SchemaName,
			because: "the caller has to be able to tell WHICH schema the rejected call was aimed at");
		AssertNothingWasSaved();
	}

	[Test]
	[Description("The same validation failure on a real save is NOT mislabelled as a dry run.")]
	public void TryUpdatePage_ShouldNotReportTheFailureAsADryRun_WhenTheSameValidationFailsOnASave() {
		// Arrange - the guard is `options.DryRun`, so the pairing matters: stamping unconditionally would make
		// every failed save claim nothing was written, inverting the defect instead of fixing it.
		PageUpdateOptions options = new() {
			SchemaName = SchemaName,
			Body = """[{"operation":"merge","name":"UsrPanel","values":{"title":"New"}}]""",
			Mode = "replace",
			DryRun = false
		};

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeFalse(because: "a body with no marker pairs cannot be written");
		response.DryRun.Should().BeFalse(
			because: "a real save that failed must never claim the safety of a dry run");
		AssertNothingWasSaved();
	}
	[Test]
	[Description("An append dry run REFUSES a body with no recognizable section instead of reporting a clean no-op.")]
	public void TryUpdatePage_ShouldRefuse_WhenTheAppendBodyCarriesNoRecognizableSection() {
		// Arrange - the reported hole: a bare operation list is valid JavaScript, and append skips marker-
		// integrity validation, so every section read as empty and the dry run answered
		// `success: true, incoming: 0` - a confident "nothing will change" for a body that was never understood.
		StubCurrentBody(WebBody("""[{"operation":"merge","name":"UsrPanel","values":{"title":"Old"}}]"""));
		PageUpdateOptions options = new() {
			SchemaName = SchemaName,
			Body = """[{"operation":"merge","name":"UsrPanel","values":{"title":"New"}}]""",
			Mode = "append",
			DryRun = true
		};

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeFalse(
			because: "silently discarding the caller's entire fragment is the defect this projection exists to prevent");
		response.AppendProjection.Should().BeNull(
			because: "a projection for a body that was never understood would be the confident lie, not the fix");
		response.DryRun.Should().BeTrue(because: "a refused dry run must still say it was a dry run");
		AssertNothingWasSaved();
	}

	[Test]
	[Description("A real append save refuses the same unrecognizable body, and writes nothing.")]
	public void TryUpdatePage_ShouldRefuseAndNotSave_WhenTheAppendSaveBodyCarriesNoRecognizableSection() {
		// Arrange - the save mattered more than the dry run here: it would have written the CURRENT body back
		// unchanged and reported success, so the caller's fragment vanished with a green result.
		StubCurrentBody(WebBody("""[{"operation":"merge","name":"UsrPanel","values":{"title":"Old"}}]"""));
		PageUpdateOptions options = new() {
			SchemaName = SchemaName,
			Body = """[{"operation":"merge","name":"UsrPanel","values":{"title":"New"}}]""",
			Mode = "append",
			DryRun = false
		};

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeFalse(because: "the body carries no section the merge could read");
		response.Error.Should().Contain("no recognizable page section",
			because: "the caller has to learn their body was not understood, not that their merge was a no-op");
		AssertNothingWasSaved();
	}

	[Test]
	[Description("A proper fragment is unaffected by the recognizability rule.")]
	public void TryUpdatePage_ShouldStillProject_WhenTheAppendBodyCarriesAMarkedSection() {
		// Arrange - the regression guard for the rule itself: the normal append path must stay untouched.
		StubCurrentBody(WebBody("""[{"operation":"merge","name":"UsrPanel","values":{"title":"Old"}}]"""));

		// Act
		bool result = _command.TryUpdatePage(
			AppendDryRun("""[{"operation":"merge","name":"UsrOther","values":{"title":"New"}}]"""),
			out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "a fragment carrying a real section marker is exactly what append is for");
		response.AppendProjection.ProjectedOperationCount.Should().Be(2,
			because: "the rule rejects unreadable bodies, and must not narrow what a valid append can do");
	}
	[Test]
	[Description("A caption bound to a string ALREADY registered on the server does not warn on a dry run.")]
	public void TryUpdatePage_ShouldNotWarnAboutTheCaption_WhenTheKeyIsAlreadyRegisteredOnTheServer() {
		// Arrange - the direction that actually proves the gates were unified. The OLD dry-run check resolved
		// only against the resources passed on the call, so a key the server already carries looked
		// unregistered and warned, while the save accepted it. Same fragment, no explicit resources.
		const string resourceKey = "ProbeLabel_caption";
		StubCurrentBodyWithRegisteredString(WebBody("[]"), resourceKey);
		string insertBoundToRegisteredKey =
			"[{\"operation\":\"insert\",\"name\":\"ProbeLabel\",\"parentName\":\"Main\","
			+ "\"values\":{\"type\":\"crt.Label\",\"caption\":\"#ResourceString(" + resourceKey + ")#\"}}]";

		// Act
		bool result = _command.TryUpdatePage(AppendDryRun(insertBoundToRegisteredKey), out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "the caption resolves against a string the server already registers");
		(response.Warnings ?? []).Should().NotContain(w => w.Contains("unregistered localizable strings"),
			because: "warning here is the false positive the old fragment-scoped check produced, and the save never agreed with it");
		AssertNothingWasSaved();
	}

	[Test]
	[Description("The dry run forwards the save's caption error verbatim, not merely similar wording.")]
	public void TryUpdatePage_ShouldForwardTheSaveCaptionErrorVerbatim_WhenAppendDryRun() {
		// Arrange - "in the save's own words" is the claim; a substring match on both sides would still pass
		// if the two paths drifted into differently-worded messages that happen to share a phrase.
		StubCurrentBody(WebBody("[]"));
		const string insertWithUnregisteredCaption =
			"""[{"operation":"insert","name":"ProbeLabel","parentName":"Main","values":{"type":"crt.Label","caption":"#ResourceString(ProbeLabel_caption)#"}}]""";

		// Act
		_command.TryUpdatePage(AppendDryRun(insertWithUnregisteredCaption), out PageUpdateResponse dryRunResponse);
		_command.TryUpdatePage(
			new PageUpdateOptions { SchemaName = SchemaName, Body = WebBody(insertWithUnregisteredCaption), Mode = "append" },
			out PageUpdateResponse saveResponse);

		// Assert
		saveResponse.Success.Should().BeFalse(because: "the save refuses an unregistered caption");
		dryRunResponse.Warnings.Should().ContainSingle(w => w == saveResponse.Error,
			because: "BuildCaptionGateWarnings forwards the gate's Error unchanged, so the two can never drift apart");
	}

	[Test]
	[Description("A mobile append is unaffected by the marker-recognizability rule, which is web-only.")]
	public void TryUpdatePage_ShouldProjectAndNotRefuse_WhenAppendDryRunTargetsAMobilePage() {
		// Arrange - a mobile body is a JSON object and carries NO markers at all, so if the recognizability
		// rule were ever lifted out of ValidateWebInput into the shared ValidateInput, every mobile append
		// would start failing. Nothing else in the suite would catch that.
		StubCurrentBody("""{"viewConfigDiff":[{"operation":"merge","name":"UsrPanel","values":{"title":"Old"}}]}""");
		PageUpdateOptions options = new() {
			SchemaName = SchemaName,
			Body = """{"viewConfigDiff":[{"operation":"merge","name":"UsrOther","values":{"title":"New"}}]}""",
			Mode = "append",
			DryRun = true
		};

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "a markerless mobile fragment is the normal shape, not a malformed body");
		(response.Warnings ?? []).Should().NotContain(w => w.Contains("no recognizable page section"),
			because: "the rule is web-only by placement, and that placement is what this pins");
		AssertNothingWasSaved();
	}

	[Test]
	[Description("A fragment whose only marker is an AMD-envelope section is refused, not silently discarded.")]
	public void TryUpdatePage_ShouldRefuse_WhenTheAppendBodyCarriesOnlyAnAmdEnvelopeSection() {
		// Arrange - SCHEMA_DEPS is a REQUIRED marker but the merge never reads it from an incoming body, so
		// the first version of the recognizability rule accepted this and still discarded everything.
		// Reproduced on a live stand before this was narrowed: `success: true, incomingOperationCount: 0`.
		StubCurrentBody(WebBody("""[{"operation":"merge","name":"UsrPanel","values":{"title":"Old"}}]"""));
		PageUpdateOptions options = new() {
			SchemaName = SchemaName,
			Body = """define("X", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function(){ return {}; });""",
			Mode = "append",
			DryRun = true
		};

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeFalse(
			because: "recognizability must mean 'the merge can read something from it', not 'it has any marker'");
		response.Error.Should().Contain("no recognizable page section",
			because: "asserting only the failure would also pass for a syntax or merge error");
		AssertNothingWasSaved();
	}

	[Test]
	[Description("A full-config append body keeps its own precise error rather than the generic one.")]
	public void TryUpdatePage_ShouldKeepTheFullConfigError_WhenTheAppendBodyIsFullConfigOnly() {
		// Arrange - the reason the recognized set deliberately includes the full-config spellings the merge
		// does NOT read. Narrowing it to "only what the merge reads" would swap this precise, actionable
		// message for the generic unrecognizable-body one, and nothing else would notice.
		StubCurrentBody(WebBody("[]"));
		PageUpdateOptions options = new() {
			SchemaName = SchemaName,
			Body = """define("X", function(){ return { viewModelConfig: /**SCHEMA_VIEW_MODEL_CONFIG*/{}/**SCHEMA_VIEW_MODEL_CONFIG*/ }; });""",
			Mode = "append",
			DryRun = true
		};

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeFalse(because: "append cannot merge a full-config body");
		response.Error.Should().NotContain("no recognizable page section",
			because: "the generic rule must not pre-empt the specific diagnosis that names --mode replace");
		AssertNothingWasSaved();
	}

	[Test]
	[Description("A handlers-only fragment does not get the 'everything discarded' warning when it lands.")]
	public void TryUpdatePage_ShouldNotWarnAboutDiscardedOperations_WhenTheFragmentCarriesNoViewConfigDiffOps() {
		// Arrange - a current body with no SCHEMA_VIEW_CONFIG_DIFF pair sets ViewConfigDiffApplied=false, but a
		// handlers-only fragment merges and lands correctly. The warning used to fire anyway and sent the
		// caller to --mode replace for a write that had succeeded.
		StubCurrentBody(HandlersOnlyBody("[]"));
		PageUpdateOptions options = new() {
			SchemaName = SchemaName,
			Body = HandlersOnlyBody("[{request:\"crt.SaveRecordRequest\",handler:()=>null}]"),
			Mode = "append",
			DryRun = true
		};

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "a handlers-only append is valid and its handlers do land");
		(response.Warnings ?? []).Should().NotContain(w => w.Contains("EVERY viewConfigDiff operation"),
			because: "describing a loss that did not happen sends the caller to fix a write that succeeded");
		AssertNothingWasSaved();
	}

	/// <summary>
	/// A body with NO SCHEMA_VIEW_CONFIG_DIFF marker pair, used to drive ViewConfigDiffApplied=false while the
	/// handlers section still merges normally.
	/// </summary>
	private static string HandlersOnlyBody(string handlersInner) =>
		"define(\"" + SchemaName + "\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
		"handlers: /**SCHEMA_HANDLERS*/" + handlersInner + "/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";
	[Test]
	[Description("The insert-downgrade detector is structurally inert on an append and stays silent.")]
	public void TryUpdatePage_ShouldNotWarnAboutAnInsertDowngrade_WhenAppendReplacesAnInsertWithAnInsert() {
		// Arrange - the shape that WOULD trip PageInsertDowngradeDetector if an append could produce it: the
		// prior body introduces a component with an `insert`, and the final body no longer carries that
		// insert. An append cannot produce it, because a current `insert X` is only ever replaced by an
		// incoming entry of the SAME identity - another `insert X` - and every non-matching entry is carried
		// over verbatim. The detector runs anyway: it lives in the shared TryPrepareWrite, where the replace
		// save CAN trip it. This pins the claim that its presence on the append path is not coverage, so a
		// future change to the merge identity that makes an append drop an insert fails here instead of
		// silently gaining a warning nobody expected.
		StubCurrentBody(WebBody(
			"""[{"operation":"insert","name":"UsrWidget","parentName":"Main","values":{"type":"crt.Input","label":"Old"}}]"""));

		// Act
		bool result = _command.TryUpdatePage(
			AppendDryRun("""[{"operation":"insert","name":"UsrWidget","parentName":"Main","values":{"type":"crt.Input","label":"New"}}]"""),
			out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "replacing an insert with an insert of the same identity is a normal append");
		response.AppendProjection.ReplacedOperationCount.Should().Be(1,
			because: "the merge really did replace it - otherwise this test would prove nothing about the detector");
		(response.Warnings ?? []).Should().NotContain(w => w.Contains("downgrade"),
			because: "an append never drops an insert for a transform, so the detector must stay silent here");
		AssertNothingWasSaved();
	}
}
