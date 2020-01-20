using System;
using Clio.UserEnvironment;
using CommandLine;

namespace Clio.Command
{
	[Verb("unreg-web-app", Aliases = new string[] { "unreg" }, HelpText = "Unregister application's settings from the list")]
	public class UnregAppOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Name", Required = true, HelpText = "Application name")]
		public string Name { get; set; }
	}

	public class UnregAppCommand: Command<UnregAppOptions> {

		private ISettingsRepository _settingsRepository;

		public UnregAppCommand(ISettingsRepository settingsRepository) {
			_settingsRepository = settingsRepository;
		}

		public override int Execute(UnregAppOptions options) {
			try {
				_settingsRepository.RemoveEnvironment(options.Name);
				_settingsRepository.ShowSettingsTo(Console.Out, string.Empty);
				Console.WriteLine();
				Console.WriteLine("Done");
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e.Message);
				return 1;
			}
		}
	}
}
