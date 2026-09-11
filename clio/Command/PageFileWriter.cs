using System;
using System.Text.RegularExpressions;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command;

/// <summary>
/// Persists a successful <c>get-page</c> read to <c>.clio-pages/{schema}/</c> as
/// <c>body.js</c> / <c>bundle.json</c> / <c>meta.json</c> (the latter carrying the conflict-detection
/// baseline) and ensures the <c>.gitignore</c> hygiene file. Shared by the CLI <c>get-page</c> verb and
/// the MCP <c>get-page</c> tool so both produce a byte-identical workspace layout that the
/// <see cref="IPageBaselineGuard"/> can later discover for <c>update-page</c> / <c>sync-pages</c>.
/// </summary>
public interface IPageFileWriter {

	/// <summary>
	/// Writes the page files for a successful <paramref name="response"/> and returns the response
	/// enriched with the written file paths (<see cref="PageGetResponse.Files"/>). The heavy
	/// <c>bundle</c> / <c>raw</c> payloads are preserved on the returned object — callers that need a
	/// compact envelope (e.g. the MCP tool) strip them afterwards. On a write failure a failed
	/// <see cref="PageGetResponse"/> with a descriptive error is returned instead.
	/// </summary>
	/// <param name="response">The successful get-page response to persist.</param>
	/// <param name="schemaName">The page schema name (directory name under <c>.clio-pages</c>).</param>
	/// <param name="environmentName">Registered environment name captured into the baseline (nullable).</param>
	/// <param name="uri">Direct Creatio URI captured into the baseline (nullable).</param>
	/// <param name="outputDirectory">Optional explicit anchor override; <c>null</c> uses workspace/home resolution.</param>
	PageGetResponse WritePageFiles(
		PageGetResponse response,
		string schemaName,
		string environmentName,
		string uri,
		string outputDirectory);
}

/// <inheritdoc />
public sealed class PageFileWriter : IPageFileWriter {

	private const string ClioPagesDirectoryName = ".clio-pages";

	// Sibling of `.locks` at the `.clio-pages` root: every transient copy of a page tree lives under
	// `.staging/{schema}/`, so the pages root itself never carries litter and a purge of one schema's
	// residue is a WHOLE-SEGMENT match that cannot reach another schema's in-flight publication. The
	// `.gitignore` written at the root already excludes it, `.locks` included.
	private const string StagingDirectoryName = ".staging";

