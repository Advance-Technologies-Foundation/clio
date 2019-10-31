using System;
using Bpmonline.Client;
using CommandLine;

namespace clio
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
				if (!String.IsNullOrEmpty(Safe)) {
					bool result;
					if (bool.TryParse(Safe, out result)) {
						return result;
					}
				}
				return null;
			}
		}
	}

	internal class RegAppCommand {

		private static EnvironmentSettings _settings { get; set; }

		private static BpmonlineClient _creatioClient { get => new BpmonlineClient(_settings.Uri, _settings.Login, _settings.Password, _settings.IsNetCore); }

		public static int RegApp(RegAppOptions options) {
			try {
				var repository = new SettingsRepository();
				var environment = new EnvironmentSettings() {
					Login = options.Login,
					Password = options.Password,
					Uri = options.Uri,
					Maintainer = options.Maintainer,
					Safe = options.SafeValue,
					IsNetCore = options.IsNetCore
				};
				if (!String.IsNullOrEmpty(options.ActiveEnvironment)) {
					if (repository.IsExistInEnvironment(options.ActiveEnvironment))
						repository.SetActiveEnvironment(options.ActiveEnvironment);
					else
						throw new Exception($"Not found environment {options.ActiveEnvironment} in settings");
				}
				repository.ConfigureEnvironment(options.Name, environment);
				options.Environment = options.Environment ?? options.Name;
				repository.ShowSettingsTo(Console.Out, options.Name);
				Console.WriteLine();
				_settings = repository.GetEnvironment(options);
				Console.WriteLine($"Try login to {options.Uri} with {options.Name} credentials...");
				_creatioClient.Login();
				Console.WriteLine($"Login done");
				return 0;
			} catch (Exception e) {
				Console.WriteLine($"{e.Message}");
				return 1;
			}
		}
	}
}
