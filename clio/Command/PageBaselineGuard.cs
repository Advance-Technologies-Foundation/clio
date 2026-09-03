using System;
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
	/// The resolved <c>meta.json</c> path (may be <c>null</c> when resolution itself failed), whether
	/// the check is armed, and a diagnostic <c>Warning</c> the caller must surface on its response
	/// envelope (<c>null</c> on the normal path). When a caller already pinned
	/// <see cref="PageUpdateOptions.ExpectedChecksum"/>
	/// explicitly (CLI <c>--expected-checksum</c> or MCP <c>checksum</c>), that manual checksum wins the
	/// comparison and is left untouched, and the baseline's schema UId is still armed while the
	/// schema-absent marker is NOT (a pinned checksum asserts the schema existed, so a stale absent
	/// marker must not veto it) — but if a matching on-disk baseline exists, the method still reports armed so the
	/// post-save refresh moves that baseline forward to the new checksum, instead of leaving it pinned at
	/// the overwritten value (which would raise a false conflict on the next unpinned save).
	/// <para>
	/// The warning exists because "no check" is a legitimate outcome AND a failure mode, and the two used
	/// to be indistinguishable. A missing baseline stays silent; an unreadable one, or an anchor that
	/// could not be resolved, reports that external-modification detection is disarmed. It is a warning
	/// and never an exception: a discovery failure must not fail a write the caller is entitled to make.
	/// </para>
	/// </returns>
	(string MetaFilePath, bool Armed, string Warning) TryArm(PageUpdateOptions options, string outputDirectory);

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
	public (string MetaFilePath, bool Armed, string Warning) TryArm(PageUpdateOptions options, string outputDirectory) {
		// A caller-pinned checksum (CLI --expected-checksum, MCP `checksum`) is honored verbatim: it wins
		// the comparison and is never overwritten from disk. Everything ELSE the on-disk baseline knows -
		// the schema UId and the schema-absent marker - is still armed from it, because pinning a checksum
		// says nothing about schema identity, and dropping those two would silently disable the
		// schema-uid-mismatch and schema-created-externally conflicts on the pinned path (issue #1320).
		bool callerPinnedChecksum = !string.IsNullOrWhiteSpace(options.ExpectedChecksum);
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
			// A malformed anchor/body-file path must not break the write — degrade to no check, but say so:
			// silently skipping the check is exactly the invisible failure AC-02 removes.
			return (null, false,
				$"External-modification detection is DISARMED for '{options.SchemaName}': the .clio-pages baseline "
				+ $"location could not be resolved ({ex.Message}).");
		}
		PageBaselineInfo baseline = PageBaselineStore.TryReadBaseline(
			_fileSystem, _fileGate, metaFilePath, out string readWarning);
		string warning = readWarning ?? resolveWarning;
		if (baseline is null || !PageBaselineStore.MatchesEnvironment(baseline, options.Environment, options.Uri)) {
			return (metaFilePath, false, warning);
		}
		// The schema-identity half of the baseline is armed on BOTH paths, with ONE exception: the
		// schema-absent marker is armed only on the unpinned path. A caller-pinned checksum asserts
		// "an editable schema existed and had this checksum", so a stale on-disk `editableSchemaExists:
		// false` must not veto it - arming it there produced a false schema-created-externally on a save
		// whose pin matched the server, and, when the schema had since been deleted, skipped the checksum
		// comparison altogether (IsCreateReplacing short-circuits before it).
		options.ExpectedSchemaUId = baseline.EditableSchemaUId;
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
				warning ??= $"The checksum pinned for '{options.SchemaName}' differs from the baseline clio last "
					+ "recorded for this page. If it was copied out of a conflict response rather than from a fresh "
					+ "get-page, this save overwrites the change that caused the conflict - re-read the page and "
					+ "merge before saving.";
			}
			return (metaFilePath, true, warning);
		}
		options.ExpectedChecksum = baseline.Checksum;
		return (metaFilePath, true, warning);
	}

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
