using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Clio.Common;
using Clio.Requests;
using Clio.UserEnvironment;
using Clio.Utilities;
using CommandLine;
using FluentValidation;

namespace Clio.Command;

[Verb("reg-web-app", Aliases = ["reg", "cfg"], HelpText = "Configure a web application settings")]
public class RegAppOptions : EnvironmentNameOptions {

	#region Properties: Public

	[Option('a', "active-environment", Required = false, HelpText = "Set as default web application")]
	public string ActiveEnvironment { get; set; }

	[Option("ActiveEnvironment", Required = false, Hidden = true, HelpText = "Alias for --active-environment")]
	public string ActiveEnvironmentAlias {
		get => ActiveEnvironment;
		set { if (!string.IsNullOrEmpty(value)) ActiveEnvironment = value; }
	}

	[Option("check-login", Required = false, HelpText = "Try login after registration")]
	public bool CheckLogin { get; set; }

	[Option("checkLogin", Required = false, Hidden = true, HelpText = "Alias for --check-login")]
	public bool CheckLoginAlias {
		get => CheckLogin;
		set { if (value) CheckLogin = value; }
	}

	[Option("add-from-iis", Required = false, HelpText = "Register all Creatios from IIS")]
	public bool FromIis { get; set; }

	[Option("auth-flow", Required = false, HelpText = "Authentication flow: client-credentials or authorization-code")]
	public string AuthFlow { get; set; }

	[Option("redirect-port", Required = false, HelpText = "Loopback OAuth callback port")]
	public int? RedirectPort { get; set; }

	[Option("redirect-uri", Required = false, HelpText = "Registered OAuth redirect URI")]
	public string RedirectUri { get; set; }

	[Option("host", Required = false, HelpText = "Computer name where IIS is hosted")]
	public string Host { get; set; }

	#endregion

}

public class RegAppCommand : Command<RegAppOptions> {

	#region Fields: Private

	private readonly ISettingsRepository _settingsRepository;
	private readonly IApplicationClientFactory _applicationClientFactory;
	private readonly IPowerShellFactory _powerShellFactory;
	private readonly ILogger _logger;
	private readonly IEnvironmentRuntimeDetectionService _environmentRuntimeDetectionService;
	private readonly IIisEnvironmentDiscoveryService _iisEnvironmentDiscoveryService;

	#endregion

	#region Constructors: Public

	public RegAppCommand(ISettingsRepository settingsRepository, IApplicationClientFactory applicationClientFactory,
		IPowerShellFactory powerShellFactory, ILogger logger,
		IEnvironmentRuntimeDetectionService environmentRuntimeDetectionService = null,
		IIisEnvironmentDiscoveryService iisEnvironmentDiscoveryService = null){
		_settingsRepository = settingsRepository;
		_applicationClientFactory = applicationClientFactory;
		_powerShellFactory = powerShellFactory;
		_logger = logger;
		_environmentRuntimeDetectionService = environmentRuntimeDetectionService;
		_iisEnvironmentDiscoveryService = iisEnvironmentDiscoveryService;
	}

	#endregion

	#region Methods: Public

