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

public class ListInstalledAppsCommand : BaseDataContextCommand<ListInstalledAppsOptions>
{
	public ListInstalledAppsCommand(IDataProvider provider, ILogger logger) : base(provider, logger) {
	}

	#region Constructors: Public

	public ListInstalledAppsCommand(IDataProvider provider, ILogger logger, IApplicationClient applicationClient, EnvironmentSettings environmentSettings)
			: base(provider, logger, applicationClient, environmentSettings) {
	}

    #endregion

    #region Methods: Public

    public override int Execute(ListInstalledAppsOptions options){
		ConsoleTable table = new();
		table.Columns.Add(nameof(SysInstalledApp.Name));
		table.Columns.Add(nameof(SysInstalledApp.Code));
		table.Columns.Add(nameof(SysInstalledApp.Version));
		table.Columns.Add(nameof(SysInstalledApp.Description));
		
		List<SysInstalledApp> applications = AppDataContextFactory.GetAppDataContext(Provider)
																.Models<SysInstalledApp>()
																.ToList();

        if (!options.JsonFormat) {
			applications.ForEach(m => { table.AddRow(m.Name, m.Code, m.Version, m.Description); });
			Logger.PrintTable(table);
		} else {
			Logger.Write(JsonSerializer.Serialize(applications, new JsonSerializerOptions {
				WriteIndented = true
			}));
		}
		return 0;
	}

	#endregion

}
