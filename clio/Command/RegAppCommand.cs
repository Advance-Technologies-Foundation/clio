using System;
using Clio.Common;
using Clio.UserEnvironment;
using CommandLine;

namespace Clio.Command
{
	[Verb("reg-web-app", Aliases = new string[] { "reg", "cfg" }, HelpText = "Configure a web application settings")]
	public class RegAppOptions : EnvironmentNameOptions
	{
		[Option('a', "ActiveEnvironment", Required = false, HelpText = "Set a web application by default")]
		public string ActiveEnvironment { get; set; }
	}

	public class RegAppCommand : Command<RegAppOptions>
	{
		private readonly ISettingsRepository _settingsRepository;
		private readonly IApplicationClientFactory _applicationClientFactory;

		public RegAppCommand(ISettingsRepository settingsRepository, IApplicationClientFactory applicationClientFactory) {
			_settingsRepository = settingsRepository;
			_applicationClientFactory = applicationClientFactory;
		}

		public override int Execute(RegAppOptions options) {
			try {
				var environment = new EnvironmentSettings {
					Login = options.Login,
					Password = options.Password,
					Uri = options.Uri,
					Maintainer = options.Maintainer,
					Safe = options.SafeValue.HasValue ? options.SafeValue : false,
					IsNetCore = options.IsNetCore ?? false,
					DeveloperModeEnabled = options.DeveloperModeEnabled
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
				_settingsRepository.ConfigureEnvironment(options.Name, environment);
				Console.WriteLine($"Envronment {options.Name} was configured...");
				environment = _settingsRepository.GetEnvironment(options);
				Console.WriteLine($"Try login to {environment.Uri} with {environment.Login} credentials ...");
				var creatioClient = _applicationClientFactory.CreateClient(environment);
				creatioClient.Login();
				Console.WriteLine($"Login successfull");
				return 0;
			} catch (Exception e) {
				Console.WriteLine($"{e.Message}");
				return 1;
			}
		}
	}
}