	public override int Execute(RegAppOptions options){
		try {
			OAuthFlow? requestedAuthFlow = ParseAuthFlow(options);
			if (options.RedirectPort is < 1 or > 65535) {
				throw new ValidationException("--redirect-port must be between 1 and 65535.");
			}
			int? shortCircuitResult = TryExecuteWithoutRegistration(options);
			if (shortCircuitResult.HasValue) {
				return shortCircuitResult.Value;
			}
			// A blank name resolves to the ACTIVE environment, so a nameless registration must not inherit
			// anything from an unrelated environment that happens to be active.
			EnvironmentSettings? existingEnvironment = string.IsNullOrWhiteSpace(options.EnvironmentName)
				? null
				: _settingsRepository.FindEnvironment(options.EnvironmentName);
			OAuthFlow authFlow = requestedAuthFlow ?? existingEnvironment?.AuthFlow ?? OAuthFlow.ClientCredentials;
			// Re-registration keeps the settings that make the inherited flow usable; otherwise updating just
			// the url would drop the client id and the redirect and break every later command.
			string clientId = string.IsNullOrWhiteSpace(options.ClientId) ? existingEnvironment?.ClientId : options.ClientId;
			int? redirectPort = options.RedirectPort ?? existingEnvironment?.RedirectPort;
			string redirectUri = string.IsNullOrWhiteSpace(options.RedirectUri) ? existingEnvironment?.RedirectUri : options.RedirectUri;
			// Validate the EFFECTIVE flow, not just the one passed on this invocation.
			ValidateAuthorizationCodeOptions(authFlow, clientId, options);
			
			// Resolve the runtime BEFORE anything is persisted. Detection is allowed to refuse, and a refusal
			// that leaves a registered environment behind is worse than a plain failure: the stored IsNetCore
			// was guessed, nothing verified it, and every later command builds its URLs from it (issue #1435).
			bool resolvedIsNetCore = ResolveIsNetCore(options, existingEnvironment);
			EnvironmentSettings environment = new() {
				Login = options.Login,
				Password = options.Password,
				Uri = options.Uri?.TrimEnd('/'),
				Maintainer = options.Maintainer,
				Safe = options.SafeValue ?? false,
				IsNetCore = resolvedIsNetCore,
				DeveloperModeEnabled = options.DeveloperModeEnabled,
				ClientId = clientId,
				ClientSecret = options.ClientSecret,
				AuthAppUri = options.AuthAppUri,
				AuthFlow = authFlow,
				RedirectPort = redirectPort,
				RedirectUri = redirectUri,
				WorkspacePathes = options.WorkspacePathes,
				EnvironmentPath = options.EnvironmentPath
			};
			_settingsRepository.ConfigureEnvironment(options.EnvironmentName, environment);

			_logger.WriteInfo($"Environment {options.EnvironmentName} was configured...");
			environment = _settingsRepository.GetEnvironment(options);

			if (options.CheckLogin) {
				_logger.WriteInfo(
					$"Try login to {environment.Uri} with {environment.Login ?? environment.ClientId} credentials ...");
				using IOwnedApplicationClient creatioClient = _applicationClientFactory.CreateOwnedClient(environment);
				creatioClient.Login();
				_logger.WriteInfo("Login successful");
			}
			return 0;
		} catch (ValidationException vex) {
			vex.Errors.Select(e => new {e.ErrorMessage, e.ErrorCode, e.Severity})
				.ToList().ForEach(e => {
					_logger.WriteError($"{e.Severity.ToString().ToUpper()} ({e.ErrorCode}) - {e.ErrorMessage}");
				});
			return 1;
		} catch (Exception e) {
			_logger.WriteError($"{e.Message}");
			return 1;
		}
	}

	#endregion

	#region Methods: Private

	// Branches that finish without registering an environment. Returns the exit code when one of them ran,
	// null when the command must continue with a normal registration.
	private int? TryExecuteWithoutRegistration(RegAppOptions options) {
		if (options.FromIis) {
			RegisterIisEnvironments(options);
			return 0;
		}

		if (options.EnvironmentName?.ToLower(CultureInfo.InvariantCulture) == "open") {
			_settingsRepository.OpenFile();
			return 0;
		}

		if (string.IsNullOrWhiteSpace(options.ActiveEnvironment)) {
			return null;
		}

		if (!_settingsRepository.IsEnvironmentExists(options.ActiveEnvironment)) {
			throw new Exception($"Not found environment {options.ActiveEnvironment} in settings");
		}
		_settingsRepository.SetActiveEnvironment(options.ActiveEnvironment);
		_logger.WriteInfo($"Active environment set to {options.ActiveEnvironment}");
		return 0;
	}

	private void RegisterIisEnvironments(RegAppOptions options) {
		DiscoverIisEnvironments(options).ToList().ForEach(site => {
			EnvironmentSettings settings = new() {
				Login = "Supervisor",
				Password = "Supervisor",
				Uri = site.Uri,
				Maintainer = "Customer",
				Safe = false,
				IsNetCore = site.IsNetCore,
				DeveloperModeEnabled = true,
				EnvironmentPath = site.PhysicalPath
			};
			_settingsRepository.ConfigureEnvironment(site.Name, settings);
			_logger.WriteInfo($"Environment {site.Name} was added from {options.Host ?? "localhost"}");
		});
	}

	private static void ValidateAuthorizationCodeOptions(OAuthFlow authFlow, string clientId, RegAppOptions options) {
		if (authFlow != OAuthFlow.AuthorizationCode) {
			return;
		}
		if (string.IsNullOrWhiteSpace(clientId)) {
			throw new ValidationException("Authorization-code sign-in requires --clientId. clio ships no default client; ask whoever administers "
				+ (options.Uri ?? "the environment") + " which OAuth client to use.");
		}
		if (!string.IsNullOrWhiteSpace(options.ClientSecret)) {
			throw new ValidationException("Authorization-code sign-in uses a public client and does not accept --clientSecret.");
		}
	}

