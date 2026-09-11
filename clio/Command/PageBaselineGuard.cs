using System;
using System.Collections.Generic;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command;

/// <summary>
/// Orchestrates the conflict-detection baseline around a page write so that every page-modifying
/// entry point — the CLI <c>update-page</c> verb, the MCP <c>update-page</c> tool, and the MCP
/// <c>sync-pages</c> tool — discovers and refreshes the on-disk baseline identically. The baseline
/// itself lives in <c>.clio-pages/{schema}/meta.json</c> and is owned by <see cref="PageBaselineStore"/>;
/// this service is the single chokepoint that arms the in-memory check before a save and persists the
/// fresh checksum afterwards. All operations are best-effort and fail toward "no check" — a missing,
/// legacy, or foreign-environment baseline must never block a write with a false conflict.
/// </summary>
public interface IPageBaselineGuard {

	/// <summary>
	/// Discovers the on-disk baseline for the page targeted by <paramref name="options"/> and, when it
	/// was captured against the same environment, arms the external-modification check by populating
	/// <see cref="PageUpdateOptions.ExpectedChecksum"/>, <see cref="PageUpdateOptions.ExpectedSchemaUId"/>,
	/// and <see cref="PageUpdateOptions.ExpectedSchemaAbsent"/> on <paramref name="options"/>.
	/// </summary>
	/// <param name="options">The pending write request. Mutated in place when a baseline is armed.</param>
	/// <param name="outputDirectory">Optional anchor override (MCP <c>output-directory</c>); <c>null</c> for the CLI.</param>
	/// <returns>
	/// The resolved <c>meta.json</c> path (may be <c>null</c> when resolution itself failed),
	/// <c>RefreshBaseline</c> — whether the on-disk baseline must be moved forward AFTER a successful save,
	/// which is NOT the same question as whether the conflict check runs (it runs whenever
	/// <see cref="PageUpdateOptions.ExpectedChecksum"/> is set, pinned or armed) — and a diagnostic
	/// <c>Warning</c> the caller must surface on its response
	/// envelope (<c>null</c> on the normal path). When a caller already pinned
	/// <see cref="PageUpdateOptions.ExpectedChecksum"/>
	/// explicitly (CLI <c>--expected-checksum</c> or MCP <c>checksum</c>), that manual checksum wins the
	/// comparison and is left untouched, and NEITHER the baseline's schema UId nor its schema-absent marker
	/// is armed from disk — the comparison already runs against the resolved target schema, so a matching
	/// pin proves the caller read that schema and a stale on-disk identity must not veto it — but if a
	/// matching on-disk baseline exists, <c>RefreshBaseline</c> is still <see langword="true"/> so the
	/// post-save refresh moves that baseline forward to the new checksum, instead of leaving it pinned at
	/// the overwritten value (which would raise a false conflict on the next unpinned save).
	/// When <see cref="PageUpdateOptions.TargetPackageUId"/> or <see cref="PageUpdateOptions.TargetSchemaUId"/>
	/// redirects the write, nothing on disk describes the schema being written, so no baseline is read and
	/// the disk-derived identity halves are cleared. A <c>--target-schema-uid</c> redirect KEEPS a
	/// caller-supplied checksum — it names the target outright, so the pin describes exactly the schema the
	/// write lands on — while a <c>--target-package-uid</c> redirect clears it, because the hierarchy
	/// resolver may land on a different or newly created replacing schema. Either way the method reports
	/// <c>RefreshBaseline: false</c> with a warning.
	/// <para>
	/// The warning exists because "no check" is a legitimate outcome AND a failure mode, and the two used
	/// to be indistinguishable. A missing baseline stays silent; an unreadable one, or an anchor that
	/// could not be resolved, reports that external-modification detection is disarmed. It is a warning
	/// and never an exception: a discovery failure must not fail a write the caller is entitled to make.
	/// </para>
	/// </returns>
	(string MetaFilePath, bool RefreshBaseline, string Warning) TryArm(PageUpdateOptions options, string outputDirectory);

