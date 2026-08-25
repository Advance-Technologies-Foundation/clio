using System;
using System.Globalization;
using Clio.Common;
using Clio.UserEnvironment;
using CommandLine;
using FluentValidation;
using FluentValidation.Results;

namespace Clio.Command;

/// <summary>
/// Options for adding package-specific NLog routing to a registered local Creatio environment.
/// </summary>
[Verb("add-custom-logging", HelpText = "Add package-specific NLog file routing to a local Creatio environment")]
public class AddCustomLoggingOptions : EnvironmentOptions {

	/// <summary>Gets or sets the package whose generated logger should be routed.</summary>
	[Option("package-name", Required = true, HelpText = "Package name whose generated LoggerName is configured")]
	public string PackageName { get; set; }

	/// <summary>Gets or sets the minimum NLog level for the package logger.</summary>
	[Option("min-level", Required = false, Default = "Info",
		HelpText = "Minimum NLog level: Trace, Debug, Info, Warn, Error, Fatal, or Off (default: Info)")]
	public string MinLevel { get; set; } = "Info";

	/// <summary>Gets or sets the optional log file name beneath the environment's TodayLogPath.</summary>
	[Option("file-name", Required = false,
		HelpText = "Optional log file name under TodayLogPath; .log is appended when omitted")]
	public string FileName { get; set; }
}

/// <summary>
/// Validates <see cref="AddCustomLoggingOptions"/> before local configuration files are inspected.
/// </summary>
public sealed class AddCustomLoggingOptionsValidator : AbstractValidator<AddCustomLoggingOptions> {
	/// <summary>Initializes validation rules for the add-custom-logging command.</summary>
	public AddCustomLoggingOptionsValidator() {
		RuleFor(options => options)
			.Must(options => string.IsNullOrWhiteSpace(options.Uri)
				&& string.IsNullOrWhiteSpace(options.Login)
				&& string.IsNullOrWhiteSpace(options.Password)
				&& string.IsNullOrWhiteSpace(options.ClientId)
				&& string.IsNullOrWhiteSpace(options.ClientSecret)
				&& string.IsNullOrWhiteSpace(options.AuthAppUri)
				&& string.IsNullOrWhiteSpace(options.EnvironmentPath)
				&& !options.IsNetCore.HasValue)
			.WithMessage("add-custom-logging accepts only a registered environment name; direct connection and runtime overrides are not allowed.");
		RuleFor(options => options.Environment)
			.NotEmpty()
			.WithMessage("A registered environment name is required.");
		RuleFor(options => options.PackageName)
			.NotEmpty()
			.WithMessage("Package name is required.")
			.Must(CustomLoggingConfigurator.IsSafePackageName)
			.WithMessage("Package name may contain only letters, digits, underscore, dot, and hyphen, and must start with a letter or underscore.");
		RuleFor(options => options.MinLevel)
			.Must(CustomLoggingConfigurator.IsSupportedMinLevel)
			.WithMessage("Min level must be one of: Trace, Debug, Info, Warn, Error, Fatal, Off.");
		RuleFor(options => options.FileName)
			.Must(CustomLoggingConfigurator.IsSafeFileName)
			.WithMessage("File name must be a simple file name without directories or NLog layout expressions.");
	}
}

/// <summary>Restarts a registered Creatio environment.</summary>
public interface IEnvironmentRestartService {
	/// <summary>Restarts the environment represented by the refreshed <paramref name="environment"/> snapshot.</summary>
	/// <param name="environment">Refreshed registered environment settings.</param>
	/// <returns>The restart command exit code.</returns>
	int Restart(EnvironmentSettings environment);
}

internal sealed class EnvironmentRestartService(
	IApplicationClientFactory applicationClientFactory,
	RestartCommand restartCommand) : IEnvironmentRestartService {
	public int Restart(EnvironmentSettings environment) {
		IApplicationClient applicationClient = applicationClientFactory.CreateEnvironmentClient(environment);
		return restartCommand.ExecuteForEnvironment(new RestartOptions(), environment, applicationClient);
	}
}

/// <summary>
/// Adds idempotent package-specific NLog routing to a registered local Creatio installation.
/// </summary>
public class AddCustomLoggingCommand(
	IValidator<AddCustomLoggingOptions> validator,
	ISettingsRepository settingsRepository,
	ICustomLoggingConfigurator configurator,
	IEnvironmentRestartService restartService,
	ILogger logger) : Command<AddCustomLoggingOptions> {
	private const string MissingEnvironmentFormat = "Environment '{0}' is not registered.";
	private const string MissingEnvironmentPathFormat =
		"Environment '{0}' does not have a local EnvironmentPath. add-custom-logging can only edit a registered local Creatio installation.";
	private const string MissingRestartUriFormat =
		"Environment '{0}' does not have a Uri and cannot be restarted. Configure logging without --restart-environment or update the registration.";
	private const string ConfiguredFormat = "Configured logger '{0}' with target '{1}'.";
	private const string AlreadyConfiguredFormat = "Logger '{0}' and target '{1}' are already configured; no files changed.";
	private const string LogPathFormat = "Configured log path: {0}";
	private const string EnvironmentFormat = "Configured environment '{0}'.";
	private const string RestartRequiredMessage =
		"Restart is required before the new logging route is guaranteed to be active. Re-run with --restart-environment to restart now.";
	private const string RestartingFormat = "Restarting environment '{0}' to activate the logging route.";

	/// <inheritdoc />
	public override int Execute(AddCustomLoggingOptions options) {
		ValidationResult validationResult = validator.Validate(options);
		if (!validationResult.IsValid) {
			PrintErrors(validationResult.Errors, logger);
			return 1;
		}
		EnvironmentSettings environment = settingsRepository.FindCurrentEnvironment(options.Environment);
		if (environment is null) {
			logger.WriteError(string.Format(CultureInfo.InvariantCulture, MissingEnvironmentFormat, options.Environment));
			return 1;
		}
		if (string.IsNullOrWhiteSpace(environment.EnvironmentPath)) {
			logger.WriteError(string.Format(CultureInfo.InvariantCulture, MissingEnvironmentPathFormat, options.Environment));
			return 1;
		}
		if (options.RestartEnvironment && string.IsNullOrWhiteSpace(environment.Uri)) {
			logger.WriteError(string.Format(CultureInfo.InvariantCulture, MissingRestartUriFormat, options.Environment));
			return 1;
		}

		CustomLoggingConfigurationResult result = configurator.Configure(
			environment.EnvironmentPath,
			options.PackageName,
			options.MinLevel,
			options.FileName);
		if (!result.Success) {
			logger.WriteError(result.ErrorMessage);
			return 1;
		}

		logger.WriteInfo(string.Format(
			CultureInfo.InvariantCulture,
			result.Changed ? ConfiguredFormat : AlreadyConfiguredFormat,
			result.LoggerName,
			result.TargetName));
		logger.WriteInfo(string.Format(CultureInfo.InvariantCulture, LogPathFormat, result.LogPath));
		logger.WriteInfo(string.Format(CultureInfo.InvariantCulture, EnvironmentFormat, options.Environment));

		if (!options.RestartEnvironment) {
			logger.WriteInfo(RestartRequiredMessage);
			return 0;
		}

		logger.WriteInfo(string.Format(CultureInfo.InvariantCulture, RestartingFormat, options.Environment));
		return restartService.Restart(environment);
	}
}
