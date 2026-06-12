using System;
using System.Text.Json;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Owns the conflict-detection baseline persisted in <c>.clio-pages/{schema}/meta.json</c>:
/// discovery before a write, refresh after a successful write, and removal when fresh metadata
/// could not be obtained. All operations are best-effort and fail toward "no check" — a broken,
/// missing, or legacy meta.json must never block a page write with a false conflict.
/// <para>
/// Static and <see cref="IFileSystem"/>-parameterised by deliberate parity with
/// <see cref="PageOutputDirectoryResolver"/>; no DI registration required.
/// </para>
/// </summary>
internal static class PageBaselineStore {

	private const string ClioPagesDirectoryName = ".clio-pages";
	private const string MetaFileName = "meta.json";

	/// <summary>
	/// Resolves the <c>meta.json</c> path for <paramref name="schemaName"/>. When
	/// <paramref name="bodyFile"/> points inside a <c>.clio-pages/{schema-name}/</c> directory the
	/// sibling <c>meta.json</c> wins (covers custom anchors without an explicit output directory);
	/// otherwise the path is derived from <see cref="PageOutputDirectoryResolver.ResolveAnchor"/>.
	/// The file is not required to exist.
	/// </summary>
	internal static string ResolveMetaFilePath(
		IFileSystem fileSystem,
		string currentDirectory,
		string homeDirectory,
		string homeFallbackAnchor,
		string outputDirectory,
		string bodyFile,
		string schemaName) {
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
			} catch {
				// fall through to anchor resolution — a malformed body-file path must not break discovery.
			}
		}
		string anchor = PageOutputDirectoryResolver.ResolveAnchor(
			fileSystem, currentDirectory, homeDirectory, homeFallbackAnchor, outputDirectory);
		return fileSystem.Path.Combine(anchor, ClioPagesDirectoryName, schemaName, MetaFileName);
	}

	/// <summary>
	/// Reads the baseline block from <paramref name="metaFilePath"/>. Returns <c>null</c> when the
	/// file is missing, unparseable, or carries no <c>baseline</c> property (legacy format) — the
	/// caller must then skip the conflict check entirely.
	/// </summary>
	internal static PageBaselineInfo TryReadBaseline(IFileSystem fileSystem, string metaFilePath) {
		try {
			if (string.IsNullOrWhiteSpace(metaFilePath) || !fileSystem.File.Exists(metaFilePath)) {
				return null;
			}
			string json = fileSystem.File.ReadAllText(metaFilePath);
			PageMetaFileModel meta = JsonSerializer.Deserialize<PageMetaFileModel>(json);
			return meta?.Baseline;
		} catch {
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
	/// the store never creates <c>.clio-pages</c> directories on the write path. Best-effort:
	/// I/O or parse errors are swallowed.
	/// </summary>
	internal static void RefreshExistingBaseline(
		IFileSystem fileSystem,
		string metaFilePath,
		string schemaName,
		string environmentName,
		string environmentUri,
		string savedSchemaUId,
		string newChecksum,
		string newModifiedOn,
		string capturedAt) {
		try {
			if (string.IsNullOrWhiteSpace(metaFilePath) || !fileSystem.File.Exists(metaFilePath)) {
				return;
			}
			PageMetaFileModel meta = JsonSerializer.Deserialize<PageMetaFileModel>(
				fileSystem.File.ReadAllText(metaFilePath));
			if (meta is null) {
				return;
			}
			PageMetaFileModel updated = new() {
				FetchedAt = meta.FetchedAt,
				Page = meta.Page,
				Baseline = new PageBaselineInfo {
					SchemaName = schemaName,
					EnvironmentName = string.IsNullOrWhiteSpace(environmentName) ? null : environmentName,
					EnvironmentUri = string.IsNullOrWhiteSpace(environmentUri) ? null : environmentUri,
					EditableSchemaExists = true,
					EditableSchemaUId = savedSchemaUId,
					Checksum = newChecksum,
					ModifiedOn = newModifiedOn,
					CapturedAt = capturedAt
				}
			};
			fileSystem.File.WriteAllText(metaFilePath, JsonSerializer.Serialize(updated));
		} catch {
			// best-effort — a failed refresh must not fail the save that already succeeded.
		}
	}

	/// <summary>
	/// Removes the <c>baseline</c> block from <c>meta.json</c> (keeping <c>fetchedAt</c>/<c>page</c>)
	/// when fresh post-save metadata could not be obtained, so the next write fails toward
	/// "no check" instead of a false conflict against a stale checksum. Best-effort.
	/// </summary>
	internal static void DeleteBaseline(IFileSystem fileSystem, string metaFilePath) {
		try {
			if (string.IsNullOrWhiteSpace(metaFilePath) || !fileSystem.File.Exists(metaFilePath)) {
				return;
			}
			PageMetaFileModel meta = JsonSerializer.Deserialize<PageMetaFileModel>(
				fileSystem.File.ReadAllText(metaFilePath));
			if (meta?.Baseline is null) {
				return;
			}
			PageMetaFileModel updated = new() {
				FetchedAt = meta.FetchedAt,
				Page = meta.Page,
				Baseline = null
			};
			fileSystem.File.WriteAllText(metaFilePath, JsonSerializer.Serialize(updated));
		} catch {
			// best-effort — see RefreshExistingBaseline.
		}
	}

	private static string NormalizeUri(string uri) => uri.Trim().TrimEnd('/');
}
