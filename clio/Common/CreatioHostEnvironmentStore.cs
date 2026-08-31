using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
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

		HashSet<string> variableNames = new(StringComparer.OrdinalIgnoreCase);
		if (environmentVariables.Any(variable => !IsCertificatePasswordEnvironmentVariable(variable.Key)
			|| variable.Value is null
			|| !variableNames.Add(variable.Key)))
		{
			throw new ArgumentException(
				"Only unique Kestrel certificate password environment variables can be persisted.",
				nameof(environmentVariables));
		}

		string directory = Path.GetDirectoryName(path)
			?? throw new InvalidOperationException("The host environment store directory could not be resolved.");
		EnsureNotSymbolicLink(ClioRuntimePaths.Home, isDirectory: true);
		EnsureNotSymbolicLink(directory, isDirectory: true);
		_fileSystem.CreateDirectoryIfNotExists(directory);
		EnsureNotSymbolicLink(directory, isDirectory: true);
		_fileSecurityHardening.HardenDirectory(directory);
		EnsureNotSymbolicLink(path, isDirectory: false);
		string json = JsonSerializer.Serialize(environmentVariables, JsonOptions);
		try
		{
			_fileSystem.WriteOwnerOnlyTextToFile(path, json);
			_fileSecurityHardening.HardenFile(path);
		}
		catch
		{
			// Do not leave a secret-bearing file behind when its ownership boundary could not be
			// established. The next deployment/start will fail closed rather than use an unprotected value.
			_fileSystem.DeleteFileIfExists(path);
			throw;
		}
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
			EnsureNotSymbolicLink(ClioRuntimePaths.Home, isDirectory: true);
			string storeDirectory = Path.GetDirectoryName(path)
				?? throw new InvalidOperationException("The host environment store directory could not be resolved.");
			EnsureNotSymbolicLink(storeDirectory, isDirectory: true);
			EnsureNotSymbolicLink(path, isDirectory: false);
			Dictionary<string, string>? environmentVariables =
				JsonSerializer.Deserialize<Dictionary<string, string>>(_fileSystem.ReadAllText(path));
			if (environmentVariables is null
				|| environmentVariables.Any(variable => !IsCertificatePasswordEnvironmentVariable(variable.Key)
					|| variable.Value is null))
			{
				throw new JsonException(
					"The saved host environment must contain only Kestrel certificate password values.");
			}

			return new Dictionary<string, string>(environmentVariables, StringComparer.OrdinalIgnoreCase);
		}
		catch (Exception exception) when (exception is ArgumentException or JsonException or IOException)
		{
			throw new InvalidOperationException(
				$"The saved Creatio host environment is invalid or cannot be read: {path}.", exception);
		}
	}

	private void EnsureNotSymbolicLink(string path, bool isDirectory)
	{
		IFileSystemInfo fileSystemInfo = isDirectory
			? _fileSystem.GetDirectoryInfo(path)
			: _fileSystem.GetFilesInfos(path);
		if (fileSystemInfo is not null
			&& (!string.IsNullOrEmpty(fileSystemInfo.LinkTarget)
				|| fileSystemInfo.Attributes.HasFlag(FileAttributes.ReparsePoint)))
		{
			throw new IOException($"The host environment store path must not be a symbolic link: {path}.");
		}
	}

	private static bool IsCertificatePasswordEnvironmentVariable(string name)
	{
		if (!IsValidEnvironmentVariableName(name))
		{
			return false;
		}

		string[] segments = name.Split("__", StringSplitOptions.None);
		return (segments.Length == 5
				&& string.Equals(segments[0], "Kestrel", StringComparison.OrdinalIgnoreCase)
				&& string.Equals(segments[1], "Endpoints", StringComparison.OrdinalIgnoreCase)
				&& !string.IsNullOrEmpty(segments[2])
				&& string.Equals(segments[3], "Certificate", StringComparison.OrdinalIgnoreCase)
				&& string.Equals(segments[4], "Password", StringComparison.OrdinalIgnoreCase))
			|| (segments.Length == 4
				&& string.Equals(segments[0], "Kestrel", StringComparison.OrdinalIgnoreCase)
				&& string.Equals(segments[1], "Certificates", StringComparison.OrdinalIgnoreCase)
				&& !string.IsNullOrEmpty(segments[2])
				&& string.Equals(segments[3], "Password", StringComparison.OrdinalIgnoreCase));
	}

	private static bool IsValidEnvironmentVariableName(string value)
	{
		if (string.IsNullOrEmpty(value)
			|| !(value[0] == '_' || value[0] is >= 'A' and <= 'Z' or >= 'a' and <= 'z'))
		{
			return false;
		}

		for (int index = 1; index < value.Length; index++)
		{
			char character = value[index];
			if (!(character == '_' || character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9'))
			{
				return false;
			}
		}

		return true;
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
