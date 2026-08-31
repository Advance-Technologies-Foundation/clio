using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Clio.Common;

/// <summary>
/// Persists the transient environment values required to start a Creatio host again.
/// </summary>
public interface ICreatioHostEnvironmentStore
{
	/// <summary>
	/// Saves environment values for the application at <paramref name="workingDirectory"/>.
	/// </summary>
	/// <param name="workingDirectory">The application directory used to identify the host.</param>
	/// <param name="environmentVariables">The environment values to restore on a later start.</param>
	void Save(string workingDirectory, IReadOnlyDictionary<string, string> environmentVariables);

	/// <summary>
	/// Loads environment values previously saved for the application at <paramref name="workingDirectory"/>.
	/// </summary>
	/// <param name="workingDirectory">The application directory used to identify the host.</param>
	/// <returns>The saved environment values, or an empty dictionary when none were saved.</returns>
	IReadOnlyDictionary<string, string> Load(string workingDirectory);
}

/// <inheritdoc cref="ICreatioHostEnvironmentStore" />
public sealed class CreatioHostEnvironmentStore : ICreatioHostEnvironmentStore
{
	private const string StoreDirectoryName = "host-environments";
	private const string StoreFileSuffix = ".json";
	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

	private readonly IFileSystem _fileSystem;
	private readonly IFileSecurityHardening _fileSecurityHardening;

	/// <summary>
	/// Initializes a new instance of the <see cref="CreatioHostEnvironmentStore"/> class.
	/// </summary>
	/// <param name="fileSystem">File-system abstraction used to persist the environment.</param>
	/// <param name="fileSecurityHardening">Helper that restricts the store to the current user.</param>
	public CreatioHostEnvironmentStore(IFileSystem fileSystem, IFileSecurityHardening fileSecurityHardening)
	{
		_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
		_fileSecurityHardening = fileSecurityHardening ?? throw new ArgumentNullException(nameof(fileSecurityHardening));
	}

	/// <inheritdoc />
	public void Save(string workingDirectory, IReadOnlyDictionary<string, string> environmentVariables)
	{
		string path = GetStorePath(workingDirectory);
		if (environmentVariables is null || environmentVariables.Count == 0)
		{
			_fileSystem.DeleteFileIfExists(path);
			return;
		}

		if (environmentVariables.Any(variable => string.IsNullOrWhiteSpace(variable.Key) || variable.Value is null))
		{
			throw new ArgumentException("Host environment variable names and values must be non-null.", nameof(environmentVariables));
		}

		string directory = Path.GetDirectoryName(path)
			?? throw new InvalidOperationException("The host environment store directory could not be resolved.");
		_fileSystem.CreateDirectoryIfNotExists(directory);
		_fileSecurityHardening.HardenDirectory(directory);
		string json = JsonSerializer.Serialize(environmentVariables, JsonOptions);
		_fileSystem.WriteOwnerOnlyTextToFile(path, json);
		_fileSecurityHardening.HardenFile(path);
	}

	/// <inheritdoc />
	public IReadOnlyDictionary<string, string> Load(string workingDirectory)
	{
		string path = GetStorePath(workingDirectory);
		if (!_fileSystem.ExistsFile(path))
		{
			return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		}

		try
		{
			Dictionary<string, string>? environmentVariables =
				JsonSerializer.Deserialize<Dictionary<string, string>>(_fileSystem.ReadAllText(path));
			if (environmentVariables is null
				|| environmentVariables.Any(variable => string.IsNullOrWhiteSpace(variable.Key) || variable.Value is null))
			{
				throw new JsonException("The saved host environment must contain non-null names and values.");
			}

			return new Dictionary<string, string>(environmentVariables, StringComparer.OrdinalIgnoreCase);
		}
		catch (Exception exception) when (exception is JsonException or IOException)
		{
			throw new InvalidOperationException(
				$"The saved Creatio host environment is invalid or cannot be read: {path}.", exception);
		}
	}

	private static string GetStorePath(string workingDirectory)
	{
		if (string.IsNullOrWhiteSpace(workingDirectory))
		{
			throw new ArgumentException("Working directory is required.", nameof(workingDirectory));
		}

		string normalizedDirectory = Path.GetFullPath(workingDirectory);
		string? pathRoot = Path.GetPathRoot(normalizedDirectory);
		if (!string.Equals(normalizedDirectory, pathRoot, StringComparison.Ordinal))
		{
			normalizedDirectory = normalizedDirectory.TrimEnd(
				Path.DirectorySeparatorChar,
				Path.AltDirectorySeparatorChar);
		}
		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedDirectory));
		string key = Convert.ToHexString(hash).ToLowerInvariant();
		return Path.Combine(ClioRuntimePaths.Home, StoreDirectoryName, key + StoreFileSuffix);
	}
}
