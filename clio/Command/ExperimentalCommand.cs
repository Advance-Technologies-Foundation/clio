using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Knowledge;
using Clio.Common;
using Clio.UserEnvironment;
using CommandLine;
using ConsoleTables;
using ModelContextProtocol.Server;

namespace Clio.Command;

/// <summary>
/// Options for the <c>experimental</c> command, which lists clio feature flags and toggles them on or off.
/// </summary>
/// <remarks>
/// This command is the always-available management surface for feature gates, so it is deliberately
/// NOT itself gated behind a <see cref="FeatureToggleAttribute"/>.
/// </remarks>
[Verb("experimental", Aliases = ["exp"], HelpText = "List and toggle clio experimental feature flags")]
public class ExperimentalOptions {

	/// <summary>
	/// Gets or sets the feature key to toggle. When omitted, the command lists all known feature flags.
	/// </summary>
	[Option("name", Required = false, HelpText = "The feature key to enable or disable. When omitted, all known feature flags are listed.")]
	public string Name { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the feature named by <see cref="Name"/> should be enabled.
	/// </summary>
	[Option("enable", SetName = "enable", HelpText = "Enable the feature flag named by --name.")]
	public bool Enable { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the feature named by <see cref="Name"/> should be disabled.
	/// </summary>
	[Option("disable", SetName = "disable", HelpText = "Disable the feature flag named by --name.")]
	public bool Disable { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether to explicitly list all known feature flags. Listing is
	/// also the default behavior when no toggle arguments are supplied.
	/// </summary>
	[Option("list", Required = false, HelpText = "List all known feature flags and their state (default when no other arguments are supplied).")]
	public bool List { get; set; }

}

/// <summary>
/// Lists clio feature flags and toggles them on or off, persisting changes to <c>appsettings.json</c>.
/// </summary>
/// <remarks>
/// Feature keys are decoupled from CLI verbs: a single key gates one or more commands (or MCP
/// tools/resources) via <see cref="FeatureToggleAttribute"/>. Keys are compared case-insensitively.
/// </remarks>
public class ExperimentalCommand : Command<ExperimentalOptions> {

	private readonly ISettingsRepository _settingsRepository;
	private readonly IFeatureToggleService _featureToggleService;
	private readonly ILogger _logger;

	/// <summary>
	/// Initializes a new instance of the <see cref="ExperimentalCommand"/> class.
	/// </summary>
	/// <param name="settingsRepository">The settings repository backing the feature flags.</param>
	/// <param name="featureToggleService">The service that resolves feature catalog and state.</param>
	/// <param name="logger">The logger used for all command output.</param>
	public ExperimentalCommand(ISettingsRepository settingsRepository, IFeatureToggleService featureToggleService, ILogger logger) {
		_settingsRepository = settingsRepository;
		_featureToggleService = featureToggleService;
		_logger = logger;
	}

	/// <inheritdoc/>
	public override int Execute(ExperimentalOptions options) {
		// --list always wins: when explicitly requested, list and return regardless of any other
		// arguments (including --name/--enable/--disable), so the flag is never silently ignored.
		if (options.List) {
			ListFeatures();
			return 0;
		}
		if (string.IsNullOrWhiteSpace(options.Name)) {
			if (options.Enable || options.Disable) {
				_logger.WriteError("--enable/--disable require a feature key. Use --name <key> --enable (or --disable).");
				return 1;
			}
			ListFeatures();
			return 0;
		}
		return Toggle(options);
	}

	private int Toggle(ExperimentalOptions options) {
		if (options.Enable == options.Disable) {
			_logger.WriteError("Specify exactly one of --enable or --disable when using --name.");
			return 1;
		}
		bool enable = options.Enable;
		string key = options.Name.Trim();
		bool keyIsKnown = GetKnownFeatureKeys().Contains(key, StringComparer.OrdinalIgnoreCase);
		_settingsRepository.SetFeature(key, enable);
		_logger.WriteInfo($"Feature '{key}' is now {(enable ? "ENABLED" : "DISABLED")}.");
		if (!keyIsKnown) {
			_logger.WriteWarning($"No command or MCP tool currently references the feature key '{key}'.");
		}
		// Some features carry a one-time notice shown only at the moment they are ENABLED (e.g. a Beta-mode
		// heads-up). Disabling never shows it, and it is keyed case-insensitively like every other key here.
		if (enable && FeatureEnableNotices.TryGetValue(key, out string enableNotice)) {
			_logger.WriteWarning(enableNotice);
		}
		return 0;
	}

	private void ListFeatures() {
		IReadOnlyList<FeatureToggleInfo> catalog = _featureToggleService.GetCatalog(GetGatedTypes());
		HashSet<string> catalogKeys = catalog
			.Select(info => info.FeatureName)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		List<(string Key, bool Enabled, bool Orphan)> rows = catalog
			.Select(info => (info.FeatureName, info.Enabled, Orphan: false))
			.ToList();

		// Standalone feature keys (gating a registration profile, not a single attributed type) are
		// recognized keys with no attribute carrier, so they are listed explicitly with their current
		// settings state rather than as orphans. catalogKeys.Add doubles as the filter: it adds the key
		// and yields it only when it was not already present, so each standalone key is listed at most
		// once. Where enumerates the source exactly once, preserving the original add-then-act order.
		foreach (string key in StandaloneFeatureKeys.Where(catalogKeys.Add)) {
			rows.Add((key, _featureToggleService.IsFeatureEnabled(key), Orphan: false));
		}

		// Settings keys that no attribute references are surfaced as orphans so a leftover/renamed
		// flag in appsettings.json is still visible and manageable.
		foreach (KeyValuePair<string, bool> feature in _settingsRepository.GetFeatures()) {
			if (!catalogKeys.Contains(feature.Key)) {
				rows.Add((feature.Key, feature.Value, Orphan: true));
			}
		}

		if (rows.Count == 0) {
			_logger.WriteInfo("No feature flags are defined.");
			return;
		}

		ConsoleTable table = new() {
			Columns = { "Feature", "State" },
		};
		foreach ((string key, bool enabled, bool orphan) in rows.OrderBy(row => row.Key, StringComparer.OrdinalIgnoreCase)) {
			string state = enabled ? "ENABLED" : "DISABLED";
			if (orphan) {
				state += " (orphan - no command uses this key)";
			}
			table.Rows.Add([key, state]);
		}
		_logger.PrintTable(table);
	}

	// Feature keys clio recognizes that are NOT derived from a [FeatureToggle] attribute on an
	// options/MCP type (registration-filter profiles, etc.), listed so `clio experimental` shows them
	// and `--enable/--disable` does not warn they are unknown. Compared case-insensitively.
	internal static readonly string[] StandaloneFeatureKeys = [
		// Gates a DI registration value (KnowledgeUnsequencedGitOptions), not an attributed type, so it
		// cannot be discovered through GetGatedTypes.
		KnowledgeUnsequencedGitOptions.FeatureName
	];

	// One-time warnings shown only when a feature is ENABLED (never on disable/list). Keyed
	// case-insensitively. The mobile-page-converter Beta heads-up is TEMPORARY — remove that entry when
	// the converter graduates out of Beta. Notice text is written verbatim to the console, the log file
	// and every additional sink, so it carries no markup - Markdown asterisks would show up literally.
	// Emphasize with UPPER CASE instead.
	internal static readonly IReadOnlyDictionary<string, string> FeatureEnableNotices =
		new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
			["mobile-page-converter"] =
				"⚠️ Heads up! Enabling this feature will activate the agent in BETA MODE. "
				+ "Please be aware that behavior may vary and improvements are ongoing.",
			[KnowledgeUnsequencedGitOptions.FeatureName] =
				"⚠️ Local development aid. While enabled, a Git knowledge source whose manifest omits "
				+ "\"sequence\" is loaded by deriving the sequence from libraryVersion, and such a bundle "
				+ "may replace the active generation with different content under the SAME sequence - the "
				+ "content-integrity check that detects a swapped guidance corpus does not apply to it. "
				+ "Rollbacks to a lower sequence are still refused. The flag is persistent and affects "
				+ "every later clio run against every environment; disable it when you are done.",
		};

	private static IEnumerable<string> GetKnownFeatureKeys() =>
		GetGatedTypes()
			.Select(type => type.GetCustomAttribute<FeatureToggleAttribute>(inherit: false))
			.Where(attribute => attribute is not null)
			.Select(attribute => attribute.FeatureName)
			.Concat(StandaloneFeatureKeys)
			.Distinct(StringComparer.OrdinalIgnoreCase);

	// Sources every feature-gated type clio knows about: command option types from the CLI verb set
	// PLUS MCP tool/resource/prompt types (so MCP-only gated features are listed too).
	private static IEnumerable<Type> GetGatedTypes() =>
		Program.GetCommandOptionTypes()
			.Concat(GetMcpDiscoveredTypes())
			.Distinct()
			.Where(type => type.GetCustomAttribute<FeatureToggleAttribute>(inherit: false) is not null);

	// MCP type discovery is single-sourced in McpFeatureToggleFilter so this listing path enumerates
	// exactly the same tool/resource/prompt set the BindingsModule registration path gates. All THREE
	// markers are scanned with inherit:true to match the registration filter, otherwise a prompt-only
	// (or inherited-marker) gated feature would be gated at registration yet invisible here.
	private static IEnumerable<Type> GetMcpDiscoveredTypes() {
		Assembly mcpAssembly = Assembly.GetExecutingAssembly();
		return McpFeatureToggleFilter.GetAttributedTypes(mcpAssembly, typeof(McpServerToolTypeAttribute))
			.Concat(McpFeatureToggleFilter.GetAttributedTypes(mcpAssembly, typeof(McpServerResourceTypeAttribute)))
			.Concat(McpFeatureToggleFilter.GetAttributedTypes(mcpAssembly, typeof(McpServerPromptTypeAttribute)))
			.Distinct();
	}

}
