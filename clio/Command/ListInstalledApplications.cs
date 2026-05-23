using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ATF.Repository.Providers;
using Clio.Common;
using CommandLine;
using ConsoleTables;
using CreatioModel;
namespace Clio.Command;

[Verb("list-apps", Aliases = new[] {"get-app-list", "lia", "apps", "app-list", "apps-list", "list - installed - applications" },
	HelpText = "List installed apps")]
public class ListInstalledAppsOptions : EnvironmentOptions
{
	[Option("json", Required = false, HelpText = "Use json format for output", Default = false)]
	public bool JsonFormat { get; set; }
}

public class ListInstalledAppsCommand : BaseDataContextCommand<ListInstalledAppsOptions>
{
	private readonly IInstalledApplicationQueryService _installedApplicationQueryService;

	/// <summary>
	/// Initializes a new instance of the <see cref="ListInstalledAppsCommand"/> class.
	/// </summary>
	/// <param name="provider">Data provider for the current environment.</param>
	/// <param name="logger">Logger used for CLI output.</param>
	/// <param name="installedApplicationQueryService">Installed application query service.</param>
	public ListInstalledAppsCommand(
		IDataProvider provider,
		ILogger logger,
		IInstalledApplicationQueryService installedApplicationQueryService) : base(provider, logger) {
		_installedApplicationQueryService = installedApplicationQueryService;
	}

	#region Constructors: Public

	/// <summary>
	/// Initializes a new instance of the <see cref="ListInstalledAppsCommand"/> class.
	/// </summary>
	/// <param name="provider">Data provider for the current environment.</param>
	/// <param name="logger">Logger used for CLI output.</param>
	/// <param name="applicationClient">Application client for the current environment.</param>
	/// <param name="environmentSettings">Resolved environment settings.</param>
	/// <param name="installedApplicationQueryService">Installed application query service.</param>
	public ListInstalledAppsCommand(
		IDataProvider provider,
		ILogger logger,
		IApplicationClient applicationClient,
		EnvironmentSettings environmentSettings,
		IInstalledApplicationQueryService installedApplicationQueryService)
			: base(provider, logger, applicationClient, environmentSettings) {
		_installedApplicationQueryService = installedApplicationQueryService;
	}

    #endregion

    #region Methods: Public

	/// <summary>
	/// Returns installed applications as structured items for non-CLI consumers.
	/// </summary>
	/// <param name="query">Optional installed application filters.</param>
	/// <returns>Installed application records.</returns>
	public IReadOnlyList<InstalledApplicationListItem> GetInstalledApplications(InstalledApplicationQuery? query = null) {
		return _installedApplicationQueryService.GetApplications(query)
			.Select(application => new InstalledApplicationListItem(
				application.Id,
				application.Name,
				application.Code,
				application.Version,
				application.Description))
			.ToList();
	}

    public override int Execute(ListInstalledAppsOptions options){
		ConsoleTable table = new();
		table.Columns.Add(nameof(SysInstalledApp.Name));
		table.Columns.Add(nameof(SysInstalledApp.Code));
		table.Columns.Add(nameof(SysInstalledApp.Version));
		table.Columns.Add(nameof(SysInstalledApp.Description));
		
		IReadOnlyList<SysInstalledApp> applications = _installedApplicationQueryService.GetApplications();

        if (!options.JsonFormat) {
			foreach (SysInstalledApp application in applications) {
				table.AddRow(application.Name, application.Code, application.Version, application.Description);
			}
			Logger.PrintTable(table);
		} else {
			Logger.Write(JsonSerializer.Serialize(GetInstalledApplications(), new JsonSerializerOptions {
				WriteIndented = true
			}));
		}
		return 0;
	}

	#endregion

}
