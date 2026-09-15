namespace Clio.Tests.Command;

using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageUpdateCommandConflictTests
{
	private const string SelectQueryUrl = "http://test/DataService/json/SyncReply/SelectQuery";
	private const string GetSchemaUrl = "http://test/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema";
	private const string SaveSchemaUrl = "http://test/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema";
	private const string SchemaUId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
	private const string SchemaName = "Test_FormPage";

	private const string ValidBody =
		"define(\"Test_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	// A valid web body that inserts a crt.IndicatorWidget whose title binds an unregistered, non-Usr,
	// non-DS-bound localizable key — the ENG-93098 shape.
	private const string MetricBodyUnregisteredTitle =
		"define(\"Test_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"insert\",\"name\":\"IndicatorWidget_CriticalRequests\"," +
		"\"parentName\":\"Main\",\"values\":{\"type\":\"crt.IndicatorWidget\",\"config\":{" +
		"\"title\":\"#ResourceString(IndicatorWidget_CriticalRequests_title)#\"," +
		"\"text\":{\"template\":\"{0}\",\"metricMacros\":\"{0}\"}}}}]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private PageUpdateCommand _command;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		ILogger logger = Substitute.For<ILogger>();
		_serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns(SelectQueryUrl);
		_serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema").Returns(GetSchemaUrl);
		_serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema").Returns(SaveSchemaUrl);
		StubNameMetadata();
		StubDesignerEndpoints();
		_command = new PageUpdateCommand(
			_applicationClient, _serviceUrlBuilder, logger, Substitute.For<IPageBaselineGuard>(), new PersistedResourceKeyReader(), CreateHierarchyClient());
	}

	private void StubNameMetadata() {
		_applicationClient.ExecutePostRequest(
				SelectQueryUrl,
				Arg.Is<string>(body => !body.Contains("byUId")),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($$"""{"success": true, "rows": [{"UId": "{{SchemaUId}}"}]}""");
	}

	private void StubChecksumRow(string checksum) {
		_applicationClient.ExecutePostRequest(
				SelectQueryUrl,
				Arg.Is<string>(body => body.Contains("byUId")),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($$"""{"success": true, "rows": [{"Checksum": "{{checksum}}", "ModifiedOn": "2026-06-12T09:00:00"}]}""");
	}

	private void StubChecksumRowMissing() {
		_applicationClient.ExecutePostRequest(
				SelectQueryUrl,
				Arg.Is<string>(body => body.Contains("byUId")),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success": true, "rows": []}""");
	}

	private void StubChecksumRowWithNullChecksum() {
		_applicationClient.ExecutePostRequest(
				SelectQueryUrl,
				Arg.Is<string>(body => body.Contains("byUId")),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success": true, "rows": [{"ModifiedOn": "2026-06-12T09:00:00"}]}""");
	}

	// The replacing schema does not yet exist in the design package, so the lookup by package must
	// return no rows — otherwise ResolveEditableUId treats it as an existing (non-replacing) schema.
	private void StubReplacingSchemaAbsentInPackage() {
		_applicationClient.ExecutePostRequest(
				SelectQueryUrl,
				Arg.Is<string>(body => body.Contains("byPackage")),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success": true, "rows": []}""");
	}

	private PageUpdateCommand CreateReplacingCommand() =>
		new(_applicationClient, _serviceUrlBuilder, Substitute.For<ILogger>(), Substitute.For<IPageBaselineGuard>(), new PersistedResourceKeyReader(), CreateHierarchyClient(isCreateReplacing: true));

	private void StubDesignerEndpoints() {
		_applicationClient.ExecutePostRequest(
				GetSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($$"""{"success": true, "schema": {"body": "old body", "name": "{{SchemaName}}" } }""");
		_applicationClient.ExecutePostRequest(
				SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success": true}""");
	}

	private static IPageDesignerHierarchyClient CreateHierarchyClient(bool isCreateReplacing = false) {
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		string designPackageUId = isCreateReplacing ? "design-pkg-uid" : "test-pkg-uid";
		hierarchyClient.GetDesignPackageUId(SchemaUId).Returns(designPackageUId);
		hierarchyClient.GetParentSchemas(SchemaUId, designPackageUId).Returns([
			new PageDesignerHierarchySchema { UId = SchemaUId, Name = SchemaName, PackageUId = "test-pkg-uid" }
		]);
		return hierarchyClient;
	}

	private static PageUpdateOptions CreateOptions(
		string expectedChecksum = null,
		string expectedSchemaUId = null,
		bool expectedSchemaAbsent = false,
		bool force = false,
		bool dryRun = false) =>
		new() {
			SchemaName = SchemaName,
			Body = ValidBody,
			DryRun = dryRun,
			ExpectedChecksum = expectedChecksum,
			ExpectedSchemaUId = expectedSchemaUId,
			ExpectedSchemaAbsent = expectedSchemaAbsent,
			Force = force
		};

	[Test]
	[Description("TryUpdatePage must return a checksum-mismatch conflict and skip SaveSchema when the server checksum differs from the baseline.")]
	public void TryUpdatePage_ShouldReturnConflict_WhenExpectedChecksumDiffersFromServer() {
		// Arrange
		StubChecksumRow("server-checksum");
		PageUpdateOptions options = CreateOptions(expectedChecksum: "baseline-checksum");

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeFalse(because: "an external modification must block the save");
		response.Conflict.Should().BeTrue(because: "the response must carry the machine-readable conflict marker");
		response.ConflictDetails.Reason.Should().Be(PageConflictReasons.ChecksumMismatch,
			because: "the server checksum differs from the baseline checksum");
		response.ConflictDetails.ExpectedChecksum.Should().Be("baseline-checksum",
			because: "the details must show what the caller expected");
		response.ConflictDetails.ActualChecksum.Should().Be("server-checksum",
			because: "the details must show what the server currently holds");
		response.Error.Should().Contain("Re-run get-page",
			because: "the error must guide the agent to reload and rebase instead of retrying blindly");
		_applicationClient.DidNotReceive().ExecutePostRequest(
			SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("TryUpdatePage must save despite a checksum mismatch when force=true, and populate the post-save checksum fields.")]
	public void TryUpdatePage_ShouldSaveSchemaAndReturnNewChecksum_WhenForceTrueDespiteChecksumMismatch() {
		// Arrange
		StubChecksumRow("fresh-after-save");
		PageUpdateOptions options = CreateOptions(expectedChecksum: "baseline-checksum", force: true);

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "force=true deliberately bypasses the conflict check");
		response.Success.Should().BeTrue(because: "the save must proceed when forced");
		response.Conflict.Should().BeFalse(because: "no conflict is reported on a forced overwrite");
		response.NewChecksum.Should().Be("fresh-after-save",
			because: "a forced save must still return the fresh checksum so the caller can refresh its baseline");
		response.SavedSchemaUId.Should().Be(SchemaUId,
			because: "the caller needs to know which schema the save wrote to");
		_applicationClient.Received(1).ExecutePostRequest(
			SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("TryUpdatePage must skip the conflict check and the post-save checksum query when no baseline options are supplied (regression-safe default).")]
	public void TryUpdatePage_ShouldSkipConflictCheck_WhenNoBaselineOptionsProvided() {
		// Arrange
		PageUpdateOptions options = CreateOptions();

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "the legacy no-baseline flow must be unaffected");
		response.Success.Should().BeTrue(because: "the save must proceed exactly as before the feature");
		response.Conflict.Should().BeFalse(because: "no baseline means nothing to conflict with");
		response.NewChecksum.Should().BeNull(because: "the no-baseline path must cost zero extra SysSchema queries");
		_applicationClient.DidNotReceive().ExecutePostRequest(
			SelectQueryUrl, Arg.Is<string>(body => body.Contains("byUId")), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("TryUpdatePage must proceed and return the fresh checksum when the server checksum matches the baseline.")]
	public void TryUpdatePage_ShouldSaveSchema_WhenChecksumMatchesBaseline() {
		// Arrange
		StubChecksumRow("same-checksum");
		PageUpdateOptions options = CreateOptions(expectedChecksum: "same-checksum");

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "a matching checksum proves no external modification happened");
		response.Success.Should().BeTrue(because: "the save must proceed on a clean baseline");
		response.NewChecksum.Should().Be("same-checksum",
			because: "the post-save refresh must report the current server checksum");
	}

	[Test]
	[Description("TryUpdatePage must preserve a stale caller checksum when target-package-uid names the existing package, so a concurrent edit is refused instead of overwritten.")]
	public void TryUpdatePage_ShouldReturnConflict_WhenTargetPackageUidNamesExistingPackageAndChecksumIsStale() {
		// Arrange
		StubChecksumRow("server-checksum");
		PageUpdateOptions options = CreateOptions(expectedChecksum: "baseline-checksum");
		options.TargetPackageUId = "test-pkg-uid";

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeFalse(because: "an explicit checksum must remain an active precondition after target-package resolution");
		response.Conflict.Should().BeTrue(because: "a same-package target can still have been edited concurrently");
		response.ConflictDetails.Reason.Should().Be(PageConflictReasons.ChecksumMismatch,
			because: "the resolved target exists and its checksum differs from the caller's pin");
		_applicationClient.DidNotReceive().ExecutePostRequest(
			SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("TryUpdatePage must apply the CONDITIONAL on-disk baseline when target-package-uid resolves to the very schema it was captured for, so an UNPINNED caller - the ordinary case, since callers omit the optional pin and rely on the baseline - still gets a conflict instead of silently overwriting a concurrent writer (PR #1356 review).")]
	public void TryUpdatePage_ShouldReturnConflict_WhenTargetPackageUidResolvesToTheBaselineSchemaAndDiskBaselineIsStale() {
		// Arrange — no ExpectedChecksum: the baseline arrives only as the conditional one the guard carried.
		StubChecksumRow("server-checksum");
		PageUpdateOptions options = CreateOptions();
		options.TargetPackageUId = "test-pkg-uid";
		options.ConditionalBaselineSchemaUId = SchemaUId;
		options.ConditionalBaselineChecksum = "baseline-checksum";

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeFalse(
			because: "the selector named the package that already owns the schema, so the baseline still describes the write's actual target and must keep protecting it");
		response.Conflict.Should().BeTrue(because: "the schema was edited concurrently");
		response.ConflictDetails.Reason.Should().Be(PageConflictReasons.ChecksumMismatch,
			because: "the resolved target's server checksum differs from the baseline the guard recorded");
		options.ConditionalBaselineApplied.Should().BeTrue(
			because: "a promoted baseline must also be refreshed after a successful save, or the next unpinned save arms from a superseded checksum");
		_applicationClient.DidNotReceive().ExecutePostRequest(
			SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("TryUpdatePage must mark the conditional baseline as matched when a PINNED save's selector resolves to the schema the baseline describes, so the successful save refreshes it - without that, the caller's next UNPINNED save conflicted with its own previous save (issue #1538).")]
	public void TryUpdatePage_ShouldMarkTheBaselineMatched_WhenAPinnedSaveResolvesToTheBaselineSchema() {
		// Arrange — the caller pins the checksum the server currently holds, so the save proceeds.
		StubChecksumRow("server-checksum");
		PageUpdateOptions options = CreateOptions();
		options.ExpectedChecksum = "server-checksum";
		options.TargetPackageUId = "test-pkg-uid";
		options.ConditionalBaselineSchemaUId = SchemaUId;

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "the pinned checksum matches the server's, so nothing was modified externally");
		response.Success.Should().BeTrue(because: "the save must land");
		options.ConditionalBaselineApplied.Should().BeTrue(
			because: "the write landed on the very schema the on-disk baseline describes, so that baseline now holds a superseded checksum and must be refreshed");
		options.ExpectedChecksum.Should().Be("server-checksum",
			because: "the caller's explicit pin is the stronger witness and must keep governing the conflict check, unchanged by the refresh decision");
	}

	[Test]
	[Description("The target match compares schema UIds by VALUE, not by spelling: a braced --target-schema-uid against a bare recorded UId is the same schema written two ways, and reading it as a redirect reintroduces the issue #1538 stale baseline for that one input shape.")]
	public void TryUpdatePage_ShouldMarkTheBaselineMatched_WhenTheRecordedUIdIsSpelledDifferently() {
		// Arrange — the recorded baseline carries the braced spelling; the resolved schema carries the bare one.
		StubChecksumRow("server-checksum");
		PageUpdateOptions options = CreateOptions();
		options.ExpectedChecksum = "server-checksum";
		options.TargetPackageUId = "test-pkg-uid";
		options.ConditionalBaselineSchemaUId = "{" + SchemaUId + "}";

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "the pin matches the server, so the save proceeds");
		response.Success.Should().BeTrue(because: "nothing about GUID spelling makes this an external modification");
		options.ConditionalBaselineApplied.Should().BeTrue(
			because: "both spellings name the same schema, so the baseline describing it is still the one this save must refresh");
	}

	[Test]
	[Description("The downstream identity check must use the same VALUE comparison: an UNPINNED save promotes the recorded spelling into ExpectedSchemaUId, and comparing that braced spelling to the bare resolved UId as raw strings reported schema-uid-mismatch for one and the same schema (PR #1540 review, P3).")]
	public void TryUpdatePage_ShouldNotReportUIdMismatch_WhenAnUnpinnedSaveCarriesADifferentlySpelledUId() {
		// Arrange — no ExpectedChecksum: the promoted conditional baseline is the only witness, and it
		// carries the braced spelling of the very schema the selector resolves to.
		StubChecksumRow("server-checksum");
		PageUpdateOptions options = CreateOptions();
		options.TargetPackageUId = "test-pkg-uid";
		options.ConditionalBaselineSchemaUId = "{" + SchemaUId + "}";
		options.ConditionalBaselineChecksum = "server-checksum";

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "the promoted baseline matches the server checksum, so the save proceeds");
		response.Success.Should().BeTrue(
			because: "two spellings of the same GUID are the same schema — the identity check must not read them as a redirect");
		options.ConditionalBaselineApplied.Should().BeTrue(
			because: "the write landed on the schema the on-disk baseline describes, so that baseline must be refreshed");
	}

	[Test]
	[Description("The value comparison must not turn into a loose one: a recorded UId that is not a GUID at all still falls back to an exact string comparison, so a non-matching value is not quietly accepted.")]
	public void TryUpdatePage_ShouldNotMarkTheBaselineMatched_WhenTheRecordedUIdIsNotAGuid() {
		// Arrange
		StubChecksumRow("server-checksum");
		PageUpdateOptions options = CreateOptions();
		options.ExpectedChecksum = "server-checksum";
		options.TargetPackageUId = "test-pkg-uid";
		options.ConditionalBaselineSchemaUId = "not-a-guid";

		// Act
		_command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		response.Conflict.Should().BeFalse(because: "the pin matches the resolved target's checksum");
		options.ConditionalBaselineApplied.Should().BeFalse(
			because: "an unparseable recorded value that differs from the resolved UId is not evidence the write landed on the baseline's page");
	}

	[Test]
	[Description("TryUpdatePage must NOT mark the conditional baseline as matched when a PINNED save's selector resolves to a different schema - the genuine redirect, where refreshing would stamp another schema's identity into the schema-name-keyed baseline (issue #1538 AC-2).")]
	public void TryUpdatePage_ShouldNotMarkTheBaselineMatched_WhenAPinnedSaveResolvesElsewhere() {
		// Arrange
		StubChecksumRow("server-checksum");
		PageUpdateOptions options = CreateOptions();
		options.ExpectedChecksum = "server-checksum";
		options.TargetPackageUId = "other-pkg-uid";
		options.ConditionalBaselineSchemaUId = "11111111-0000-0000-0000-000000000000";

		// Act
		_command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		response.Conflict.Should().BeFalse(
			because: "the pin matches the resolved target's checksum, so nothing was modified externally");
		options.ConditionalBaselineApplied.Should().BeFalse(
			because: "the baseline describes another schema, so refreshing it from this write would corrupt the schema-name-keyed baseline");
	}

	[Test]
	[Description("TryUpdatePage must DISCARD the conditional baseline when the selector resolves to a different schema than the one it was captured for - that is the genuine redirect, where the baseline describes nothing about the write's target.")]
	public void TryUpdatePage_ShouldIgnoreTheConditionalBaseline_WhenTheResolvedTargetIsADifferentSchema() {
		// Arrange
		StubChecksumRow("server-checksum");
		PageUpdateOptions options = CreateOptions();
		options.TargetPackageUId = "other-pkg-uid";
		options.ConditionalBaselineSchemaUId = "11111111-0000-0000-0000-000000000000";
		options.ConditionalBaselineChecksum = "baseline-checksum";

		// Act
		_command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		response.Conflict.Should().BeFalse(
			because: "a baseline captured for another schema is not evidence about this write's target, and refusing on it is the false conflict this change set removes");
		options.ExpectedChecksum.Should().BeNull(because: "an inapplicable baseline must not be promoted");
		options.ConditionalBaselineApplied.Should().BeFalse(because: "nothing was promoted, so nothing may be refreshed from it");
	}

	[Test]
	[Description("TryUpdatePage must return a schema-created-externally conflict when the baseline says absent but a replacing schema now exists.")]
	public void TryUpdatePage_ShouldReturnConflict_WhenBaselineSaysAbsentButReplacingSchemaExists() {
		// Arrange
		PageUpdateOptions options = CreateOptions(expectedSchemaAbsent: true);

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeFalse(because: "a replacing schema created outside the session is an external modification");
		response.Conflict.Should().BeTrue(because: "the response must carry the conflict marker");
		response.ConflictDetails.Reason.Should().Be(PageConflictReasons.SchemaCreatedExternally,
			because: "the baseline recorded no editable schema but the design package now contains one");
	}

	[Test]
	[Description("TryUpdatePage must return a schema-deleted-externally conflict when the baseline has a checksum but the editable schema row no longer exists.")]
	public void TryUpdatePage_ShouldReturnConflict_WhenBaselineHasChecksumButSchemaRowMissing() {
		// Arrange
		StubChecksumRowMissing();
		PageUpdateOptions options = CreateOptions(expectedChecksum: "baseline-checksum");

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeFalse(because: "deletion of the editable schema outside the session is an external modification");
		response.Conflict.Should().BeTrue(because: "the response must carry the conflict marker");
		response.ConflictDetails.Reason.Should().Be(PageConflictReasons.SchemaDeletedExternally,
			because: "the baseline expected an existing schema that is no longer in SysSchema");
	}

	[Test]
	[Description("TryUpdatePage must return a schema-uid-mismatch conflict when the resolved editable schema UId differs from the baseline UId.")]
	public void TryUpdatePage_ShouldReturnConflict_WhenEditableSchemaUIdDiffersFromBaseline() {
		// Arrange
		PageUpdateOptions options = CreateOptions(
			expectedChecksum: "baseline-checksum",
			expectedSchemaUId: "11111111-0000-0000-0000-000000000000");

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeFalse(because: "a different editable schema identity means the baseline no longer applies");
		response.Conflict.Should().BeTrue(because: "the response must carry the conflict marker");
		response.ConflictDetails.Reason.Should().Be(PageConflictReasons.SchemaUIdMismatch,
			because: "the resolved editable UId does not match the baseline UId");
		response.ConflictDetails.ActualSchemaUId.Should().Be(SchemaUId,
			because: "the details must show which schema the write would actually target");
	}

	[Test]
	[Description("TryUpdatePage must save (no conflict) when the baseline says absent and the replacing schema is the legitimate first-time write being created now.")]
	public void TryUpdatePage_ShouldSaveSchema_WhenBaselineAbsentAndIsCreateReplacing() {
		// Arrange
		StubChecksumRow("fresh-after-save");
		StubReplacingSchemaAbsentInPackage();
		PageUpdateCommand command = CreateReplacingCommand();
		PageUpdateOptions options = CreateOptions(expectedSchemaAbsent: true);

		// Act
		bool result = command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "an absent baseline plus a replacing-schema write is the legitimate first-time save, not an external modification");
		response.Success.Should().BeTrue(because: "the first-time replacing write must proceed");
		response.Conflict.Should().BeFalse(because: "creating the replacing schema now is exactly what the absent baseline expects");
		_applicationClient.Received(1).ExecutePostRequest(
			SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("TryUpdatePage must return a schema-deleted-externally conflict when the baseline has a checksum but the editable schema now only exists as a replacing (not-yet-saved) schema.")]
	public void TryUpdatePage_ShouldReturnConflict_WhenBaselineHasChecksumButIsCreateReplacing() {
		// Arrange
		StubReplacingSchemaAbsentInPackage();
		PageUpdateCommand command = CreateReplacingCommand();
		PageUpdateOptions options = CreateOptions(expectedChecksum: "baseline-checksum");

		// Act
		bool result = command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeFalse(because: "a baseline expecting an existing editable schema cannot reconcile with a replacing-only schema state");
		response.Conflict.Should().BeTrue(because: "the response must carry the conflict marker");
		response.ConflictDetails.Reason.Should().Be(PageConflictReasons.SchemaDeletedExternally,
			because: "the editable schema the baseline expected is gone — only a replacing schema remains");
		_applicationClient.DidNotReceive().ExecutePostRequest(
			SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("TryUpdatePage must fail open (save, no conflict) when the server row is present but its Checksum is NULL/blank, distinguishing 'checksum unavailable' from a value mismatch.")]
	public void TryUpdatePage_ShouldSaveSchema_WhenServerChecksumIsNull() {
		// Arrange
		StubChecksumRowWithNullChecksum();
		PageUpdateOptions options = CreateOptions(expectedChecksum: "baseline-checksum");

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "a present row with a NULL checksum is checksum-unavailable, not proof of an external edit");
		response.Conflict.Should().BeFalse(because: "fail-open avoids the misleading 'modified externally' that would loop the agent");
		_applicationClient.Received(1).ExecutePostRequest(
			SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("TryUpdatePage must report conflicts on dry-run too, before the dry-run short-circuit.")]
	public void TryUpdatePage_ShouldReturnConflict_WhenDryRunWithStaleChecksum() {
		// Arrange
		StubChecksumRow("server-checksum");
		PageUpdateOptions options = CreateOptions(expectedChecksum: "baseline-checksum", dryRun: true);

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeFalse(because: "dry-run must surface the conflict so the agent learns about it before a real save");
		response.Conflict.Should().BeTrue(because: "dry-run reports the same conflict contract as a real save");
		response.ConflictDetails.Reason.Should().Be(PageConflictReasons.ChecksumMismatch,
			because: "the conflict reason is identical regardless of dry-run");
	}

	[Test]
	[Description("TryUpdatePage on a dry run surfaces an unresolved inserted widget title as a WARNING (not a hard failure) so update-page --dry-run does not report clean for exactly the ENG-93098 body a real save rejects.")]
	public void TryUpdatePage_ShouldWarnOnUnresolvedWidgetTitle_WhenDryRun() {
		// Arrange
		PageUpdateOptions options = CreateOptions(dryRun: true);
		options.Body = MetricBodyUnregisteredTitle;

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(
			because: "a dry run validates without saving and must not hard-fail on the body-only heuristic (no schema context)");
		response.Success.Should().BeTrue(because: "dry run does not save, so it reports success");
		response.DryRun.Should().BeTrue(because: "the response reflects dry-run mode");
		response.Warnings.Should().NotBeNullOrEmpty(
			because: "the unresolved widget title must surface as a dry-run warning instead of a silent green result");
		response.Warnings.Should().Contain(
			w => w.Contains("IndicatorWidget_CriticalRequests_title") && w.Contains("render raw"),
			because: "the advisory must name the unresolved key and the raw-render failure");
		_applicationClient.DidNotReceive().ExecutePostRequest(
			SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("TryUpdatePage on a dry run does NOT warn when the widget-title resource is supplied — the binding will resolve.")]
	public void TryUpdatePage_ShouldNotWarnOnWidgetTitle_WhenDryRunAndResourceProvided() {
		// Arrange
		PageUpdateOptions options = CreateOptions(dryRun: true);
		options.Body = MetricBodyUnregisteredTitle;
		options.Resources = "{\"IndicatorWidget_CriticalRequests_title\": \"Critical Requests\"}";

		// Act
		bool result = _command.TryUpdatePage(options, out PageUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "the dry run validates successfully");
		response.Success.Should().BeTrue(because: "dry run does not save");
		response.Warnings.Should().BeNullOrEmpty(
			because: "the title key is registered via resources, so the binding resolves and no warning is emitted");
	}
}
