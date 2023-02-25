using Clio.Common;
using Clio.Requests;
using Clio.UserEnvironment;
using CommandLine;
using FluentValidation;
using System;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Clio.Command {
	[Verb("reg-web-app", Aliases = new string[] { "reg", "cfg" }, HelpText = "Configure a web application settings")]
	public class RegAppOptions : EnvironmentNameOptions {
		[Option('a', "ActiveEnvironment", Required = false, HelpText = "Set as default web application")]
		public string ActiveEnvironment {
			get; set;
		}

		[Option("all-from-IIS", Required = true, HelpText = "Register all Creatios from IIS")]
		public bool FromIis {
			get; set;
		}


		[Option("WithLogin", Required = false, HelpText = "Try login after registration")]
		public bool TryLogIn {
			get; set;
		}
	}

	public class RegAppCommand : Command<RegAppOptions> {
		private readonly ISettingsRepository _settingsRepository;
		private readonly IApplicationClientFactory _applicationClientFactory;

		public RegAppCommand(ISettingsRepository settingsRepository, IApplicationClientFactory applicationClientFactory) {
			_settingsRepository = settingsRepository;
			_applicationClientFactory = applicationClientFactory;
		}

		public override int Execute(RegAppOptions options) {
			try
			{
				if (options.FromIis && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				{
					//TODO: Autofac cannot resolve IISScannerHandler becacase IISScannerHandler needs RegAppCommand
					IISScannerHandler iis = new IISScannerHandler(_settingsRepository, this);
					iis.Handle(new IISScannerRequest { Content = "clio://IISScannerRequest/?return=registerAll" }, CancellationToken.None);
					return 0;
				}

				if (options.Name.ToLower(CultureInfo.InvariantCulture) == "open")
				{
					_settingsRepository.OpenSettingsFile();
					return 0;
				}
				else
				{
					var environment = new EnvironmentSettings
					{
						Login = options.Login,
						Password = options.Password,
						Uri = options.Uri,
						Maintainer = options.Maintainer,
						Safe = options.SafeValue.HasValue ? options.SafeValue : false,
						IsNetCore = options.IsNetCore ?? false,
						DeveloperModeEnabled = options.DeveloperModeEnabled,
						ClientId = options.ClientId,
						ClientSecret = options.ClientSecret,
						AuthAppUri = options.AuthAppUri
					};
					if (!string.IsNullOrWhiteSpace(options.ActiveEnvironment))
					{
						if (_settingsRepository.IsEnvironmentExists(options.ActiveEnvironment))
						{
							_settingsRepository.SetActiveEnvironment(options.ActiveEnvironment);
							Console.WriteLine($"Active environment set to {options.ActiveEnvironment}");
							return 0;
						}
						else
						{
							throw new Exception($"Not found environment {options.ActiveEnvironment} in settings");
						}
					}
					_settingsRepository.ConfigureEnvironment(options.Name, environment);
					Console.WriteLine($"Environment {options.Name} was configured...");
					environment = _settingsRepository.GetEnvironment(options);
					Console.WriteLine($"Try login to {environment.Uri} with {environment.Login} credentials ...");

					if (options.TryLogIn)
					{
						var creatioClient = _applicationClientFactory.CreateClient(environment);
						creatioClient.Login();
						Console.WriteLine($"Login successfull");
					}
					return 0;
				}
			}
			catch (ValidationException vex)
			{
				vex.Errors.Select(e => new { e.ErrorMessage, e.ErrorCode, e.Severity })
				.ToList().ForEach(e =>
				{
					Console.WriteLine($"{e.Severity.ToString().ToUpper()} ({e.ErrorCode}) - {e.ErrorMessage}");
				});
				return 1;
			}
			catch (Exception e)
			{
				Console.WriteLine($"{e.Message}");
				return 1;
			}
		}
	}
}
