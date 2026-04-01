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
	private readonly IFileSystem _fileSystem;
	private readonly bool _applyRepairs;
	private SettingsBootstrapResult? _result;

	public SettingsBootstrapService(IFileSystem? fileSystem = null, bool applyRepairs = true) {
		_fileSystem = fileSystem ?? SettingsRepository.FileSystem;
		_applyRepairs = applyRepairs;
	}

	public SettingsBootstrapResult GetResult() {
		return _result ??= Load();
	}

	public SettingsBootstrapReport GetReport() {
		return GetResult().Report;
	}

	private SettingsBootstrapResult Load() {
		string settingsFilePath = SettingsRepository.AppSettingsFile;
		if (!_fileSystem.File.Exists(settingsFilePath)) {
			Settings settings = SettingsRepository.CreateDefaultSettings();
			if (_applyRepairs) {
				SettingsRepository.SaveSettings(_fileSystem, settings);
			}
			return BuildResult(
				"healthy",
				settingsFilePath,
				settings.ActiveEnvironmentKey,
				settings,
				[],
				[]);
		}
		string fileContent;
		try {
			fileContent = _fileSystem.File.ReadAllText(settingsFilePath);
		}
		catch (Exception e) {
			return BuildBroken(settingsFilePath, "settings-file-unreadable",
				$"Unable to read appsettings.json: {e.Message}");
		}
		if (string.IsNullOrWhiteSpace(fileContent)) {
			return BuildBroken(settingsFilePath, "settings-file-unreadable",
				"appsettings.json is empty or whitespace and cannot be parsed.");
		}
		Settings? settingsModel;
		try {
			settingsModel = JsonConvert.DeserializeObject<Settings>(fileContent);
		}
		catch (Exception e) {
			return BuildBroken(settingsFilePath, "settings-file-unreadable",
				$"appsettings.json could not be parsed: {e.Message}");
		}
		if (settingsModel is null) {
			return BuildBroken(settingsFilePath, "settings-file-unreadable",
				"appsettings.json could not be deserialized into clio settings.");
		}
		string? originalActiveEnvironmentKey = settingsModel.ActiveEnvironmentKey;
		List<SettingsIssue> issues = [];
		List<SettingsRepair> repairs = [];
		bool changed = false;
		if (settingsModel.Environments is null || settingsModel.Environments.Count == 0) {
			issues.Add(new SettingsIssue("missing-environments",
				"Registered environments are missing, null, or empty."));
			repairs.Add(new SettingsRepair("create-default-environment",
				"Initialized the default 'dev' environment and selected it as active."));
			settingsModel = SettingsRepository.CreateDefaultSettings(settingsModel);
			changed = true;
		}
		if (settingsModel.Environments is not null && settingsModel.Environments.Count > 0
			&& (string.IsNullOrWhiteSpace(settingsModel.ActiveEnvironmentKey)
				|| !settingsModel.Environments.ContainsKey(settingsModel.ActiveEnvironmentKey))) {
			issues.Add(new SettingsIssue("invalid-active-environment",
				"ActiveEnvironmentKey is missing or does not point to a configured environment."));
			string resolvedEnvironmentKey = settingsModel.Environments.First().Key;
			repairs.Add(new SettingsRepair("set-active-environment",
				$"Selected '{resolvedEnvironmentKey}' as the active environment."));
			settingsModel.ActiveEnvironmentKey = resolvedEnvironmentKey;
			changed = true;
		}
		SettingsRepository.AttachDbServers(settingsModel);
		if (changed && _applyRepairs) {
			SettingsRepository.SaveSettings(_fileSystem, settingsModel);
		}
		return BuildResult(
			changed ? "repaired" : "healthy",
			settingsFilePath,
			originalActiveEnvironmentKey,
			settingsModel,
			issues,
			repairs);
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
