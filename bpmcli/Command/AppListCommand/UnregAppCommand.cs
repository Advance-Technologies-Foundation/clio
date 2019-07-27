using System;
using CommandLine;

namespace bpmcli
{
	[Verb("unreg-web-app", Aliases = new string[] { "unreg" }, HelpText = "Unregister application's settings from the list")]
	internal class UnregAppOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Name", Required = true, HelpText = "Application name")]
		public string Name { get; set; }
	}

	internal class UnregAppCommand {
		public static int UnregApplication(UnregAppOptions options) {
			try {
				var repository = new SettingsRepository();
				repository.RemoveEnvironment(options.Name);
				repository.ShowSettingsTo(Console.Out);
				Console.WriteLine("Done");
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e.Message);
				return 1;
			}
		}
	}
}