	/// <summary>
	/// After a successful, non-dry-run save with an armed baseline: persists the fresh post-save
	/// checksum into the existing <c>meta.json</c>, or removes the baseline block when the command
	/// could not obtain fresh metadata — so the next write never compares against a stale checksum.
	/// </summary>
	/// <param name="metaFilePath">The <c>meta.json</c> path returned by <see cref="TryArm"/>.</param>
	/// <param name="options">The write request whose environment identity the refreshed baseline records.</param>
	/// <param name="response">The successful response carrying <c>NewChecksum</c>/<c>NewModifiedOn</c>/<c>SavedSchemaUId</c>.</param>
	/// <returns>
	/// <c>null</c> when the baseline was persisted (or legitimately not present); otherwise a diagnostic
	/// the caller must surface as a response WARNING. Never throws: the save has already landed on the
	/// server, so turning a lost refresh into a failed response would misreport a successful write —
	/// strictly worse than the silent loss it replaces. The warning is what makes the loss visible.
	/// </returns>
	string RefreshOrDrop(string metaFilePath, PageUpdateOptions options, PageUpdateResponse response);
}

/// <inheritdoc />
public sealed class PageBaselineGuard : IPageBaselineGuard {

	/// <summary>
	/// Single source of truth for the advice appended to every uncorroborated / divergent pinned-checksum
	/// warning. The three exits that emit it carried a character-for-character copy each, and the tests
	/// assert on it with <c>Contain(...)</c> against a space-joined string - so rewording one copy would
	/// have gone unnoticed.
	/// </summary>
	internal const string PinnedChecksumMergeAdvice =
		"If it was copied out of a conflict response rather than from a fresh get-page, this save "
		+ "overwrites the change that caused the conflict - re-read the page and merge before saving.";

	private readonly IFileSystem _fileSystem;
	private readonly IInterprocessFileGate _fileGate;

	/// <summary>
	/// Initializes a new instance of the <see cref="PageBaselineGuard"/> class.
	/// </summary>
	/// <param name="fileSystem">File-system abstraction used to read and rewrite <c>meta.json</c>.</param>
	/// <param name="fileGate">
	/// Interprocess gate that serialises every <c>meta.json</c> read-modify-write against other clio
	/// processes working in the same workspace. Optional with a <c>null</c> default only so the existing
	/// target-typed test instantiations across the page-tool fixtures keep compiling; in production the
	/// container always supplies it, and a <c>null</c> gate degrades to the pre-gate direct write.
	/// </param>
	public PageBaselineGuard(IFileSystem fileSystem, IInterprocessFileGate fileGate = null) {
		_fileSystem = fileSystem;
		_fileGate = fileGate;
	}