	private static OAuthFlow? ParseAuthFlow(RegAppOptions options) {
		if (string.IsNullOrWhiteSpace(options.AuthFlow)) return null;
		if (string.Equals(options.AuthFlow, "authorization-code", StringComparison.OrdinalIgnoreCase)) return OAuthFlow.AuthorizationCode;
		if (string.Equals(options.AuthFlow, "client-credentials", StringComparison.OrdinalIgnoreCase)) return OAuthFlow.ClientCredentials;
		throw new ValidationException("--auth-flow must be client-credentials or authorization-code.");
	}

	private IEnumerable<IisEnvironmentDescriptor> DiscoverIisEnvironments(RegAppOptions options) {
		if (_iisEnvironmentDiscoveryService != null) {
			return _iisEnvironmentDiscoveryService.Discover(options.Login, options.Password, options.Host);
		}

		_powerShellFactory.Initialize(options.Login, options.Password, options.Host);
		return IisScannerHandler.GetSites(_powerShellFactory)
			.Select(site => new IisEnvironmentDescriptor(
				site.Key,
				site.Value.PhysicalPath,
				site.Value.Url.ToString().TrimEnd('/'),
				site.Value.SiteType == SiteType.Core));
	}

	private bool ResolveIsNetCore(RegAppOptions options, EnvironmentSettings? existingEnvironment) {
		if (options.IsNetCore.HasValue) {
			return options.IsNetCore.Value;
		}

		if (string.IsNullOrWhiteSpace(options.Uri)) {
			return existingEnvironment?.IsNetCore ?? false;
		}

		if (_environmentRuntimeDetectionService == null) {
			throw new InvalidOperationException(
				"Runtime auto-detection is not available. Rerun reg-web-app with --IsNetCore true or --IsNetCore false.");
		}

		EnvironmentSettings detectionEnvironment = BuildDetectionEnvironment(options, existingEnvironment);
		bool isNetCore;
		try {
			isNetCore = _environmentRuntimeDetectionService.Detect(detectionEnvironment);
		} catch (InvalidOperationException detectionRefusal)
			when (CanKeepRecordedRuntime(options, existingEnvironment)) {
			//A re-registration of an environment whose runtime is already recorded, at the SAME uri: the recorded
			//value is not a guess this command would be inventing, and refusing here would discard the rest of the
			//update (password rotation, maintainer, workspace paths) over a runtime nobody asked to change. A site
			//that is permanently undecidable - all probes 401 behind SSO, or both services answering - would
			//otherwise be unregisterable without the hidden --IsNetCore flag.
			_logger.WriteWarning(
				$"{detectionRefusal.Message} Keeping the runtime already recorded for "
				+ $"{options.EnvironmentName}: {(existingEnvironment!.IsNetCore ? ".NET Core / NET8" : ".NET Framework")}."
				+ " Pass --IsNetCore to change it.");
			return existingEnvironment.IsNetCore;
		}
		_logger.WriteInfo($"Auto-detected runtime: {(isNetCore ? ".NET Core / NET8" : ".NET Framework")}");
		return isNetCore;
	}

	/// <summary>
	/// Whether a detection refusal may fall back to the runtime already recorded for this environment.
	/// </summary>
	/// <remarks>
	/// Only when the environment already exists AND the supplied uri is the one it was registered with. A NEW
	/// registration has nothing to fall back to, and a CHANGED uri points at a different site whose runtime the
	/// recorded value says nothing about - both keep the hard failure issue #1435 introduced.
	/// </remarks>
	private static bool CanKeepRecordedRuntime(RegAppOptions options, EnvironmentSettings? existingEnvironment) {
		if (existingEnvironment is null || string.IsNullOrWhiteSpace(existingEnvironment.Uri)) {
			return false;
		}
		return string.Equals(existingEnvironment.Uri.TrimEnd('/'), options.Uri?.TrimEnd('/'),
			StringComparison.OrdinalIgnoreCase);
	}

	private static EnvironmentSettings BuildDetectionEnvironment(
		RegAppOptions options,
		EnvironmentSettings? existingEnvironment) =>
		new() {
			Uri = options.Uri?.TrimEnd('/') ?? existingEnvironment?.Uri,
			Login = options.Login ?? existingEnvironment?.Login,
			Password = options.Password ?? existingEnvironment?.Password,
			ClientId = options.ClientId ?? existingEnvironment?.ClientId,
			ClientSecret = options.ClientSecret ?? existingEnvironment?.ClientSecret,
			AuthAppUri = options.AuthAppUri ?? existingEnvironment?.AuthAppUri
		};

	#endregion

}
