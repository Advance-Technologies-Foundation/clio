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

	[Option('a', "ActiveEnvironment", Required = false, HelpText = "Set as default web application")]
	public string ActiveEnvironment { get; set; }

	[Option("checkLogin", Required = false, HelpText = "Try login after registration")]
	public bool CheckLogin { get; set; }

	[Option("add-from-iis", Required = false, HelpText = "Register all Creatios from IIS")]
	public bool FromIis { get; set; }

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
			if (options.FromIis) {
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
				return 0;
			}

			if (options.EnvironmentName?.ToLower(CultureInfo.InvariantCulture) == "open") {
				_settingsRepository.OpenFile();
				return 0;
			}
			if (!string.IsNullOrWhiteSpace(options.ActiveEnvironment)) {
				if (_settingsRepository.IsEnvironmentExists(options.ActiveEnvironment)) {
					_settingsRepository.SetActiveEnvironment(options.ActiveEnvironment);
					_logger.WriteInfo($"Active environment set to {options.ActiveEnvironment}");
					return 0;
				}
				throw new Exception($"Not found environment {options.ActiveEnvironment} in settings");
			}
			EnvironmentSettings? existingEnvironment = string.IsNullOrWhiteSpace(options.EnvironmentName)
				? null
				: _settingsRepository.FindEnvironment(options.EnvironmentName);
			bool resolvedIsNetCore = ResolveIsNetCore(options, existingEnvironment);
			EnvironmentSettings environment = new() {
				Login = options.Login,
				Password = options.Password,
				Uri = options.Uri?.TrimEnd('/'),
				Maintainer = options.Maintainer,
				Safe = options.SafeValue ?? false,
				IsNetCore = resolvedIsNetCore,
				DeveloperModeEnabled = options.DeveloperModeEnabled,
				ClientId = options.ClientId,
				ClientSecret = options.ClientSecret,
				AuthAppUri = options.AuthAppUri,
				WorkspacePathes = options.WorkspacePathes, 
				EnvironmentPath = options.EnvironmentPath
			};
			_settingsRepository.ConfigureEnvironment(options.EnvironmentName, environment);
			_logger.WriteInfo($"Environment {options.EnvironmentName} was configured...");
			environment = _settingsRepository.GetEnvironment(options);

			if (options.CheckLogin) {
				_logger.WriteInfo(
					$"Try login to {environment.Uri} with {environment.Login ?? environment.ClientId} credentials ...");
				IApplicationClient creatioClient = _applicationClientFactory.CreateClient(environment);
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

	private IEnumerable<IisEnvironmentDescriptor> DiscoverIisEnvironments(RegAppOptions options) {
		if (_iisEnvironmentDiscoveryService != null) {
			return _iisEnvironmentDiscoveryService.Discover(options.Login, options.Password, options.Host);
		}

		_powerShellFactory.Initialize(options.Login, options.Password, options.Host);
		return IISScannerHandler.GetSites(_powerShellFactory)
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
		bool isNetCore = _environmentRuntimeDetectionService.Detect(detectionEnvironment);
		_logger.WriteInfo($"Auto-detected runtime: {(isNetCore ? ".NET Core / NET8" : ".NET Framework")}");
		return isNetCore;
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