	/// <inheritdoc />
	public (string MetaFilePath, bool RefreshBaseline, string Warning) TryArm(PageUpdateOptions options, string outputDirectory) {
		// A caller-pinned checksum (CLI --expected-checksum, MCP `checksum`) is honored verbatim: it wins
		// the comparison and is never overwritten from disk. The schema-absent marker is still armed from
		// the on-disk baseline on the unpinned path only, and the baseline's schema UId likewise - see the
		// arming block below for why a pin makes the disk-derived UId the weaker witness (issue #1320).
		// Normalize the pin ONCE, here, at the choke point every caller reaches - not at an individual
		// mapper. The arming predicate below is whitespace-tolerant (IsNullOrWhiteSpace) while the
		// comparison downstream is a strict Ordinal one (PageUpdateOptions.cs:437), so a padded value arms
		// the check and then fails it, reporting a ChecksumMismatch that never happened. The MCP mapper
		// trims its own argument, but the CLI `--expected-checksum` is bound verbatim by CommandLineParser
		// and is never trimmed, so a value passed with a trailing newline - the natural shape when it is
		// piped from a file or from a shell substitution that keeps it - produced exactly the false
		// conflict this change set exists to remove. Whitespace-only collapses
		// to null so it stays equivalent to "not supplied" rather than arming the guard with nothing to
		// compare.
		options.ExpectedChecksum = string.IsNullOrWhiteSpace(options.ExpectedChecksum)
			? null
			: options.ExpectedChecksum.Trim();
		bool callerPinnedChecksum = !string.IsNullOrWhiteSpace(options.ExpectedChecksum);
		// A REDIRECT makes the baseline inapplicable, so nothing here is armed and nothing is even read.
		// The baseline is keyed by schema name alone and `get-page` has no redirect option, so both the
		// on-disk baseline and any checksum copied out of a get-page response describe the schema the
		// hierarchy resolver picks automatically - never the one --target-package-uid/--target-schema-uid
		// sends the write to. Arming from it produced a false schema-uid-mismatch or
		// schema-deleted-externally, and reporting armed let RefreshOrDrop stamp the REDIRECTED schema's
		// identity into the schema-name-keyed baseline, so the next ordinary save was refused too.
		if (!string.IsNullOrWhiteSpace(options.TargetPackageUId)
			|| !string.IsNullOrWhiteSpace(options.TargetSchemaUId)) {
			return ArmForRedirectedWrite(options, callerPinnedChecksum);
		}
		string metaFilePath;
		string resolveWarning;
		try {
			// H1: reading the process-global cwd to resolve the meta.json anchor must serialize against
			// the MCP workspace tools that PIN cwd. In the MCP path this runs under the per-tenant lock
			// (ordering per-tenant → CwdLock); in the single-threaded CLI path CwdLock is uncontended.
			lock (McpToolExecutionLock.CwdLock) {
				metaFilePath = PageBaselineStore.ResolveMetaFilePath(
					_fileSystem,
					_fileSystem.Directory.GetCurrentDirectory(),
					Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
					ClioRuntimePaths.Home,
					outputDirectory,
					options.BodyFile,
					options.SchemaName,
					out resolveWarning);
			}
		} catch (Exception ex) {
			// A malformed anchor/body-file path must not break the write — degrade to no LOCAL check, but say
			// so: silently skipping the check is exactly the invisible failure AC-02 removes. "DISARMED" is
			// only true when nothing governs the comparison. On a pinned save it is affirmatively FALSE:
			// TryCheckForExternalModification gates on ExpectedChecksum alone and never consults the returned flag, so
			// detection runs, driven entirely by a client-supplied baseline that nothing local can
			// corroborate. Telling the caller it is off would be the more dangerous of the two errors.
			// REDACTED: this reaches the MCP response and usually a third-party model, and the message of a
			// path-resolution failure routinely carries an absolute path including the user name. The same
			// redactor is applied to the sibling persisted-resource-key warning in this change set.
			string failureDetail = SensitiveErrorTextRedactor.Redact(ex.Message);
			return (null, false, callerPinnedChecksum
				? $"The checksum pinned for '{options.SchemaName}' governs this save but could not be corroborated "
					+ $"locally: the .clio-pages baseline location could not be resolved ({failureDetail}). "
					+ PinnedChecksumMergeAdvice
				: $"External-modification detection is DISARMED for '{options.SchemaName}': the .clio-pages "
					+ $"baseline location could not be resolved ({failureDetail}).");
		}
		PageBaselineInfo baseline = PageBaselineStore.TryReadBaseline(
			_fileSystem, _fileGate, metaFilePath, out string readWarning);
		// ACCUMULATE, never `??=` into one slot. The discovery warnings and the pinned-save trace describe
		// DIFFERENT facts and co-occur on exactly the path where the pin is least trustworthy:
		// TryReadBaseline sets readWarning only when .clio-pages/meta.json exists but cannot be read or
		// deserialized, and on that path it also returns a null baseline - so a single slot filled by the
		// corrupt-meta warning silently swallowed the trace saying an uncorroborated pin is driving the
		// save. ResolveMetaFilePath's malformed-body-file warning collided the same way.
		List<string> warnings = new();
		AddWarning(warnings, readWarning);
		// The resolve warning embeds the raw body-file path, so it is redacted for the same reason as the
		// failure detail above: it travels to the MCP caller verbatim.
		AddWarning(warnings, SensitiveErrorTextRedactor.Redact(resolveWarning));
		if (baseline is null || !PageBaselineStore.MatchesEnvironment(baseline, options.Environment, options.Uri)) {
			if (callerPinnedChecksum) {
				// The pin still GOVERNS the save on this path: TryCheckForExternalModification gates on
				// ExpectedChecksum alone and never consults the returned flag. So a pinned overwrite used to reach the
				// server with no trace at all whenever no local baseline matched - and the two commonest
				// causes are documented-normal, not exotic: an explicit output-directory anchor, and an
				// --uri/--login invocation that cannot satisfy MatchesEnvironment. "Uncorroborated" is a
				// weaker statement than "divergent", so the wording differs from the block below, but
				// staying silent is exactly the invisible bypass this guard exists to expose.
				AddWarning(warnings,
					$"The checksum pinned for '{options.SchemaName}' governs this save but could not be "
					+ "corroborated locally: no .clio-pages baseline was found for this anchor and environment. "
					+ PinnedChecksumMergeAdvice);
			}
			return (metaFilePath, false, JoinWarnings(warnings));
		}
		// The schema-identity half of the baseline is armed on the UNPINNED path only. A caller-pinned
		// checksum asserts "an editable schema existed and had this checksum", so a stale on-disk
		// `editableSchemaExists: false` must not veto it - arming it there produced a false
		// schema-created-externally on a save whose pin matched the server, and, when the schema had since
		// been deleted, skipped the checksum comparison altogether (IsCreateReplacing short-circuits before
		// it). The disk-derived schema UId is the weaker witness for the same reason: the comparison runs
		// against the checksum of the RESOLVED target schema, so a matching pin already proves the caller
		// read exactly that schema, while a stale on-disk UId would refuse it as schema-uid-mismatch. A
		// genuine identity change is still refused - the content differs, so the checksum comparison
		// reports checksum-mismatch instead (issue #1320).
		options.ExpectedSchemaAbsent = !baseline.EditableSchemaExists && !callerPinnedChecksum;
		if (callerPinnedChecksum) {
			// The explicit checksum wins the comparison, so it is left untouched. The matching on-disk
			// baseline must still move forward after the save: report armed so RefreshOrDrop persists the
			// post-save checksum. Otherwise the next unpinned save auto-arms from a now-superseded
			// checksum and raises a false conflict.
			//
			// A MACHINE-READABLE TRACE when the pin disagrees with the baseline. The bypass this guards
			// against is: a save is refused, the caller copies actualChecksum out of conflictDetails,
			// resubmits the SAME body with the new pin, and the guard passes - the other author's edit is
			// gone, the response says success:true / conflict:false, and RefreshOrDrop then rewrites
			// meta.json to the post-save checksum, erasing the only local record that the pin ever
			// diverged. Without this, a caller that took the bypass and a caller that made a legitimate
			// up-to-date save are byte-identical on the wire. Guidance prose is not enough for something
			// only a machine reads.
			if (!string.IsNullOrWhiteSpace(baseline.Checksum)
				&& !string.Equals(baseline.Checksum, options.ExpectedChecksum, StringComparison.Ordinal)) {
				AddWarning(warnings,
					$"The checksum pinned for '{options.SchemaName}' differs from the baseline clio last "
					+ "recorded for this page. " + PinnedChecksumMergeAdvice);
				// AND THE IDENTITY HALF, on this path only. The schema UId is deliberately not armed from
				// disk - a stale on-disk UId refused saves whose pin matched the server, which is the false
				// positive this change set removes - but SysSchema.Checksum is CONTENT-derived, so two
				// schemas carrying a verbatim-copied body share it: a matching pin proves the caller read a
				// schema with this content, not that it read THIS schema. While the pin agrees with the
				// recorded baseline the local record corroborates both halves and there is nothing to
				// report; once it diverges, neither half is corroborated any more, and the identity the
				// caller can no longer see is the one that decides which package the write lands in.
				if (!string.IsNullOrWhiteSpace(baseline.EditableSchemaUId)) {
					AddWarning(warnings,
						$"The schema-identity check is not armed for this pinned save of '{options.SchemaName}', "
						+ $"and clio last recorded schema {baseline.EditableSchemaUId} for this page and "
						+ "environment. A checksum is derived from the page content, so a matching pin does not "
						+ "by itself prove the write is landing on that same schema; pass target-schema-uid "
						+ "when the target matters.");
				}
			}

			return (metaFilePath, true, JoinWarnings(warnings));
		}
		options.ExpectedChecksum = baseline.Checksum;
		options.ExpectedSchemaUId = baseline.EditableSchemaUId;
		return (metaFilePath, true, JoinWarnings(warnings));
	}

