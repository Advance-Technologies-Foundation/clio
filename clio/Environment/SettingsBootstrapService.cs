using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Clio.UserEnvironment;

public interface ISettingsBootstrapService {
	SettingsBootstrapResult GetResult();
	SettingsBootstrapReport GetReport();

	/// <summary>
	/// Reads the settings file WITHOUT applying any repair or migration write.
	/// </summary>
	/// <remarks>
	/// <see cref="GetResult"/> may write the file back (a pending migration, or a missing file that is
	/// created with defaults). That is correct for a command that is starting up, and wrong for a
	/// read-only path such as <see cref="ISettingsRepository.Reload"/>, which a <c>ReadOnly</c> MCP tool
	/// calls on every invocation. This overload never writes.
	/// </remarks>
	/// <returns>
	/// The settings as they are on disk, with the same report and broken-file semantics. A missing file
	/// is reported as status <c>file-missing</c> instead of the repairing path's <c>healthy</c>, so a
	/// caller can tell "no configuration file" from "a file with no environments".
	/// </returns>
	SettingsBootstrapResult GetResultWithoutRepairs();
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
	private const string SettingsFileMissingCode = "settings-file-missing";

	/// <summary>
	/// Issue code for a settings file that is valid JSON but whose shape this clio build cannot bind.
	/// </summary>
	/// <remarks>
	/// Distinct from <c>settings-file-unreadable</c> on purpose: the file is intact, so every message built
	/// from this code must send the reader to restart the resident process, never to edit the file.
	/// </remarks>
	internal const string SettingsShapeMismatchCode = "settings-shape-mismatch";
	private const int CurrentSettingsVersion = 3;

	/// <summary>
	/// Status reported when appsettings.json does not exist and the caller asked for no repair write.
	/// </summary>
	/// <remarks>
	/// The repairing path creates the file and is genuinely "healthy" afterwards. A read-only caller
	/// gets no file created, so reporting "healthy" with zero environments would look like a real,
	/// empty configuration — and a caller that already holds settings would replace them with nothing.
	/// </remarks>
	internal const string FileMissingStatus = "file-missing";
	private readonly IFileSystem _fileSystem;
	private readonly bool _applyRepairs;

	public SettingsBootstrapService(IFileSystem? fileSystem = null, bool applyRepairs = true) {
		_fileSystem = fileSystem ?? SettingsRepository.FileSystem;
		_applyRepairs = applyRepairs;
	}

	public SettingsBootstrapResult GetResult() {
		return Load(_applyRepairs);
	}

	public SettingsBootstrapReport GetReport() {
		return GetResult().Report;
	}

	public SettingsBootstrapResult GetResultWithoutRepairs() {
		return Load(applyRepairs: false);
	}

