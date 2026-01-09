using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Clio.Common.db;

public interface IPostgresToolsPathDetector
{
	string FindPgRestore();
	bool IsPgRestoreAvailable(string pgToolsPath = null);
	string GetPgRestorePath(string pgToolsPath = null);
}

public class PostgresToolsPathDetector : IPostgresToolsPathDetector
{
	private readonly IFileSystem _fileSystem;
	private string _cachedPgRestorePath;
	private bool _cacheInitialized;

	public PostgresToolsPathDetector(IFileSystem fileSystem) {
		_fileSystem = fileSystem;
	}

	public string FindPgRestore() {
		if (_cacheInitialized) {
			return _cachedPgRestorePath;
		}

		string pgRestorePath = SearchForPgRestore();
		_cachedPgRestorePath = pgRestorePath;
		_cacheInitialized = true;
		return pgRestorePath;
	}

	public bool IsPgRestoreAvailable(string pgToolsPath = null) {
		return !string.IsNullOrEmpty(GetPgRestorePath(pgToolsPath));
	}

	public string GetPgRestorePath(string pgToolsPath = null) {
		if (!string.IsNullOrEmpty(pgToolsPath)) {
			string explicitPath = Path.Combine(pgToolsPath, GetPgRestoreExecutableName());
			if (_fileSystem.ExistsFile(explicitPath)) {
				return explicitPath;
			}
			return null;
		}

		return FindPgRestore();
	}

	private string SearchForPgRestore() {
		string executableName = GetPgRestoreExecutableName();

		string pathFromEnvironment = FindInPath(executableName);
		if (!string.IsNullOrEmpty(pathFromEnvironment)) {
			return pathFromEnvironment;
		}

		string pathFromCommonLocations = FindInCommonLocations(executableName);
		return pathFromCommonLocations;
	}

	private string FindInPath(string executableName) {
		string pathVariable = Environment.GetEnvironmentVariable("PATH");
		if (string.IsNullOrEmpty(pathVariable)) {
			return null;
		}

		string[] paths = pathVariable.Split(Path.PathSeparator);
		foreach (string path in paths) {
			try {
				string fullPath = Path.Combine(path, executableName);
				if (_fileSystem.ExistsFile(fullPath)) {
					return fullPath;
				}
			} catch {
				// Ignore invalid paths
			}
		}

		return null;
	}

	private string FindInCommonLocations(string executableName) {
		string[] commonLocations = GetCommonPgRestoreLocations();

		foreach (string location in commonLocations) {
			try {
				if (!_fileSystem.ExistsDirectory(location)) {
					continue;
				}

				string fullPath = Path.Combine(location, executableName);
				if (_fileSystem.ExistsFile(fullPath)) {
					return fullPath;
				}
			} catch {
				// Ignore errors accessing directories
			}
		}

		return null;
	}

	private string[] GetCommonPgRestoreLocations() {
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
			return GetWindowsCommonLocations();
		}

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
			return GetLinuxCommonLocations();
		}

		if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
			return GetMacOSCommonLocations();
		}

		return Array.Empty<string>();
	}

	private string[] GetWindowsCommonLocations() {
		var locations = new System.Collections.Generic.List<string>();

		string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
		string postgresBaseDir = Path.Combine(programFiles, "PostgreSQL");

		if (_fileSystem.ExistsDirectory(postgresBaseDir)) {
			try {
				string[] versionDirs = _fileSystem.GetDirectories(postgresBaseDir);
				foreach (string versionDir in versionDirs.OrderByDescending(d => d)) {
					locations.Add(Path.Combine(versionDir, "bin"));
				}
			} catch {
				// Ignore errors
			}
		}

		return locations.ToArray();
	}

	private string[] GetLinuxCommonLocations() {
		var locations = new System.Collections.Generic.List<string> {
			"/usr/bin",
			"/usr/local/bin"
		};

		string postgresBaseDir = "/usr/lib/postgresql";
		if (_fileSystem.ExistsDirectory(postgresBaseDir)) {
			try {
				string[] versionDirs = _fileSystem.GetDirectories(postgresBaseDir);
				foreach (string versionDir in versionDirs.OrderByDescending(d => d)) {
					locations.Add(Path.Combine(versionDir, "bin"));
				}
			} catch {
				// Ignore errors
			}
		}

		return locations.ToArray();
	}

	private string[] GetMacOSCommonLocations() {
		var locations = new System.Collections.Generic.List<string> {
			"/usr/local/bin",
			"/opt/homebrew/bin"
		};

		string postgresBaseDir = "/Library/PostgreSQL";
		if (_fileSystem.ExistsDirectory(postgresBaseDir)) {
			try {
				string[] versionDirs = _fileSystem.GetDirectories(postgresBaseDir);
				foreach (string versionDir in versionDirs.OrderByDescending(d => d)) {
					locations.Add(Path.Combine(versionDir, "bin"));
				}
			} catch {
				// Ignore errors
			}
		}

		return locations.ToArray();
	}

	private static string GetPgRestoreExecutableName() {
		return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "pg_restore.exe" : "pg_restore";
	}
}
