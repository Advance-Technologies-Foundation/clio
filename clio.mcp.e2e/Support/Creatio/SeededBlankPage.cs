using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using Clio.Package;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E.Support.Creatio;

/// <summary>
/// The Freedom UI page <c>ClioMcp_BlankPageToSave</c> that the page write fixtures share on the stand, and the
/// one place that brings it back to its blank body.
/// </summary>
/// <remarks>
/// <para>
/// <c>PageSyncToolE2ETests</c> and <c>PageUpdateToolE2ETests</c> write this one page from about fifteen tests
/// and build every probe body from the body they read first. Each test used to trust the cleanup of the test
/// before it, so one restore that did not take effect failed up to seven later tests with errors about a
/// missing empty marker pair (TeamCity builds 16330337 and 16330482). A test now takes a
/// <see cref="Lease"/> and the fixture's <c>[TearDown]</c> ends it.
/// </para>
/// <para>
/// The lease isolates the tests; it does not tolerate the defect that made them fail. It always brings the
/// page back to blank, and then FAILS the test for every observation that is the signature of the defect:
/// a page that an earlier, verified reset left blank and that is not blank now, or reads blank from another
/// stored schema or checksum than that reset left (<see cref="DecideStart"/>); a completed reset that the
/// first <c>get-page</c> does not show (<see cref="JudgeResetReads"/>) or that did not modify the stored
/// schema that read resolved (<see cref="JudgeResetLanded"/>); a read
/// that only looks blank - the body <c>get-page</c> synthesizes when it finds no editable schema, or
/// another stored schema of the page than the one the reset wrote
/// (<see cref="WhyReadDoesNotShowReset"/>); two stored schemas of the page in one package
/// (<see cref="JudgeStoredRows"/>). A run that was red before the lease existed because of the defect is
/// still red; only the cascade into unrelated tests is gone.
/// </para>
/// <para>
/// The reset lives in <c>[TearDown]</c> rather than in a <c>finally</c> block of the test: an exception
/// thrown in <c>finally</c> replaces the assertion that failed in the test body, while NUnit reports a
/// teardown failure next to it.
/// </para>
/// </remarks>
internal static class SeededBlankPage {

	/// <summary>The schema name of the shared page.</summary>
	internal const string SchemaName = "ClioMcp_BlankPageToSave";

	/// <summary>
	/// How many reads the reset makes at most until one shows its write. The poll exists only so that the
	/// page really is blank before the next test; a reset that needed more than one read still fails the
	/// test (<see cref="JudgeResetReads"/>).
	/// </summary>
	internal const int ResetObservationAttempts = 6;

	private static readonly TimeSpan ResetObservationPollInterval = TimeSpan.FromSeconds(1);
	private static readonly TimeSpan OperationTimeout = TimeSpan.FromMinutes(3);
	private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
	private const int DiagnosticSnippetLength = 400;
	private const string ClientUnitSchemaManagerName = "ClientUnitSchemaManager";

	// Every section whose content a probe fills. SCHEMA_ARGS is left out on purpose: the blank body carries
	// "()" there and no test writes it.
	private static readonly string[] SectionMarkers = [
		"SCHEMA_DEPS",
		"SCHEMA_VIEW_CONFIG_DIFF",
		"SCHEMA_VIEW_MODEL_CONFIG_DIFF",
		"SCHEMA_MODEL_CONFIG_DIFF",
		"SCHEMA_HANDLERS",
		"SCHEMA_CONVERTERS",
		"SCHEMA_VALIDATORS"
	];

	private const string LastSectionMarker = "/**SCHEMA_VALIDATORS*/";

	/// <summary>
	/// Process-wide record of the leases: which lease is the first of the test process, which fixtures
	/// already checked the stored rows, how the previous lease ended and what its verified reset left.
	/// </summary>
	internal static LeaseLedger Ledger { get; } = new();

	/// <summary>
	/// Records that a test wrote the page outside a lease (<c>ClioPagesConcurrencyE2ETests</c>), so the next
	/// lease does not compare the page with the last verified reset. Call it before the write.
	/// </summary>
	/// <param name="writer">The test that writes.</param>
	internal static void RecordWriteOutsideLease(string writer) => Ledger.RecordWriteOutsideLease(writer);

	/// <summary>
	/// The blank body the reset writes: the body clio itself gives a replacing schema it creates
	/// (<see cref="PageUpdateCommand.BuildEmptyReplacingBody"/>), which is also what <c>get-page</c> returns
	/// for a page that has no editable schema yet.
	/// </summary>
	/// <returns>The blank web page body of <see cref="SchemaName"/>.</returns>
	internal static string BuildBlankBody() =>
		PageUpdateCommand.BuildEmptyReplacingBody(SchemaName, PageSchemaType.Web);

	/// <summary>
	/// Decides whether <paramref name="body"/> is the blank page: every section between a marker pair is
	/// empty and nothing but the closing brackets of the module follows the last section.
	/// </summary>
	/// <param name="body">The body <c>get-page</c> wrote to <c>body.js</c>.</param>
	/// <param name="reason">What makes the body not blank; <c>null</c> when it is blank.</param>
	/// <returns><c>true</c> when a probe can be built from the body without discarding content.</returns>
	internal static bool IsBlank(string? body, out string? reason) {
		if (string.IsNullOrWhiteSpace(body)) {
			reason = "the body is empty";
			return false;
		}
		foreach (string markerName in SectionMarkers) {
			string marker = Regex.Escape($"/**{markerName}*/");
			Match section = Regex.Match(body, $"{marker}(?<content>[\\s\\S]*?){marker}",
				RegexOptions.CultureInvariant, RegexTimeout);
			if (!section.Success) {
				reason = $"the {markerName} marker pair is missing";
				return false;
			}
			string content = RemoveWhitespace(section.Groups["content"].Value);
			if (content is not ("" or "[]" or "{}")) {
				reason = $"the {markerName} section is not empty: {Shorten(content)}";
				return false;
			}
		}
		string tail = RemoveWhitespace(body[(body.LastIndexOf(LastSectionMarker, StringComparison.Ordinal)
			+ LastSectionMarker.Length)..]);
		if (tail.Any(character => character is not ('}' or ')' or ';'))) {
			reason = $"content follows the module: {Shorten(tail)}";
			return false;
		}
		reason = null;
		return true;
	}

	// ------------------------------------------------------------------------------------------------
	// Decisions. Pure functions of what was read and saved, so SeededBlankPageTests covers them without a
	// stand.
	// ------------------------------------------------------------------------------------------------

	/// <summary>
	/// Classifies one read of the page. A body that is blank is a <see cref="PageState.SavedBlank"/> page
	/// only when the read resolved an editable schema; otherwise it is the body <c>get-page</c> synthesizes
	/// when it finds none, which proves nothing about what is stored.
	/// </summary>
	/// <param name="read">The read.</param>
	/// <param name="reason">Why the page is not a saved blank page; <c>null</c> when it is.</param>
	/// <returns>The state of the page as this read shows it.</returns>
	internal static PageState Classify(PageObservation read, out string? reason) {
		ArgumentNullException.ThrowIfNull(read);
		if (!read.Success) {
			reason = $"get-page failed: {read.Error}";
			return PageState.Unreadable;
		}
		if (!IsBlank(read.Body, out string? notBlankReason)) {
			reason = $"{notBlankReason}; components named in the body: {DescribeComponents(read.Body)}";
			return PageState.NotBlank;
		}
		if (!read.EditableSchemaExists || string.IsNullOrWhiteSpace(read.EditableSchemaUId)) {
			reason = "the body is blank, but get-page resolved NO editable schema in the design package "
				+ $"(editableSchemaExists={read.EditableSchemaExists}, willCreateReplacing={read.WillCreateReplacing}): "
				+ "this is the body get-page synthesizes, not a saved page";
			return PageState.SynthesizedBlank;
		}
		reason = null;
		return PageState.SavedBlank;
	}