	/// <summary>
	/// Arms nothing from disk for a write that <c>--target-package-uid</c> / <c>--target-schema-uid</c>
	/// redirects, and decides whether a caller-supplied checksum survives it.
	/// </summary>
	private static (string MetaFilePath, bool RefreshBaseline, string Warning) ArmForRedirectedWrite(
		PageUpdateOptions options, bool callerPinnedChecksum) {
		// THE TWO REDIRECT KINDS ARE NOT THE SAME for a caller-supplied pin. --target-package-uid lets
		// the hierarchy resolver land on a different - possibly newly created replacing - schema, so a
		// checksum taken from get-page describes something else and must not govern the write.
		// --target-schema-uid names the target outright (TryResolveContext sets EditableSchemaUId from
		// it with IsCreateReplacing: false), so the comparison would run against exactly the schema the
		// caller pinned. Nulling the pin there turned external-modification detection OFF on a
		// destructive write and reported success: true / conflict: false, which is the failure this
		// guard exists to prevent, not a case of an inapplicable baseline.
		bool pinDescribesTheTarget = !string.IsNullOrWhiteSpace(options.TargetSchemaUId);
		// The DISK-derived halves are dropped for both kinds - that part is the real fix: the baseline
		// is keyed by schema name alone and get-page has no redirect option, so nothing on disk
		// describes the redirected schema. Arming from it produced a false schema-uid-mismatch or
		// schema-deleted-externally, and reporting armed let RefreshOrDrop stamp the REDIRECTED
		// schema's identity into the schema-name-keyed baseline, refusing the next ordinary save too.
		options.ExpectedSchemaUId = null;
		options.ExpectedSchemaAbsent = false;
		if (callerPinnedChecksum && pinDescribesTheTarget) {
			// The pin stays and still governs the save: TryCheckForExternalModification gates on
			// ExpectedChecksum alone. Nothing local corroborates it, which is what the trace says.
			return (null, false,
				$"The checksum pinned for '{options.SchemaName}' governs this save but could not be "
				+ "corroborated locally: target-schema-uid redirects the write to a schema the "
				+ ".clio-pages baseline does not describe, because get-page always reads the "
				+ "automatically resolved schema and has no redirect of its own. " + PinnedChecksumMergeAdvice);
		}
		options.ExpectedChecksum = null;
		return (null, false, callerPinnedChecksum
			? $"The checksum pinned for '{options.SchemaName}' was ignored and external-modification "
				+ "detection did not run for this save: target-package-uid redirects the write to a "
				+ "schema that neither the .clio-pages baseline nor a checksum taken from get-page "
				+ "describes - the hierarchy resolver may land on a different or newly created replacing "
				+ "schema - because get-page always reads the automatically resolved schema and has no "
				+ "redirect of its own. The write proceeds unchecked. Pass target-schema-uid instead to "
				+ "keep the pin in force."
			: $"External-modification detection did not run for this save of '{options.SchemaName}': "
				+ "target-package-uid / target-schema-uid redirect the write to a schema the .clio-pages "
				+ "baseline does not describe, because get-page always reads the automatically resolved "
				+ "schema and has no redirect of its own. The write proceeds unchecked.");
	}

