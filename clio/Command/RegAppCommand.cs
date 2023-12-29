using Clio.Common;
using Clio.Requests;
using Clio.UserEnvironment;
using Clio.Utilities;
using CommandLine;
using FluentValidation;
using Microsoft.CodeAnalysis;
using System;
using System.Globalization;
using System.Linq;

namespace Clio.Command
{
	[Verb("reg-web-app", Aliases = new string[] {"reg", "cfg"}, HelpText = "Configure a web application settings")]
	public class RegAppOptions : EnvironmentNameOptions
	{

		[Option('a', "ActiveEnvironment", Required = false, HelpText = "Set as default web application")]
		public string ActiveEnvironment { get; set; }

		[Option("add-from-iis", Required = false, HelpText = "Register all Creatios from IIS")]
		public bool FromIis { get; set; }

		[Option("checkLogin", Required = false, HelpText = "Try login after registration")]
		public bool CheckLogin { get; set; }

		[Option("host", Required = false, HelpText = "Computer name where IIS is hosted")]
		public string Host { get; set; }

	}

	public class RegAppCommand : Command<RegAppOptions>
	{

		private readonly ISettingsRepository _settingsRepository;
		private readonly IApplicationClientFactory _applicationClientFactory;
		private readonly IPowerShellFactory _powerShellFactory;

		public RegAppCommand(ISettingsRepository settingsRepository, IApplicationClientFactory applicationClientFactory,
			IPowerShellFactory powerShellFactory){
			_settingsRepository = settingsRepository;
			_applicationClientFactory = applicationClientFactory;
			_powerShellFactory = powerShellFactory;
		}

		public override int Execute(RegAppOptions options){
			try {
				if (options.FromIis) {
					_powerShellFactory.Initialize(options.Login, options.Password, options.Host);
					var sites = IISScannerHandler.getSites(_powerShellFactory);

					sites.ToList().ForEach(site => {
						_settingsRepository.ConfigureEnvironment(site.Key, new EnvironmentSettings() {
							Login = "Supervisor",
							Password = "Supervisor",
							Uri = site.Value.ToString(),
							Maintainer = "Customer",
							Safe = false,
							IsNetCore = false,
							DeveloperModeEnabled = true,
						});
						Console.WriteLine($"Environment {site.Key} was added from {options.Host ?? "localhost"}");
					});
					return 0;
				}

				if (options.EnvironmentName?.ToLower(CultureInfo.InvariantCulture) == "open") {
					_settingsRepository.OpenFile();
					return 0;
				} else {
					var environment = new EnvironmentSettings {
						Login = options.Login,
						Password = options.Password,
						Uri = options.Uri,
						Maintainer = options.Maintainer,
						Safe = options.SafeValue.HasValue ? options.SafeValue : false,
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
							Console.WriteLine($"Active environment set to {options.ActiveEnvironment}");
							return 0;
						} else {
							throw new Exception($"Not found environment {options.ActiveEnvironment} in settings");
						}
					}
					_settingsRepository.ConfigureEnvironment(options.EnvironmentName, environment);
					Console.WriteLine($"Environment {options.EnvironmentName} was configured...");
					environment = _settingsRepository.GetEnvironment(options);

					if (options.CheckLogin) {
						Console.WriteLine(
							$"Try login to {environment.Uri} with {environment.Login ?? environment.ClientId} credentials ...");
						var creatioClient = _applicationClientFactory.CreateClient(environment);
						creatioClient.Login();
						Console.WriteLine($"Login successful");
					}
					return 0;
				}
			} catch (ValidationException vex) {
				vex.Errors.Select(e => new {e.ErrorMessage, e.ErrorCode, e.Severity})
					.ToList().ForEach(e => {
						Console.WriteLine($"{e.Severity.ToString().ToUpper()} ({e.ErrorCode}) - {e.ErrorMessage}");
					});
				return 1;
			} catch (Exception e) {
				Console.WriteLine($"{e.Message}");
				return 1;
			}
		}

	}

	[Verb("open-settings", Aliases = new string[] {"conf", "configuration", "settings", "os"},
		HelpText = "Open configuration file")]
	public class OpenCfgOptions
	{ }

	public class OpenCfgCommand : Command<OpenCfgOptions>
	{

		public OpenCfgCommand(){ }

		public override int Execute(OpenCfgOptions options){
			try {
				SettingsRepository.OpenSettingsFile();
				return 0;
			} catch (Exception e) {
				Console.WriteLine($"{e.Message}");
				return 1;
			}
		}

	}
}