	/// <summary>
	/// Decides what the start of a lease does with the first read of the page. Only the first lease of the
	/// test process may find the page not blank, or without an editable schema, without failing. A later lease
	/// that follows a verified reset, with no write outside a lease since, also fails when the page reads
	/// blank from another editable schema or with another checksum than that reset left.
	/// </summary>
	/// <param name="first">The read made before the test.</param>
	/// <param name="prior">What the leases before this one left (<see cref="LeaseLedger.Start"/>).</param>
	/// <returns>Whether to reset the page, and the failure or note to report.</returns>
	internal static StartDecision DecideStart(PageObservation first, PriorLeases prior) {
		ArgumentNullException.ThrowIfNull(prior);
		PageState state = Classify(first, out string? reason);
		const string firstLeaseCase = "This is the FIRST lease of the test process, so it is not treated as a "
			+ "failure: a developer's stand may start dirty, and the code cannot tell whether the page has been saved "
			+ "on this site before.";
		string laterLeaseCase = DescribeLaterLeaseCase(prior);
		bool firstLeaseOfProcess = prior.IsFirstLeaseOfProcess;
		return state switch {
			PageState.SavedBlank when WhyNotTheLastVerifiedReset(first, prior) is { } changed => new StartDecision(
				Reset: true,
				Failure: $"'{SchemaName}' reads blank before this test, but {changed}. {laterLeaseCase} "
					+ "The page was reset so the next test starts clean.",
				Note: null),
			PageState.SavedBlank => new StartDecision(Reset: false, Failure: null,
				Note: prior.PreviousEnd == PreviousLeaseEnd.Verified && prior.WriterOutsideLease is not null
					? $"'{SchemaName}' is not compared with the last verified reset: {prior.WriterOutsideLease} wrote the "
						+ "page outside a lease since then."
					: null),
			PageState.Unreadable => new StartDecision(Reset: false,
				Failure: $"get-page must read the shared page '{SchemaName}' before the test. {reason}", Note: null),
			PageState.SynthesizedBlank when firstLeaseOfProcess => new StartDecision(Reset: false, Failure: null,
				Note: $"'{SchemaName}' has no editable schema before the test ({reason}). {firstLeaseCase}"),
			PageState.NotBlank when firstLeaseOfProcess => new StartDecision(Reset: true, Failure: null,
				Note: $"'{SchemaName}' was found NOT blank before the test ({reason}); it is reset. {firstLeaseCase}"),
			PageState.SynthesizedBlank => new StartDecision(Reset: true,
				Failure: $"'{SchemaName}' only LOOKS blank before this test: {reason}. {laterLeaseCase} "
					+ "The page was reset so the next test starts clean.",
				Note: null),
			_ => new StartDecision(Reset: true,
				Failure: $"'{SchemaName}' was found NOT blank before this test: {reason}. {laterLeaseCase} "
					+ "The page was reset so the next test starts clean.",
				Note: null)
		};
	}

	// Compares a blank read with what the previous lease's verified reset left. Only meaningful when that
	// lease ended verified and no test wrote the page outside a lease since; otherwise the state is unknown.
	private static string? WhyNotTheLastVerifiedReset(PageObservation first, PriorLeases prior) {
		if (prior.PreviousEnd != PreviousLeaseEnd.Verified || prior.WriterOutsideLease is not null
			|| prior.LastVerified is not { } verified) {
			return null;
		}
		if (!string.Equals(NormalizeGuid(first.EditableSchemaUId), NormalizeGuid(verified.EditableSchemaUId),
				StringComparison.OrdinalIgnoreCase)) {
			return $"from editable schema {first.EditableSchemaUId} (checksum {first.Checksum ?? "none"}) while the "
				+ $"previous lease's verified reset was read from editable schema {verified.EditableSchemaUId} "
				+ $"(checksum {verified.Checksum}): the read resolves another stored schema of the page";
		}
		return string.Equals(first.Checksum, verified.Checksum, StringComparison.OrdinalIgnoreCase)
			? null
			: $"with checksum {first.Checksum ?? "none"} while the previous lease's verified reset left checksum "
				+ $"{verified.Checksum} on the same editable schema {verified.EditableSchemaUId}, and nothing that ran "
				+ "since was supposed to write it";
	}

	// The second half of a later lease's failure: it says only what the record of the previous lease proves.
	private static string DescribeLaterLeaseCase(PriorLeases prior) => prior.PreviousEnd switch {
		PreviousLeaseEnd.Verified when prior.WriterOutsideLease is not null =>
			"This is NOT the first lease of the test process. The lease before it ended with a reset that get-page "
			+ $"showed on its first read, but since then {prior.WriterOutsideLease} wrote the page outside a lease. "
			+ "That test saves the body it read, so it should have left a blank page blank; its output shows what "
			+ "get-page resolved before and after its saves.",
		PreviousLeaseEnd.Verified =>
			"This is NOT the first lease of the test process. The lease before it ended with a reset that get-page "
			+ "showed on its first read (editable schema "
			+ $"{prior.LastVerified?.EditableSchemaUId ?? "?"}, checksum {prior.LastVerified?.Checksum ?? "?"}, the "
			+ "checksum the save reported, stored modification time moved), and no test wrote the page outside a "
			+ "lease since. So an earlier completed save did not land where it was read, or this read does not show "
			+ "the latest save - the product defect, not this test.",
		PreviousLeaseEnd.NotVerified =>
			"This is NOT the first lease of the test process, and the lease before it did NOT end with a verified "
			+ "reset: its reset failed or was not shown by the first read (see the failure of the previous test). This "
			+ "state may be what that test or its reset left, so it is not by itself a new occurrence of the defect.",
		_ =>
			"This is NOT the first lease of the test process, and the lease before it did not complete: its reset "
			+ "threw or never ran (see the failure of the previous test, in its TearDown or its lease start). This "
			+ "state may be what that test left, so it is not by itself a new occurrence of the defect."
	};

	/// <summary>
	/// Says why <paramref name="read"/> does not prove that the reset <paramref name="reset"/> is what
	/// <c>get-page</c> returns. A blank body is not enough: the read must come from an editable schema and
	/// carry the checksum the reset save reported.
	/// </summary>
	/// <param name="read">A read made after the reset save.</param>
	/// <param name="reset">The reset save.</param>
	/// <returns><c>null</c> when the read shows the reset; otherwise the reason it does not.</returns>
	internal static string? WhyReadDoesNotShowReset(PageObservation read, ResetSave reset) {
		ArgumentNullException.ThrowIfNull(reset);
		PageState state = Classify(read, out string? reason);
		if (state != PageState.SavedBlank) {
			return reason;
		}
		if (string.IsNullOrWhiteSpace(reset.Checksum)) {
			return "the body is blank, but the reset save reported no post-save checksum, so nothing proves that "
				+ "the read shows the schema the reset wrote";
		}
		if (!string.Equals(read.Checksum, reset.Checksum, StringComparison.OrdinalIgnoreCase)) {
			return $"the body is blank, but the read resolved editable schema {read.EditableSchemaUId} with checksum "
				+ $"{read.Checksum ?? "none"} while the reset save reported checksum {reset.Checksum}: the read shows "
				+ "another stored schema of the page than the one the reset wrote, or a state from before the reset";
		}
		return null;
	}

	/// <summary>
	/// Judges the reads made after a successful reset save. The reset passes only when the FIRST read already
	/// shows it; a later read that shows it keeps the page usable for the next test but is still a failure.
	/// </summary>
	/// <param name="reset">The reset save.</param>
	/// <param name="reads">The reads made after it, in order; the poll stops at the first that shows the reset.</param>
	/// <param name="phase">When the reset ran, for the message.</param>
	/// <returns><c>null</c> when the first read showed the reset; otherwise the failure.</returns>
	internal static string? JudgeResetReads(ResetSave reset, IReadOnlyList<PageObservation> reads, string phase) {
		ArgumentNullException.ThrowIfNull(reset);
		ArgumentNullException.ThrowIfNull(reads);
		if (reads.Count == 0) {
			return $"the reset of '{SchemaName}' {phase} was saved but never read back";
		}
		string checksums = $"checksum the reset save reported: {reset.Checksum ?? "none"}; checksums read: "
			+ string.Join(", ", reads.Select(read => read.Checksum ?? "none"));
		string allReads = string.Join(" || ", reads.Select((read, index) => $"read {index + 1}: {read.Describe()}"));
		string? lastReason = WhyReadDoesNotShowReset(reads[^1], reset);
		if (lastReason is not null) {
			return $"sync-pages reported the reset of '{SchemaName}' {phase} as saved (conflict check "
				+ $"{reset.ConflictCheck ?? "none"}), yet none of {reads.Count} get-page read(s) showed it. Last read: "
				+ $"{lastReason}. {checksums}. {allReads}";
		}
		if (reads.Count > 1) {
			return $"the reset of '{SchemaName}' {phase} was saved, but get-page showed it only on read {reads.Count} "
				+ $"of at most {ResetObservationAttempts}: a completed write must be visible to the next read. First "
				+ $"read: {WhyReadDoesNotShowReset(reads[0], reset)}. {checksums}. {allReads}";
		}
		return null;
	}

