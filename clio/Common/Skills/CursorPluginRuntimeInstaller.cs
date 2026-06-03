using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Clio.Common.Skills;

/// <summary>
/// Default <see cref="ICursorPluginRuntimeInstaller"/> — a C# port of the toolkit
/// installer's <c>load_plugin_runtime_paths</c> + <c>copy_plugin_runtime_surface</c>.
/// </summary>
public sealed class CursorPluginRuntimeInstaller(IFileSystem fileSystem) : ICursorPluginRuntimeInstaller {
	private const string ReleaseManifestFileName = ".release-manifest.json";

	private readonly IFileSystem _fileSystem = fileSystem;

	/// <inheritdoc />
	public void Install(string sourceRoot, string targetPluginDir) {
		IReadOnlyList<string> runtimePaths = LoadPluginRuntimePaths(sourceRoot);
		_fileSystem.CreateDirectoryIfNotExists(targetPluginDir);

		foreach (string relativePath in runtimePaths) {
			// The manifest comes from an attacker-influenceable source (a cloned/--repo
			// checkout). Reject entries that would escape the source or target roots
			// before any delete/copy touches the filesystem.
			EnsureSafeRelativePath(relativePath);
			string source = _fileSystem.Combine(sourceRoot, relativePath);
			string target = _fileSystem.Combine(targetPluginDir, relativePath);
			EnsureContained(source, sourceRoot, relativePath);
			EnsureContained(target, targetPluginDir, relativePath);

			// Never dereference a symlinked entry — it could point at a sensitive file
			// outside the checkout. Skip it (the toolkit ships no symlinks).
			if (IsSymbolicLink(source)) {
				continue;
			}

			if (_fileSystem.ExistsDirectory(source)) {
				_fileSystem.DeleteDirectoryIfExists(target);
				_fileSystem.CopyDirectoryWithFilter(source, target, overwrite: true, filter: IncludeInCopy);
			}
			else if (_fileSystem.ExistsFile(source)) {
				string targetDirectory = _fileSystem.GetDirectoryInfo(target).Parent?.FullName;
				if (!string.IsNullOrEmpty(targetDirectory)) {
					_fileSystem.CreateDirectoryIfNotExists(targetDirectory);
				}

				_fileSystem.CopyFile(source, target, overwrite: true);
			}
			// Missing source entries are skipped, matching the toolkit installer.
		}
	}

	private void EnsureSafeRelativePath(string relativePath) {
		if (string.IsNullOrWhiteSpace(relativePath)
			|| _fileSystem.IsPathRooted(relativePath)
			|| relativePath.Contains(':', StringComparison.Ordinal)) {
			throw new InvalidOperationException(
				$"Unsafe plugin_runtime entry '{relativePath}': must be a relative path.");
		}

		foreach (string segment in relativePath.Split('/', '\\')) {
			if (segment == "..") {
				throw new InvalidOperationException(
					$"Unsafe plugin_runtime entry '{relativePath}': must not contain '..' segments.");
			}
		}
	}

	private void EnsureContained(string candidate, string root, string relativePath) {
		string fullRoot = _fileSystem.GetFullPath(root);
		string fullCandidate = _fileSystem.GetFullPath(candidate);
		string boundary = fullRoot.EndsWith(_fileSystem.DirectorySeparatorChar)
			? fullRoot
			: fullRoot + _fileSystem.DirectorySeparatorChar;
		if (!string.Equals(fullCandidate, fullRoot, StringComparison.OrdinalIgnoreCase)
			&& !fullCandidate.StartsWith(boundary, StringComparison.OrdinalIgnoreCase)) {
			throw new InvalidOperationException(
				$"Unsafe plugin_runtime entry '{relativePath}': resolves outside '{fullRoot}'.");
		}
	}

	private bool IsSymbolicLink(string path) {
		if (_fileSystem.ExistsDirectory(path)) {
			return _fileSystem.GetDirectoryInfo(path).LinkTarget is not null;
		}

		if (_fileSystem.ExistsFile(path)) {
			return _fileSystem.GetFilesInfos(path).LinkTarget is not null;
		}

		return false;
	}

	private IReadOnlyList<string> LoadPluginRuntimePaths(string sourceRoot) {
		string manifestPath = _fileSystem.Combine(sourceRoot, ReleaseManifestFileName);
		if (!_fileSystem.ExistsFile(manifestPath)) {
			throw new InvalidOperationException(
				$"Toolkit source at '{sourceRoot}' is missing {ReleaseManifestFileName}; cannot resolve the Cursor plugin runtime surface.");
		}

		using JsonDocument document = JsonDocument.Parse(_fileSystem.ReadAllText(manifestPath));
		if (!document.RootElement.TryGetProperty("plugin_runtime", out JsonElement runtime)
			|| runtime.ValueKind != JsonValueKind.Array) {
			throw new InvalidOperationException(
				$"{ReleaseManifestFileName} in '{sourceRoot}' must contain 'plugin_runtime' as an array of strings.");
		}

		List<string> paths = [];
		foreach (JsonElement item in runtime.EnumerateArray()) {
			if (item.ValueKind != JsonValueKind.String) {
				throw new InvalidOperationException(
					$"{ReleaseManifestFileName} 'plugin_runtime' must contain only strings.");
			}

			paths.Add(item.GetString());
		}

		return paths;
	}

	private static bool IncludeInCopy(string path) =>
		!path.Contains("__pycache__", StringComparison.Ordinal)
		&& !path.EndsWith(".pyc", StringComparison.OrdinalIgnoreCase);
}
