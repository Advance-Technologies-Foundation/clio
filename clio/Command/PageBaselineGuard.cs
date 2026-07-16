using System;
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
	/// The resolved <c>meta.json</c> path (may be <c>null</c> when resolution itself failed) and whether
	/// the check is armed. When a caller already pinned <see cref="PageUpdateOptions.ExpectedChecksum"/>
	/// explicitly (CLI <c>--expected-checksum</c>), that manual checksum wins the comparison and is left
	/// untouched — but if a matching on-disk baseline exists, the method still reports armed so the
	/// post-save refresh moves that baseline forward to the new checksum, instead of leaving it pinned at
	/// the overwritten value (which would raise a false conflict on the next unpinned save).
	/// </returns>
	(string MetaFilePath, bool Armed) TryArm(PageUpdateOptions options, string outputDirectory);

	/// <summary>
	/// After a successful, non-dry-run save with an armed baseline: persists the fresh post-save
	/// checksum into the existing <c>meta.json</c>, or removes the baseline block when the command
	/// could not obtain fresh metadata — so the next write never compares against a stale checksum.
	/// </summary>
	/// <param name="metaFilePath">The <c>meta.json</c> path returned by <see cref="TryArm"/>.</param>
	/// <param name="options">The write request whose environment identity the refreshed baseline records.</param>
	/// <param name="response">The successful response carrying <c>NewChecksum</c>/<c>NewModifiedOn</c>/<c>SavedSchemaUId</c>.</param>
	void RefreshOrDrop(string metaFilePath, PageUpdateOptions options, PageUpdateResponse response);
}

/// <inheritdoc />
public sealed class PageBaselineGuard : IPageBaselineGuard {

	private readonly IFileSystem _fileSystem;

	/// <summary>
	/// Initializes a new instance of the <see cref="PageBaselineGuard"/> class.
	/// </summary>
	/// <param name="fileSystem">File-system abstraction used to read and rewrite <c>meta.json</c>.</param>
	public PageBaselineGuard(IFileSystem fileSystem) {
		_fileSystem = fileSystem;
	}

	/// <inheritdoc />
	public (string MetaFilePath, bool Armed) TryArm(PageUpdateOptions options, string outputDirectory) {
		// A caller-pinned --expected-checksum (CLI) is honored verbatim: it wins the comparison and is
		// never overwritten from disk. For MCP callers ExpectedChecksum is always null here, so the
		// on-disk baseline drives the check exactly as before.
		bool callerPinnedChecksum = !string.IsNullOrWhiteSpace(options.ExpectedChecksum);
		string metaFilePath;
		try {
			// H1: reading the process-global cwd to resolve the meta.json anchor must serialize against
			// the MCP workspace tools that PIN cwd. In the MCP path this runs under the per-tenant lock
			// (ordering per-tenant → CwdLock); in the single-threaded CLI path CwdLock is uncontended.
			lock (McpServer.Tools.McpToolExecutionLock.CwdLock) {
				metaFilePath = PageBaselineStore.ResolveMetaFilePath(
					_fileSystem,
					_fileSystem.Directory.GetCurrentDirectory(),
					Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
					ClioRuntimePaths.Home,
					outputDirectory,
					options.BodyFile,
					options.SchemaName);
			}
		} catch {
			// A malformed anchor/body-file path must not break the write — degrade to no check.
			return (null, false);
		}
		PageBaselineInfo baseline = PageBaselineStore.TryReadBaseline(_fileSystem, metaFilePath);
		if (baseline is null || !PageBaselineStore.MatchesEnvironment(baseline, options.Environment, options.Uri)) {
			return (metaFilePath, false);
		}
		if (callerPinnedChecksum) {
			// Explicit checksum wins the comparison, so we do NOT arm the check from disk. But the matching
			// on-disk baseline must still move forward after the save: report armed (without touching
			// options.ExpectedChecksum) so RefreshOrDrop persists the post-save checksum. Otherwise the next
			// unpinned save auto-arms from a now-superseded checksum and raises a false conflict.
			return (metaFilePath, true);
		}
		options.ExpectedChecksum = baseline.Checksum;
		options.ExpectedSchemaUId = baseline.EditableSchemaUId;
		options.ExpectedSchemaAbsent = !baseline.EditableSchemaExists;
		return (metaFilePath, true);
	}

	/// <inheritdoc />
	public void RefreshOrDrop(string metaFilePath, PageUpdateOptions options, PageUpdateResponse response) {
		if (string.IsNullOrWhiteSpace(response.NewChecksum)) {
			PageBaselineStore.DeleteBaseline(_fileSystem, metaFilePath);
			return;
		}
		PageBaselineStore.RefreshExistingBaseline(
			_fileSystem,
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
