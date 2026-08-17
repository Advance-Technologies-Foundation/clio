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

	// Platform client-unit schema names are alphanumeric + underscore. Validating before building the
	// target directory keeps the recursive delete below contained inside `.clio-pages/`: a name that
	// matches this pattern cannot contain a path separator, `..`, or a drive/volume marker, so the
	// destructive write can never escape the workspace anchor via the schema name.
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
		// H-1 (ENG-95262): the recursive delete plus the three writes below are one indivisible unit of
		// work on files another clio process (a CLI update-page, a second MCP call, a worker child) may be
		// reading or rewriting at the same instant. Hold the schema's interprocess gate across the whole
		// sequence — the sentinel lives in a sibling `.locks` directory precisely because the delete below
		// would otherwise remove the lock file from under its own holder.
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

	private PageGetResponse WritePageFilesUnderLock(
		PageGetResponse response,
		string schemaName,
		string environmentName,
		string uri,
		string rootDir,
		string schemaDir) {
		try {
			if (_fileSystem.Directory.Exists(schemaDir)) {
				_fileSystem.Directory.Delete(schemaDir, recursive: true);
			}
			_fileSystem.Directory.CreateDirectory(schemaDir);
			EnsureGitIgnoreEntry(rootDir);
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
			_fileSystem.File.WriteAllText(bodyFile, response.Raw.Body);
			_fileSystem.File.WriteAllText(bundleFile, System.Text.Json.JsonSerializer.Serialize(response.Bundle));
			// meta.json is the file every other page path reads, so it is written atomically: a reader
			// outside this gate (an older clio, a foreign tool) must never observe a truncated prefix.
			PageBaselineStore.WriteMetaAtomically(_fileSystem, metaFile, new PageMetaFileModel {
				FetchedAt = fetchedAt,
				Page = response.Page,
				Baseline = baseline
			});
		} catch (Exception ex) {
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
