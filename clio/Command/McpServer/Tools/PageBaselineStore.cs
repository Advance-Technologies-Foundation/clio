using System;
using System.Text.Json;
using Clio.Command.McpServer.Tools;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command;

/// <summary>
/// Owns the conflict-detection baseline persisted in <c>.clio-pages/{schema}/meta.json</c>:
/// discovery before a write, refresh after a successful write, and removal when fresh metadata
/// could not be obtained. All operations are best-effort and fail toward "no check" — a broken,
/// missing, or legacy meta.json must never block a page write with a false conflict.
/// <para>
/// Every operation is bounded by the schema's interprocess file gate
/// (<c>.clio-pages/.locks/{schema}.lock</c>, see <see cref="ResolveSchemaLockFilePath"/>) so a
/// read-modify-write cannot interleave with another clio process, and every write lands atomically
/// (temp file + replace) so no reader can observe a half-written baseline.
/// </para>
/// <para>
/// Failures no longer disappear. Each operation reports a human-readable diagnostic that the caller
/// surfaces as a response WARNING: a lost refresh leaves conflict detection pointing at a superseded
/// checksum, and the caller must learn that even though the save itself succeeded. A MISSING
/// meta.json is not a failure and stays silent — it is the legitimate "no baseline captured" state.
/// </para>
/// <para>
/// Static and <see cref="IFileSystem"/>-parameterised by deliberate parity with
/// <see cref="PageOutputDirectoryResolver"/>; no DI registration required. The gate is passed in as a
/// parameter for the same reason, and may be <c>null</c> (then the disk touch is ungated, which is the
/// pre-gate behaviour) so test call sites that predate it keep working.
/// </para>
/// </summary>
internal static class PageBaselineStore {

	private const string ClioPagesDirectoryName = ".clio-pages";
	private const string MetaFileName = "meta.json";
	private const string LocksDirectoryName = ".locks";

	/// <summary>
	/// Resolves the <c>meta.json</c> path for <paramref name="schemaName"/>. When
	/// <paramref name="bodyFile"/> points inside a <c>.clio-pages/{schema-name}/</c> directory the
	/// sibling <c>meta.json</c> wins (covers custom anchors without an explicit output directory);
	/// otherwise the path is derived from <see cref="PageOutputDirectoryResolver.ResolveAnchor"/>.
	/// The file is not required to exist.
	/// </summary>
	/// <param name="warning">
	/// Set when the supplied <paramref name="bodyFile"/> could not be inspected, so the sibling
	/// meta.json may have been passed over in favour of the anchor-derived one — a silent switch of
	/// baseline location that can disarm conflict detection. <c>null</c> on the normal path.
	/// </param>
	internal static string ResolveMetaFilePath(
		IFileSystem fileSystem,
		string currentDirectory,
		string homeDirectory,
		string homeFallbackAnchor,
		string outputDirectory,
		string bodyFile,
		string schemaName,
		out string warning) {
		warning = null;
		if (!string.IsNullOrWhiteSpace(bodyFile)) {
			try {
				string bodyDir = fileSystem.Path.GetDirectoryName(fileSystem.Path.GetFullPath(bodyFile));
				if (bodyDir is not null
					&& string.Equals(fileSystem.Path.GetFileName(bodyDir), schemaName, StringComparison.OrdinalIgnoreCase)) {
					string parent = fileSystem.Path.GetDirectoryName(bodyDir);
					if (parent is not null
						&& string.Equals(fileSystem.Path.GetFileName(parent), ClioPagesDirectoryName, StringComparison.Ordinal)) {
						return fileSystem.Path.Combine(bodyDir, MetaFileName);
					}
				}
			} catch (Exception ex) {
				// fall through to anchor resolution — a malformed body-file path must not break discovery,
				// but the caller has to know the baseline location was picked without inspecting it.
				warning = $"The body-file path '{bodyFile}' could not be inspected ({ex.Message}), so the conflict-detection "
					+ "baseline was located from the workspace anchor instead of next to the body file. If the two differ, "
					+ "external-modification detection is not armed for this page.";
			}
		}
		string anchor = PageOutputDirectoryResolver.ResolveAnchor(
			fileSystem, currentDirectory, homeDirectory, homeFallbackAnchor, outputDirectory);
		return fileSystem.Path.Combine(anchor, ClioPagesDirectoryName, schemaName, MetaFileName);
	}

