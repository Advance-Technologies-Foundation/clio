using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
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

/// <summary>
/// Thrown when a settings write is refused because a member of the file could not be bound.
/// </summary>
/// <remarks>
/// A distinct type, and not a bare <see cref="InvalidOperationException"/>, because the refusal is
/// EXPECTED and self-inflicted: every write path is disabled on purpose while the file holds something
/// this build cannot represent. Callers that treat an update failure as best effort (the startup
/// automatic-update check) need to report this one rather than swallow it, since the refusal is exactly
/// what stops clio from updating its way out of the skew. Derives from
/// <see cref="InvalidOperationException"/> so existing handlers keep working.
/// </remarks>
public sealed class SettingsShapeMismatchException : InvalidOperationException {

	/// <summary>Initializes the exception with the caller-facing refusal text.</summary>
	/// <param name="message">The refusal text, naming the member and the way out.</param>
	public SettingsShapeMismatchException(string message) : base(message) { }
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
) {

	/// <summary>
	/// The shape-mismatch issue this report carries, or <see langword="null"/> when it carries none.
	/// </summary>
	/// <remarks>
	/// One member rather than the same <c>Issues.FirstOrDefault(code == …)</c> scan at four call sites.
	/// The sites are the two caller-facing messages, the settings-write refusal and the bootstrap's own
	/// decision not to save; they must agree, and a copied predicate is how they stop agreeing. Note that
	/// this is NOT keyed on <see cref="Status"/>: a mismatch is reported both as <c>issues-detected</c>
	/// (degraded, environments usable) and as <c>broken</c> (the environment collection itself).
	/// </remarks>
	public SettingsIssue? ShapeMismatch => Issues?.FirstOrDefault(issue =>
		string.Equals(issue.Code, SettingsBootstrapService.SettingsShapeMismatchCode, StringComparison.Ordinal));
}

public sealed record SettingsBootstrapResult(
	Settings Settings,
	EnvironmentSettings? ResolvedEnvironment,
	SettingsBootstrapReport Report
);

