using System;
using System.Linq;
using Clio.Command.CreatioInstallCommand;
using Clio.Common;
using Clio.UserEnvironment;
using Clio.YAML;
using CommandLine;

namespace Clio.Command;

/// <summary>Options for executing a YAML scenario.</summary>
[Verb("run", Aliases = ["scenario", "run-scenario"],
	HelpText = "Run a YAML scenario and refresh environments between dependent steps")]
public class ScenarioRunnerOptions : EnvironmentOptions {
	internal override bool RequiredEnvironment => false;

	/// <summary>Gets or sets the scenario file path.</summary>
	[Option("file-name", Required = true, HelpText = "Scenario file name")]
	public string FileName { get; set; }
}

/// <summary>Executes the commands declared in a YAML scenario.</summary>
public class ScenarioRunnerCommand : Command<ScenarioRunnerOptions> {
	private const string SettingsReloadFailedMessage =
		"Scenario cannot refresh clio settings before an environment-dependent step.";
	private const string AmbiguousEnvironmentTargetMessage =
		"A scenario step cannot combine a named environment with a direct application or authentication URI.";
	private const string IncompleteDirectTargetMessage =
		"A direct authentication URI requires a direct application URI in the same scenario step.";

	private readonly ILogger _logger;
	private readonly IScenario _scenario;
	private readonly ISettingsRepository _settingsRepository;

	/// <summary>Initializes a new instance of the <see cref="ScenarioRunnerCommand"/> class.</summary>
	/// <param name="scenario">Scenario parser and step provider.</param>
	/// <param name="logger">Command logger.</param>
	/// <param name="settingsRepository">Repository used to resolve current environments.</param>
	public ScenarioRunnerCommand(IScenario scenario, ILogger logger, ISettingsRepository settingsRepository) {
		_scenario = scenario;
		_logger = logger;
		_settingsRepository = settingsRepository;
	}

	/// <inheritdoc />
	public override int Execute(ScenarioRunnerOptions options) {
		int result = 0;
		_logger.WriteInfo($"[{DateTime.Now:hh:mm:ss}] Scenario started");

		var steps = _scenario
			.InitScript(options.FileName)
			.GetSteps(GetType().Assembly.GetTypes())
			.ToList();
		foreach ((object commandOption, string stepDescription) in steps) {
			_logger.WriteInfo($"[{DateTime.Now:hh:mm:ss}] Starting step: {stepDescription}");
			if (commandOption is EnvironmentOptions stepOptions
				&& stepOptions is not RegAppOptions and not PfInstallerOptions
				&& !TryConfigureEnvironmentStep(stepOptions, options)) {
				result += 1;
				_logger.WriteInfo($"[{DateTime.Now:hh:mm:ss}] Finished step: {stepDescription}");
				_logger.WriteLine();
				continue;
			}

			result += Program.ExecuteCommandWithOption(commandOption);
			_logger.WriteInfo($"[{DateTime.Now:hh:mm:ss}] Finished step: {stepDescription}");
			_logger.WriteLine();
		}
		_logger.WriteInfo($"[{DateTime.Now:hh:mm:ss}] Scenario finished");
		return result >= 1 ? 1 : 0;
	}

	private bool TryConfigureEnvironmentStep(EnvironmentOptions stepOptions, ScenarioRunnerOptions scenarioOptions) {
		ApplyScenarioEnvironmentDefault(stepOptions, scenarioOptions);
		bool hasNamedEnvironment = !string.IsNullOrWhiteSpace(stepOptions.Environment);
		bool hasDirectUri = !string.IsNullOrWhiteSpace(stepOptions.Uri);
		bool hasDirectAuthAppUri = !string.IsNullOrWhiteSpace(stepOptions.AuthAppUri);
		if (!ValidateTargetIdentity(hasNamedEnvironment, hasDirectUri, hasDirectAuthAppUri)
			|| !TryResolveBaseSettings(stepOptions, hasNamedEnvironment, hasDirectUri,
				out EnvironmentSettings baseSettings)) {
			return false;
		}

		EnvironmentSettings resolvedSettings;
		try {
			resolvedSettings = baseSettings.Fill(stepOptions, NonInteractiveConsole.Shared);
		}
		catch (SafeEnvironmentConfirmationRequiredException exception) {
			_logger.WriteError(exception.Message);
			return false;
		}
		resolvedSettings.EnvironmentPath = string.IsNullOrWhiteSpace(stepOptions.EnvironmentPath)
			? baseSettings.EnvironmentPath
			: stepOptions.EnvironmentPath;
		IServiceProvider container = new BindingsModule().Register(
			resolvedSettings,
			NonInteractiveConsole.ForceInContainer);
		Program.Container = container;
		return true;
	}

	private static void ApplyScenarioEnvironmentDefault(EnvironmentOptions stepOptions,
		ScenarioRunnerOptions scenarioOptions) {
		if (string.IsNullOrWhiteSpace(stepOptions.Environment)
			&& string.IsNullOrWhiteSpace(stepOptions.Uri)
			&& string.IsNullOrWhiteSpace(stepOptions.AuthAppUri)
			&& !string.IsNullOrWhiteSpace(scenarioOptions.Environment)) {
			stepOptions.Environment = scenarioOptions.Environment;
		}
	}

	private bool ValidateTargetIdentity(bool hasNamedEnvironment, bool hasDirectUri,
		bool hasDirectAuthAppUri) {
		if (hasNamedEnvironment && (hasDirectUri || hasDirectAuthAppUri)) {
			_logger.WriteError(AmbiguousEnvironmentTargetMessage);
			return false;
		}
		if (!hasDirectUri && hasDirectAuthAppUri) {
			_logger.WriteError(IncompleteDirectTargetMessage);
			return false;
		}
		return true;
	}

	private bool TryResolveBaseSettings(EnvironmentOptions stepOptions, bool hasNamedEnvironment,
		bool hasDirectUri, out EnvironmentSettings baseSettings) {
		if (!hasNamedEnvironment && hasDirectUri) {
			baseSettings = new EnvironmentSettings();
			return true;
		}
		if (!TryReloadSettings()) {
			baseSettings = null;
			return false;
		}

		baseSettings = _settingsRepository.FindEnvironment(stepOptions.Environment);
		if (baseSettings is not null) {
			return true;
		}
		if (!stepOptions.RequiredEnvironment && !hasNamedEnvironment) {
			baseSettings = new EnvironmentSettings { Login = "default" };
			return true;
		}

		_logger.WriteError(EnvironmentNotFoundError.Build(stepOptions.Environment, _settingsRepository));
		return false;
	}

	private bool TryReloadSettings() {
		SettingsReloadResult reloadResult = _settingsRepository.Reload();
		if (!string.IsNullOrWhiteSpace(reloadResult.Warning)) {
			_logger.WriteWarning(reloadResult.Warning);
		}
		if (reloadResult.Reloaded) {
			return true;
		}

		_logger.WriteError(SettingsReloadFailedMessage);
		return false;
	}
}
