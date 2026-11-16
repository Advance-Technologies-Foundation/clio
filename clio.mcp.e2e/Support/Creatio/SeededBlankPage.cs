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
/// a page that an earlier, verified reset left blank and that is not blank now (<see cref="DecideStart"/>);
/// a completed reset that the first <c>get-page</c> does not show (<see cref="JudgeResetReads"/>); a read
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

	// Process-wide: only the FIRST lease of the test process may find the page not blank (or without an
	// editable schema) without failing, because a developer's stand may start dirty and a fresh site has no
	// editable schema until the first save. Every later lease starts after a reset this class verified.
	private static int _leasesStarted;

	// The fixtures whose first lease already listed and checked the stored rows of the page.
	private static readonly HashSet<string> FixturesWithStoredRowsCheck = new(StringComparer.Ordinal);

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
	/// test process may find the page not blank, or without an editable schema, without failing.
	/// </summary>
	/// <param name="first">The read made before the test.</param>
	/// <param name="firstLeaseOfProcess">Whether no lease was started in this test process before.</param>
	/// <returns>Whether to reset the page, and the failure or note to report.</returns>
	internal static StartDecision DecideStart(PageObservation first, bool firstLeaseOfProcess) {
		PageState state = Classify(first, out string? reason);
		const string firstLeaseCase = "This is the FIRST lease of the test process, so it is not treated as a "
			+ "failure: a developer's stand may start dirty and a fresh site has no editable schema of the page yet.";
		const string laterLeaseCase = "This is NOT the first lease of the test process: the lease before it ended "
			+ "with a reset that get-page showed on its first read, from an editable schema with the checksum the "
			+ "save reported, and nothing else writes other content to the page. So an earlier completed save did "
			+ "not land where it was read, or this read does not show the latest save - the product defect, not "
			+ "this test.";
		return state switch {
			PageState.SavedBlank => new StartDecision(Reset: false, Failure: null, Note: null),
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

	// ------------------------------------------------------------------------------------------------
	// The lease's two steps over an injected page access, so the unit tests drive them with a fake.
	// ------------------------------------------------------------------------------------------------

	/// <summary>
	/// The start of a lease: reads the page, optionally checks its stored rows, resets it when it is not a
	/// saved blank page, and reports every observation that must fail the test.
	/// </summary>
	/// <param name="access">Reads and writes the page.</param>
	/// <param name="firstLeaseOfProcess">Whether no lease was started in this test process before.</param>
	/// <param name="checkStoredRows">Whether to list and judge the stored rows of the page.</param>
	/// <param name="pollInterval">The wait between the reads after a reset.</param>
	/// <param name="cancellationToken">Cancels the calls.</param>
	/// <returns>The failures (empty when the test may run) and the log of what was observed.</returns>
	internal static async Task<LeaseReport> BeginAsync(
		IPageAccess access, bool firstLeaseOfProcess, bool checkStoredRows, TimeSpan pollInterval,
		CancellationToken cancellationToken) {
		ArgumentNullException.ThrowIfNull(access);
		List<string> failures = [];
		List<string> log = [];
		PageObservation first = await access.ReadAsync(cancellationToken);
		log.Add($"[seed-page] read before the test ({(firstLeaseOfProcess ? "first lease of the test process" : "not the first lease of the test process")}): {first.Describe()}");
		StartDecision decision = DecideStart(first, firstLeaseOfProcess);
		if (checkStoredRows || decision.Reset) {
			// Before the reset: the reset overwrites what these rows show.
			StoredRows stored = await access.ReadStoredRowsAsync(cancellationToken);
			log.Add($"[seed-page] stored schemas of '{SchemaName}' before the test:{Environment.NewLine}{DescribeStoredRows(stored)}");
			if (checkStoredRows && JudgeStoredRows(stored, first.DesignPackageUId, requireOneInDesignPackage: false,
					unreadableIsFailure: false) is { } storedFailure) {
				failures.Add(storedFailure);
			}
		}
		if (decision.Note is not null) {
			log.Add($"[seed-page] {decision.Note}");
		}
		if (decision.Failure is not null) {
			failures.Add(decision.Failure);
		}
		if (decision.Reset) {
			failures.AddRange(await ResetAndVerifyAsync(access, "before the test", pollInterval, log, cancellationToken));
		}
		return new LeaseReport(failures, log);
	}

	/// <summary>
	/// The end of a lease: always writes the blank body and requires the first read to show that write.
	/// It does not skip the write when the page already looks blank, because a blank body alone cannot tell
	/// a reset page from a read that shows no editable schema or another stored schema.
	/// </summary>
	/// <param name="access">Reads and writes the page.</param>
	/// <param name="pollInterval">The wait between the reads after the reset.</param>
	/// <param name="cancellationToken">Cancels the calls.</param>
	/// <returns>The failures (empty when the reset was visible at once) and the log of what was observed.</returns>
	internal static async Task<LeaseReport> EndAsync(
		IPageAccess access, TimeSpan pollInterval, CancellationToken cancellationToken) {
		ArgumentNullException.ThrowIfNull(access);
		List<string> log = [];
		IReadOnlyList<string> failures =
			await ResetAndVerifyAsync(access, "after the test", pollInterval, log, cancellationToken);
		return new LeaseReport(failures, log);
	}

	private static async Task<IReadOnlyList<string>> ResetAndVerifyAsync(
		IPageAccess access, string phase, TimeSpan pollInterval, List<string> log,
		CancellationToken cancellationToken) {
		ResetSave reset = await access.WriteBlankAsync(cancellationToken);
		log.Add($"[seed-page] reset save {phase}: {reset.Describe()}");
		if (!reset.Success) {
			return [
				$"sync-pages must reset the shared page '{SchemaName}' to its blank body {phase}. Error of the reset: "
				+ $"{reset.Error ?? "sync-pages returned no page result"}. Warnings: {string.Join(" | ", reset.Warnings)}"
			];
		}
		List<PageObservation> reads = [];
		// The poll keeps the isolation: the next test must not start before the page is blank. Whether the
		// reset PASSES is decided by JudgeResetReads, which accepts the first read only.
		await BoundedPollGate.PollUntilAsync(
			async pollToken => {
				PageObservation read = await access.ReadAsync(pollToken);
				reads.Add(read);
				return read;
			},
			read => WhyReadDoesNotShowReset(read, reset) is null,
			ResetObservationAttempts,
			pollInterval,
			cancellationToken);
		for (int index = 0; index < reads.Count; index++) {
			log.Add($"[seed-page] read {index + 1} after the reset {phase}: {reads[index].Describe()}");
		}
		return JudgeResetReads(reset, reads, phase) is { } failure ? [failure] : [];
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
		bool firstLeaseOfProcess = Interlocked.Exchange(ref _leasesStarted, 1) == 0;
		bool checkStoredRows = IsFirstLeaseOfFixture(TestContext.CurrentContext.Test.ClassName);
		// Not the test's token: the lease has its own budget.
		using CancellationTokenSource cancellation = new(OperationTimeout);
		LeaseReport report;
		using (McpPageAccess access = new(session, environmentName)) {
			report = await BeginAsync(access, firstLeaseOfProcess, checkStoredRows, ResetObservationPollInterval,
				cancellation.Token);
		}
		WriteToTestOutput(report.Log);
		await FailOnAsync(report, session, environmentName, "before the test", savesOfTheTest: null);
		return new Lease(session, environmentName);
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
		string[] duplicated = stored.Rows
			.GroupBy(row => row.PackageName, StringComparer.OrdinalIgnoreCase)
			.Where(group => group.Count() > 1)
			.Select(group => $"{group.Key} x{group.Count()}")
			.ToArray();
		description.Append(duplicated.Length > 0
			? $"SysSchema rows: {stored.Rows.Count}. MORE THAN ONE ROW IN ONE PACKAGE ({string.Join(", ", duplicated)}): "
				+ "a read and a write can resolve different schemas of this page."
			: $"SysSchema rows: {stored.Rows.Count}, at most one per package.");
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

	private static bool IsFirstLeaseOfFixture(string? fixtureName) {
		lock (FixturesWithStoredRowsCheck) {
			return FixturesWithStoredRowsCheck.Add(fixtureName ?? string.Empty);
		}
	}

	/// <summary>
	/// Fails the current test when <paramref name="report"/> carries failures, with the log of the lease and
	/// the state of the page in the message.
	/// </summary>
	private static async Task FailOnAsync(
		LeaseReport report, McpServerSession session, string environmentName, string phase, string? savesOfTheTest) {
		if (report.Failures.Count == 0) {
			return;
		}
		using CancellationTokenSource diagnosticsCts = new(OperationTimeout);
		string diagnostics = await DescribeAsync(session, environmentName, diagnosticsCts.Token);
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
	internal sealed record LeaseReport(IReadOnlyList<string> Failures, IReadOnlyList<string> Log);

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
		private readonly McpServerSession _session;
		private readonly string _environmentName;
		private readonly ConcurrentQueue<string> _saves = new();
		private readonly Action<string, IReadOnlyDictionary<string, object?>, CallToolResult> _observer;

		internal Lease(McpServerSession session, string environmentName) {
			_session = session;
			_environmentName = environmentName;
			_observer = RecordSave;
			_session.ToolResultObserver = _observer;
		}

		/// <summary>
		/// Ends the lease. When the test FAILED, first writes what the stand holds for the page and what the
		/// test's saves reported to the test output, before the reset overwrites that state. Then always
		/// resets the page and fails the test when the first read does not show the reset.
		/// </summary>
		internal async Task EndAsync() {
			if (ReferenceEquals(_session.ToolResultObserver, _observer)) {
				_session.ToolResultObserver = null;
			}
			string saves = _saves.IsEmpty ? "none recorded" : string.Join(" || ", _saves);
			await ReportStateOfFailedTestAsync(saves);
			using CancellationTokenSource cancellation = new(OperationTimeout);
			LeaseReport report;
			using (McpPageAccess access = new(_session, _environmentName)) {
				report = await SeededBlankPage.EndAsync(access, ResetObservationPollInterval, cancellation.Token);
			}
			await FailOnAsync(report, _session, _environmentName, "after the test", saves);
		}

		// Never throws: it runs in [TearDown] and must not replace the failure it describes.
		private async Task ReportStateOfFailedTestAsync(string saves) {
			try {
				if (TestContext.CurrentContext.Result.Outcome.Status != NUnit.Framework.Interfaces.TestStatus.Failed) {
					return;
				}
				using CancellationTokenSource diagnosticsCts = new(OperationTimeout);
				string state = await DescribeAsync(_session, _environmentName, diagnosticsCts.Token);
				WriteToTestOutput([
					$"[seed-page] the test FAILED. State of '{SchemaName}' as the test left it, BEFORE the reset:{state}",
					$"[seed-page] saves of the test on '{SchemaName}': {saves}"
				]);
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
		}
	}
}
