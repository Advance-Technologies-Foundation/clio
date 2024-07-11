using System;
using Clio.UserEnvironment;
using CommandLine;

namespace Clio.Command
{
	[Verb("show-web-app-list", Aliases = new string[] { "envs" ,"show-web-app" }, HelpText = "Show the list of web applications and their settings")]
	public class AppListOptions
	{
		[Value(0, MetaName = "App name", Required = false, HelpText = "Name of application")]
		public string Name { get; set; }
	}

	public class ShowAppListCommand : Command<AppListOptions>
	{

		private readonly ISettingsRepository _settingsRepository;

		public ShowAppListCommand(ISettingsRepository settingsRepository) {
			_settingsRepository = settingsRepository;
		}

		public override int Execute(AppListOptions options) {
			try {
				Console.OutputEncoding = System.Text.Encoding.UTF8;
				_settingsRepository.ShowSettingsTo(Console.Out, options.Name);
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e.Message);
				return 1;
			}
		}
	}
}
