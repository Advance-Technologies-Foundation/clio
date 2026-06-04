using System;

namespace Clio.Common.Skills;

/// <summary>
/// Default <see cref="IUserHomeProvider"/> backed by the current user's profile directory.
/// </summary>
public sealed class UserHomeProvider(IFileSystem fileSystem) : IUserHomeProvider {
	private readonly IFileSystem _fileSystem = fileSystem;

	/// <inheritdoc />
	public string GetUserHome() {
		// Mirror the toolkit installer's Python Path.home(): honor USERPROFILE on
		// Windows and HOME on Unix, falling back to the OS profile folder. This is
		// what makes home overrides effective — Environment.GetFolderPath(UserProfile)
		// alone does NOT consult USERPROFILE on Windows.
		string home = OperatingSystem.IsWindows()
			? Environment.GetEnvironmentVariable("USERPROFILE")
			: Environment.GetEnvironmentVariable("HOME");
		if (string.IsNullOrWhiteSpace(home)) {
			home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		}

		return _fileSystem.GetFullPath(home);
	}

	/// <inheritdoc />
	public string GetAgentHome(string agentId) {
		agentId.CheckArgumentNullOrWhiteSpace(nameof(agentId));
		return _fileSystem.Combine(GetUserHome(), $".{agentId.Trim()}");
	}

	/// <inheritdoc />
	public string GetAgentsDir() => _fileSystem.Combine(GetUserHome(), ".agents");

	/// <inheritdoc />
	public string GetClioDir() => _fileSystem.Combine(GetUserHome(), ".clio");
}
