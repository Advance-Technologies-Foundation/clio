/*
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Management.Automation;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Clio.Common.db;
using Clio.Common.K8;
using Clio.Common.ScenarioHandlers;
using Clio.UserEnvironment;
using CommandLine;
using MediatR;
using StackExchange.Redis;
using IFileSystem = Clio.Common.IFileSystem;

namespace Clio.Command;

[Verb("deploy-creatio", Aliases = new []{"dc", "ic", "install-creation"}, HelpText = "Deploy Creatio from zip file")]
public class PfInstallerOptions : EnvironmentNameOptions
{

	#region Fields: Private

	private readonly Dictionary<string, string> _productList = new Dictionary<string, string> {
		{"s", "Studio"},
		{"semse", "SalesEnterprise_Marketing_ServiceEnterprise"},
		{"bcj", "BankSales_BankCustomerJourney_Lending_Marketing"}
	};

	private CreatioDBType _dbType;
	private CreatioRuntimePlatform _platform;

	#endregion

	#region Properties: Internal

	internal CreatioDBType DBType {
		get {
			if (DB.ToLower() == "pg") {
				return CreatioDBType.PostgreSQL;
			}
			if (DB.ToLower() == "mssql") {
				return CreatioDBType.MSSQL;
			}
			return _dbType;
		}
		set { _dbType = value; }
	}

	internal CreatioRuntimePlatform RuntimePlatform {
		get {
			if (Platform.ToLower() == "net6") {
				return CreatioRuntimePlatform.NET6;
			}
			if (Platform.ToLower() == "netframework" || Platform.ToLower() == "nf") {
				return CreatioRuntimePlatform.NETFramework;
			}
			return _platform;
		}
		set { _platform = value; }
	}

	#endregion

	#region Properties: Public

	[Option("db", Required = false, HelpText = "DB type: pg|mssql")]
	public string DB { get; set; }

	[Option("platform", Required = false, HelpText = "Runtime platform: net6|netframework")]
	public string Platform { get; set; }

	public string Product {
		get {
			if (_productList.ContainsKey(ProductKey)) {
				return _productList[ProductKey];
			}
			return ProductKey;
		}
		set { ProductKey = value; }
	}

	[Option("product", Required = false, HelpText = "Product name")]
	public string ProductKey { get; set; }

	[Option("SiteName", Required = false, HelpText = "SiteName")]
	public string SiteName { get; set; }

	[Option("SitePort", Required = false, HelpText = "Site port")]
	public int SitePort { get; set; }

	[Option("ZipFile", Required = false, HelpText = "Sets Zip File path")]
	public string ZipFile { get; set; }

	#endregion

}

public class InstallerCommand : Command<PfInstallerOptions>
{

	#region Fields: Private

	private static readonly Action<string, string, IProgress<double>> CopyFileWithProgress =
		(sourcePath, destinationPath, progress) => {
			const int bufferSize = 1024 * 1024; // 1MB
			byte[] buffer = new byte[bufferSize];
			int bytesRead;

			using FileStream sourceStream = new(sourcePath, FileMode.Open, FileAccess.Read);
			long totalBytes = sourceStream.Length;

			using FileStream destinationStream = new(destinationPath, FileMode.OpenOrCreate, FileAccess.Write);
			while ((bytesRead = sourceStream.Read(buffer, 0, buffer.Length)) > 0) {
				destinationStream.Write(buffer, 0, bytesRead);
				// Report progress
				double percentage = 100d * sourceStream.Position / totalBytes;
				progress.Report(percentage);
			}
		};

	private readonly string _iisRootFolder;
	private readonly IPackageArchiver _packageArchiver;
	private readonly k8Commands _k8;
	private readonly IMediator _mediator;
	private readonly RegAppCommand _registerCommand;
	private readonly IFileSystem _fileSystem;

	#endregion

	#region Fields: Protected

	protected string ProductFolder;
	protected string RemoteArtefactServerPath;

	#endregion

	#region Constructors: Public

	public InstallerCommand(IPackageArchiver packageArchiver, k8Commands k8,
		IMediator mediator, RegAppCommand registerCommand, ISettingsRepository settingsRepository,
		IFileSystem fileSystem){
		_packageArchiver = packageArchiver;
		_k8 = k8;
		_mediator = mediator;
		_registerCommand = registerCommand;
		_fileSystem = fileSystem;
		_iisRootFolder = settingsRepository.GetIISClioRootPath();
		ProductFolder = settingsRepository.GetCreatioProductsFolder();
		RemoteArtefactServerPath = settingsRepository.GetRemoteArtefactServerPath();
	}

	public InstallerCommand(){ }

	#endregion

	#region Methods: Private

	private static int ExitWithErrorMessage(string message){
		Console.WriteLine(message);
		return 1;
	}

	private static int ExitWithOkMessage(string message){
		Console.WriteLine(message);
		return 0;
	}

	private static int FindEmptyRedisDb(int port){
		ConnectionMultiplexer redis = ConnectionMultiplexer.Connect("localhost");
		IServer server = redis.GetServer("localhost", port);
		int count = server.DatabaseCount;
		for (int i = 1; i < count; i++) {
			long records = server.DatabaseSize(i);
			if (records == 0) {
				return i;
			}
		}
		return -1;
	}

	private static int StartWebBrowser(PfInstallerOptions options){
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

	private string CopyLocalWhenNetworkDrive(string path){
		if (path.StartsWith(@"\\")) {
			return CopyZipLocal(path);
		}
		return new DriveInfo(Path.GetPathRoot(path)) switch {
			{DriveType: DriveType.Network} => CopyZipLocal(path),
			_ => path
		};
	}

	private string CopyZipLocal(string src){
		if (!Directory.Exists(ProductFolder)) {
			Directory.CreateDirectory(ProductFolder);
		}

		FileInfo srcInfo = new(src);
		string dest = Path.Join(ProductFolder, srcInfo.Name);

		if (File.Exists(dest)) {
			return dest;
		}

		Console.WriteLine($"Detected network drive as source, copying to local folder {ProductFolder}");
		Console.Write("Copy Progress:    ");
		Progress<double> progressReporter = new(progress => {
			string result = progress switch {
				< 10 => progress.ToString("0").PadLeft(2) + " %",
				< 100 => progress.ToString("0").PadLeft(1) + " %",
				100 => "100 %",
				_ => ""
			};
			Console.CursorLeft = 15;
			Console.Write(result);
		});
		CopyFileWithProgress(src, dest, progressReporter);
		return dest;
	}

	private async Task<int> CreateIISSite(DirectoryInfo unzippedDirectory, PfInstallerOptions options){
		Console.WriteLine("[Create IIS Site] - Started");
		CreateIISSiteRequest request = new() {
			Arguments = new Dictionary<string, string> {
				{"siteName", options.SiteName},
				{"port", options.SitePort.ToString()},
				{"sourceDirectory", unzippedDirectory.FullName},
				{"destinationDirectory", _iisRootFolder}, {
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

	private void CreatePgTemplate(DirectoryInfo unzippedDirectory, string tmpDbName){
		k8Commands.ConnectionStringParams csp = _k8.GetPostgresConnectionString();
		Postgres postgres = new(csp.DbPort, csp.DbUsername, csp.DbPassword);

		bool exists = postgres.CheckTemplateExists(tmpDbName);
		if (exists) {
			return;
		}
		FileInfo src = unzippedDirectory.GetDirectories("db").FirstOrDefault()?.GetFiles("*.backup").FirstOrDefault();
		Console.WriteLine($"[Starting Database restore] - {DateTime.Now:hh:mm:ss}");

		_k8.CopyBackupFileToPod(k8Commands.PodType.Postgres, src.FullName, src.Name);

		postgres.CreateDb(tmpDbName);
		_k8.RestorePgDatabase(src.Name, tmpDbName);
		postgres.SetDatabaseAsTemplate(tmpDbName);
		_k8.DeleteBackupImage(k8Commands.PodType.Postgres, src.Name);
		Console.WriteLine($"[Completed Database restore] - {DateTime.Now:hh:mm:ss}");
	}

	private int DoMsWork(DirectoryInfo unzippedDirectory, string siteName){
		FileInfo src = unzippedDirectory.GetDirectories("db").FirstOrDefault()?.GetFiles("*.bak").FirstOrDefault();
		Console.WriteLine($"[Starting Database restore] - {DateTime.Now:hh:mm:ss}");
		_k8.CopyBackupFileToPod(k8Commands.PodType.Mssql, src.FullName, $"{siteName}.bak");

		k8Commands.ConnectionStringParams csp = _k8.GetMssqlConnectionString();
		Mssql mssql = new(csp.DbPort, csp.DbUsername, csp.DbPassword);

		bool exists = mssql.CheckDbExists(siteName);
		if (!exists) {
			mssql.CreateDb(siteName, $"{siteName}.bak");
		}
		_k8.DeleteBackupImage(k8Commands.PodType.Mssql, $"{siteName}.bak");
		return 0;
	}

	private int DoPgWork(DirectoryInfo unzippedDirectory, string destDbName){
		string tmpDbName = "template_" + unzippedDirectory.Name;
		k8Commands.ConnectionStringParams csp = _k8.GetPostgresConnectionString();
		Postgres postgres = new(csp.DbPort, csp.DbUsername, csp.DbPassword);

		CreatePgTemplate(unzippedDirectory, tmpDbName);
		postgres.CreateDbFromTemplate(tmpDbName, destDbName);
		Console.WriteLine($"[Database created] - {destDbName}");
		return 0;
	}

	private Version GetLatestProductVersion(string latestBranchPath, Version latestVersion, string product,
		CreatioRuntimePlatform platform){
		string dirPath = Path.Combine(latestBranchPath, latestVersion.ToString(),
			GetProductDirectoryName(product, platform));
		if (Directory.Exists(dirPath)) {
			return latestVersion;
		}
		Version previousVersion = new Version(latestVersion.Major, latestVersion.Minor, latestVersion.Build,
			latestVersion.Revision - 1);
		return GetLatestProductVersion(latestBranchPath, previousVersion, product, platform);
	}

	private string GetProductDirectoryName(string product, CreatioRuntimePlatform platform){
		return $"{product}{platform.ToRuntimePlatformString()}_Softkey_ENU";
	}

	private string GetProductFileNameWithoutBuildNumber(string product, CreatioDBType creatioDBType,
		CreatioRuntimePlatform creatioRuntimePlatform){
		return $"_{product}{creatioRuntimePlatform.ToRuntimePlatformString()}_Softkey_{creatioDBType}_ENU.zip";
	}

	private async Task<int> UpdateConnectionString(DirectoryInfo unzippedDirectory, PfInstallerOptions options){
		Console.WriteLine("[CheckUpdate connection string] - Started");
		InstallerHelper.DatabaseType dbType = InstallerHelper.DetectDataBase(unzippedDirectory);
		k8Commands.ConnectionStringParams csParam = dbType switch {
			InstallerHelper.DatabaseType.Postgres => _k8.GetPostgresConnectionString(),
			InstallerHelper.DatabaseType.MsSql => _k8.GetMssqlConnectionString()
		};

		int redisDb = FindEmptyRedisDb(csParam.RedisPort);

		ConfigureConnectionStringRequest request = dbType switch {
			InstallerHelper.DatabaseType.Postgres => new ConfigureConnectionStringRequest {
				Arguments = new Dictionary<string, string> {
					{"folderPath", Path.Join(_iisRootFolder, options.SiteName)}, {
						"dbString",
						$"Server=127.0.0.1;Port={csParam.DbPort};Database={options.SiteName};User ID={csParam.DbUsername};password={csParam.DbPassword};Timeout=500; CommandTimeout=400;MaxPoolSize=1024;"
					},
					{"redis", $"host=127.0.0.1;db={redisDb};port={csParam.RedisPort}"}, {
						"isNetFramework",
						(InstallerHelper.DetectFramework(unzippedDirectory) ==
							InstallerHelper.FrameworkType.NetFramework).ToString()
					}
				}
			},
			InstallerHelper.DatabaseType.MsSql => new ConfigureConnectionStringRequest {
				Arguments = new Dictionary<string, string> {
					{"folderPath", Path.Join(_iisRootFolder, options.SiteName)}, {
						"dbString",
						$"Data Source=127.0.0.1,{csParam.DbPort};Initial Catalog={options.SiteName};User Id={csParam.DbUsername}; Password={csParam.DbPassword};MultipleActiveResultSets=True;Pooling=true;Max Pool Size=100"
					},
					{"redis", $"host=127.0.0.1;db={redisDb};port={csParam.RedisPort}"}, {
						"isNetFramework",
						(InstallerHelper.DetectFramework(unzippedDirectory) ==
							InstallerHelper.FrameworkType.NetFramework).ToString()
					}
				}
			}
		};

		return (await _mediator.Send(request)).Value switch {
			(HandlerError error) => ExitWithErrorMessage(error.ErrorDescription),
			(ConfigureConnectionStringResponse {
				Status: BaseHandlerResponse.CompletionStatus.Success
			} result) => ExitWithOkMessage(result.Description),
			(ConfigureConnectionStringResponse {
				Status: BaseHandlerResponse.CompletionStatus.Failure
			} result) => ExitWithErrorMessage(result.Description),
			_ => ExitWithErrorMessage("Unknown error occured")
		};
	}

	#endregion

	#region Methods: Internal

	internal string GetBuildFilePathFromOptions(string remoteArtifactServerPath, string product,
		CreatioDBType creatioDBType, CreatioRuntimePlatform platform){
		Version latestBranchVersion = GetLatestVersion(remoteArtifactServerPath);
		string latestBranchesBuildPath = Path.Combine(remoteArtifactServerPath, latestBranchVersion.ToString());
		IDirectoryInfo latestBranchesDireInfo = _fileSystem.GetDirectoryInfo(latestBranchesBuildPath);
		IOrderedEnumerable<IDirectoryInfo> latestBranchSubdirectories = latestBranchesDireInfo.GetDirectories()
			.OrderByDescending(dir => dir.CreationTimeUtc);
		List<IDirectoryInfo> revisionDirectories = new List<IDirectoryInfo>();
		foreach (IDirectoryInfo subdir in latestBranchSubdirectories) {
			if (Version.TryParse(subdir.Name, out Version ver)) {
				revisionDirectories.Add(subdir);
			}
		}
		if (revisionDirectories.Count == 0) {
			revisionDirectories.Add(latestBranchesDireInfo);
		}
		string productZipFileName = GetProductFileNameWithoutBuildNumber(product, creatioDBType, platform);
		foreach (IDirectoryInfo searchDir in revisionDirectories) {
			IOrderedEnumerable<IFileInfo> zipFiles = searchDir.GetFiles("*.zip", SearchOption.AllDirectories).ToList()
				.OrderByDescending(product => product.LastWriteTime);
			foreach (IFileInfo zipFile in zipFiles) {
				if (zipFile.Name.Contains(productZipFileName, StringComparison.OrdinalIgnoreCase)) {
					return zipFile.FullName;
				}
			}
		}
		throw new ItemNotFoundException(productZipFileName);
	}

	internal Version GetLatestVersion(string remoteArtifactServerPath){
		string[] branches = _fileSystem.GetDirectories(remoteArtifactServerPath);
		//var branches = Directory.GetDirectories(remoteArtifactServerPath);
		List<Version> version = new List<Version>();
		foreach (string branch in branches) {
			string branchName = branch.Split('\\').Last();
			if (Version.TryParse(branchName, out Version ver)) {
				version.Add(ver);
			}
		}
		return version.Max();
	}

	#endregion

	#region Methods: Public

	public override int Execute(PfInstallerOptions options){
		if (string.IsNullOrEmpty(options.ZipFile) && !string.IsNullOrEmpty(options.Product)) {
			options.ZipFile = GetBuildFilePathFromOptions(options.Product, options.DBType, options.RuntimePlatform);
		}
		if (!File.Exists(options.ZipFile)) {
			Console.WriteLine($"Could not find zip file: {options.ZipFile}");
			return 1;
		}
		if (!Directory.Exists(_iisRootFolder)) {
			Directory.CreateDirectory(_iisRootFolder);
		}
		while (string.IsNullOrEmpty(options.SiteName)) {
			Console.WriteLine("Please enter site name:");
			options.SiteName = Console.ReadLine();

			if (Directory.Exists(Path.Join(_iisRootFolder, options.SiteName))) {
				Console.WriteLine(
					$"Site with name {options.SiteName} already exists in {Path.Join(_iisRootFolder, options.SiteName)}");
				options.SiteName = string.Empty;
			}
		}

		while (options.SitePort is <= 0 or > 65536) {
			Console.WriteLine(
				$"Please enter site port, Max value - 65535:{Environment.NewLine}(recommended range between 40000 and 40100)");
			if (int.TryParse(Console.ReadLine(), out int value)) {
				options.SitePort = value;
			} else {
				Console.WriteLine("Site port must be an in value");
			}
		}

		options.ZipFile = CopyLocalWhenNetworkDrive(options.ZipFile);
		Console.WriteLine($"[Staring unzipping] - {options.ZipFile}");
		DirectoryInfo unzippedDirectory = InstallerHelper.UnzipOrTakeExisting(options.ZipFile, _packageArchiver);
		Console.WriteLine($"[Unzip completed] - {unzippedDirectory.FullName}");
		Console.WriteLine();

		int dbRestoreResult = InstallerHelper.DetectDataBase(unzippedDirectory) switch {
			InstallerHelper.DatabaseType.MsSql => DoMsWork(unzippedDirectory, options.SiteName),
			_ => DoPgWork(unzippedDirectory, options.SiteName)
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

		_ = updateConnectionStringResult switch {
			0 => StartWebBrowser(options),
			_ => ExitWithErrorMessage($"Could not open: http://{InstallerHelper.FetFQDN()}:{options.SitePort}")
		};

		Console.WriteLine("Press any key to exit...");
		Console.ReadKey();
		return 0;
	}

	public string GetBuildFilePathFromOptions(string product, CreatioDBType dBType,
		CreatioRuntimePlatform runtimePlatform){
		return GetBuildFilePathFromOptions(RemoteArtefactServerPath, product, dBType, runtimePlatform);
	}

	#endregion

}
*/