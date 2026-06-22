using System;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using Clio.UserEnvironment;
using CommandLine;

namespace Clio.Command;

/// <summary>
/// Options for the <c>get-user-culture</c> diagnostic verb. Uses the standard
/// <c>--environment/-e</c> (and direct-connection) options; it has no <c>--caption-culture</c>
/// override, so a failed resolution is always a hard error.
/// </summary>
[Verb("get-user-culture", Aliases = ["profile-language"],
	HelpText = "Print the logged-in Creatio user's profile culture (e.g. en-US, uk-UA) for an environment.")]
public sealed class GetUserCultureCommandOptions : EnvironmentOptions
{ }

/// <summary>
/// CLI entry point for the <c>get-user-culture</c> verb. Resolves the connected user's profile
/// culture through <see cref="ICurrentUserCultureResolver"/> (reading
/// <c>ApplicationInfoService.svc/GetApplicationInfo</c>) and prints it, or prints a user-friendly
/// <c>Error:</c> and exits non-zero when the culture cannot be resolved. Mirrors the
/// <c>get-component-info</c> verb's factory-based, environment-from-settings resolution pattern.
/// </summary>
public sealed class GetUserCultureCommand
{
	private readonly ICurrentUserCultureResolverFactory _resolverFactory;
	private readonly ISettingsRepository _settingsRepository;
	private readonly ILogger _logger;

	/// <summary>Initializes the command with the resolver factory, settings repository, and logger.</summary>
	public GetUserCultureCommand(
		ICurrentUserCultureResolverFactory resolverFactory,
		ISettingsRepository settingsRepository,
		ILogger logger)
	{
		_resolverFactory = resolverFactory ?? throw new ArgumentNullException(nameof(resolverFactory));
		_settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	/// <summary>Executes the verb. Returns 0 on a resolved culture, non-zero on failure.</summary>
	public int Execute(GetUserCultureCommandOptions options)
	{
		return ExecuteAsync(options, CancellationToken.None).GetAwaiter().GetResult();
	}

	internal async Task<int> ExecuteAsync(GetUserCultureCommandOptions options, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(options);

		EnvironmentSettings settings;
		try
		{
			settings = ResolveEnvironmentSettings(options);
		}
		catch (Exception ex)
		{
			_logger.WriteError($"Error: {ex.Message}");
			return 1;
		}

		ICurrentUserCultureResolver resolver = _resolverFactory.Create(settings);
		CultureResolution resolution = await resolver.ResolveAsync(cancellationToken).ConfigureAwait(false);

		// The verb has no --caption-culture override, so a failed resolution is always a hard error
		// (the M-4 hard-abort path). Branch on Success first (the CultureResolution invariant).
		if (!resolution.Success)
		{
			_logger.WriteError($"Error: {DescribeFailure(resolution.FailureReason)}");
			return 1;
		}

		_logger.WriteLine(resolution.Culture);
		return 0;
	}

	private EnvironmentSettings ResolveEnvironmentSettings(GetUserCultureCommandOptions options)
	{
		if (string.IsNullOrWhiteSpace(options.Environment) && string.IsNullOrWhiteSpace(options.Uri))
		{
			string activeEnvName = _settingsRepository.GetDefaultEnvironmentName();
			if (!string.IsNullOrWhiteSpace(activeEnvName) && _settingsRepository.IsEnvironmentExists(activeEnvName))
			{
				options.Environment = activeEnvName;
			}
		}

		return _settingsRepository.GetEnvironment(options);
	}

	private static string DescribeFailure(string? reason) => reason switch
	{
		CurrentUserCultureResolver.ReasonUserCultureMissing =>
			"the environment did not return a user profile culture (sysValues.userCulture was missing or empty).",
		CurrentUserCultureResolver.ReasonUserCultureInvalid =>
			"the environment returned an unrecognized user profile culture that .NET cannot parse.",
		CurrentUserCultureResolver.ReasonUnauthorized =>
			"not authorized to read the user profile culture from the environment. Check the credentials.",
		CurrentUserCultureResolver.ReasonUnreachable =>
			"could not reach the environment to read the user profile culture. Check the URL and connectivity.",
		CurrentUserCultureResolver.ReasonNoActiveEnvironment =>
			"no environment specified. Pass -e <environment> or register a default environment.",
		_ => "the user profile culture could not be resolved."
	};
}