	/// <summary>
	/// Judges the stored <c>SysSchema</c> rows of the page. Two rows of one name in one package are two
	/// schemas of the page: the name lookups of <c>get-page</c> and <c>update-page</c> take one row without
	/// an order, so a read and a write can then address different schemas.
	/// </summary>
	/// <param name="stored">The rows read from the database.</param>
	/// <param name="designPackageUId">The design package <c>get-page</c> reported; may be empty.</param>
	/// <param name="requireOneInDesignPackage">
	/// Whether the design package must hold exactly one row (after a save) rather than at most one (a fresh
	/// site has none before the first save).
	/// </param>
	/// <param name="unreadableIsFailure">Whether rows that could not be read fail the check.</param>
	/// <returns><c>null</c> when the rows are in order; otherwise the failure.</returns>
	internal static string? JudgeStoredRows(
		StoredRows stored, string? designPackageUId, bool requireOneInDesignPackage, bool unreadableIsFailure) {
		ArgumentNullException.ThrowIfNull(stored);
		if (stored.Rows is null) {
			return unreadableIsFailure
				? $"the stored SysSchema rows of '{SchemaName}' could not be read, so the number of schemas of the page "
					+ $"in its design package is unknown: {stored.Error}"
				: null;
		}
		if (unreadableIsFailure && stored.Rows.Any(row => PackageKey(row) is null)) {
			return $"{stored.Rows.Count(row => PackageKey(row) is null)} stored SysSchema row(s) of '{SchemaName}' carry "
				+ "no package, so two schemas of the page in one package cannot be counted: "
				+ string.Join(", ", stored.Rows.Where(row => PackageKey(row) is null).Select(row => row.UId));
		}
		// A row whose package the answer does not carry takes no part: it must not be counted as a duplicate.
		string[] duplicated = stored.Rows
			.Where(row => PackageKey(row) is not null)
			.GroupBy(row => PackageKey(row)!, StringComparer.OrdinalIgnoreCase)
			.Where(group => group.Count() > 1)
			.Select(group => $"{group.First().PackageName} ({group.Key}) holds {group.Count()}: "
				+ string.Join(", ", group.Select(row => $"{row.UId} createdOn={row.CreatedOn}")))
			.ToArray();
		if (duplicated.Length > 0) {
			return $"one package holds MORE THAN ONE stored schema named '{SchemaName}': {string.Join("; ", duplicated)}. "
				+ "get-page and update-page find the schema by name and take one row without an order, so a read and "
				+ "a write can address different schemas of the page - a completed save is then not what the next "
				+ "read returns. Two simultaneous first saves of a page create such a pair.";
		}
		if (!requireOneInDesignPackage) {
			return null;
		}
		if (string.IsNullOrWhiteSpace(designPackageUId)) {
			return $"get-page reported no design package for '{SchemaName}', so the stored schema of the page in it "
				+ "cannot be counted";
		}
		int inDesignPackage = CountRowsInPackage(stored, designPackageUId);
		return inDesignPackage == 1
			? null
			: $"the design package {designPackageUId} holds {inDesignPackage} stored schema(s) named '{SchemaName}' "
				+ "after completed saves; exactly one is expected";
	}