	/// <summary>
	/// Resolves the interprocess sentinel that guards every touch of one schema's page files, given
	/// that schema's <c>meta.json</c> path: <c>{anchor}/.clio-pages/.locks/{schema}.lock</c>.
	/// <para>
	/// The sentinel deliberately sits in a sibling <c>.locks</c> directory rather than inside
	/// <c>.clio-pages/{schema}/</c>, because <see cref="PageFileWriter"/> deletes that subtree
	/// recursively on every get-page: a sentinel inside it would be unlinked from under its holder on
	/// Unix, and on Windows would make the delete fail against the open exclusive handle and turn a
	/// working get-page into an error. The <c>.gitignore</c> that <see cref="PageFileWriter"/> writes
	/// at the <c>.clio-pages</c> root already excludes everything below it, <c>.locks</c> included.
	/// </para>
	/// </summary>
	/// <returns>The lock file path, or <c>null</c> when it cannot be derived from the supplied path.</returns>
	internal static string ResolveSchemaLockFilePath(IFileSystem fileSystem, string metaFilePath) {
		if (string.IsNullOrWhiteSpace(metaFilePath)) {
			return null;
		}
		try {
			string schemaDir = fileSystem.Path.GetDirectoryName(metaFilePath);
			if (string.IsNullOrEmpty(schemaDir)) {
				return null;
			}
			string schemaName = fileSystem.Path.GetFileName(schemaDir);
			string rootDir = fileSystem.Path.GetDirectoryName(schemaDir);
			if (string.IsNullOrEmpty(schemaName) || string.IsNullOrEmpty(rootDir)) {
				return null;
			}
			return ResolveSchemaLockFilePath(fileSystem, rootDir, schemaName);
		} catch (Exception) {
			return null;
		}
	}

	/// <summary>
	/// Resolves the interprocess sentinel for <paramref name="schemaName"/> from the
	/// <c>.clio-pages</c> root directory. See <see cref="ResolveSchemaLockFilePath(IFileSystem,string)"/>
	/// for why the sentinel lives outside the per-schema directory.
	/// </summary>
	internal static string ResolveSchemaLockFilePath(IFileSystem fileSystem, string clioPagesRootDir, string schemaName) {
		if (string.IsNullOrWhiteSpace(clioPagesRootDir) || string.IsNullOrWhiteSpace(schemaName)) {
			return null;
		}
		return fileSystem.Path.Combine(clioPagesRootDir, LocksDirectoryName, $"{schemaName}.lock");
	}

	/// <summary>
	/// Reads the baseline block from <paramref name="metaFilePath"/>. Returns <c>null</c> when the
	/// file is missing, unparseable, or carries no <c>baseline</c> property (legacy format) — the
	/// caller must then skip the conflict check entirely.
	/// </summary>
	/// <param name="warning">
	/// Set when an EXISTING meta.json could not be read or parsed, meaning external-modification
	/// detection is disarmed for this page without the caller having asked for that. Stays <c>null</c>
	/// when the file is simply absent (no baseline was ever captured) or is legacy-format.
	/// </param>
	internal static PageBaselineInfo TryReadBaseline(
		IFileSystem fileSystem,
		IInterprocessFileGate gate,
		string metaFilePath,
		out string warning) {
		warning = null;
		if (string.IsNullOrWhiteSpace(metaFilePath)
			|| (!FileExistsQuietly(fileSystem, metaFilePath)
				&& AbsenceIsDefinitive(fileSystem, metaFilePath))) {
			// Answered without the gate ONLY when the absence is definitive — see AbsenceIsDefinitive. A
			// missing file inside an existing .clio-pages tree may be a get-page mid-rewrite, and treating
			// that as "no baseline" is how an update-page comes to run with no expected checksum.
			return null;
		}
		try {
			return RunGated<PageBaselineInfo>(gate, fileSystem, metaFilePath, () => {
				if (!fileSystem.File.Exists(metaFilePath)) {
					return null;
				}
				string json = fileSystem.File.ReadAllText(metaFilePath);
				PageMetaFileModel meta = JsonSerializer.Deserialize<PageMetaFileModel>(json);
				return meta?.Baseline;
			});
		} catch (Exception ex) {
			warning = $"External-modification detection is DISARMED for this page: the conflict baseline "
				+ $"'{metaFilePath}' exists but could not be read ({ex.Message}). Re-run get-page to recapture it.";
			return null;
		}
	}

