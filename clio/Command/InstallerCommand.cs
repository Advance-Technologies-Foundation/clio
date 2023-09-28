using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation.Runspaces;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Common.K8;
using Clio.Common.ScenarioHandlers;
using CommandLine;
using MediatR;
using StackExchange.Redis;

namespace Clio.Command;

[Verb("deploy-creatio", HelpText = "Deploy Creatio from zip file")]
public class PfInstallerOptions : EnvironmentNameOptions
{

	#region Properties: Public

	[Option("SiteName", Required = false, HelpText = "SiteName")]
	public string SiteName { get; set; }

	[Option("SitePort", Required = false, HelpText = "Site port")]
	public int SitePort { get; set; }

	[Option("ZipFile", Required = true, HelpText = "Sets Zip File path")]
	public string ZipFile { get; set; }

	#endregion

}

public class InstallerCommand : Command<PfInstallerOptions>
{

	#region Constants: Private

	private const string IISRootFolder = @"D:\Projects\inetpub\wwwroot";

	#endregion

	#region Fields: Private

	private readonly IPackageArchiver _packageArchiver;
	private readonly IProcessExecutor _processExecutor;
	private readonly k8Commands _k8;
	private readonly IMediator _mediator;
	private readonly RegAppCommand _registerCommand;

	#endregion

	#region Constructors: Public

	public InstallerCommand(IPackageArchiver packageArchiver, IProcessExecutor processExecutor, k8Commands k8,
		IMediator mediator, RegAppCommand registerCommand) {
		_packageArchiver = packageArchiver;
		_processExecutor = processExecutor;
		_k8 = k8;
		_mediator = mediator;
		_registerCommand = registerCommand;
	}

	#endregion

	#region Methods: Private

