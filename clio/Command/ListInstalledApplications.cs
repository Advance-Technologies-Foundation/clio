using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ATF.Repository;
using ATF.Repository.Providers;
using Clio.Common;
using CommandLine;
using ConsoleTables;
using CreatioModel;
namespace Clio.Command;

[Verb("get-app-list", Aliases = new[] {"lia", "list-apps", "apps", "app-list", "apps-list", "list - installed - applications" },
	HelpText = "List installed apps")]
public class ListInstalledAppsOptions : EnvironmentOptions
{
	[Option("json", Required = false, HelpText = "Use json format for output", Default = false)]
	public bool JsonFormat { get; set; }
}

public class ListInstalledAppsCommand : Command<ListInstalledAppsOptions>
{

	#region Fields: Private

	private readonly IDataProvider _provider;
	private readonly ILogger _logger;

	#endregion

	#region Constructors: Public

	public ListInstalledAppsCommand(IDataProvider provider, ILogger logger){
		_provider = provider;
		_logger = logger;
	}

	#endregion

	#region Methods: Public

	public override int Execute(ListInstalledAppsOptions options){
		ConsoleTable table = new();
		table.Columns.Add(nameof(SysInstalledApp.Name));
		table.Columns.Add(nameof(SysInstalledApp.Code));
		table.Columns.Add(nameof(SysInstalledApp.Version));
		var applications = AppDataContextFactory.GetAppDataContext(_provider)
			.Models<SysInstalledApp>()
			.ToList();
		if (!options.JsonFormat) {
			applications.ForEach(m => { table.AddRow(m.Name, m.Code, m.Version); });
			_logger.PrintTable(table);
		} else {
			_logger.Write(JsonSerializer.Serialize(applications, new JsonSerializerOptions {
				WriteIndented = true
			}));
		}
		return 0;
	}

	#endregion

}