	/// <summary>Counts the stored rows of the page in one package.</summary>
	/// <param name="stored">The rows read from the database.</param>
	/// <param name="packageUId">The package.</param>
	/// <returns>The number of rows; <c>-1</c> when the rows could not be read.</returns>
	internal static int CountRowsInPackage(StoredRows stored, string? packageUId) {
		ArgumentNullException.ThrowIfNull(stored);
		return stored.Rows is null
			? -1
			: stored.Rows.Count(row => string.Equals(
				NormalizeGuid(row.PackageUId), NormalizeGuid(packageUId), StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// Judges whether the reset save modified the stored schema that the first read after it resolved. A
	/// blank-over-blank save writes the same body and, when the checksum depends on the content only, the same
	/// checksum, so only the stored <c>SysSchema.ModifiedOn</c> tells a save that landed from one that did not.
	/// Both values come from the site (<c>execute-esq</c>), so the clock of the test agent takes no part.
	/// </summary>
	/// <param name="before">The stored rows read before the reset save.</param>
	/// <param name="after">The stored rows read after the first read that showed the reset.</param>
	/// <param name="firstRead">The first read after the reset save.</param>
	/// <param name="phase">When the reset ran, for the message.</param>
	/// <param name="rowsRequired">Whether rows that could not be read fail the check.</param>
	/// <returns><c>null</c> when the schema the read resolved was modified by the save; otherwise the failure.</returns>
	internal static string? JudgeResetLanded(
		StoredRows before, StoredRows after, PageObservation firstRead, string phase, bool rowsRequired) {
		ArgumentNullException.ThrowIfNull(before);
		ArgumentNullException.ThrowIfNull(after);
		ArgumentNullException.ThrowIfNull(firstRead);
		if (before.Rows is null || after.Rows is null) {
			return rowsRequired
				? $"the reset of '{SchemaName}' {phase} cannot be shown to have modified a stored schema: the SysSchema "
					+ $"rows could not be read (before the reset: {before.Error ?? "read"}; after it: {after.Error ?? "read"})"
				: null;
		}
		string schemaUId = NormalizeGuid(firstRead.EditableSchemaUId);
		StoredSchemaRow? afterRow = after.Rows.FirstOrDefault(row =>
			string.Equals(NormalizeGuid(row.UId), schemaUId, StringComparison.OrdinalIgnoreCase));
		if (afterRow is null) {
			return $"the first read after the reset of '{SchemaName}' {phase} resolved editable schema "
				+ $"{firstRead.EditableSchemaUId}, but no stored SysSchema row of the page has that UId";
		}
		StoredSchemaRow? beforeRow = before.Rows.FirstOrDefault(row =>
			string.Equals(NormalizeGuid(row.UId), schemaUId, StringComparison.OrdinalIgnoreCase));
		if (beforeRow is null || ModificationMoved(beforeRow.ModifiedOn, afterRow.ModifiedOn)) {
			// No row before: the reset created the schema the read resolved.
			return null;
		}
		return $"sync-pages reported the reset of '{SchemaName}' {phase} as saved and the first read resolved editable "
			+ $"schema {firstRead.EditableSchemaUId}, but the stored SysSchema.ModifiedOn of that schema did not move: "
			+ $"{beforeRow.ModifiedOn} before the reset save, {afterRow.ModifiedOn} after the first read. A blank body "
			+ "and an unchanged checksum cannot tell a blank-over-blank save that landed from one that did not; this "
			+ "save did not land in the schema get-page reads";
	}

	/// <summary>
	/// Judges whether <c>get-page</c> resolves the one stored schema of the page in its design package: the
	/// read must come from an editable schema whose UId and checksum are those of that row. After completed
	/// saves anything else is a completed save that is not what the next read returns.
	/// </summary>
	/// <param name="read">A read made after the saves.</param>
	/// <param name="stored">The stored rows read after that read.</param>
	/// <returns><c>null</c> when the read resolves that row; otherwise the failure.</returns>
	internal static string? JudgeReadAgainstDesignPackageRow(PageObservation read, StoredRows stored) {
		ArgumentNullException.ThrowIfNull(read);
		ArgumentNullException.ThrowIfNull(stored);
		if (!read.Success) {
			return $"get-page could not read '{SchemaName}': {read.Error}";
		}
		if (!read.EditableSchemaExists || string.IsNullOrWhiteSpace(read.EditableSchemaUId)) {
			return $"get-page resolved NO editable schema of '{SchemaName}' (editableSchemaExists="
				+ $"{read.EditableSchemaExists}) although the saves completed";
		}
		if (stored.Rows is null) {
			return $"the stored SysSchema rows of '{SchemaName}' could not be read, so the editable schema get-page "
				+ $"resolved cannot be matched to one: {stored.Error}";
		}
		StoredSchemaRow[] inDesignPackage = stored.Rows.Where(row => string.Equals(
			NormalizeGuid(row.PackageUId), NormalizeGuid(read.DesignPackageUId), StringComparison.OrdinalIgnoreCase)).ToArray();
		if (inDesignPackage.Length != 1) {
			return $"the design package {read.DesignPackageUId} holds {inDesignPackage.Length} stored schema(s) of "
				+ $"'{SchemaName}', so the editable schema get-page resolved cannot be matched to one";
		}
		StoredSchemaRow row = inDesignPackage[0];
		bool sameSchema = string.Equals(NormalizeGuid(read.EditableSchemaUId), NormalizeGuid(row.UId),
			StringComparison.OrdinalIgnoreCase);
		bool sameChecksum = string.Equals(read.Checksum, row.Checksum, StringComparison.OrdinalIgnoreCase);
		return sameSchema && sameChecksum
			? null
			: $"get-page resolved editable schema {read.EditableSchemaUId} with checksum {read.Checksum ?? "none"}, but "
				+ $"the one stored schema of '{SchemaName}' in the design package is {row.UId} with checksum {row.Checksum}: "
				+ "the read does not return what the completed saves stored";
	}

	// The stored ModifiedOn is the raw DataService value (ISO 8601). Compared as instants when both parse,
	// otherwise as text: any change counts as moved.
	private static bool ModificationMoved(string before, string after) =>
		DateTime.TryParse(before, System.Globalization.CultureInfo.InvariantCulture,
			System.Globalization.DateTimeStyles.RoundtripKind, out DateTime beforeTime)
		&& DateTime.TryParse(after, System.Globalization.CultureInfo.InvariantCulture,
			System.Globalization.DateTimeStyles.RoundtripKind, out DateTime afterTime)
			? afterTime > beforeTime
			: !string.Equals(before, after, StringComparison.Ordinal);

	// ------------------------------------------------------------------------------------------------
	// The lease's two steps over an injected page access, so the unit tests drive them with a fake.
	// ------------------------------------------------------------------------------------------------

	/// <summary>
	/// The start of a lease: reads the page, optionally checks its stored rows, resets it when it is not a
	/// saved blank page, and reports every observation that must fail the test.
	/// </summary>
	/// <param name="access">Reads and writes the page.</param>
	/// <param name="prior">What the leases before this one left.</param>
	/// <param name="checkStoredRows">Whether to list and judge the stored rows of the page.</param>
	/// <param name="pollInterval">The wait between the reads after a reset.</param>
	/// <param name="cancellationToken">Cancels the calls.</param>
	/// <param name="echo">Receives each log line as it is observed, so a later exception does not lose it.</param>
	/// <returns>
	/// The failures (empty when the test may run), the log of what was observed, and the first read after the
	/// reset when the lease reset the page and that reset was verified.
	/// </returns>
	internal static async Task<LeaseReport> BeginAsync(
		IPageAccess access, PriorLeases prior, bool checkStoredRows, TimeSpan pollInterval,
		CancellationToken cancellationToken, Action<string>? echo = null) {
		ArgumentNullException.ThrowIfNull(access);
		ArgumentNullException.ThrowIfNull(prior);
		List<string> failures = [];
		List<string> log = [];
		void Log(string line) {
			log.Add(line);
			echo?.Invoke(line);
		}
		PageObservation first = await access.ReadAsync(cancellationToken);
		Log($"[seed-page] read before the test ({prior.Describe()}): {first.Describe()}");
		StartDecision decision = DecideStart(first, prior);
		StoredRows? stored = null;
		if (checkStoredRows || decision.Reset) {
			// Before the reset: the reset overwrites what these rows show.
			stored = await access.ReadStoredRowsAsync(cancellationToken);
			Log($"[seed-page] stored schemas of '{SchemaName}' before the test:{Environment.NewLine}{DescribeStoredRows(stored)}");
			// The first lease of the process only reports rows it cannot read; a later one fails on them.
			if (checkStoredRows && JudgeStoredRows(stored, first.DesignPackageUId, requireOneInDesignPackage: false,
					unreadableIsFailure: !prior.IsFirstLeaseOfProcess) is { } storedFailure) {
				failures.Add(storedFailure);
			}
		}
		if (decision.Note is not null) {
			Log($"[seed-page] {decision.Note}");
		}
		if (decision.Failure is not null) {
			failures.Add(decision.Failure);
		}
		PageObservation? verified = null;
		if (decision.Reset) {
			(IReadOnlyList<string> resetFailures, verified) = await ResetAndVerifyAsync(access, "before the test",
				stored!, rowsRequired: !prior.IsFirstLeaseOfProcess, pollInterval, Log, cancellationToken);
			failures.AddRange(resetFailures);
		}
		return new LeaseReport(failures, log, verified);
	}

	/// <summary>
	/// The end of a lease: always writes the blank body and requires the first read to show that write.
	/// It does not skip the write when the page already looks blank, because a blank body alone cannot tell
	/// a reset page from a read that shows no editable schema or another stored schema. The stored rows are
	/// read before and after the save: the schema the first read resolved must have been modified by it.
	/// </summary>
	/// <param name="access">Reads and writes the page.</param>
	/// <param name="pollInterval">The wait between the reads after the reset.</param>
	/// <param name="cancellationToken">Cancels the calls.</param>
	/// <param name="echo">Receives each log line as it is observed, so a later exception does not lose it.</param>
	/// <returns>
	/// The failures (empty when the reset was visible at once), the log of what was observed, and the verified
	/// first read when there are no failures.
	/// </returns>
	internal static async Task<LeaseReport> EndAsync(
		IPageAccess access, TimeSpan pollInterval, CancellationToken cancellationToken, Action<string>? echo = null) {
		ArgumentNullException.ThrowIfNull(access);
		List<string> log = [];
		void Log(string line) {
			log.Add(line);
			echo?.Invoke(line);
		}
		StoredRows before = await access.ReadStoredRowsAsync(cancellationToken);
		Log($"[seed-page] stored schemas of '{SchemaName}' before the reset after the test:{Environment.NewLine}{DescribeStoredRows(before)}");
		(IReadOnlyList<string> failures, PageObservation? verified) = await ResetAndVerifyAsync(
			access, "after the test", before, rowsRequired: true, pollInterval, Log, cancellationToken);
		return new LeaseReport(failures, log, verified);
	}

	private static async Task<(IReadOnlyList<string> Failures, PageObservation? Verified)> ResetAndVerifyAsync(
		IPageAccess access, string phase, StoredRows before, bool rowsRequired, TimeSpan pollInterval,
		Action<string> log, CancellationToken cancellationToken) {
		ResetSave reset = await access.WriteBlankAsync(cancellationToken);
		log($"[seed-page] reset save {phase}: {reset.Describe()}");
		if (!reset.Success) {
			return ([
				$"sync-pages must reset the shared page '{SchemaName}' to its blank body {phase}. Error of the reset: "
				+ $"{reset.Error ?? "sync-pages returned no page result"}. Warnings: {string.Join(" | ", reset.Warnings)}"
			], null);
		}
		List<PageObservation> reads = [];
		// The poll keeps the isolation: the next test must not start before the page is blank. Whether the
		// reset PASSES is decided by JudgeResetReads, which accepts the first read only.
		await BoundedPollGate.PollUntilAsync(
			async pollToken => {
				PageObservation read = await access.ReadAsync(pollToken);
				reads.Add(read);
				log($"[seed-page] read {reads.Count} after the reset {phase}: {read.Describe()}");
				return read;
			},
			read => WhyReadDoesNotShowReset(read, reset) is null,
			ResetObservationAttempts,
			pollInterval,
			cancellationToken);
		if (JudgeResetReads(reset, reads, phase) is { } readFailure) {
			return ([readFailure], null);
		}
		// The first read showed the reset by body and checksum; the stored modification time says whether the
		// save really landed in the schema that read resolved.
		StoredRows after = await access.ReadStoredRowsAsync(cancellationToken);
		log($"[seed-page] stored schemas of '{SchemaName}' after the reset {phase}:{Environment.NewLine}{DescribeStoredRows(after)}");
		return JudgeResetLanded(before, after, reads[0], phase, rowsRequired) is { } landedFailure
			? ([landedFailure], null)
			: ([], reads[0]);
	}

	// ------------------------------------------------------------------------------------------------
	// The stand-facing API of the fixtures.
	// ------------------------------------------------------------------------------------------------

	/// <summary>
	/// Starts a test's use of the shared page: resets it when it is not a saved blank page and FAILS the test
	/// when that state is one only the product defect produces (see <see cref="DecideStart"/>). The first
	/// lease of each fixture also lists the stored schemas of the page and fails when one package holds two.
	/// Call it only from a test that is allowed to write the stand.
	/// </summary>
	/// <param name="session">The MCP server session of the fixture.</param>
	/// <param name="environmentName">The sandbox environment the test writes.</param>
	/// <returns>The lease to hand to <see cref="Lease.EndAsync"/> in <c>[TearDown]</c>.</returns>
	internal static async Task<Lease> LeaseAsync(McpServerSession session, string environmentName) {
		ArgumentNullException.ThrowIfNull(session);
		(PriorLeases prior, bool checkStoredRows) = Ledger.Start(TestContext.CurrentContext.Test.ClassName);
		Func<CancellationToken, Task<string>> describe =
			cancellationToken => DescribeAsync(session, environmentName, cancellationToken);
		// Not the test's token: the lease has its own budget.
		using CancellationTokenSource cancellation = new(OperationTimeout);
		LeaseReport report;
		using (McpPageAccess access = new(session, environmentName)) {
			report = await BeginAsync(access, prior, checkStoredRows, ResetObservationPollInterval,
				cancellation.Token, line => WriteToTestOutput([line]));
		}
		if (report.Failures.Count > 0) {
			// The test fails here and its TearDown has no lease to end: this start is the lease's last step.
			Ledger.RecordEnd(report);
		}
		await FailOnAsync(report, describe, "before the test", savesOfTheTest: null);
		return new Lease(session, () => new McpPageAccess(session, environmentName), describe, Ledger,
			ResetObservationPollInterval);
	}

	/// <summary>
	/// Reads what <c>get-page</c> resolves for the page and its stored <c>SysSchema</c> rows, for a test that
	/// asserts on them itself (<c>ClioPagesConcurrencyE2ETests</c>).
	/// </summary>
	/// <param name="session">An MCP server session.</param>
	/// <param name="environmentName">The sandbox environment.</param>
	/// <param name="cancellationToken">Cancels the reads.</param>
	/// <returns>The read and the stored rows.</returns>
	internal static async Task<(PageObservation Read, StoredRows Stored)> ReadStoredStateAsync(
		McpServerSession session, string environmentName, CancellationToken cancellationToken) {
		ArgumentNullException.ThrowIfNull(session);
		using McpPageAccess access = new(session, environmentName);
		PageObservation read = await access.ReadAsync(cancellationToken);
		StoredRows stored = await access.ReadStoredRowsAsync(cancellationToken);
		return (read, stored);
	}

	/// <summary>
	/// Describes what the stand holds for the page: what <c>get-page</c> resolves and every stored
	/// <c>SysSchema</c> row of that name. Never throws; a part that could not be read says why.
	/// </summary>
	/// <param name="session">The MCP server session of the fixture.</param>
	/// <param name="environmentName">The sandbox environment.</param>
	/// <param name="cancellationToken">Cancels the reads.</param>
	/// <returns>A multi-line description for a failure message or the build log.</returns>
	internal static async Task<string> DescribeAsync(
		McpServerSession session, string environmentName, CancellationToken cancellationToken) {
		StringBuilder description = new();
		try {
			description.AppendLine().AppendLine($"--- state of '{SchemaName}' on '{environmentName}' ---");
			using McpPageAccess access = new(session, environmentName);
			try {
				PageObservation read = await access.ReadAsync(cancellationToken);
				description.AppendLine($"get-page: {read.Describe()}");
				description.AppendLine($"get-page body: {Shorten(read.Body ?? string.Empty)}");
				description.AppendLine($"get-page schema chain (first is the page): {read.SchemaChain ?? "not available"}");
			} catch (Exception readFailure) {
				description.AppendLine($"get-page could not be read: {readFailure.Message}");
			}
			description.AppendLine(DescribeStoredRows(await access.ReadStoredRowsAsync(cancellationToken)));
		} catch (Exception describeFailure) {
			// Diagnostics must never replace the failure they describe.
			description.AppendLine($"the state could not be described: {describeFailure.Message}");
		}
		return description.ToString();
	}

	/// <summary>
	/// Formats the stored rows of the page, one line per row, and names the packages that hold more than
	/// one.
	/// </summary>
	/// <param name="stored">The rows read from the database.</param>
	/// <returns>One line per row and a verdict line, or the reason the rows could not be read.</returns>
	internal static string DescribeStoredRows(StoredRows stored) {
		ArgumentNullException.ThrowIfNull(stored);
		if (stored.Rows is null) {
			return $"SysSchema rows could not be read: {stored.Error}";
		}
		StringBuilder description = new();
		foreach (StoredSchemaRow row in stored.Rows) {
			description.AppendLine(
				$"SysSchema row: name={SchemaName} uId={row.UId} package={row.PackageName} ({row.PackageUId}) "
				+ $"extendParent={row.ExtendParent} checksum={row.Checksum} "
				+ $"createdOn={row.CreatedOn} modifiedOn={row.ModifiedOn}");
		}
		// Grouped exactly as JudgeStoredRows groups them, so the printout and the verdict cannot disagree.
		string[] duplicated = stored.Rows
			.Where(row => PackageKey(row) is not null)
			.GroupBy(row => PackageKey(row)!, StringComparer.OrdinalIgnoreCase)
			.Where(group => group.Count() > 1)
			.Select(group => $"{group.First().PackageName} x{group.Count()}")
			.ToArray();
		int withoutPackage = stored.Rows.Count(row => PackageKey(row) is null);
		description.Append(duplicated.Length > 0
			? $"SysSchema rows: {stored.Rows.Count}. MORE THAN ONE ROW IN ONE PACKAGE ({string.Join(", ", duplicated)}): "
				+ "a read and a write can resolve different schemas of this page."
			: $"SysSchema rows: {stored.Rows.Count}, at most one per package.");
		if (withoutPackage > 0) {
			description.Append($" {withoutPackage} row(s) carry no package and are not counted.");
		}
		return description.ToString();
	}

	/// <summary>Parses the rows array of the <c>SysSchema</c> SelectQuery answer.</summary>
	/// <param name="rows">The rows array.</param>
	/// <returns>The stored rows of the page.</returns>
	internal static StoredRows ParseStoredRows(JsonElement rows) {
		List<StoredSchemaRow> parsed = [];
		foreach (JsonElement row in rows.EnumerateArray()) {
			parsed.Add(new StoredSchemaRow(
				Text(row, "UId"), Text(row, "PackageName"), Text(row, "PackageUId"), Text(row, "ExtendParent"),
				Text(row, "Checksum"), Text(row, "CreatedOn"), Text(row, "ModifiedOn")));
		}
		return new StoredRows(parsed, Error: null);
	}

	/// <summary>
	/// Formats the <c>SysSchema</c> rows of the page and names the packages that hold more than one.
	/// </summary>
	/// <param name="rows">The rows array of the SelectQuery answer.</param>
	/// <returns>One line per row and a verdict line.</returns>
	internal static string DescribeStoredLayers(JsonElement rows) => DescribeStoredRows(ParseStoredRows(rows));

	/// <summary>
	/// Fails the current test when <paramref name="report"/> carries failures, with the log of the lease and
	/// the state of the page in the message.
	/// </summary>
	private static async Task FailOnAsync(
		LeaseReport report, Func<CancellationToken, Task<string>> describe, string phase, string? savesOfTheTest) {
		if (report.Failures.Count == 0) {
			return;
		}
		using CancellationTokenSource diagnosticsCts = new(OperationTimeout);
		string diagnostics = await describe(diagnosticsCts.Token);
		report.Failures.Count.Should().Be(0,
			because: "the shared page '{0}' must be a saved blank page {1}, and a completed reset must be what the "
				+ "next get-page returns. {2}{3}What the lease observed: {4}{5}State now (after the reset): {6}",
			SchemaName, phase, string.Join(" ## ", report.Failures), Environment.NewLine,
			string.Join(Environment.NewLine, report.Log),
			savesOfTheTest is null ? Environment.NewLine : $"{Environment.NewLine}Saves of the test: {savesOfTheTest}{Environment.NewLine}",
			diagnostics);
	}

	/// <summary>
	/// Writes to the output of the current test and to the build log. Never throws: it runs in
	/// <c>[TearDown]</c> and must not replace the failure of the test.
	/// </summary>
	private static void WriteToTestOutput(IEnumerable<string> lines) {
		try {
			foreach (string line in lines) {
				TestContext.Out.WriteLine(line);
				TestContext.Progress.WriteLine(line);
			}
		} catch (Exception) {
			// No test output to write to.
		}
	}

	private static string DescribeComponents(string? body) {
		if (string.IsNullOrEmpty(body)) {
			return "none";
		}
		string[] names = Regex.Matches(body, "\"name\"\\s*:\\s*\"(?<name>[^\"]+)\"", RegexOptions.CultureInvariant,
				RegexTimeout)
			.Select(match => match.Groups["name"].Value)
			.Distinct(StringComparer.Ordinal)
			.Take(20)
			.ToArray();
		return names.Length == 0 ? "none" : string.Join(", ", names);
	}

	private static string? PackageKey(StoredSchemaRow row) =>
		Guid.TryParse(row.PackageUId, out Guid packageUId)
			? packageUId.ToString("D")
			: row.PackageName is "?" or "" ? null : row.PackageName;

	private static string NormalizeGuid(string? value) =>
		Guid.TryParse(value, out Guid guid) ? guid.ToString("D") : value?.Trim() ?? string.Empty;

	private static string Text(JsonElement row, string name) =>
		row.ValueKind == JsonValueKind.Object && row.TryGetProperty(name, out JsonElement value)
			? value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText()
			: "?";

	private static string RemoveWhitespace(string text) =>
		string.Concat(text.Where(character => !char.IsWhiteSpace(character)));

	private static string Shorten(string text) =>
		text.Length <= DiagnosticSnippetLength ? text : text[..DiagnosticSnippetLength] + "...";

	private static void TryDeleteDirectory(string path) {
		try {
			if (Directory.Exists(path)) {
				Directory.Delete(path, recursive: true);
			}
		} catch (Exception deleteFailure) when (deleteFailure is IOException or UnauthorizedAccessException) {
			// A leftover temp directory is harmless and must not fail the test.
		}
	}

	/// <summary>What one read of the page shows.</summary>
	internal enum PageState {
		/// <summary><c>get-page</c> failed.</summary>
		Unreadable,
		/// <summary>The body carries content.</summary>
		NotBlank,
		/// <summary>The body is blank but no editable schema was resolved: the body is synthesized.</summary>
		SynthesizedBlank,
		/// <summary>The body is blank and comes from an editable schema.</summary>
		SavedBlank
	}

	/// <summary>
	/// One <c>get-page</c> read of the page: the body it wrote to disk and the fields of the response that
	/// tell which stored schema it resolved (<c>editable.editableSchemaExists</c>,
	/// <c>editable.editableSchemaUId</c>, <c>editable.checksum</c>, <c>editable.modifiedOn</c>,
	/// <c>page.designPackageUId</c>, <c>page.designPackageName</c>,
	/// <c>page.willCreateReplacingInDesignPackage</c>).
	/// </summary>
	internal sealed record PageObservation(
		bool Success,
		string? Error,
		string? Body,
		bool EditableSchemaExists,
		string? EditableSchemaUId,
		string? Checksum,
		string? ModifiedOn,
		string? PagePackageName,
		string? DesignPackageName,
		string? DesignPackageUId,
		bool WillCreateReplacing,
		string? SchemaChain = null) {

		/// <summary>The facts that tell which stored schema the read resolved.</summary>
		public string Describe() => Success
			? $"editableSchema={EditableSchemaUId ?? "none"} exists={EditableSchemaExists} "
				+ $"checksum={Checksum ?? "none"} modifiedOn={ModifiedOn ?? "none"} "
				+ $"pagePackage={PagePackageName} designPackage={DesignPackageName} ({DesignPackageUId}) "
				+ $"willCreateReplacing={WillCreateReplacing} bodyLength={Body?.Length ?? 0} blank={IsBlank(Body, out _)}"
			: $"failed: {Error}";
	}

	/// <summary>
	/// What <c>sync-pages</c> reported for the reset save. <c>sync-pages</c> reports no schema UId for a
	/// save; the post-save checksum (<c>pages[].digest.checksum</c>, read from the row the save wrote) is
	/// what identifies the written schema.
	/// </summary>
	internal sealed record ResetSave(
		bool Success, string? Error, string? Checksum, string? ConflictCheck, IReadOnlyList<string> Warnings) {

		/// <summary>The facts of the save for the log.</summary>
		public string Describe() => Success
			? $"saved, checksumAfterSave={Checksum ?? "none"} conflictCheck={ConflictCheck ?? "none"} "
				+ "schemaUId=not reported by sync-pages (match the checksum to a SysSchema row)"
			: $"failed: {Error ?? "sync-pages returned no page result"}";
	}

	/// <summary>One stored <c>SysSchema</c> row of the page.</summary>
	internal sealed record StoredSchemaRow(
		string UId, string PackageName, string PackageUId, string ExtendParent, string Checksum, string CreatedOn,
		string ModifiedOn);

	/// <summary>The stored rows of the page, or why they could not be read.</summary>
	/// <param name="Rows">The rows; <c>null</c> when the read failed.</param>
	/// <param name="Error">Why the read failed.</param>
	internal sealed record StoredRows(IReadOnlyList<StoredSchemaRow>? Rows, string? Error);

	/// <summary>What the start of a lease does with the first read.</summary>
	/// <param name="Reset">Whether the page is reset.</param>
	/// <param name="Failure">The failure of the test; <c>null</c> when the state is acceptable.</param>
	/// <param name="Note">A line for the log when the state is unusual but exempt.</param>
	internal sealed record StartDecision(bool Reset, string? Failure, string? Note);

	/// <summary>The outcome of one step of a lease.</summary>
	/// <param name="Failures">What must fail the test; empty when nothing does.</param>
	/// <param name="Log">What the step read and saved, in order.</param>
	/// <param name="VerifiedRead">
	/// The first read after the step's reset when that reset was verified (shown by the first read, stored
	/// modification time moved); <c>null</c> when the step did not reset or the reset was not verified.
	/// </param>
	internal sealed record LeaseReport(
		IReadOnlyList<string> Failures, IReadOnlyList<string> Log, PageObservation? VerifiedRead = null);

	/// <summary>How the lease before the current one ended.</summary>
	internal enum PreviousLeaseEnd {
		/// <summary>No lease was started in this test process before.</summary>
		NoLeaseYet,
		/// <summary>Its last step was a reset that the first read showed and that modified the schema read.</summary>
		Verified,
		/// <summary>Its last step reported failures and no verified reset.</summary>
		NotVerified,
		/// <summary>It started and never recorded an end: its reset threw, or its TearDown never ended it.</summary>
		DidNotComplete
	}

	/// <summary>What the first read after a verified reset resolved.</summary>
	internal sealed record VerifiedReset(string EditableSchemaUId, string Checksum);

	/// <summary>What the leases before the current one left, as <see cref="LeaseLedger.Start"/> returns it.</summary>
	/// <param name="PreviousEnd">How the previous lease ended.</param>
	/// <param name="LastVerified">What its verified reset left; <c>null</c> unless it ended verified.</param>
	/// <param name="WriterOutsideLease">The tests that wrote the page outside a lease since; <c>null</c> when none.</param>
	internal sealed record PriorLeases(
		PreviousLeaseEnd PreviousEnd, VerifiedReset? LastVerified, string? WriterOutsideLease) {

		/// <summary>No lease before this one in the test process.</summary>
		internal static PriorLeases None { get; } = new(PreviousLeaseEnd.NoLeaseYet, null, null);

		/// <summary>Whether no lease was started in this test process before.</summary>
		internal bool IsFirstLeaseOfProcess => PreviousEnd == PreviousLeaseEnd.NoLeaseYet;

		/// <summary>The facts for the log.</summary>
		public string Describe() => IsFirstLeaseOfProcess
			? "first lease of the test process"
			: $"not the first lease of the test process; previous lease: {PreviousEnd}"
				+ (LastVerified is { } verified
					? $" (editable schema {verified.EditableSchemaUId}, checksum {verified.Checksum})"
					: string.Empty)
				+ (WriterOutsideLease is null ? string.Empty : $"; written outside a lease since by {WriterOutsideLease}");
	}

	/// <summary>
	/// The record of the leases of one test process. <see cref="Ledger"/> is the process-wide instance; the
	/// unit tests create their own.
	/// </summary>
	internal sealed class LeaseLedger {
		private readonly object _gate = new();
		private readonly HashSet<string> _fixturesWithStoredRowsCheck = new(StringComparer.Ordinal);
		private PreviousLeaseEnd _previousEnd = PreviousLeaseEnd.NoLeaseYet;
		private VerifiedReset? _lastVerified;
		private string? _writerOutsideLease;

		/// <summary>
		/// Starts a lease: returns what the leases before it left and whether it is the first lease of its
		/// fixture (which lists and judges the stored rows). Until <see cref="RecordEnd"/> is called, the next
		/// lease sees this one as <see cref="PreviousLeaseEnd.DidNotComplete"/>.
		/// </summary>
		/// <param name="fixtureName">The fixture of the test that takes the lease.</param>
		/// <returns>The prior leases and whether to check the stored rows.</returns>
		internal (PriorLeases Prior, bool CheckStoredRows) Start(string? fixtureName) {
			lock (_gate) {
				PriorLeases prior = new(_previousEnd, _lastVerified, _writerOutsideLease);
				bool checkStoredRows = _fixturesWithStoredRowsCheck.Add(fixtureName ?? string.Empty);
				_previousEnd = PreviousLeaseEnd.DidNotComplete;
				return (prior, checkStoredRows);
			}
		}

		/// <summary>Records the last step of a lease: verified when that step's reset was verified.</summary>
		/// <param name="report">The report of the lease's last step.</param>
		internal void RecordEnd(LeaseReport report) {
			ArgumentNullException.ThrowIfNull(report);
			lock (_gate) {
				if (report.VerifiedRead is { EditableSchemaUId: { Length: > 0 } schemaUId, Checksum: { Length: > 0 } checksum }) {
					_previousEnd = PreviousLeaseEnd.Verified;
					_lastVerified = new VerifiedReset(schemaUId, checksum);
				} else {
					_previousEnd = PreviousLeaseEnd.NotVerified;
					_lastVerified = null;
				}
				_writerOutsideLease = null;
			}
		}

		/// <summary>Records a write of the page outside a lease; the next lease then does not compare.</summary>
		/// <param name="writer">The test that writes.</param>
		internal void RecordWriteOutsideLease(string writer) {
			lock (_gate) {
				_writerOutsideLease = _writerOutsideLease is null ? writer : $"{_writerOutsideLease}, {writer}";
			}
		}
	}

	/// <summary>The reads and the one write the lease makes; a fake in the unit tests.</summary>
	internal interface IPageAccess {

		/// <summary>Reads the page with <c>get-page</c>.</summary>
		Task<PageObservation> ReadAsync(CancellationToken cancellationToken);

		/// <summary>Writes the blank body with <c>sync-pages</c>, forced.</summary>
		Task<ResetSave> WriteBlankAsync(CancellationToken cancellationToken);

		/// <summary>Reads the stored <c>SysSchema</c> rows of the page. Does not throw.</summary>
		Task<StoredRows> ReadStoredRowsAsync(CancellationToken cancellationToken);
	}

	/// <summary>
	/// The page access over the real MCP server. It uses a temp anchor of its own, so no
	/// <c>.clio-pages</c> baseline of a test takes part and none is left behind for one.
	/// </summary>
	private sealed class McpPageAccess : IPageAccess, IDisposable {
		private readonly McpServerSession _session;
		private readonly string _environmentName;
		private readonly string _anchor = Directory.CreateTempSubdirectory("clio-e2e-seed-page-").FullName;

		internal McpPageAccess(McpServerSession session, string environmentName) {
			_session = session;
			_environmentName = environmentName;
		}

		public async Task<PageObservation> ReadAsync(CancellationToken cancellationToken) {
			CallToolResult result = await _session.CallToolAsync(
				PageGetTool.ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["schema-name"] = SchemaName,
						["environment-name"] = _environmentName,
						["output-directory"] = _anchor
					}
				},
				cancellationToken);
			PageGetResponse response = EntitySchemaStructuredResultParser.Extract<PageGetResponse>(result);
			string? body = response.Success && !string.IsNullOrWhiteSpace(response.Files?.BodyFile)
				? await File.ReadAllTextAsync(response.Files.BodyFile, cancellationToken)
				: null;
			return new PageObservation(
				response.Success,
				response.Error,
				body,
				response.Editable?.EditableSchemaExists ?? false,
				response.Editable?.EditableSchemaUId,
				response.Editable?.Checksum,
				response.Editable?.ModifiedOn,
				response.Page?.PackageName,
				response.Page?.DesignPackageName,
				response.Page?.DesignPackageUId,
				response.Page?.WillCreateReplacingInDesignPackage ?? false,
				await ReadSchemaChainAsync(response, cancellationToken));
		}

		public async Task<ResetSave> WriteBlankAsync(CancellationToken cancellationToken) {
			CallToolResult result = await _session.CallToolAsync(
				PageSyncTool.ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = _environmentName,
						["pages"] = new[] {
							new Dictionary<string, object?> {
								["schema-name"] = SchemaName,
								["body"] = BuildBlankBody(),
								// The reset overwrites whatever the test left, by design.
								["force"] = true
							}
						},
						["validate"] = true,
						["skip-sampling"] = true,
						["output-directory"] = _anchor
					}
				},
				cancellationToken);
			PageSyncResponse response = EntitySchemaStructuredResultParser.Extract<PageSyncResponse>(result);
			PageSyncPageResult? page = response.Pages?.FirstOrDefault();
			return new ResetSave(
				page?.Success ?? false,
				page?.Error,
				page?.Digest?.Checksum,
				page?.ConflictCheck,
				page?.Validation?.Warnings?.ToArray() ?? []);
		}

