using System;
using System.Collections.Generic;
using System.Text.Json;
using Clio.UserEnvironment;

namespace Clio.Common;

/// <summary>
/// Remembers which app version clio last installed, so <c>clio info</c> can name it without a feed call and
/// without an environment.
/// </summary>
/// <remarks>
/// Needed because neither existing reader answers this question: <c>IBundledPackageCatalog</c> reads an
/// archive clio ships (there is none for a fetched app), and <c>IInstalledToolkitVersions</c> reads metadata
/// an agent wrote on this machine. What clio last pushed to an environment is clio's own fact, so clio keeps
/// it, next to its settings file.
/// </remarks>
public interface IInstalledAppVersions {

	/// <summary>Reads the recorded versions, keyed by app name. Empty when nothing was ever installed.</summary>
	IReadOnlyDictionary<string, string> Read();

	/// <summary>Records the version clio has just installed for an app.</summary>
	void Record(string appName, string version);

}

/// <inheritdoc />
public sealed class InstalledAppVersions : IInstalledAppVersions {

	#region Constants: Private

	private const string StoreFileName = "installed-apps.json";

	#endregion

	#region Fields: Private

	private readonly IFileSystem _fileSystem;
	private readonly ISettingsRepository _settingsRepository;

	#endregion

	#region Constructors: Public

	public InstalledAppVersions(IFileSystem fileSystem, ISettingsRepository settingsRepository) {
		fileSystem.CheckArgumentNull(nameof(fileSystem));
		settingsRepository.CheckArgumentNull(nameof(settingsRepository));
		_fileSystem = fileSystem;
		_settingsRepository = settingsRepository;
	}

	#endregion

	#region Properties: Private

	private string StorePath => _fileSystem.Combine(
		System.IO.Path.GetDirectoryName(_settingsRepository.AppSettingsFilePath), StoreFileName);

	#endregion

	#region Methods: Public

	/// <inheritdoc />
	public IReadOnlyDictionary<string, string> Read() {
		try {
			if (!_fileSystem.ExistsFile(StorePath)) {
				return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			}
			Dictionary<string, string> stored = JsonSerializer
				.Deserialize<Dictionary<string, string>>(_fileSystem.ReadAllText(StorePath));
			return stored is null
				? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
				: new Dictionary<string, string>(stored, StringComparer.OrdinalIgnoreCase);
		} catch (Exception exception) when (exception is JsonException or SystemException) {
			// An unreadable record must never stop clio from reporting every other version it knows.
			return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		}
	}

	/// <inheritdoc />
	public void Record(string appName, string version) {
		appName.CheckArgumentNullOrWhiteSpace(nameof(appName));
		version.CheckArgumentNullOrWhiteSpace(nameof(version));
		Dictionary<string, string> versions = new(Read(), StringComparer.OrdinalIgnoreCase) {
			[appName] = version
		};
		_fileSystem.WriteAllTextToFile(StorePath,
			JsonSerializer.Serialize(versions, new JsonSerializerOptions { WriteIndented = true }));
	}

	#endregion

}