	private static void AddWarning(List<string> warnings, string warning) {
		if (!string.IsNullOrWhiteSpace(warning)) {
			warnings.Add(warning.Trim());
		}
	}

	/// <summary>
	/// Collapses the accumulated traces back into the single string the tuple contract carries. Null when
	/// nothing was recorded, so "no warning" stays distinguishable from "an empty one".
	/// </summary>
	private static string JoinWarnings(List<string> warnings) =>
		warnings.Count == 0 ? null : string.Join(" ", warnings);

	/// <inheritdoc />
	public string RefreshOrDrop(string metaFilePath, PageUpdateOptions options, PageUpdateResponse response) {
		if (string.IsNullOrWhiteSpace(response.NewChecksum)) {
			return PageBaselineStore.DeleteBaseline(_fileSystem, _fileGate, metaFilePath);
		}
		return PageBaselineStore.RefreshExistingBaseline(
			_fileSystem,
			_fileGate,
			metaFilePath,
			new PageBaselineInfo {
				SchemaName = options.SchemaName,
				EnvironmentName = string.IsNullOrWhiteSpace(options.Environment) ? null : options.Environment,
				EnvironmentUri = string.IsNullOrWhiteSpace(options.Uri) ? null : options.Uri,
				EditableSchemaExists = true,
				EditableSchemaUId = response.SavedSchemaUId,
				Checksum = response.NewChecksum,
				ModifiedOn = response.NewModifiedOn,
				CapturedAt = DateTime.UtcNow.ToString("o")
			});
	}
}