	private SettingsBootstrapResult Load(bool applyRepairs) {
		return SettingsRepository.ExecuteWithSettingsLock(_fileSystem, () => {
			for (int attempt = 0; attempt < 3; attempt++) {
				try {
					return LoadUnlocked(applyRepairs);
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

	private SettingsBootstrapResult LoadUnlocked(bool applyRepairs) {
		string settingsFilePath = SettingsRepository.AppSettingsFile;
		if (!_fileSystem.File.Exists(settingsFilePath)) {
			Settings emptySettings = new() {
				Environments = [],
				SettingsVersion = CurrentSettingsVersion,
				DeployCreatioDefaults = DeployCreatioDefaults.CreateWithDefaultSitePortRange()
			};
			if (applyRepairs) {
				SettingsRepository.SaveSettings(_fileSystem, emptySettings, expectedContent: null,
					verifyExpectedContent: true);
				return BuildResult("healthy", settingsFilePath, null, emptySettings, [], []);
			}
			return BuildResult(FileMissingStatus, settingsFilePath, null, emptySettings,
				[new SettingsIssue(SettingsFileMissingCode, "appsettings.json does not exist.")], []);
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
		// Distinguishing a damaged file from a future-shaped one has to happen BEFORE binding: a section
		// whose value changed shape (a scalar that became an object) surfaces as the very same
		// JsonReaderException type and text as a syntax error, so the exception cannot tell them apart.
		// A successful JToken.Parse can: it proves the bytes are well-formed JSON.
		bool isWellFormedJson;
		try {
			JToken.Parse(fileContent);
			isWellFormedJson = true;
		}
		catch (JsonException) {
			isWellFormedJson = false;
		}
		if (!isWellFormedJson) {
			try {
				JsonConvert.DeserializeObject<Settings>(fileContent);
			}
			catch (Exception e) {
				return BuildBroken(settingsFilePath, SettingsFileUnreadableCode,
					$"appsettings.json could not be parsed: {e.Message}");
			}
		}
		Settings? settingsModel;
		List<string> bindFailurePaths = [];
		try {
			settingsModel = JsonConvert.DeserializeObject<Settings>(fileContent,
				CreateTolerantSerializerSettings(bindFailurePaths));
		}
		catch (Exception e) {
			return BuildBroken(settingsFilePath, SettingsFileUnreadableCode,
				$"appsettings.json could not be parsed: {e.Message}");
		}
		if (settingsModel is null) {
			return BuildBroken(settingsFilePath, SettingsFileUnreadableCode,
				"appsettings.json could not be deserialized into clio settings.");
		}
		string? shapeMismatchMessage = bindFailurePaths.Count > 0
			? BuildShapeMismatchMessage(bindFailurePaths)
			: null;
		// Only a root object or an unbindable Environments collection is fatal: without the environment list
		// there is nothing an environment-scoped tool could run against. Any other unbindable section leaves
		// the environments intact, so the caller keeps working and only the diagnostic changes.
		if (shapeMismatchMessage is not null && EnvironmentsAreUnbindable(bindFailurePaths)) {
			return BuildBroken(settingsFilePath, SettingsShapeMismatchCode, shapeMismatchMessage);
		}
		string? originalActiveEnvironmentKey = settingsModel.ActiveEnvironmentKey;
		List<SettingsIssue> issues = [];
		if (shapeMismatchMessage is not null) {
			issues.Add(new SettingsIssue(SettingsShapeMismatchCode, shapeMismatchMessage));
		}
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
		// NEVER write the file back while a section could not be bound. Serializing the model this build
		// produced would drop the section a newer clio wrote - the user's settings would be destroyed by a
		// migration that only meant to stamp a version number.
		if (applyRepairs && shapeMismatchMessage is null && ApplyMigrations(settingsModel, repairs)) {
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
	/// Builds serializer settings that record member-level bind failures instead of aborting the load.
	/// </summary>
	/// <param name="bindFailurePaths">Collects the JSON path of every handled bind failure, in order.</param>
	private static JsonSerializerSettings CreateTolerantSerializerSettings(List<string> bindFailurePaths) {
		return new JsonSerializerSettings {
			Error = (_, args) => {
				string path = args.ErrorContext.Path ?? string.Empty;
				// The event fires once per nesting level while the error is unhandled, so the innermost
				// (most specific) path arrives first and the outer repeats add nothing.
				if (!bindFailurePaths.Contains(path)) {
					bindFailurePaths.Add(path);
				}
				args.ErrorContext.Handled = true;
			}
		};
	}

	/// <summary>
	/// True when the environment collection itself, rather than some unrelated section, failed to bind.
	/// </summary>
	private static bool EnvironmentsAreUnbindable(IEnumerable<string> bindFailurePaths) {
		return bindFailurePaths.Any(path =>
			string.Equals(path, nameof(Settings.Environments), StringComparison.OrdinalIgnoreCase));
	}

	private static string BuildShapeMismatchMessage(IReadOnlyList<string> bindFailurePaths) {
		string sections = string.Join(", ", bindFailurePaths.Where(path => !string.IsNullOrEmpty(path)));
		if (string.IsNullOrEmpty(sections)) {
			sections = "(root)";
		}
		return $"appsettings.json is valid JSON, but this clio version ({CurrentClioVersion}) cannot bind "
			+ $"the following section(s): {sections}. A newer clio has most likely written the file. "
			+ "Do NOT edit the file - restart the resident process (the MCP session) so it runs the new "
			+ "clio build. The settings themselves are intact and are left untouched.";
	}

	private static string CurrentClioVersion =>
		typeof(SettingsBootstrapService).Assembly.GetName().Version?.ToString() ?? "unknown";

	/// <summary>
	/// Applies one-time settings migrations and returns true when the model was changed.
	/// </summary>
	private static bool ApplyMigrations(Settings settings, List<SettingsRepair> repairs) {
		if ((settings.SettingsVersion ?? 0) >= CurrentSettingsVersion) {
			return false;
		}
		// Preserve legacy auto-update choices now that clio updates are opt-in.
		// Migration 2 makes automatic IIS port selection visible and immediately usable after an upgrade.
		// Preserve a user-configured range; only materialize the built-in range when the setting was absent.
		if ((settings.SettingsVersion ?? 0) < 2
			&& settings.DeployCreatioDefaults?.SitePortRange is null) {
			settings.DeployCreatioDefaults ??= new DeployCreatioDefaults();
			settings.DeployCreatioDefaults.SitePortRange = [
				DeployCreatioDefaults.DefaultSitePortRangeStart,
				DeployCreatioDefaults.DefaultSitePortRangeEnd
			];
			repairs.Add(new SettingsRepair(
				"deploy-creatio-site-port-range-added",
				$"deploy-creatio default site-port-range was set to "
				+ $"[{DeployCreatioDefaults.DefaultSitePortRangeStart}, {DeployCreatioDefaults.DefaultSitePortRangeEnd}]"));
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
