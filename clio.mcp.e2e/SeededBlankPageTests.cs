using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Mcp.E2E.Support.Creatio;
using FluentAssertions;

namespace Clio.Mcp.E2E;

/// <summary>
/// Unit tests for the decisions <see cref="SeededBlankPage"/> makes without a stand: whether a body read from
/// the shared page <c>ClioMcp_BlankPageToSave</c> is its blank body; which states of the page at the start of
/// a lease, and which reads after a reset, FAIL the test because only the product defect produces them; and
/// how the stored <c>SysSchema</c> rows of the page are reported and judged. The two steps of the lease run
/// here over a scripted page access. They need no MCP server and no Creatio, so they are categorized
/// <c>Unit</c> rather than <c>McpE2E.Sandbox</c>.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class SeededBlankPageTests {

	private const string EmptyViewConfigDiff = "/**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/";

	[Test]
	[Description("The body the reset writes is itself recognized as blank, so a reset page is not reset again by the next test.")]
	public void IsBlank_ShouldAcceptTheBodyTheResetWrites() {
		// Arrange
		string body = SeededBlankPage.BuildBlankBody();

		// Act
		bool blank = SeededBlankPage.IsBlank(body, out string? reason);

		// Assert
		blank.Should().BeTrue(
			because: "a reset that wrote a body the check rejects would rewrite the page before every test. Reason: {0}",
			reason);
		reason.Should().BeNull(because: "a blank body has nothing to report");
	}

	[Test]
	[Description("Whitespace inside the empty sections and after the module does not make a blank body look written.")]
	public void IsBlank_ShouldIgnoreWhitespace_InEmptySectionsAndAfterTheModule() {
		// Arrange
		string body = SeededBlankPage.BuildBlankBody()
			.Replace(EmptyViewConfigDiff, "/**SCHEMA_VIEW_CONFIG_DIFF*/ [ ]\r\n/**SCHEMA_VIEW_CONFIG_DIFF*/")
			+ "\r\n\r\n";

		// Act
		bool blank = SeededBlankPage.IsBlank(body, out string? reason);

		// Assert
		blank.Should().BeTrue(
			because: "the stand may store the same blank body with other whitespace. Reason: {0}", reason);
	}

	[Test]
	[Description("A component left in viewConfigDiff by an earlier test makes the body not blank, and the reason names the section and its content.")]
	public void IsBlank_ShouldRejectABodyThatCarriesAComponent() {
		// Arrange
		string body = SeededBlankPage.BuildBlankBody().Replace(
			EmptyViewConfigDiff,
			"/**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"insert\",\"name\":\"UsrE2ESyncPersistedField\"}]/**SCHEMA_VIEW_CONFIG_DIFF*/");

		// Act
		bool blank = SeededBlankPage.IsBlank(body, out string? reason);

		// Assert
		blank.Should().BeFalse(
			because: "this is the body later tests read in TeamCity build 16330482 and could not build a probe from");
		reason.Should().Contain("SCHEMA_VIEW_CONFIG_DIFF",
			because: "the reason must name the section that is not empty");
		reason.Should().Contain("UsrE2ESyncPersistedField",
			because: "the reason must show what the section holds, which names the test that left it");
	}

	[Test]
	[Description("The comment the visibility test appends after the module makes the body not blank.")]
	public void IsBlank_ShouldRejectABodyWithContentAfterTheModule() {
		// Arrange
		string body = SeededBlankPage.BuildBlankBody() + "\n// clio-sync-visibility-0123456789abcdef\n";

		// Act
		bool blank = SeededBlankPage.IsBlank(body, out string? reason);

		// Assert
		blank.Should().BeFalse(
			because: "every section is still empty, so only the text after the module shows the marker was left behind");
		reason.Should().Contain("clio-sync-visibility",
			because: "the reason must show the leftover content");
	}

	[Test]
	[Description("A body without one of the marker pairs, and an empty body, are not blank: no probe can be built from them.")]
	public void IsBlank_ShouldRejectABodyWithoutAMarkerPair_AndAnEmptyBody() {
		// Arrange
		string withoutHandlers = SeededBlankPage.BuildBlankBody()
			.Replace("/**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/", "[]");

		// Act
		bool missingMarkerIsBlank = SeededBlankPage.IsBlank(withoutHandlers, out string? missingMarkerReason);
		bool emptyIsBlank = SeededBlankPage.IsBlank("  ", out string? emptyReason);

		// Assert
		missingMarkerIsBlank.Should().BeFalse(because: "a body that lost a marker pair is not the blank page");
		missingMarkerReason.Should().Contain("SCHEMA_HANDLERS", because: "the reason must name the missing pair");
		emptyIsBlank.Should().BeFalse(because: "a read that returned nothing proves nothing about the page");
		emptyReason.Should().Contain("empty", because: "the reason must say the body was empty");
	}

	private const string ResetChecksum = "aaaa1111";
	private const string TwinChecksum = "bbbb2222";
	private const string WrittenSchemaUId = "22222222-2222-2222-2222-222222222222";
	private const string TwinSchemaUId = "33333333-3333-3333-3333-333333333333";
	private const string DesignPackageUId = "0b0b0b0b-0000-0000-0000-00000000000b";

	private static string BodyWithComponent() => SeededBlankPage.BuildBlankBody().Replace(
		EmptyViewConfigDiff,
		"/**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"insert\",\"name\":\"UsrE2ESyncPersistedField\"}]/**SCHEMA_VIEW_CONFIG_DIFF*/");

	// A read of the schema the reset wrote: blank, from an editable schema, with the checksum the save reported.
	private static SeededBlankPage.PageObservation SavedBlankRead(
		string checksum = ResetChecksum, string schemaUId = WrittenSchemaUId) =>
		new(Success: true, Error: null, Body: SeededBlankPage.BuildBlankBody(), EditableSchemaExists: true,
			EditableSchemaUId: schemaUId, Checksum: checksum, ModifiedOn: "2026-11-16T10:00:00", PagePackageName: "Custom",
			DesignPackageName: "Custom", DesignPackageUId: DesignPackageUId, WillCreateReplacing: false);

	// The body get-page synthesizes when it finds no editable schema: blank, but nothing stored was read.
	private static SeededBlankPage.PageObservation SynthesizedRead() =>
		SavedBlankRead() with {
			EditableSchemaExists = false, EditableSchemaUId = null, Checksum = null, ModifiedOn = null,
			WillCreateReplacing = true
		};

	private static SeededBlankPage.PageObservation NotBlankRead() =>
		SavedBlankRead(checksum: "cccc3333") with { Body = BodyWithComponent() };

	private static SeededBlankPage.ResetSave SavedReset(string? checksum = ResetChecksum) =>
		new(Success: true, Error: null, Checksum: checksum, ConflictCheck: "not-applied", Warnings: []);

	private static SeededBlankPage.StoredSchemaRow Row(string uId, string package, string packageUId, string createdOn = "2026-11-16T09:00:00") =>
		new(uId, package, packageUId, ExtendParent: "true", Checksum: "c", CreatedOn: createdOn, ModifiedOn: createdOn);

	/// <summary>A scripted page: the reads are returned in order (the last one repeats), every call is counted.</summary>
	private sealed class ScriptedPage : SeededBlankPage.IPageAccess {
		private readonly Queue<SeededBlankPage.PageObservation> _reads;
		private readonly SeededBlankPage.ResetSave _reset;
		private readonly SeededBlankPage.StoredRows _stored;

		internal ScriptedPage(
			IEnumerable<SeededBlankPage.PageObservation> reads, SeededBlankPage.ResetSave? reset = null,
			SeededBlankPage.StoredRows? stored = null) {
			_reads = new Queue<SeededBlankPage.PageObservation>(reads);
			_reset = reset ?? SavedReset();
			_stored = stored ?? new SeededBlankPage.StoredRows(
				[Row("11111111-1111-1111-1111-111111111111", "AutoTestClioMcp", "0a0a0a0a-0000-0000-0000-00000000000a")], null);
		}

		internal int Reads { get; private set; }
		internal int Writes { get; private set; }
		internal int StoredRowReads { get; private set; }

		public Task<SeededBlankPage.PageObservation> ReadAsync(CancellationToken cancellationToken) {
			Reads++;
			return Task.FromResult(_reads.Count > 1 ? _reads.Dequeue() : _reads.Peek());
		}

		public Task<SeededBlankPage.ResetSave> WriteBlankAsync(CancellationToken cancellationToken) {
			Writes++;
			return Task.FromResult(_reset);
		}

		public Task<SeededBlankPage.StoredRows> ReadStoredRowsAsync(CancellationToken cancellationToken) {
			StoredRowReads++;
			return Task.FromResult(_stored);
		}
	}

	[Test]
	[Description("A blank body is a saved blank page only when the read resolved an editable schema; without one it is the body get-page synthesizes, and a body with a component is not blank.")]
	public void Classify_ShouldTellASavedBlankPage_FromASynthesizedBody_AndFromContent() {
		// Arrange
		SeededBlankPage.PageObservation saved = SavedBlankRead();
		SeededBlankPage.PageObservation synthesized = SynthesizedRead();
		SeededBlankPage.PageObservation written = NotBlankRead();
		SeededBlankPage.PageObservation failed = SavedBlankRead() with { Success = false, Error = "boom", Body = null };

		// Act
		SeededBlankPage.PageState savedState = SeededBlankPage.Classify(saved, out string? savedReason);
		SeededBlankPage.PageState synthesizedState = SeededBlankPage.Classify(synthesized, out string? synthesizedReason);
		SeededBlankPage.PageState writtenState = SeededBlankPage.Classify(written, out string? writtenReason);
		SeededBlankPage.PageState failedState = SeededBlankPage.Classify(failed, out string? failedReason);

		// Assert
		savedState.Should().Be(SeededBlankPage.PageState.SavedBlank,
			because: "a blank body read from an editable schema is the reset page");
		savedReason.Should().BeNull(because: "a saved blank page has nothing to report");
		synthesizedState.Should().Be(SeededBlankPage.PageState.SynthesizedBlank,
			because: "get-page returns the same blank text when it finds NO editable schema, which proves nothing about what is stored");
		synthesizedReason.Should().Contain("NO editable schema",
			because: "the reason must say that the blank body was not read from a stored schema");
		writtenState.Should().Be(SeededBlankPage.PageState.NotBlank,
			because: "a component in viewConfigDiff is content");
		writtenReason.Should().Contain("UsrE2ESyncPersistedField",
			because: "the reason must name what the page holds, which names the test that wrote it");
		failedState.Should().Be(SeededBlankPage.PageState.Unreadable, because: "a failed read shows no page at all");
		failedReason.Should().Contain("boom", because: "the reason must carry the error of get-page");
	}

	[Test]
	[Description("A page found not blank at the start of a later lease is reset AND fails the test: after a verified reset only the defect leaves it not blank (TeamCity build 16330482).")]
	public void DecideStart_ShouldResetAndFail_WhenALaterLeaseFindsThePageNotBlank() {
		// Arrange
		SeededBlankPage.PageObservation first = NotBlankRead();

		// Act
		SeededBlankPage.StartDecision decision = SeededBlankPage.DecideStart(first, firstLeaseOfProcess: false);

		// Assert
		decision.Reset.Should().BeTrue(because: "the next test must still start from a blank page");
		decision.Failure.Should().NotBeNull(
			because: "the lease before it verified its reset, so a page that is not blank now is the signature of the defect and must not pass silently");
		decision.Failure.Should().Contain("NOT blank before this test").And.Contain("UsrE2ESyncPersistedField").And
			.Contain("NOT the first lease",
				because: "the message must say what was found, what it contained and which case it was");
	}

	[Test]
	[Description("Only the first lease of the test process may find the page not blank without failing: a developer's stand may start dirty. It is still reset.")]
	public void DecideStart_ShouldResetWithoutFailing_WhenTheFirstLeaseFindsThePageNotBlank() {
		// Arrange
		SeededBlankPage.PageObservation first = NotBlankRead();

		// Act
		SeededBlankPage.StartDecision decision = SeededBlankPage.DecideStart(first, firstLeaseOfProcess: true);

		// Assert
		decision.Reset.Should().BeTrue(because: "a dirty stand is brought to blank before the first test");
		decision.Failure.Should().BeNull(because: "nothing verified the page before the first lease of the process");
		decision.Note.Should().Contain("FIRST lease", because: "the log must say why the state was accepted");
	}

	[Test]
	[Description("A page that only looks blank - no editable schema - fails a later lease and is reset, but is accepted on the first lease, where a fresh site has no editable schema yet.")]
	public void DecideStart_ShouldFailALaterLease_ButNotTheFirst_WhenTheBlankBodyIsSynthesized() {
		// Arrange
		SeededBlankPage.PageObservation first = SynthesizedRead();

		// Act
		SeededBlankPage.StartDecision later = SeededBlankPage.DecideStart(first, firstLeaseOfProcess: false);
		SeededBlankPage.StartDecision firstLease = SeededBlankPage.DecideStart(first, firstLeaseOfProcess: true);

		// Assert
		later.Failure.Should().Contain("only LOOKS blank",
			because: "the lease before it read the page from an editable schema, so a read without one lost the schema the reset wrote");
		later.Reset.Should().BeTrue(because: "the next test must start from a saved blank page");
		firstLease.Failure.Should().BeNull(because: "a fresh site has no editable schema of the page before the first save");
		firstLease.Reset.Should().BeFalse(because: "there is nothing to overwrite and the teardown of the test writes the page anyway");
	}

	[Test]
	[Description("A saved blank page at the start of a lease needs no reset and reports nothing, on the first lease and on later ones.")]
	public void DecideStart_ShouldDoNothing_WhenThePageIsASavedBlankPage([Values] bool firstLeaseOfProcess) {
		// Arrange
		SeededBlankPage.PageObservation first = SavedBlankRead();

		// Act
		SeededBlankPage.StartDecision decision = SeededBlankPage.DecideStart(first, firstLeaseOfProcess);

		// Assert
		decision.Reset.Should().BeFalse(because: "the page is already what the test needs");
		decision.Failure.Should().BeNull(because: "a saved blank page is the expected state");
	}

	[Test]
	[Description("A read shows the reset only when it is blank, comes from an editable schema and carries the checksum the reset save reported; a blank read of another schema, a synthesized body, or a reset without a checksum do not prove it.")]
	public void WhyReadDoesNotShowReset_ShouldRequireTheWrittenSchema_NotOnlyABlankBody() {
		// Arrange
		SeededBlankPage.ResetSave reset = SavedReset();

		// Act
		string? written = SeededBlankPage.WhyReadDoesNotShowReset(SavedBlankRead(), reset);
		string? twin = SeededBlankPage.WhyReadDoesNotShowReset(SavedBlankRead(TwinChecksum, TwinSchemaUId), reset);
		string? synthesized = SeededBlankPage.WhyReadDoesNotShowReset(SynthesizedRead(), reset);
		string? notBlank = SeededBlankPage.WhyReadDoesNotShowReset(NotBlankRead(), reset);
		string? noChecksum = SeededBlankPage.WhyReadDoesNotShowReset(SavedBlankRead(), SavedReset(checksum: null));

		// Assert
		written.Should().BeNull(because: "this read is the schema the reset wrote");
		twin.Should().Contain(TwinChecksum).And.Contain(ResetChecksum).And.Contain(TwinSchemaUId,
			because: "an untouched second schema of the page is blank too; only the checksum tells it from the written one");
		synthesized.Should().Contain("NO editable schema",
			because: "the synthesized body is blank although nothing stored was read");
		notBlank.Should().Contain("UsrE2ESyncPersistedField", because: "content is the plainest sign the reset is not shown");
		noChecksum.Should().Contain("no post-save checksum",
			because: "without the checksum of the save nothing ties the blank read to the written schema");
	}

	[Test]
	[Description("A reset that the first read already shows passes; one that became readable only on a later read fails, and so does one that no read showed.")]
	public void JudgeResetReads_ShouldAcceptOnlyAResetTheFirstReadShows() {
		// Arrange
		SeededBlankPage.ResetSave reset = SavedReset();
		SeededBlankPage.PageObservation[] atOnce = [SavedBlankRead()];
		SeededBlankPage.PageObservation[] late = [NotBlankRead(), NotBlankRead(), SavedBlankRead()];
		SeededBlankPage.PageObservation[] never = Enumerable.Repeat(SavedBlankRead(TwinChecksum, TwinSchemaUId), 6).ToArray();

		// Act
		string? atOnceFailure = SeededBlankPage.JudgeResetReads(reset, atOnce, "after the test");
		string? lateFailure = SeededBlankPage.JudgeResetReads(reset, late, "after the test");
		string? neverFailure = SeededBlankPage.JudgeResetReads(reset, never, "after the test");

		// Assert
		atOnceFailure.Should().BeNull(because: "a completed write that the next read shows is the contract");
		lateFailure.Should().Contain("only on read 3",
			because: "a completed write that needed three reads to be visible is the defect, even though the page is blank in the end");
		lateFailure.Should().Contain(ResetChecksum).And.Contain("cccc3333",
			because: "the message must carry the checksum of the save and the checksums the reads returned");
		neverFailure.Should().Contain("none of 6 get-page read(s) showed it",
			because: "six blank reads of another schema must not count as a verified reset (TeamCity build 16330337)");
	}

	[Test]
	[Description("The start of a later lease on a saved blank page writes nothing and reports no failure.")]
	public async Task BeginAsync_ShouldNotWrite_WhenThePageIsASavedBlankPage() {
		// Arrange
		ScriptedPage page = new([SavedBlankRead()]);

		// Act
		SeededBlankPage.LeaseReport report = await SeededBlankPage.BeginAsync(
			page, firstLeaseOfProcess: false, checkStoredRows: false, TimeSpan.Zero, CancellationToken.None);

		// Assert
		report.Failures.Should().BeEmpty(because: "the page is in the state every test expects");
		page.Writes.Should().Be(0, because: "a saved blank page needs no reset before the test");
	}

	[Test]
	[Description("The start of a later lease on a page that still holds an earlier test's component resets the page and reports the failure, so the run is red and the next test starts clean.")]
	public async Task BeginAsync_ShouldResetAndReportAFailure_WhenALaterLeaseFindsAnEarlierTestsContent() {
		// Arrange
		ScriptedPage page = new([NotBlankRead(), SavedBlankRead()]);

		// Act
		SeededBlankPage.LeaseReport report = await SeededBlankPage.BeginAsync(
			page, firstLeaseOfProcess: false, checkStoredRows: false, TimeSpan.Zero, CancellationToken.None);

		// Assert
		page.Writes.Should().Be(1, because: "the page is brought back to blank so the failure does not cascade");
		report.Failures.Should().ContainSingle(because: "the reset itself was visible at once, so only the found state fails")
			.Which.Should().Contain("NOT blank before this test",
				because: "a page left not blank after a verified reset must fail the test that finds it");
		page.StoredRowReads.Should().Be(1, because: "the stored rows are recorded before the reset overwrites the state");
	}

	[Test]
	[Description("The same found state on the first lease of the process resets the page without a failure.")]
	public async Task BeginAsync_ShouldResetWithoutAFailure_WhenTheFirstLeaseFindsADirtyPage() {
		// Arrange
		ScriptedPage page = new([NotBlankRead(), SavedBlankRead()]);

		// Act
		SeededBlankPage.LeaseReport report = await SeededBlankPage.BeginAsync(
			page, firstLeaseOfProcess: true, checkStoredRows: false, TimeSpan.Zero, CancellationToken.None);

		// Assert
		page.Writes.Should().Be(1, because: "a dirty stand is reset before the first test");
		report.Failures.Should().BeEmpty(because: "a developer's stand may start dirty and the reset was visible at once");
		report.Log.Should().Contain(line => line.Contains("FIRST lease"),
			because: "the log must say which case it was");
	}

	[Test]
	[Description("The first lease of a fixture reports two stored schemas of the page in one package as a failure, even when the page reads blank.")]
	public async Task BeginAsync_ShouldReportAFailure_WhenOnePackageHoldsTwoSchemasOfThePage() {
		// Arrange
		SeededBlankPage.StoredRows stored = new([
			Row("11111111-1111-1111-1111-111111111111", "AutoTestClioMcp", "0a0a0a0a-0000-0000-0000-00000000000a"),
			Row(WrittenSchemaUId, "Custom", DesignPackageUId),
			Row(TwinSchemaUId, "Custom", DesignPackageUId)
		], null);
		ScriptedPage page = new([SavedBlankRead()], stored: stored);

		// Act
		SeededBlankPage.LeaseReport report = await SeededBlankPage.BeginAsync(
			page, firstLeaseOfProcess: true, checkStoredRows: true, TimeSpan.Zero, CancellationToken.None);

		// Assert
		report.Failures.Should().ContainSingle(because: "the page reads blank, so only the stored rows fail")
			.Which.Should().Contain(WrittenSchemaUId).And.Contain(TwinSchemaUId,
				because: "the failure must name both schemas a read and a write can resolve");
		report.Log.Should().Contain(line => line.Contains("MORE THAN ONE ROW IN ONE PACKAGE"),
			because: "the rows are written to the output in every run");
	}

	[Test]
	[Description("The end of a lease writes the blank body even when the page already looks blank, and passes when the first read shows that write.")]
	public async Task EndAsync_ShouldAlwaysWriteAndPass_WhenTheFirstReadShowsTheReset() {
		// Arrange
		ScriptedPage page = new([SavedBlankRead()]);

		// Act
		SeededBlankPage.LeaseReport report = await SeededBlankPage.EndAsync(page, TimeSpan.Zero, CancellationToken.None);

		// Assert
		page.Writes.Should().Be(1,
			because: "a page that looks blank cannot be told from a stale or synthesized read without a save to compare with");
		page.Reads.Should().Be(1, because: "the first read showed the reset, so no poll was needed");
		report.Failures.Should().BeEmpty(because: "a completed reset that the next read shows is the contract");
	}

	[Test]
	[Description("A reset whose first read still shows an earlier test's content fails the test although a later read shows it: the poll keeps the page usable, it does not make the stale read pass.")]
	public async Task EndAsync_ShouldReportAFailure_WhenTheResetBecameReadableOnlyOnALaterRead() {
		// Arrange
		ScriptedPage page = new([NotBlankRead(), SavedBlankRead()]);

		// Act
		SeededBlankPage.LeaseReport report = await SeededBlankPage.EndAsync(page, TimeSpan.Zero, CancellationToken.None);

		// Assert
		page.Reads.Should().Be(2, because: "the poll stops at the first read that shows the reset");
		report.Failures.Should().ContainSingle(because: "the reset was saved and became visible, so one observation fails")
			.Which.Should().Contain("only on read 2",
				because: "a completed write the next read does not show is the defect of TeamCity build 16330482");
	}

	[Test]
	[Description("A reset that six reads answer with a blank body of another schema fails: the pristine body must not count as a verified reset.")]
	public async Task EndAsync_ShouldReportAFailure_WhenEveryReadIsABlankBodyOfAnotherSchema() {
		// Arrange
		ScriptedPage page = new([SavedBlankRead(TwinChecksum, TwinSchemaUId)]);

		// Act
		SeededBlankPage.LeaseReport report = await SeededBlankPage.EndAsync(page, TimeSpan.Zero, CancellationToken.None);

		// Assert
		page.Reads.Should().Be(SeededBlankPage.ResetObservationAttempts,
			because: "the poll uses its whole budget when no read shows the reset");
		report.Failures.Should().ContainSingle(because: "the reset was saved but never shown")
			.Which.Should().Contain("none of 6 get-page read(s) showed it").And.Contain(TwinChecksum).And.Contain(ResetChecksum,
				because: "the blank reads carry another checksum than the save reported (TeamCity build 16330337: six reads of the pristine body)");
	}

	[Test]
	[Description("A reset that sync-pages refuses fails without reading the page.")]
	public async Task EndAsync_ShouldReportAFailure_WhenTheResetSaveFails() {
		// Arrange
		ScriptedPage page = new([SavedBlankRead()],
			reset: new SeededBlankPage.ResetSave(false, "save refused", null, null, ["w1"]));

		// Act
		SeededBlankPage.LeaseReport report = await SeededBlankPage.EndAsync(page, TimeSpan.Zero, CancellationToken.None);

		// Assert
		report.Failures.Should().ContainSingle(because: "the page could not be reset")
			.Which.Should().Contain("save refused", because: "the failure must carry the error of the tool");
		page.Reads.Should().Be(0, because: "there is no write to look for");
	}

	[Test]
	[Description("Stored rows: two schemas of the page in one package fail; one per package passes; after completed saves the design package must hold exactly one; unreadable rows fail only where the check is the subject of the test; rows without a package are not counted as duplicates.")]
	public void JudgeStoredRows_ShouldFailOnTwoSchemasInOnePackage_AndOnAMissingOneWhenRequired() {
		// Arrange
		SeededBlankPage.StoredSchemaRow baseRow = Row("11111111-1111-1111-1111-111111111111", "AutoTestClioMcp", "0a0a0a0a-0000-0000-0000-00000000000a");
		SeededBlankPage.StoredRows one = new([baseRow, Row(WrittenSchemaUId, "Custom", DesignPackageUId)], null);
		SeededBlankPage.StoredRows two = new(
			[baseRow, Row(WrittenSchemaUId, "Custom", DesignPackageUId), Row(TwinSchemaUId, "Custom", DesignPackageUId.ToUpperInvariant())], null);
		SeededBlankPage.StoredRows none = new([baseRow], null);
		SeededBlankPage.StoredRows unreadable = new(null, "esq failed");
		SeededBlankPage.StoredRows withoutPackage = new([Row("a", "?", "?"), Row("b", "?", "?")], null);

		// Act
		string? oneFailure = SeededBlankPage.JudgeStoredRows(one, DesignPackageUId, requireOneInDesignPackage: true, unreadableIsFailure: true);
		string? twoFailure = SeededBlankPage.JudgeStoredRows(two, DesignPackageUId, requireOneInDesignPackage: false, unreadableIsFailure: false);
		string? noneRequired = SeededBlankPage.JudgeStoredRows(none, DesignPackageUId, requireOneInDesignPackage: true, unreadableIsFailure: true);
		string? noneAllowed = SeededBlankPage.JudgeStoredRows(none, DesignPackageUId, requireOneInDesignPackage: false, unreadableIsFailure: false);
		string? unreadableStrict = SeededBlankPage.JudgeStoredRows(unreadable, DesignPackageUId, requireOneInDesignPackage: true, unreadableIsFailure: true);
		string? unreadableLenient = SeededBlankPage.JudgeStoredRows(unreadable, DesignPackageUId, requireOneInDesignPackage: false, unreadableIsFailure: false);
		string? withoutPackageFailure = SeededBlankPage.JudgeStoredRows(withoutPackage, null, requireOneInDesignPackage: false, unreadableIsFailure: false);

		// Assert
		oneFailure.Should().BeNull(because: "the page and its one replacing schema are the normal state");
		twoFailure.Should().Contain(WrittenSchemaUId).And.Contain(TwinSchemaUId).And.Contain("MORE THAN ONE",
			because: "two schemas of the page in one package are what two simultaneous first saves leave, and both must be named");
		noneRequired.Should().Contain("holds 0", because: "after completed saves the design package must hold the schema they wrote");
		noneAllowed.Should().BeNull(because: "a fresh site has no editable schema of the page before the first save");
		unreadableStrict.Should().Contain("esq failed", because: "a tripwire that cannot read the rows must not pass as if it had");
		unreadableLenient.Should().BeNull(because: "at the start of a lease the rows are a report; the lease has its own checks");
		withoutPackageFailure.Should().BeNull(because: "rows whose package the answer does not carry must not be mistaken for duplicates");
		SeededBlankPage.CountRowsInPackage(two, DesignPackageUId).Should().Be(2,
			because: "the count compares package identifiers regardless of letter case");
	}

	[Test]
	[Description("Two stored rows of the page in one package are reported as such, because a one-row name lookup can then resolve either of them.")]
	public void DescribeStoredLayers_ShouldFlagTwoRowsInOnePackage() {
		// Arrange
		using JsonDocument rows = JsonDocument.Parse("""
			[
				{"UId":"11111111-1111-1111-1111-111111111111","PackageName":"AutoTestClioMcp","PackageUId":"a","ExtendParent":false,"Checksum":"c1"},
				{"UId":"22222222-2222-2222-2222-222222222222","PackageName":"Custom","PackageUId":"b","ExtendParent":true,"Checksum":"c2"},
				{"UId":"33333333-3333-3333-3333-333333333333","PackageName":"Custom","PackageUId":"b","ExtendParent":true,"Checksum":"c3"}
			]
			""");

		// Act
		string description = SeededBlankPage.DescribeStoredLayers(rows.RootElement);

		// Assert
		description.Should().Contain("MORE THAN ONE ROW IN ONE PACKAGE (Custom x2)",
			because: "the duplicated package and the number of rows in it are the observation the investigation needs");
		description.Should().Contain("22222222-2222-2222-2222-222222222222").And
			.Contain("33333333-3333-3333-3333-333333333333",
				because: "each row is listed with its schema UId so a read and a write can be matched to a row by checksum");
	}

	[Test]
	[Description("One stored row per package - the page and its replacing schema - is reported without the duplicate warning.")]
	public void DescribeStoredLayers_ShouldNotFlagOneRowPerPackage() {
		// Arrange
		using JsonDocument rows = JsonDocument.Parse("""
			[
				{"UId":"11111111-1111-1111-1111-111111111111","PackageName":"AutoTestClioMcp","PackageUId":"a","ExtendParent":false,"Checksum":"c1"},
				{"UId":"22222222-2222-2222-2222-222222222222","PackageName":"Custom","PackageUId":"b","ExtendParent":true,"Checksum":"c2"}
			]
			""");

		// Act
		string description = SeededBlankPage.DescribeStoredLayers(rows.RootElement);

		// Assert
		description.Should().Contain("SysSchema rows: 2, at most one per package",
			because: "a page with one replacing schema is the normal state of the stand");
		description.Should().NotContain("MORE THAN ONE ROW",
			because: "the warning must appear only for a real duplicate");
	}

	[Test]
	[Description("A failure message that carries the page diagnostics keeps their JSON braces: FluentAssertions must not read them as format placeholders.")]
	public void FailureMessage_ShouldKeepJsonDiagnostics() {
		// Arrange
		const string diagnostics = "schema chain: [{\"schemaName\":\"ClioMcp_BlankPageToSave\",\"hasBody\":true}]";

		// Act
		Action act = () => false.Should().BeTrue(because: "the reset must be readable. {0}", diagnostics);

		// Assert
		act.Should().Throw<Exception>().Which.Message.Should().Contain(diagnostics,
			because: "the diagnostics are passed as a because argument exactly so their braces survive");
	}
}