		/// <summary>
		/// Lists every <c>SysSchema</c> row named <see cref="SchemaName"/> through <c>execute-esq</c>, which
		/// reads the database and not the schema designer.
		/// </summary>
		public async Task<StoredRows> ReadStoredRowsAsync(CancellationToken cancellationToken) {
			try {
				object query = SelectQueryHelper.BuildSelectQuery("SysSchema",
					[
						new SelectQueryHelper.SelectQueryColumnDefinition("UId", "UId"),
						new SelectQueryHelper.SelectQueryColumnDefinition("SysPackage.Name", "PackageName"),
						new SelectQueryHelper.SelectQueryColumnDefinition("SysPackage.UId", "PackageUId"),
						new SelectQueryHelper.SelectQueryColumnDefinition("ExtendParent", "ExtendParent"),
						new SelectQueryHelper.SelectQueryColumnDefinition("Checksum", "Checksum"),
						new SelectQueryHelper.SelectQueryColumnDefinition("CreatedOn", "CreatedOn", OrderDirection: 1,
							OrderPosition: 0),
						new SelectQueryHelper.SelectQueryColumnDefinition("ModifiedOn", "ModifiedOn")
					],
					[
						new SelectQueryHelper.SelectQueryFilterDefinition("Name", SchemaName,
							SelectQueryHelper.TextDataValueType),
						new SelectQueryHelper.SelectQueryFilterDefinition("ManagerName", ClientUnitSchemaManagerName,
							SelectQueryHelper.TextDataValueType)
					], rowCount: 50);
				CallToolResult result = await _session.CallToolAsync(
					ExecuteEsqTool.ToolName,
					new Dictionary<string, object?> {
						["args"] = new Dictionary<string, object?> {
							["environment-name"] = _environmentName,
							["query"] = JsonSerializer.SerializeToElement(query)
						}
					},
					cancellationToken);
				ExecuteEsqResponse response = EntitySchemaStructuredResultParser.Extract<ExecuteEsqResponse>(result);
				return response.Success && response.Rows is { ValueKind: JsonValueKind.Array } rows
					? ParseStoredRows(rows)
					: new StoredRows(null, response.Error ?? "no rows array in the answer");
			} catch (Exception readFailure) {
				// The rows are diagnostics: a read that fails is reported in them, not as the test's verdict, so a
				// parse failure's payload dump would only land in the published TestResults artifact of a passing run.
				PayloadDumpReader.DeleteIfPresent(readFailure.Message);
				return new StoredRows(null, readFailure.Message);
			}
		}

