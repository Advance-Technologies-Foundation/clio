using System;
using CommandLine;

namespace bpmcli
{
	[Verb("show-web-app-list", Aliases = new string[] { "apps", "show-web-app", "app" }, HelpText = "Show the list of web applications and their settings")]
	internal class AppListOptions
	{
		[Value(0, MetaName = "App name", Required = false, HelpText = "Name of application")]
		public string Name { get; set; }
	}


	internal class ShowAppListCommand {

		public static int ShowAppList(AppListOptions options) {
			try {
				var repository = new SettingsRepository();
				repository.ShowSettingsTo(Console.Out, options.Name);
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e.Message);
				return 1;
			}
		}
	}

}