public sealed class SettingsBootstrapService : ISettingsBootstrapService {
	/// <summary>Issue code for a settings file that cannot be read or is not valid JSON.</summary>
	internal const string SettingsFileUnreadableCode = "settings-file-unreadable";
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
		// Distinguishing a damaged file from one this build cannot bind has to happen BEFORE binding: a
		// member whose value changed shape (a scalar that became an object) surfaces as the very same
		// JsonReaderException type and text as a syntax error, so the exception cannot tell them apart.
		// A successful JToken.Parse can: it proves the bytes are well-formed JSON.
		try {
			JToken.Parse(fileContent);
		}
		catch (JsonException e) {
			return BuildBroken(settingsFilePath, SettingsFileUnreadableCode,
				$"appsettings.json could not be parsed: {e.Message}");
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
			// A handled member error can still leave the reader unable to finish the root object, so the
			// whole deserialization comes back null. The recorded paths are the honest explanation - "could
			// not be deserialized" would send the reader to look for damage that is not there.
			if (bindFailurePaths.Count > 0) {
				string? environmentPath = FindUnbindableEnvironmentPath(bindFailurePaths);
				return BuildBroken(settingsFilePath, SettingsShapeMismatchCode,
					environmentPath is not null
						? BuildEnvironmentBindFailureMessage(environmentPath)
						: BuildShapeMismatchMessage(bindFailurePaths, new Settings()));
			}
			return BuildBroken(settingsFilePath, SettingsFileUnreadableCode,
				"appsettings.json could not be deserialized into clio settings.");
		}
		// ANY failure under Environments is fatal, not only one on the collection itself. A dropped member
		// is invisible and its default is the DANGEROUS answer: an environment whose "Safe" flag failed to
		// bind comes back Safe = false, and destructive commands would then run against a production stand
		// with no confirmation. A dropped dictionary ENTRY is worse still - the environment simply is not
		// there, and "not registered" reads as a user mistake. Neither may degrade into a warning.
		string? unbindableEnvironmentPath = FindUnbindableEnvironmentPath(bindFailurePaths);
		if (unbindableEnvironmentPath is not null) {
			return BuildBroken(settingsFilePath, SettingsShapeMismatchCode,
				BuildEnvironmentBindFailureMessage(unbindableEnvironmentPath));
		}
		string? shapeMismatchMessage = bindFailurePaths.Count > 0
			? BuildShapeMismatchMessage(bindFailurePaths, settingsModel)
			: null;
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
				// Marking the error handled at this - the innermost - level stops it propagating, so the
				// outer levels never raise it again. The set check is only here so that a member reported
				// twice (a collection whose entries all fail the same way) is named once.
				if (!bindFailurePaths.Contains(path)) {
					bindFailurePaths.Add(path);
				}
				args.ErrorContext.Handled = true;
			}
		};
	}

	/// <summary>
	/// Returns the first bind-failure path that touches the environment collection, or <c>null</c>.
	/// </summary>
	private static string? FindUnbindableEnvironmentPath(IEnumerable<string> bindFailurePaths) {
		const string environments = nameof(Settings.Environments);
		return bindFailurePaths.FirstOrDefault(path =>
			string.Equals(path, environments, StringComparison.OrdinalIgnoreCase)
			|| path.StartsWith(environments + ".", StringComparison.OrdinalIgnoreCase)
			|| path.StartsWith(environments + "[", StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// Builds the message for a bind failure inside the environment collection.
	/// </summary>
	/// <remarks>
	/// Deliberately NOT the version-skew wording: an environment entry is hand-edited far more often than
	/// it is rewritten by a newer clio, and every member of it that silently takes its default changes what
	/// a command will do. So this message names the member and asks for it to be corrected.
	/// </remarks>
	private static string BuildEnvironmentBindFailureMessage(string bindFailurePath) {
		string? environmentKey = ExtractEnvironmentKey(bindFailurePath);
		string subject = environmentKey is null
			? "the Environments section"
			: $"environment '{environmentKey}'";
		return $"appsettings.json is valid JSON, but clio cannot bind {subject}: the member "
			+ $"'{bindFailurePath}' has a value of the wrong shape. Environments are not loaded at all "
			+ "while this is true, because a member that silently took its default value would change what "
			+ "commands do (a Safe environment would stop asking for confirmation). Correct "
			+ $"'{bindFailurePath}' by hand, or re-register the environment with 'clio reg-web-app'.";
	}

	private static string? ExtractEnvironmentKey(string bindFailurePath) {
		string prefix = nameof(Settings.Environments) + ".";
		if (!bindFailurePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
			return null;
		}
		string remainder = bindFailurePath[prefix.Length..];
		int separator = remainder.IndexOf('.');
		string key = separator < 0 ? remainder : remainder[..separator];
		return string.IsNullOrEmpty(key) ? null : key;
	}

	/// <summary>
	/// Builds the message for a bind failure outside the environment collection, choosing between the
	/// "a newer clio wrote this" and the "correct it by hand" wording on evidence rather than on assumption.
	/// </summary>
	/// <remarks>
	/// The version-skew wording tells the reader NOT to edit a file, and that advice is actively harmful
	/// when the value really is a typo: it leaves them waiting for a restart that fixes nothing. Evidence of
	/// a newer writer is a settings version this build does not know, or unknown members carried in the same
	/// section as the failure.
	/// </remarks>
	private static string BuildShapeMismatchMessage(IReadOnlyList<string> bindFailurePaths, Settings settings) {
		string sections = string.Join(", ", bindFailurePaths.Where(path => !string.IsNullOrEmpty(path)));
		if (string.IsNullOrEmpty(sections)) {
			sections = "(root)";
		}
		string preamble = $"appsettings.json is valid JSON, but this clio version "
			+ $"({Clio.Common.ClioAssemblyVersion.Current}) cannot bind the following member(s): {sections}. ";
		if (WasProbablyWrittenByANewerClio(bindFailurePaths, settings)) {
			return preamble
				+ "A newer clio has written the file. Do NOT edit it - restart the resident process (the "
				+ "MCP session) so it runs the new clio build, or update this one with 'clio update-cli' "
				+ "when there is no session to restart. The settings themselves are intact and are left "
				+ "untouched.";
		}
		return preamble
			+ $"Correct {sections} by hand. clio will not rewrite the file while a member fails to bind, "
			+ "so nothing else in it is at risk - but every setting under that member is taking its "
			+ "default value until it is fixed.";
	}

	/// <summary>
	/// Reports whether the file carries evidence that a NEWER clio wrote it.
	/// </summary>
	private static bool WasProbablyWrittenByANewerClio(IReadOnlyList<string> bindFailurePaths, Settings settings) {
		if ((settings.SettingsVersion ?? 0) > CurrentSettingsVersion) {
			return true;
		}
		IReadOnlyCollection<string> unknownMemberSections = CollectSectionsCarryingUnknownMembers(settings);
		return bindFailurePaths.Any(path =>
			unknownMemberSections.Contains(TopLevelSection(path), StringComparer.OrdinalIgnoreCase));
	}

	/// <summary>
	/// Names the top-level sections whose objects carried members this build does not know.
	/// </summary>
	private static IReadOnlyCollection<string> CollectSectionsCarryingUnknownMembers(Settings settings) {
		HashSet<string> sections = new(StringComparer.OrdinalIgnoreCase);
		if (settings.AdditionalData is { Count: > 0 }) {
			sections.Add(string.Empty);
		}
		Clio.Common.AutoUpdateSettings? autoupdate = settings.Autoupdate;
		bool autoupdateCarriesUnknown = autoupdate?.AdditionalData is { Count: > 0 }
			|| autoupdate?.Clio?.AdditionalData is { Count: > 0 }
			|| autoupdate?.Knowledge?.AdditionalData is { Count: > 0 }
			|| autoupdate?.Toolkit?.AdditionalData is { Count: > 0 };
		if (autoupdateCarriesUnknown) {
			sections.Add("autoupdate");
		}
		return sections;
	}

	private static string TopLevelSection(string path) {
		int separator = path.IndexOf('.');
		return separator < 0 ? path : path[..separator];
	}

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