	/// <summary>
	/// Determines whether the baseline was captured against the same environment the current call
	/// targets. Name compares to name (ordinal, ignore case); URI compares to URI normalized for a
	/// trailing slash (ordinal, ignore case). Any cross-mode combination or missing identity on
	/// either side is NOT a match — the conflict check is then skipped, because a baseline from a
	/// different environment is not evidence of an external modification.
	/// </summary>
	internal static bool MatchesEnvironment(PageBaselineInfo baseline, string environmentName, string uri) {
		if (baseline is null) {
			return false;
		}
		if (!string.IsNullOrWhiteSpace(baseline.EnvironmentName) && !string.IsNullOrWhiteSpace(environmentName)) {
			return string.Equals(baseline.EnvironmentName, environmentName, StringComparison.OrdinalIgnoreCase);
		}
		if (!string.IsNullOrWhiteSpace(baseline.EnvironmentUri) && !string.IsNullOrWhiteSpace(uri)) {
			return string.Equals(NormalizeUri(baseline.EnvironmentUri), NormalizeUri(uri), StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	/// <summary>
	/// Rewrites only the <c>baseline</c> block of an existing <c>meta.json</c> after a successful
	/// save, preserving <c>fetchedAt</c>/<c>page</c>. No-ops when the file does not exist —
	/// the store never creates <c>.clio-pages</c> directories on the write path. The whole
	/// read-merge-write runs inside ONE gate acquisition, so a concurrent writer cannot slip between
	/// the read and the write and have its own update overwritten.
	/// </summary>
	/// <returns>
	/// <c>null</c> when the refresh landed or legitimately no-opped; otherwise a diagnostic the caller
	/// must surface as a warning. The refresh never throws: a failed refresh must not fail the save
	/// that already succeeded — but it must not be invisible either, because the stored baseline is now
	/// behind the server and the next save can raise a false conflict.
	/// </returns>
	internal static string RefreshExistingBaseline(
		IFileSystem fileSystem,
		IInterprocessFileGate gate,
		string metaFilePath,
		PageBaselineInfo baseline) {
		// See TryReadBaseline and AbsenceIsDefinitive: the pre-gate check keeps the store's promise never to
		// create a `.clio-pages` tree, but only a DEFINITIVE absence may skip the gate. The inner check
		// still stands for the interleaving case.
		if (string.IsNullOrWhiteSpace(metaFilePath)
			|| (!FileExistsQuietly(fileSystem, metaFilePath)
				&& AbsenceIsDefinitive(fileSystem, metaFilePath))) {
			return null;
		}
		try {
			return RunGated<string>(gate, fileSystem, metaFilePath, () => {
				if (!fileSystem.File.Exists(metaFilePath)) {
					return null;
				}
				PageMetaFileModel meta = JsonSerializer.Deserialize<PageMetaFileModel>(
					fileSystem.File.ReadAllText(metaFilePath));
				if (meta is null) {
					return null;
				}
				PageMetaFileModel updated = new() {
					FetchedAt = meta.FetchedAt,
					Page = meta.Page,
					Baseline = MergeEnvironmentIdentity(baseline, meta.Baseline)
				};
				WriteMetaAtomically(fileSystem, metaFilePath, updated);
				return null;
			});
		} catch (Exception ex) {
			return $"The page was saved, but its conflict baseline '{metaFilePath}' could not be updated to the "
				+ $"post-save checksum ({ex.Message}). The next save of this page may report a conflict that is not real; "
				+ "re-run get-page to recapture the baseline.";
		}
	}

	/// <summary>
	/// Carries forward the environment identity (name + URI) of a prior capture when the incoming
	/// post-save baseline leaves a field unset. Both on-disk write paths must persist a byte-compatible
	/// identity: update-page writes name+URI from its args, but sync-pages only knows the environment
	/// name. Without this merge a sync-pages refresh would strip the <c>EnvironmentUri</c> a URI-mode
	/// get-page captured, so a later URI-mode update-page could no longer match the environment and
	/// conflict detection would silently disarm for that page.
	/// </summary>
	internal static PageBaselineInfo MergeEnvironmentIdentity(PageBaselineInfo refreshed, PageBaselineInfo previous) {
		if (refreshed is null || previous is null) {
			return refreshed;
		}
		bool nameMissing = string.IsNullOrWhiteSpace(refreshed.EnvironmentName)
			&& !string.IsNullOrWhiteSpace(previous.EnvironmentName);
		bool uriMissing = string.IsNullOrWhiteSpace(refreshed.EnvironmentUri)
			&& !string.IsNullOrWhiteSpace(previous.EnvironmentUri);
		if (!nameMissing && !uriMissing) {
			return refreshed;
		}
		return new PageBaselineInfo {
			SchemaName = refreshed.SchemaName,
			EnvironmentName = nameMissing ? previous.EnvironmentName : refreshed.EnvironmentName,
			EnvironmentUri = uriMissing ? previous.EnvironmentUri : refreshed.EnvironmentUri,
			EditableSchemaExists = refreshed.EditableSchemaExists,
			EditableSchemaUId = refreshed.EditableSchemaUId,
			Checksum = refreshed.Checksum,
			ModifiedOn = refreshed.ModifiedOn,
			CapturedAt = refreshed.CapturedAt
		};
	}

	/// <summary>
	/// Removes the <c>baseline</c> block from <c>meta.json</c> (keeping <c>fetchedAt</c>/<c>page</c>)
	/// when fresh post-save metadata could not be obtained, so the next write fails toward
	/// "no check" instead of a false conflict against a stale checksum. Gated and atomic like
	/// <see cref="RefreshExistingBaseline"/>.
	/// </summary>
	/// <returns><c>null</c> on success or a legitimate no-op; otherwise a diagnostic to surface as a warning.</returns>
	internal static string DeleteBaseline(IFileSystem fileSystem, IInterprocessFileGate gate, string metaFilePath) {
		// See TryReadBaseline and AbsenceIsDefinitive for why only a DEFINITIVE absence skips the gate.
		if (string.IsNullOrWhiteSpace(metaFilePath)
			|| (!FileExistsQuietly(fileSystem, metaFilePath)
				&& AbsenceIsDefinitive(fileSystem, metaFilePath))) {
			return null;
		}
		try {
			return RunGated<string>(gate, fileSystem, metaFilePath, () => {
				if (!fileSystem.File.Exists(metaFilePath)) {
					return null;
				}
				PageMetaFileModel meta = JsonSerializer.Deserialize<PageMetaFileModel>(
					fileSystem.File.ReadAllText(metaFilePath));
				if (meta?.Baseline is null) {
					return null;
				}
				PageMetaFileModel updated = new() {
					FetchedAt = meta.FetchedAt,
					Page = meta.Page,
					Baseline = null
				};
				WriteMetaAtomically(fileSystem, metaFilePath, updated);
				return null;
			});
		} catch (Exception ex) {
			return $"The page was saved, but the now-stale conflict baseline '{metaFilePath}' could not be removed "
				+ $"({ex.Message}). The next save of this page may report a conflict that is not real; re-run get-page "
				+ "to recapture the baseline.";
		}
	}

	/// <summary>
	/// Serialises <paramref name="meta"/> into <paramref name="metaFilePath"/> through a sibling temp
	/// file and a single replace, so a concurrent reader observes either the whole previous file or the
	/// whole new one — never a truncated prefix. Callers must already hold the schema gate.
	/// </summary>
	internal static void WriteMetaAtomically(IFileSystem fileSystem, string metaFilePath, PageMetaFileModel meta) {
		string directory = fileSystem.Path.GetDirectoryName(metaFilePath);
		string temporary = fileSystem.Path.Combine(
			string.IsNullOrEmpty(directory) ? "." : directory,
			$".{fileSystem.Path.GetFileName(metaFilePath)}.{Guid.NewGuid():N}.tmp");
		try {
			fileSystem.File.WriteAllText(temporary, JsonSerializer.Serialize(meta));
			fileSystem.File.Move(temporary, metaFilePath, overwrite: true);
		} finally {
			if (fileSystem.File.Exists(temporary)) {
				fileSystem.File.Delete(temporary);
			}
		}
	}

	// A missing meta.json is the normal "no baseline captured" state, so probing for it must never itself
	// fail a page operation — an unreadable parent directory degrades to "no baseline" exactly as an absent
	// file does.
	private static bool FileExistsQuietly(IFileSystem fileSystem, string metaFilePath) {
		try {
			return fileSystem.File.Exists(metaFilePath);
		} catch (Exception) {
			return false;
		}
	}

	// Runs the disk touch under the schema's interprocess gate when one is available. A null gate (or a
	// path whose lock sentinel cannot be derived) degrades to the direct, pre-gate behaviour rather than
	// failing the page operation.
	private static T RunGated<T>(
		IInterprocessFileGate gate, IFileSystem fileSystem, string metaFilePath, Func<T> action) {
		string lockFilePath = gate is null ? null : ResolveSchemaLockFilePath(fileSystem, metaFilePath);
		return lockFilePath is null ? action() : gate.Enter(lockFilePath, action);
	}

	private static string NormalizeUri(string uri) => uri.Trim().TrimEnd('/');

	/// <summary>
	/// True when a missing <c>meta.json</c> means "never captured" rather than "another clio is rewriting
	/// it right now", so the caller may answer without taking the gate.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The pre-gate existence check exists to keep a promise: an <c>update-page</c> in a directory with no
	/// <c>.clio-pages</c> tree must not materialise one — acquiring the gate creates a <c>.locks</c>
	/// directory. That promise is worth keeping, but the check as written could not tell an absent baseline
	/// from a TRANSIENT one: <c>PageFileWriter</c> deletes the whole schema directory while it rewrites
	/// (its own remarks name the hazard), holding the gate throughout. A concurrent <c>update-page</c>
	/// looking in that window saw "no baseline", ran with no expected checksum, and could overwrite an
	/// external change — while the completing <c>get-page</c> left a baseline nobody compared against.
	/// Silent, and precisely what conflict detection exists to prevent.
	/// </para>
	/// <para>
	/// The discriminator is the <c>.clio-pages</c> ROOT, which the writer's per-schema delete never
	/// removes. No root means no baseline was ever captured here: answer immediately and create nothing.
	/// A root that exists with the schema's file missing may be mid-rewrite, so the caller takes the gate
	/// and re-reads under it — which costs a lock only in the workspace that already has the tree the lock
	/// would live in.
	/// </para>
	/// </remarks>
	/// <param name="fileSystem">The file system.</param>
	/// <param name="metaFilePath">Full path of the schema's <c>meta.json</c>.</param>
	/// <returns><see langword="true"/> when the absence is definitive.</returns>
	private static bool AbsenceIsDefinitive(IFileSystem fileSystem, string metaFilePath) {
		try {
			string schemaDirectory = fileSystem.Path.GetDirectoryName(metaFilePath);
			string pagesRoot = string.IsNullOrWhiteSpace(schemaDirectory)
				? null
				: fileSystem.Path.GetDirectoryName(schemaDirectory);
			return string.IsNullOrWhiteSpace(pagesRoot) || !fileSystem.Directory.Exists(pagesRoot);
		} catch (Exception) {
			// An unreadable path is not evidence a baseline exists; keep the old, cheaper answer.
			return true;
		}
	}
}
