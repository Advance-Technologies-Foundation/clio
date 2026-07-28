using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using Newtonsoft.Json;

namespace Clio.UserEnvironment;

public interface ISettingsBootstrapService {
	SettingsBootstrapResult GetResult();
	SettingsBootstrapReport GetReport();
}

public sealed record SettingsIssue(
	string Code,
	string Message
);

public sealed record SettingsRepair(
	string Code,
	string Message
);

public sealed record SettingsBootstrapReport(
	string Status,
	string SettingsFilePath,
	string? ActiveEnvironmentKey,
	string? ResolvedActiveEnvironmentKey,
	int EnvironmentCount,
	IReadOnlyList<SettingsIssue> Issues,
	IReadOnlyList<SettingsRepair> RepairsApplied,
	bool CanStartBootstrapTools,
	bool CanExecuteEnvTools
);

public sealed record SettingsBootstrapResult(
	Settings Settings,
	EnvironmentSettings? ResolvedEnvironment,
	SettingsBootstrapReport Report
);

public sealed class SettingsBootstrapService : ISettingsBootstrapService {
	private const string SettingsFileUnreadableCode = "settings-file-unreadable";
	private const int CurrentSettingsVersion = 1;
	private readonly IFileSystem _fileSystem;
	private readonly bool _applyRepairs;

	public SettingsBootstrapService(IFileSystem? fileSystem = null, bool applyRepairs = true) {
		_fileSystem = fileSystem ?? SettingsRepository.FileSystem;
		_applyRepairs = applyRepairs;
	}

	public SettingsBootstrapResult GetResult() {
		return Load();
	}

	public SettingsBootstrapReport GetReport() {
		return GetResult().Report;
	}

	private SettingsBootstrapResult Load() {
		return SettingsRepository.ExecuteWithSettingsLock(_fileSystem, () => {
			for (int attempt = 0; attempt < 3; attempt++) {
				try {
					return LoadUnlocked();
				}
				catch (SettingsRepository.SettingsFileChangedException) {
					if (attempt == 2) {
						throw new IOException(
							"appsettings.json kept changing while clio was loading it. Try the command again.");
					}
					// Reload and reapply migrations if another writer won the atomic settings replacement.
				}
			}
			throw new InvalidOperationException("The settings load retry loop completed unexpectedly.");
		});
	}

	private SettingsBootstrapResult LoadUnlocked() {
		string settingsFilePath = SettingsRepository.AppSettingsFile;
		if (!_fileSystem.File.Exists(settingsFilePath)) {
			Settings emptySettings = new() { Environments = [], SettingsVersion = CurrentSettingsVersion };
			if (_applyRepairs) {
				SettingsRepository.SaveSettings(_fileSystem, emptySettings, expectedContent: null,
					verifyExpectedContent: true);
			}
			return BuildResult("healthy", settingsFilePath, null, emptySettings, [], []);
		}
		string fileContent;
		try {
			fileContent = _fileSystem.File.ReadAllText(settingsFilePath);
		}
		catch (Exception e) {
			return BuildBroken(settingsFilePath, SettingsFileUnreadableCode,
				$"Unable to read appsettings.json: {e.Message}");
		}
		if (string.IsNullOrWhiteSpace(fileContent)) {
			return BuildBroken(settingsFilePath, SettingsFileUnreadableCode,
				"appsettings.json is empty or whitespace and cannot be parsed.");
		}
		Settings? settingsModel;
		try {
			settingsModel = JsonConvert.DeserializeObject<Settings>(fileContent);
		}
		catch (Exception e) {
			return BuildBroken(settingsFilePath, SettingsFileUnreadableCode,
				$"appsettings.json could not be parsed: {e.Message}");
		}
		if (settingsModel is null) {
			return BuildBroken(settingsFilePath, SettingsFileUnreadableCode,
				"appsettings.json could not be deserialized into clio settings.");
		}
		string? originalActiveEnvironmentKey = settingsModel.ActiveEnvironmentKey;
		List<SettingsIssue> issues = [];
		List<SettingsRepair> repairs = [];
		if (settingsModel.Environments is null) {
			settingsModel.Environments = [];
		}
		if (settingsModel.Environments is not null && settingsModel.Environments.Count > 0
			&& (string.IsNullOrWhiteSpace(settingsModel.ActiveEnvironmentKey)
				|| !settingsModel.Environments.ContainsKey(settingsModel.ActiveEnvironmentKey))) {
			issues.Add(new SettingsIssue("invalid-active-environment",
				"ActiveEnvironmentKey is missing or does not point to a configured environment. " +
				"Use 'clio set-active-environment <name>' to fix this."));
		}
		SettingsRepository.AttachDbServers(settingsModel);
		if (_applyRepairs && ApplyMigrations(settingsModel, repairs)) {
			SettingsRepository.SaveSettings(_fileSystem, settingsModel, fileContent, verifyExpectedContent: true);
		}
		return BuildResult(
			issues.Count > 0 ? "issues-detected" : "healthy",
			settingsFilePath,
			originalActiveEnvironmentKey,
			settingsModel,
			issues,
			repairs);
	}

	/// <summary>
	/// Applies one-time settings migrations and returns true when the model was changed.
	/// </summary>
	private static bool ApplyMigrations(Settings settings, List<SettingsRepair> repairs) {
		if ((settings.SettingsVersion ?? 0) >= CurrentSettingsVersion) {
			return false;
		}
		// Migration 1: before #576 Autoupdate was a non-nullable bool, so the C# default
		// 'false' was serialized into appsettings.json for every user who never opted in,
		// silently disabling auto-update. Clear that legacy artifact so the opt-out default
		// (enabled) applies again. Guarded by SettingsVersion, this runs once — a deliberate
		// 'clio autoupdate --disable' made afterwards is preserved.
		if (settings.Autoupdate == false) {
			settings.Autoupdate = null;
			repairs.Add(new SettingsRepair(
				"autoupdate-legacy-default-reset",
				"auto-update was disabled by a legacy default and has been re-enabled "
				+ "(run 'clio autoupdate --disable' to opt out)"));
		}
		settings.SettingsVersion = CurrentSettingsVersion;
		return true;
	}

	private static SettingsBootstrapResult BuildBroken(string settingsFilePath, string code, string message) {
		Settings settings = new();
		return new SettingsBootstrapResult(
			settings,
			null,
			new SettingsBootstrapReport(
				"broken",
				settingsFilePath,
				null,
				null,
				0,
				[new SettingsIssue(code, message)],
				[],
				true,
				false));
	}

	private static SettingsBootstrapResult BuildResult(
		string status,
		string settingsFilePath,
		string? originalActiveEnvironmentKey,
		Settings settings,
		IReadOnlyList<SettingsIssue> issues,
		IReadOnlyList<SettingsRepair> repairs) {
		EnvironmentSettings? resolvedEnvironment = null;
		string? resolvedEnvironmentKey = null;
		if (settings.Environments is { Count: > 0 }
			&& !string.IsNullOrWhiteSpace(settings.ActiveEnvironmentKey)
			&& settings.Environments.TryGetValue(settings.ActiveEnvironmentKey, out EnvironmentSettings? environment)) {
			resolvedEnvironment = environment;
			resolvedEnvironmentKey = settings.ActiveEnvironmentKey;
		}
		return new SettingsBootstrapResult(
			settings,
			resolvedEnvironment,
			new SettingsBootstrapReport(
				status,
				settingsFilePath,
				originalActiveEnvironmentKey,
				resolvedEnvironmentKey,
				settings.Environments?.Count ?? 0,
				issues,
				repairs,
				true,
				resolvedEnvironment is not null));
	}
}
