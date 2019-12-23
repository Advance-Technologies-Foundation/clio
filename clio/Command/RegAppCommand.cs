using System;
using Creatio.Client;
using CommandLine;
using Clio.Common;
using Clio.UserEnvironment;

namespace Clio.Command
{
	[Verb("reg-web-app", Aliases = new string[] { "reg", "cfg" }, HelpText = "Configure a web application settings")]
	internal class RegAppOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Name", Required = false, HelpText = "Name of configured application")]
		public string Name { get; set; }

		[Option('a', "ActiveEnvironment", Required = false, HelpText = "Set a web application by default")]
		public string ActiveEnvironment { get; set; }

		[Option('s', "Safe", Required = false, HelpText = "Safe action in this enviroment")]
		public string Safe { get; set; }

		public bool? SafeValue
		{
			get
			{
				if (!string.IsNullOrEmpty(Safe)) {
					bool result;
					if (bool.TryParse(Safe, out result)) {
						return result;
					}
				}
				return null;
			}
		}
	}

	internal class RegAppCommand: RemoteCommand<RegAppOptions>
	{
		private readonly ISettingsRepository _settingsRepository;

		public RegAppCommand(IApplicationClient applicationClient, ISettingsRepository settingsRepository)
			: base(applicationClient) {
			_settingsRepository = settingsRepository;
		}

		public override int Execute(RegAppOptions options) {
			try {
				var environment = new EnvironmentSettings {
					Login = options.Login,
					Password = options.Password,
					Uri = options.Uri,
					Maintainer = options.Maintainer,
					Safe = options.SafeValue.HasValue ? options.SafeValue : false,
					IsNetCore = options.IsNetCore.HasValue ? options.IsNetCore.Value : false
				};
				if (!string.IsNullOrWhiteSpace(options.ActiveEnvironment)) {
					if (_settingsRepository.IsEnvironmentExists(options.ActiveEnvironment)) {
						_settingsRepository.SetActiveEnvironment(options.ActiveEnvironment);
					} else {
						throw new Exception($"Not found environment {options.ActiveEnvironment} in settings");
					}
				}
				_settingsRepository.ConfigureEnvironment(options.Name, environment);
				options.Environment = options.Environment ?? options.Name;
				_settingsRepository.ShowSettingsTo(Console.Out, options.Name);
				Console.WriteLine();
				Console.WriteLine($"Try login to {options.Uri} with {options.Name} credentials...");
				ApplicationClient.Login();
				Console.WriteLine($"Login done");
				return 0;
			} catch (Exception e) {
				Console.WriteLine($"{e.Message}");
				return 1;
			}
		}
	}
}