	// Platform client-unit schema names are alphanumeric + underscore. Validating before building the
	// target directory keeps the destructive swap below contained inside `.clio-pages/`: a name that
	// matches this pattern cannot contain a path separator, `..`, or a drive/volume marker, so neither the
	// published directory nor its `.staging/{schema}` counterpart can escape the workspace anchor.
	private static readonly Regex SchemaNamePattern =
		new("^[A-Za-z0-9_]+$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

	private readonly IFileSystem _fileSystem;
	private readonly IInterprocessFileGate _fileGate;

	/// <summary>
	/// Initializes a new instance of the <see cref="PageFileWriter"/> class.
	/// </summary>
	/// <param name="fileSystem">File-system abstraction used to write the page files.</param>
	/// <param name="fileGate">
	/// Interprocess gate that serialises the destructive prepare-and-write of one schema's page
	/// directory against other clio processes in the same workspace, so a concurrent update-page cannot
	/// read a <c>meta.json</c> that this get-page is in the middle of deleting and rewriting. Optional
	/// with a <c>null</c> default only so the existing target-typed test instantiations across the
	/// page-tool fixtures keep compiling; in production the container always supplies it.
	/// </param>
	public PageFileWriter(IFileSystem fileSystem, IInterprocessFileGate fileGate = null) {
		_fileSystem = fileSystem;
		_fileGate = fileGate;
	}

	/// <inheritdoc />
	public PageGetResponse WritePageFiles(
		PageGetResponse response,
		string schemaName,
		string environmentName,
		string uri,
		string outputDirectory) {
		if (string.IsNullOrWhiteSpace(schemaName) || !SchemaNamePattern.IsMatch(schemaName)) {
			return new PageGetResponse {
				Success = false,
				Error = $"Invalid schema name '{schemaName}': only letters, digits and underscore are allowed."
			};
		}
		// H1: reading the process-global cwd to anchor page output must serialize against the MCP
		// workspace tools that PIN cwd. In the MCP path (get-page) this runs under the per-tenant lock
		// (ordering per-tenant → CwdLock); in the single-threaded CLI path CwdLock is uncontended.
		string anchor;
		lock (McpServer.Tools.McpToolExecutionLock.CwdLock) {
			anchor = PageOutputDirectoryResolver.ResolveAnchor(
				_fileSystem,
				_fileSystem.Directory.GetCurrentDirectory(),
				Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
				ClioRuntimePaths.Home,
				outputDirectory);
		}
		string rootDir = _fileSystem.Path.Combine(anchor, ClioPagesDirectoryName);
		string schemaDir = _fileSystem.Path.Combine(rootDir, schemaName);
		// H-1 (ENG-95262): staging the three files and swapping them into place are one indivisible unit of
		// work on files another clio process (a CLI update-page, a second MCP call, a worker child) may be
		// reading or rewriting at the same instant. Hold the schema's interprocess gate across the whole
		// sequence — the sentinel lives in a sibling `.locks` directory precisely because the swap below
		// replaces the schema directory wholesale and would otherwise take the lock file with it.
		string lockFilePath = _fileGate is null
			? null
			: PageBaselineStore.ResolveSchemaLockFilePath(_fileSystem, rootDir, schemaName);
		try {
			return lockFilePath is null
				? WritePageFilesUnderLock(response, schemaName, environmentName, uri, rootDir, schemaDir)
				: _fileGate.Enter(lockFilePath,
					() => WritePageFilesUnderLock(response, schemaName, environmentName, uri, rootDir, schemaDir));
		} catch (TimeoutException ex) {
			return new PageGetResponse {
				Success = false,
				Error = $"Failed to write page files: another clio operation is still using '{schemaDir}' ({ex.Message})."
			};
		}
	}

	// ENG-95262: get-page runs in the worker cohort, so it is bounded by the parent KILLING the worker —
	// TerminateJobObject over the job on Windows, kill(-pid, SIGKILL) to the process group on Unix. No
	// `finally` runs, so whatever is on disk between two filesystem operations is what the user is left
	// with. Writing the three files straight into `.clio-pages/{schema}/` therefore had a durable failure
	// mode, not just an untidy one: meta.json is written LAST, so a kill after body.js left a directory
	// that reads as a successful get-page while carrying NO conflict baseline — PageBaselineStore then
	// answers "no baseline", the next update-page runs with no expected checksum, and an external change
	// can be overwritten silently. Nothing repaired it and nothing reported it.
	//
	// So the tree is BUILT elsewhere and SWAPPED in. A cross-platform atomic directory replacement does
	// not exist (renameat2(RENAME_EXCHANGE) is Linux-only and not exposed by .NET), so the swap is two
	// renames and there is a hairline in which the schema directory is absent. That residual state is the
	// honest "never fetched" one the rest of the page code already treats as legitimate ("fail toward no
	// check"), and it self-heals on retry — unlike a directory that exists with files missing.
	private PageGetResponse WritePageFilesUnderLock(
		PageGetResponse response,
		string schemaName,
		string environmentName,
		string uri,
		string rootDir,
		string schemaDir) {
		string stagingRoot = _fileSystem.Path.Combine(rootDir, StagingDirectoryName, schemaName);
		// A short discriminator, not a full GUID: this segment is pure added depth against the Windows
		// MAX_PATH budget of a workspace tree, and it only has to be unique among the leftovers of THIS
		// schema — which the purge below has just cleared, under a gate that excludes every other writer.
		string stagingDir = _fileSystem.Path.Combine(stagingRoot, Guid.NewGuid().ToString("N")[..8]);
		try {
			EnsureGitIgnoreEntry(rootDir);
			// Residue of THIS schema's own interrupted publication, cleared under the gate this call already
			// holds. Scoped to `.staging/{schema}/` as a whole path segment: another schema's staging is
			// guarded by another schema's gate and may be in flight right now.
			PurgeStagingResidue(stagingRoot);
			_fileSystem.Directory.CreateDirectory(stagingDir);
		} catch (Exception ex) {
			return new PageGetResponse {
				Success = false,
				Error = $"Failed to prepare output directory '{schemaDir}': {ex.Message}"
			};
		}
		string bodyFile = _fileSystem.Path.Combine(schemaDir, "body.js");
		string bundleFile = _fileSystem.Path.Combine(schemaDir, "bundle.json");
		string metaFile = _fileSystem.Path.Combine(schemaDir, "meta.json");
		string fetchedAt = DateTime.UtcNow.ToString("o");
		PageBaselineInfo baseline = BuildBaseline(schemaName, environmentName, uri, response, fetchedAt);
		try {
			_fileSystem.File.WriteAllText(_fileSystem.Path.Combine(stagingDir, "body.js"), response.Raw.Body);
			_fileSystem.File.WriteAllText(_fileSystem.Path.Combine(stagingDir, "bundle.json"),
				System.Text.Json.JsonSerializer.Serialize(response.Bundle));
			// meta.json is the file every other page path reads, so it is written atomically even inside
			// staging: a reader outside this gate (an older clio, a foreign tool) must never observe a
			// truncated prefix once the directory is published.
			PageBaselineStore.WriteMetaAtomically(_fileSystem, _fileSystem.Path.Combine(stagingDir, "meta.json"),
				new PageMetaFileModel {
					FetchedAt = fetchedAt,
					Page = response.Page,
					Baseline = baseline
				});
			PublishStagedDirectory(stagingRoot, stagingDir, schemaDir);
		} catch (Exception ex) {
			TryDeleteDirectory(stagingDir);
			return new PageGetResponse {
				Success = false,
				Error = $"Failed to write page files: {ex.Message}"
			};
		}
		return new PageGetResponse {
			Success = true,
			Page = response.Page,
			Bundle = response.Bundle,
			Raw = response.Raw,
			Editable = response.Editable,
			Files = new PageGetFilesInfo {
				BodyFile = bodyFile,
				BundleFile = bundleFile,
				MetaFile = metaFile,
				FetchedAt = fetchedAt
			}
		};
	}

	// Swaps the finished staging directory into place. The previous generation is RENAMED aside rather
	// than deleted first: a rename is O(1), so the window in which the schema directory does not exist is
	// two renames wide, whereas a recursive delete would stretch that window across the whole delete.
	// Nothing here is rollback for a kill (a kill runs no rollback) — the move-back only covers an
	// ordinary publish failure, so a plain error does not cost the user the tree they already had.
	private void PublishStagedDirectory(string stagingRoot, string stagingDir, string schemaDir) {
		// The `old-` prefix keeps the retired tree from ever colliding with a staging directory name.
		string retiredDir = _fileSystem.Path.Combine(stagingRoot, $"old-{Guid.NewGuid().ToString("N")[..8]}");
		bool retired = false;
		if (_fileSystem.Directory.Exists(schemaDir)) {
			_fileSystem.Directory.Move(schemaDir, retiredDir);
			retired = true;
		}
		try {
			_fileSystem.Directory.Move(stagingDir, schemaDir);
		} catch (Exception) {
			if (retired) {
				TryMoveDirectory(retiredDir, schemaDir);
			}
			throw;
		}
		if (retired) {
			TryDeleteDirectory(retiredDir);
		}
	}

	// Clears whatever an earlier interrupted publication of THIS schema left behind. Best-effort: residue
	// is inert (nothing reads `.staging`), so failing to remove it must never fail the fetch that repairs
	// the very state the residue came from.
	private void PurgeStagingResidue(string stagingRoot) {
		try {
			if (!_fileSystem.Directory.Exists(stagingRoot)) {
				return;
			}
			foreach (string leftover in _fileSystem.Directory.GetDirectories(stagingRoot)) {
				TryDeleteDirectory(leftover);
			}
		} catch {
			// ignore — see above.
		}
	}

	private void TryDeleteDirectory(string directory) {
		try {
			if (_fileSystem.Directory.Exists(directory)) {
				_fileSystem.Directory.Delete(directory, recursive: true);
			}
		} catch {
			// ignore — leftover staging is inert and is cleared by the next fetch of this schema.
		}
	}

	private void TryMoveDirectory(string source, string destination) {
		try {
			_fileSystem.Directory.Move(source, destination);
		} catch {
			// ignore — the publish failure is already being reported; this only tries to give the previous
			// generation back.
		}
	}

	private void EnsureGitIgnoreEntry(string rootDir) {
		try {
			if (!_fileSystem.Directory.Exists(rootDir)) {
				_fileSystem.Directory.CreateDirectory(rootDir);
			}
			string gitignorePath = _fileSystem.Path.Combine(rootDir, ".gitignore");
			if (!_fileSystem.File.Exists(gitignorePath)) {
				_fileSystem.File.WriteAllText(gitignorePath, "*\n!.gitignore\n");
			}
		} catch {
			// ignore — gitignore is best-effort hygiene; never block a successful get-page.
		}
	}

	private static PageBaselineInfo BuildBaseline(
		string schemaName,
		string environmentName,
		string uri,
		PageGetResponse response,
		string fetchedAt) {
		if (response.Editable is null) {
			return null;
		}
		return new PageBaselineInfo {
			SchemaName = schemaName,
			EnvironmentName = string.IsNullOrWhiteSpace(environmentName) ? null : environmentName,
			EnvironmentUri = string.IsNullOrWhiteSpace(uri) ? null : uri,
			EditableSchemaExists = response.Editable.EditableSchemaExists,
			EditableSchemaUId = response.Editable.EditableSchemaUId,
			Checksum = response.Editable.Checksum,
			ModifiedOn = response.Editable.ModifiedOn,
			CapturedAt = fetchedAt
		};
	}
}
