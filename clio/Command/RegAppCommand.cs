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

	#endregion

	#region Constructors: Public

	public RegAppCommand(ISettingsRepository settingsRepository, IApplicationClientFactory applicationClientFactory,
		IPowerShellFactory powerShellFactory, ILogger logger){
		_settingsRepository = settingsRepository;
		_applicationClientFactory = applicationClientFactory;
		_powerShellFactory = powerShellFactory;
		_logger = logger;
	}

	#endregion

	#region Methods: Public

	public override int Execute(RegAppOptions options){
		try {
			if (options.FromIis) {
				_powerShellFactory.Initialize(options.Login, options.Password, options.Host);
				Dictionary<string, Uri> sites = IISScannerHandler.getSites(_powerShellFactory);

				sites.ToList().ForEach(site => {
					_settingsRepository.ConfigureEnvironment(site.Key, new EnvironmentSettings {
						Login = "Supervisor",
						Password = "Supervisor",
						Uri = site.Value.ToString(),
						Maintainer = "Customer",
						Safe = false,
						IsNetCore = false,
						DeveloperModeEnabled = true
					});
					_logger.WriteInfo($"Environment {site.Key} was added from {options.Host ?? "localhost"}");
				});
				return 0;
			}

			if (options.EnvironmentName?.ToLower(CultureInfo.InvariantCulture) == "open") {
				_settingsRepository.OpenFile();
				return 0;
			}
			EnvironmentSettings environment = new() {
				Login = options.Login,
				Password = options.Password,
				Uri = options.Uri,
				Maintainer = options.Maintainer,
				Safe = options.SafeValue ?? false,
				IsNetCore = options.IsNetCore ?? false,
				DeveloperModeEnabled = options.DeveloperModeEnabled,
				ClientId = options.ClientId,
				ClientSecret = options.ClientSecret,
				AuthAppUri = options.AuthAppUri,
				WorkspacePathes = options.WorkspacePathes
			};
			if (!string.IsNullOrWhiteSpace(options.ActiveEnvironment)) {
				if (_settingsRepository.IsEnvironmentExists(options.ActiveEnvironment)) {
					_settingsRepository.SetActiveEnvironment(options.ActiveEnvironment);
					_logger.WriteInfo($"Active environment set to {options.ActiveEnvironment}");
					return 0;
				}
				throw new Exception($"Not found environment {options.ActiveEnvironment} in settings");
			}
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

}

[Verb("open-settings", Aliases = ["conf", "configuration", "settings", "os"],
	HelpText = "Open configuration file")]
public class OpenCfgOptions { }

public class OpenCfgCommand : Command<OpenCfgOptions> {

	#region Fields: Private

	private readonly ILogger _logger;

	#endregion

	#region Constructors: Public

	public OpenCfgCommand(ILogger logger){
		_logger = logger;
	}

	#endregion

	#region Methods: Public

	public override int Execute(OpenCfgOptions options){
		try {
			SettingsRepository.OpenSettingsFile();
			return 0;
		} catch (Exception e) {
			_logger.WriteInfo($"{e.Message}");
			return 1;
		}
	}

	#endregion

}