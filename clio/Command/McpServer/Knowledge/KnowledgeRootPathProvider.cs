using System;
using System.IO.Abstractions;
using Clio.UserEnvironment;

namespace Clio.Command.McpServer.Knowledge;

internal interface IKnowledgeRootPathProvider {
	string GetOrCreateRoot();
}

internal sealed class KnowledgeRootPathProvider : IKnowledgeRootPathProvider {
	internal const string DefaultDirectoryName = "knowledge";

	private readonly ISettingsRepository _settingsRepository;
	private readonly IFileSystem _fileSystem;
	private readonly object _syncRoot = new();
	private string? _root;

	public KnowledgeRootPathProvider(ISettingsRepository settingsRepository, IFileSystem fileSystem) {
		_settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
		_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
	}

	public string GetOrCreateRoot() {
		lock (_syncRoot) {
			if (_root is not null) {
				return _root;
			}
			string configured = _settingsRepository.GetKnowledgeRootPath();
			string resolved;
			if (string.IsNullOrWhiteSpace(configured)) {
				string settingsDirectory = _fileSystem.Path.GetDirectoryName(_settingsRepository.AppSettingsFilePath)
					?? throw new InvalidOperationException("Clio settings directory could not be resolved.");
				string defaultPath = _fileSystem.Path.Combine(settingsDirectory, DefaultDirectoryName);
				resolved = _settingsRepository.GetOrCreateKnowledgeRootPath(defaultPath);
			}
			else {
				resolved = configured;
			}

			string normalized = NormalizeSafeRoot(resolved);
			_fileSystem.Directory.CreateDirectory(normalized);
			_root = normalized;
			return normalized;
		}
	}

	private string NormalizeSafeRoot(string path) {
		if (string.IsNullOrWhiteSpace(path) || !_fileSystem.Path.IsPathFullyQualified(path)) {
			throw new InvalidOperationException(
				"The configured knowledge-root-path must be an absolute directory path.");
		}
		string normalized = _fileSystem.Path.GetFullPath(path);
		string filesystemRoot = _fileSystem.Path.GetPathRoot(normalized) ?? string.Empty;
		if (string.Equals(
				normalized.TrimEnd(_fileSystem.Path.DirectorySeparatorChar, _fileSystem.Path.AltDirectorySeparatorChar),
				filesystemRoot.TrimEnd(_fileSystem.Path.DirectorySeparatorChar, _fileSystem.Path.AltDirectorySeparatorChar),
				OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) {
			throw new InvalidOperationException("The configured knowledge-root-path cannot be a filesystem root.");
		}
		return normalized;
	}
}