	private static int StartWebBrowser(PfInstallerOptions options) {
		string url = $"http://{InstallerHelper.FetFQDN()}:{options.SitePort}";
		try {
			Process.Start(url);
			return 0;
		} catch {
			// Windows
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
				Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") {CreateNoWindow = true});
			}
			//Linux
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
				Process.Start("xdg-open", url);
			}
			// macOS
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
				Process.Start("open", url);
			}
			return 1;
		}
	}

	private async Task<int> CreateIISSite(DirectoryInfo unzippedDirectory, PfInstallerOptions options) {
		Console.WriteLine("[Create IIS Site] - Started");
		CreateIISSiteRequest request = new() {
			Arguments = new Dictionary<string, string> {
				{"siteName", options.SiteName},
				{"port", options.SitePort.ToString()},
				{"sourceDirectory", unzippedDirectory.FullName},
				{"destinationDirectory", IISRootFolder}, {
					"isNetFramework",
					(InstallerHelper.DetectFramework(unzippedDirectory) == InstallerHelper.FrameworkType.NetFramework)
					.ToString()
				}
			}
		};
		return (await _mediator.Send(request)).Value switch {
			(HandlerError error) => ExitWithErrorMessage(error.ErrorDescription),
			(CreateIISSiteResponse {Status: BaseHandlerResponse.CompletionStatus.Success} result) => ExitWithOkMessage(
				result.Description),
			(CreateIISSiteResponse {Status: BaseHandlerResponse.CompletionStatus.Failure} result) =>
				ExitWithErrorMessage(result.Description),
			_ => ExitWithErrorMessage("Unknown error occured")
		};
	}

	private void CreatePgTemplate(DirectoryInfo unzippedDirectory, string tmpDbName) {
		bool exists = InstallerHelper.CheckPgTemplateExists(tmpDbName);
		if (exists) {
			return;
		}

		FileInfo src = unzippedDirectory.GetDirectories("db").FirstOrDefault()?.GetFiles("*.backup").FirstOrDefault();
		Console.WriteLine($"[Starting Database restore] - {DateTime.Now:hh:mm:ss}");

		_k8.CopyBackupFileToPod(k8Commands.PodType.Postgres, src.FullName, src.Name);
		string restoreResult = _k8.RestorePgDatabase(src.Name, tmpDbName);
		string reportFilename = $@"C:\restore_{tmpDbName}_Result.txt";
		File.WriteAllText(reportFilename, restoreResult);
		Console.WriteLine($"[Report Generated in] - {reportFilename}");

		InstallerHelper.SetPgDatabaseAsTemplate(tmpDbName);
		string deleteResult = _k8.DeleteBackupImage(k8Commands.PodType.Postgres, src.Name);
		Console.WriteLine($"[Completed Database restore] - {DateTime.Now:hh:mm:ss}");
	}

	private int DoMsWork(DirectoryInfo unzippedDirectory, string zipFile) {
		// (bool isSuccess, string fileName) k8result = InstallerHelper.CopyBackupFile(unzippedDirectory, false, _processExecutor, null, InstallerHelper.DatabaseType.MsSql);
		// Console.WriteLine($"[Copying backup file completed] - {k8result.fileName}");
		// Console.WriteLine();

		string newDbName = Path.GetFileNameWithoutExtension(new FileInfo(zipFile).Name);
		//var createTemplateresult = InstallerHelper.CreateMsSqlDb(newDbName, _processExecutor);

		//InstallerHelper.DeleteFileK8(k8result.fileName, _processExecutor, InstallerHelper.DatabaseType.Postgres);
		InstallerHelper.DeleteFileK8("8.1.1.1164_Studio_Softkey_MSSQL_ENU.bak", _processExecutor,
			InstallerHelper.DatabaseType.MsSql);
		//Console.WriteLine($"[Deleted backup file] {k8result.fileName}");

		return 0;
	}

	private int DoPgWork(DirectoryInfo unzippedDirectory, string destDbName) {
		string tmpDbName = "template_" + unzippedDirectory.Name;
		CreatePgTemplate(unzippedDirectory, tmpDbName);
		InstallerHelper.CreateDbFromTemplate(tmpDbName, destDbName);
		Console.WriteLine($"[Database created] - {destDbName}");
		return 0;
	}

	private static int ExitWithErrorMessage(string message) {
		Console.WriteLine(message);
		return 1;
	}
	private static int ExitWithOkMessage(string message) {
		Console.WriteLine(message);
		return 0;
	}

	private static int FindEmptyRedisDb(int port) {
		ConnectionMultiplexer redis = ConnectionMultiplexer.Connect("localhost");
		IServer server = redis.GetServer("localhost", port);
		int count = server.DatabaseCount;
		for(int i = 1; i<count; i++) {
			long records = server.DatabaseSize(i);
			if(records == 0) {
				return i;
			}
		}
		return -1;
	}
	
	private async Task<int> UpdateConnectionString(DirectoryInfo unzippedDirectory, PfInstallerOptions options) {
		Console.WriteLine("[Update connection string] - Started");
		InstallerHelper.DatabaseType dbType = InstallerHelper.DetectDataBase(unzippedDirectory);
		k8Commands.ConnectionStringParams csParam = dbType switch {
			InstallerHelper.DatabaseType.Postgres => _k8.GetPostgresConnectionString(),
			InstallerHelper.DatabaseType.MsSql => _k8.GetMssqlConnectionString(),
		};
		
		int redisDb = FindEmptyRedisDb(csParam.RedisPort);
		
		ConfigureConnectionStringRequest request = dbType switch {
			InstallerHelper.DatabaseType.Postgres => new ConfigureConnectionStringRequest() {
				Arguments = new Dictionary<string, string> {
					{"folderPath", Path.Join(IISRootFolder, options.SiteName)},
					{"dbString", $"Server={Dns.GetHostName()};Port={csParam.DbPort};Database={options.SiteName};User ID={csParam.DbUsername};password={csParam.DbPassword};Timeout=500; CommandTimeout=400;MaxPoolSize=1024;"},
					{"redis", $"host={Dns.GetHostName()};db={redisDb};port={csParam.RedisPort}"}, 
					{"isNetFramework", (InstallerHelper.DetectFramework(unzippedDirectory) == InstallerHelper.FrameworkType.NetFramework).ToString()}
				}
			},
			InstallerHelper.DatabaseType.MsSql =>  new ConfigureConnectionStringRequest {
				Arguments = new Dictionary<string, string> {
					{"folderPath", Path.Join(IISRootFolder, options.SiteName)},
					{"dbString", $"Data Source={Dns.GetHostName()},{csParam.DbPort};Initial Catalog={options.SiteName};User Id={csParam.DbUsername}; Password={csParam.DbPassword};MultipleActiveResultSets=True;Pooling=true;Max Pool Size=100"},
					{"redis", $"host={Dns.GetHostName()};db={redisDb};port={csParam.RedisPort}"}, 
					{"isNetFramework", (InstallerHelper.DetectFramework(unzippedDirectory) == InstallerHelper.FrameworkType.NetFramework).ToString()}
				}
			}
		};
		
		return (await _mediator.Send(request)).Value switch {
			(HandlerError error) => ExitWithErrorMessage(error.ErrorDescription),
			(ConfigureConnectionStringResponse {Status: BaseHandlerResponse.CompletionStatus.Success} result) => ExitWithOkMessage(result.Description),
			(ConfigureConnectionStringResponse {Status: BaseHandlerResponse.CompletionStatus.Failure} result) => ExitWithErrorMessage(result.Description),
			_ => ExitWithErrorMessage("Unknown error occured")
		};
	}

	#endregion

	#region Methods: Public

	public override int Execute(PfInstallerOptions options) {
		
		if(!File.Exists(options.ZipFile)) {
			Console.WriteLine($"Could not find zip file: {options.ZipFile}");
			return 1;
		}
	
		while(string.IsNullOrEmpty(options.SiteName)) {
			Console.WriteLine("Please enter site name:");
			options.SiteName = Console.ReadLine();
			
			if(Directory.Exists(Path.Join(IISRootFolder, options.SiteName))) {
				Console.WriteLine($"Site with name {options.SiteName} already exists in {Path.Join(IISRootFolder, options.SiteName)}");
				options.SiteName = string.Empty;
			}
		}
		
		while(options.SitePort<=0) {
			Console.WriteLine("Please enter site port, recommended range between (400000 and 40100):");
			if (int.TryParse(Console.ReadLine(), out int value)) {
				options.SitePort = value;
			}else {
				Console.WriteLine("Site port must be an in value");
			}
		}
		
		
		Console.WriteLine($"[Staring unzipping] - {options.ZipFile}");
		DirectoryInfo unzippedDirectory = InstallerHelper.UnzipOrTakeExisting(options.ZipFile, _packageArchiver);
		Console.WriteLine($"[Unzip completed] - {unzippedDirectory.FullName}");
		Console.WriteLine();
		
		int dbRestoreResult = InstallerHelper.DetectDataBase(unzippedDirectory) switch {
			InstallerHelper.DatabaseType.Postgres => DoPgWork(unzippedDirectory,
				options.SiteName), //Need to check if db already exists
			InstallerHelper.DatabaseType.MsSql => DoMsWork(unzippedDirectory, options.ZipFile)
		};

		int createSiteResult = dbRestoreResult switch {
			0 => CreateIISSite(unzippedDirectory, options).GetAwaiter().GetResult(),
			_ => ExitWithErrorMessage("Database restore failed")
		};

		int updateConnectionStringResult = createSiteResult switch {
			0 => UpdateConnectionString(unzippedDirectory, options).GetAwaiter().GetResult(),
			_ => ExitWithErrorMessage("Failed to update ConnectionString.config file")
		};

		_registerCommand.Execute(new RegAppOptions {
			EnvironmentName = options.SiteName,
			Login = "Supervisor",
			Password = "Supervisor",
			Uri = $"http://{InstallerHelper.FetFQDN()}:{options.SitePort}",
			IsNetCore = InstallerHelper.DetectFramework(unzippedDirectory) == InstallerHelper.FrameworkType.NetCore
		});
		
		_ =updateConnectionStringResult switch {
			0 => StartWebBrowser(options),
			_ => ExitWithErrorMessage($"Could not open: http://{InstallerHelper.FetFQDN()}:{options.SitePort}")
		};

		Console.WriteLine("Press any key to exit...");
		Console.ReadKey();
		return 0;
	}

	#endregion

}