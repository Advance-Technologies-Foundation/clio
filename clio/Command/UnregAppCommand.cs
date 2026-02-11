using System;
using Clio.UserEnvironment;
using CommandLine;

namespace Clio.Command
{
	[Verb("unreg-web-app", Aliases = ["unreg"], HelpText = "Unregister application's settings from the list")]
	public class UnregAppOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Name", Required = false, HelpText = "Application name")]
		public string Name { get; set; }

		[Option("all", Required = false, HelpText = "Try login after registration")]
		public bool UnregAll {
			get; set;
		}
	}

	public class UnregAppCommand : Command<UnregAppOptions>
	{

		private readonly ISettingsRepository _settingsRepository;

		public UnregAppCommand(ISettingsRepository settingsRepository) {
			_settingsRepository = settingsRepository;
		}

		public override int Execute(UnregAppOptions options) {
			try {
				if (options.UnregAll) {
					_settingsRepository.RemoveAllEnvironment();
				} else {
					if (String.IsNullOrEmpty(options.Name)) {
						throw new ArgumentException("Name cannot be empty");
					}
					_settingsRepository.RemoveEnvironment(options.Name);
					Console.WriteLine($"Envronment {options.Name} was deleted...");
					Console.WriteLine();
					Console.WriteLine("Done");
				}
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e.Message);
				return 1;
			}
		}
	}
}