		public void Dispose() => TryDeleteDirectory(_anchor);

		private static async Task<string?> ReadSchemaChainAsync(
			PageGetResponse response, CancellationToken cancellationToken) {
			try {
				string? bundleFile = response.Files?.BundleFile;
				if (!response.Success || string.IsNullOrWhiteSpace(bundleFile) || !File.Exists(bundleFile)) {
					return null;
				}
				using JsonDocument bundle = JsonDocument.Parse(await File.ReadAllTextAsync(bundleFile, cancellationToken));
				foreach (JsonProperty property in bundle.RootElement.EnumerateObject()) {
					if (string.Equals(property.Name, "schemas", StringComparison.OrdinalIgnoreCase)) {
						return property.Value.GetRawText();
					}
				}
				return "bundle.json carries no schemas";
			} catch (Exception chainFailure) when (chainFailure is IOException or JsonException) {
				return $"bundle.json could not be read: {chainFailure.Message}";
			}
		}
	}

	/// <summary>
	/// A test's use of the shared page. The fixture's <c>[TearDown]</c> ends it. While it is held, it records
	/// what every <c>update-page</c> and <c>sync-pages</c> call on the page reported, so a failure can be
	/// matched to a stored schema.
	/// </summary>
	internal sealed class Lease {
		private readonly IToolResultObservable _observable;
		private readonly Func<IPageAccess> _openAccess;
		private readonly Func<CancellationToken, Task<string>> _describe;
		private readonly LeaseLedger _ledger;
		private readonly TimeSpan _pollInterval;
		private readonly ConcurrentQueue<string> _saves = new();
		private readonly Action<string, IReadOnlyDictionary<string, object?>, CallToolResult> _observer;

		/// <summary>Starts recording the saves of the test on <paramref name="observable"/>.</summary>
		/// <param name="observable">The MCP session of the test.</param>
		/// <param name="openAccess">Opens the page access the reset uses; disposed after the reset when disposable.</param>
		/// <param name="describe">Describes what the stand holds for the page; never throws.</param>
		/// <param name="ledger">Records how the lease ended.</param>
		/// <param name="pollInterval">The wait between the reads after the reset.</param>
		internal Lease(
			IToolResultObservable observable, Func<IPageAccess> openAccess,
			Func<CancellationToken, Task<string>> describe, LeaseLedger ledger, TimeSpan pollInterval) {
			_observable = observable;
			_openAccess = openAccess;
			_describe = describe;
			_ledger = ledger;
			_pollInterval = pollInterval;
			_observer = RecordSave;
			_observable.ToolResultObserver = _observer;
		}

		/// <summary>
		/// Ends the lease. When the test FAILED, first writes what the stand holds for the page and what the
		/// test's saves reported to the test output, before the reset overwrites that state. Then always
		/// resets the page and fails the test when the first read does not show the reset.
		/// </summary>
		internal async Task EndAsync() {
			bool testFailed;
			try {
				testFailed = TestContext.CurrentContext.Result.Outcome.Status
					== NUnit.Framework.Interfaces.TestStatus.Failed;
			} catch (Exception) {
				testFailed = false;
			}
			(LeaseReport report, string saves) = await EndAndReportAsync(testFailed, line => WriteToTestOutput([line]));
			await FailOnAsync(report, _describe, "after the test", saves);
		}

		/// <summary>
		/// The steps of <see cref="EndAsync"/> without the assertion: stops recording, reports the state of a
		/// failed test BEFORE the reset, resets and verifies, and records the end in the ledger.
		/// </summary>
		/// <param name="testFailed">Whether the test failed.</param>
		/// <param name="echo">Receives each output line as it is observed.</param>
		/// <returns>The report of the reset and the saves the test made.</returns>
		internal async Task<(LeaseReport Report, string Saves)> EndAndReportAsync(bool testFailed, Action<string> echo) {
			if (ReferenceEquals(_observable.ToolResultObserver, _observer)) {
				_observable.ToolResultObserver = null;
			}
			string saves = _saves.IsEmpty ? "none recorded" : string.Join(" || ", _saves);
			if (testFailed) {
				await ReportStateOfFailedTestAsync(saves, echo);
			}
			using CancellationTokenSource cancellation = new(OperationTimeout);
			IPageAccess access = _openAccess();
			LeaseReport report;
			try {
				report = await SeededBlankPage.EndAsync(access, _pollInterval, cancellation.Token, echo);
			} finally {
				(access as IDisposable)?.Dispose();
			}
			_ledger.RecordEnd(report);
			return (report, saves);
		}

		// Never throws: it runs in [TearDown] and must not replace the failure it describes.
		private async Task ReportStateOfFailedTestAsync(string saves, Action<string> echo) {
			try {
				using CancellationTokenSource diagnosticsCts = new(OperationTimeout);
				string state = await _describe(diagnosticsCts.Token);
				echo($"[seed-page] the test FAILED. State of '{SchemaName}' as the test left it, BEFORE the reset:{state}");
				echo($"[seed-page] saves of the test on '{SchemaName}': {saves}");
			} catch (Exception) {
				// The failure of the test stays the reported failure.
			}
		}

		// Called by the session after every tool call; keeps identifiers and checksums only, never arguments
		// or error text.
		private void RecordSave(string toolName, IReadOnlyDictionary<string, object?> arguments, CallToolResult result) {
			if (toolName is not (PageUpdateTool.ToolName or PageSyncTool.ToolName)
				|| !JsonSerializer.Serialize(arguments).Contains(SchemaName, StringComparison.Ordinal)) {
				return;
			}
			string time = DateTime.UtcNow.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
			try {
				if (toolName == PageUpdateTool.ToolName) {
					PageUpdateResponse response = EntitySchemaStructuredResultParser.Extract<PageUpdateResponse>(result);
					_saves.Enqueue($"{time}Z update-page success={response.Success} dryRun={response.DryRun} "
						+ $"savedSchemaUId={response.SavedSchemaUId ?? "not reported"} "
						+ $"newChecksum={response.NewChecksum ?? "none"} conflictCheck={response.ConflictCheck ?? "none"}");
					return;
				}
				PageSyncResponse sync = EntitySchemaStructuredResultParser.Extract<PageSyncResponse>(result);
				foreach (PageSyncPageResult page in sync.Pages ?? []) {
					if (!string.Equals(page.SchemaName, SchemaName, StringComparison.Ordinal)) {
						continue;
					}
					_saves.Enqueue($"{time}Z sync-pages success={page.Success} conflict={page.Conflict} "
						+ $"schemaUId={page.Page?.SchemaUId ?? "not reported by sync-pages"} "
						+ $"checksumAfterSave={page.Digest?.Checksum ?? "none"} conflictCheck={page.ConflictCheck ?? "none"}");
				}
			} catch (InvalidOperationException parseFailure) {
				// Diagnostics only: the test parses the same result itself and fails on it if it must, so this
				// copy's payload dump would only land in the published TestResults of a passing run.
				PayloadDumpReader.DeleteIfPresent(parseFailure.Message);
				_saves.Enqueue($"{time}Z {toolName} result could not be parsed");
			}
		}
	}
}
