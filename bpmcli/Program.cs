using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Json;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using bpmcli.environment;
using CommandLine;
using ConsoleTables;
using Newtonsoft.Json;
using Bpmonline.Client;
using bpmcli.Feature;

namespace bpmcli
{

	public class StringParser
	{
		public static IEnumerable<string> ParseArray(string input) {
			return input.Split(',').Select(p => p.Trim()).ToList();
		}
	}

	class Program {
		private static string _userName => _settings.Login;
		private static string _userPassword => _settings.Password;
		private static string _url => _settings.Uri; // Необходимо получить из конфига
		private static bool _isNetCore => _settings.IsNetCore;
		private static EnvironmentSettings _settings;
		private static string _environmentName;

		private static string ExecutorUrl => _url + @"/0/IDE/ExecuteScript";
		private static string UnloadAppDomainUrl => _url + @"/0/ServiceModel/AppInstallerService.svc/UnloadAppDomain";
		private static string DownloadPackageUrl => _url + @"/0/ServiceModel/AppInstallerService.svc/LoadPackagesToFileSystem";
		private static string UploadPackageUrl => _url + @"/0/ServiceModel/AppInstallerService.svc/LoadPackagesToDB";
		private static string UploadUrl => _url + @"/0/ServiceModel/PackageInstallerService.svc/UploadPackage";
		private static string InstallUrl => _url + @"/0/ServiceModel/PackageInstallerService.svc/InstallPackage";
		private static string LogUrl => _url + @"/0/ServiceModel/PackageInstallerService.svc/GetLogFile";
		private static string SelectQueryUrl => _url + @"/0/DataService/json/SyncReply/SelectQuery";
		private static string DeletePackageUrl => _url + @"/0/ServiceModel/AppInstallerService.svc/DeletePackage";
		private static string ClearRedisDbUrl => _url + @"/0/ServiceModel/AppInstallerService.svc/ClearRedisDb";
		private static string GetZipPackageUrl => _url + @"/0/ServiceModel/PackageInstallerService.svc/GetZipPackages";

		private static string LastVersionUrl => "https://api.github.com/repos/Advance-Technologies-Foundation/bpmcli/releases/latest";
		private static string ExecuteSqlScriptUrl => _url + @"/0/rest/BpmcliApiGateway/ExecuteSqlScript";
		private static string ApiVersionUrl => _url + @"/0/rest/BpmcliApiGateway/GetApiVersion";

		private static string DefLogFileName => "bpmclilog.txt";

		private static string InsertSysSettingsUrl => _url + @"/0/DataService/json/SyncReply/InsertSysSettingRequest";
		private static string PostSysSettingsValuesUrl => _url + @"/0/DataService/json/SyncReply/PostSysSettingsValues";

		private static string GetEntityModelsUrl => _url + @"/0/rest/BpmcliApiGateway/GetEntitySchemaModels/{0}";

		private static BpmonlineClient BpmonlineClient {
			get => new BpmonlineClient(_url, _userName, _userPassword, _isNetCore);
		}


		private static string CurrentProj =>
			new DirectoryInfo(Environment.CurrentDirectory).GetFiles("*.csproj").FirstOrDefault()?.FullName;

		public static bool _safe { get; private set; } = true;

		private static void Configure(EnvironmentOptions options) {
			var settingsRepository = new SettingsRepository();
			_environmentName = options.Environment;
			_settings = settingsRepository.GetEnvironment(options);
		}

		private static void CheckUpdate() {
			var currentVersion = GetCurrentVersion();
			var latestVersion = GetLatestVersion();
			if (currentVersion != latestVersion) {
				MessageToConsole($"You are using bpmcli version {currentVersion}, however version {latestVersion} is available." +
								 $"{Environment.NewLine}You should consider upgrading via the \'bpmcli update-cli\' command.",
					ConsoleColor.DarkYellow);
			}
		}

		private static int UpdateCli() {
			try {
				var url = GetLastReleaseUrl();
				var dir = AppDomain.CurrentDomain.BaseDirectory;
				string updaterDirPath = Path.Combine(dir, "Update");
				string tempDirPath = Path.Combine(dir, "Update", "Temp");
				string filePath = Path.Combine(updaterDirPath, "update.zip");
				string updaterName = "updater.dll";
				Directory.CreateDirectory(tempDirPath);
				Console.WriteLine("Download update.");
				using (var client = new WebClient()) {
					client.DownloadFile(url, filePath);
				}
				ZipFile.ExtractToDirectory(filePath, tempDirPath, true);
				var updaterFile = new FileInfo(Path.Combine(tempDirPath, updaterName));
				updaterFile.CopyTo(Path.Combine(dir, updaterFile.Name), true);
				var updateCmdPath = Path.Combine(dir, "update.cmd");
				var proc = new Process { StartInfo = { FileName = updateCmdPath } };
				Console.WriteLine("Start update.");
				proc.Start();
				return 0;
			} catch (Exception) {
				Console.WriteLine("Update error.");
				return 1;
			}
		}

		private static int UpdateGate(EnvironmentOptions options) {
			try {
				Configure(options);
				var dir = AppDomain.CurrentDomain.BaseDirectory;
				string packageFilePath = Path.Combine(dir, "bpmcligate", "bpmcligate.gz");
				InstallPackage(packageFilePath);
				RestartInternal();
				return 0;
			} catch (Exception e) {
				Console.WriteLine($"Update error {e.Message}");
				return 1;
			}
		}

		private static string GetLastReleaseUrl() {
			System.Threading.Tasks.Task<byte[]> body;
			using (var client = new HttpClient()) {
				client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
				client.DefaultRequestHeaders.UserAgent.TryParseAdd("request");
				using (var response = client.GetAsync(LastVersionUrl).Result) {
					response.EnsureSuccessStatusCode();
					body = response.Content.ReadAsByteArrayAsync();
				}
			}
			string json;
			var jsonStream = new MemoryStream(body.Result) { Position = 0 };
			using (var reader = new StreamReader(jsonStream, Encoding.UTF8)) {
				json = reader.ReadToEnd();
			}
			JsonObject jsonDoc = (JsonObject)JsonValue.Parse(json);
			var url = jsonDoc["assets"][0]["browser_download_url"];
			return url;
		}

		private static void MessageToConsole(string text, ConsoleColor color) {
			var currentColor = Console.ForegroundColor;
			Console.ForegroundColor = color;
			Console.WriteLine(text);
			Console.ForegroundColor = currentColor;
		}

		private static string GetCurrentVersion() {
			System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
			var fileVersionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
			return fileVersionInfo.FileVersion;
		}

		private static string GetLatestVersion() {
			System.Threading.Tasks.Task<byte[]> body;
			using (var client = new HttpClient()) {
				client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
				client.DefaultRequestHeaders.UserAgent.TryParseAdd("request");
				using (var response = client.GetAsync(LastVersionUrl).Result) {
					response.EnsureSuccessStatusCode();
					body = response.Content.ReadAsByteArrayAsync();
				}
			}
			string json;
			var jsonStream = new MemoryStream(body.Result) { Position = 0 };
			using (var reader = new StreamReader(jsonStream, Encoding.UTF8)) {
				json = reader.ReadToEnd();
			}
			var jsonDoc = (JsonObject)JsonValue.Parse(json);
			var version = jsonDoc["tag_name"];
			return version;
		}

		public static void SetupAppConnection(EnvironmentOptions options) {
			Configure(options);
			CheckApiVersion();
		}



		public static void CheckApiVersion() {
			var dir = AppDomain.CurrentDomain.BaseDirectory;
			string versionFilePath = Path.Combine(dir, "bpmcligate", "version.txt");
			var localApiVersion = new Version(File.ReadAllText(versionFilePath));
			var appApiVersion = GetAppApiVersion();
			if (appApiVersion == new Version("0.0.0.0")) {
				MessageToConsole($"Your app does not contain bpmcli API." +
				 $"{Environment.NewLine}You should consider install it via the \'bpmcli install-gate\' command.", ConsoleColor.DarkYellow);
			} else if (localApiVersion > appApiVersion) {
				MessageToConsole($"You are using bpmcli api version {appApiVersion}, however version {localApiVersion} is available." +
				 $"{Environment.NewLine}You should consider upgrading via the \'bpmcli update-gate\' command.", ConsoleColor.DarkYellow);
			}
		}

	
		private static Version GetAppApiVersion() {
			var apiVersion = new Version("0.0.0.0");
			try {
				string appVersionResponse = BpmonlineClient.ExecuteGetRequest(ApiVersionUrl).Trim('"');
				apiVersion = new Version(appVersionResponse);
			} catch (Exception) {
			}
			return apiVersion;
		}

		private static void ExecuteScript(ExecuteAssemblyOptions options) {
			string filePath = options.Name;
			string executorType = options.ExecutorType;
			var fileContent = File.ReadAllBytes(filePath);
			string body = Convert.ToBase64String(fileContent);
			string requestData = @"{""Body"":""" + body + @""",""LibraryType"":""" + executorType + @"""}";
			var responseFromServer = BpmonlineClient.ExecutePostRequest(ExecutorUrl, requestData);
			Console.WriteLine(responseFromServer);
		}

		private static void RestartInternal() {
			BpmonlineClient.ExecutePostRequest(UnloadAppDomainUrl, @"{}");
		}

		private static void ClearRedisDbInternal() {
			BpmonlineClient.ExecutePostRequest(ClearRedisDbUrl, @"{}");
		}


		private static void DownloadPackages(string packageName) {
			string requestData = "[\"" + packageName + "\"]";
			string responseFromServer = BpmonlineClient.ExecutePostRequest(DownloadPackageUrl, requestData);
			Console.WriteLine(packageName + " - " + responseFromServer);
		}

		private static void UploadPackages(string packageName) {
			string requestData = "[\"" + packageName + "\"]";
			string responseFromServer = BpmonlineClient.ExecutePostRequest(UploadPackageUrl, requestData);
			Console.WriteLine(packageName + " - " + responseFromServer);
		}

		private static void CompressionProjects(string sourcePath, string destinationPath, IEnumerable<string> names, bool skipPdb) {
			string tempPath = Path.Combine(Path.GetTempPath(), "Application_");// + DateTime.Now.ToShortDateString());
			if (Directory.Exists(tempPath)) {
				Directory.Delete(tempPath, true);
			}
			if (sourcePath == null) {
				sourcePath = Environment.CurrentDirectory;
			}
			Directory.CreateDirectory(tempPath);
			foreach (var name in names) {
				var currentSourcePath = Path.Combine(sourcePath, name);
				var currentDestinationPath = Path.Combine(tempPath, name + ".gz");
				CompressionProject(currentSourcePath, currentDestinationPath, skipPdb);
			}
			ZipFile.CreateFromDirectory(tempPath, destinationPath);
		}

		internal static IEnumerable<string> GetPackages(string inputline) {
			return StringParser.ParseArray(inputline);
		}

		private static void CompressionProject(string sourcePath, string destinationPath, bool skipPdb) {
			if (File.Exists(destinationPath)) {
				File.Delete(destinationPath);
			}
			string tempPath = CreateTempPath(sourcePath);
			CopyProjectFiles(sourcePath, tempPath);

			var files = Directory.GetFiles(tempPath, "*.*", SearchOption.AllDirectories)
				.Where(name => !name.EndsWith(".pdb") || !skipPdb);
			int directoryPathLength = tempPath.Length;
			using (Stream fileStream =
				File.Open(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None)) {
				using (var zipStream = new GZipStream(fileStream, CompressionMode.Compress)) {
					foreach (string filePath in files) {
						CompressionUtilities.ZipFile(filePath, directoryPathLength, zipStream);
					}
				}
			}
			Directory.Delete(tempPath, true);
		}

		private static string CreateTempPath(string sourcePath) {
			var directoryInfo = new DirectoryInfo(sourcePath);
			string tempPath = Path.Combine(Path.GetTempPath(), directoryInfo.Name);
			return tempPath;
		}

		private static void CopyProjectFiles(string sourcePath, string destinationPath) {
			if (Directory.Exists(destinationPath)) {
				Directory.Delete(destinationPath, true);
			}
			Directory.CreateDirectory(destinationPath);
			CopyProjectElement(sourcePath, destinationPath, "Assemblies");
			CopyProjectElement(sourcePath, destinationPath, "Bin");
			CopyProjectElement(sourcePath, destinationPath, "Data");
			CopyProjectElement(sourcePath, destinationPath, "Files");
			CopyProjectElement(sourcePath, destinationPath, "Resources");
			CopyProjectElement(sourcePath, destinationPath, "Schemas");
			CopyProjectElement(sourcePath, destinationPath, "SqlScripts");
			File.Copy(Path.Combine(sourcePath, "descriptor.json"), Path.Combine(destinationPath, "descriptor.json"));
		}

		private static void DownloadZipPackagesInternal(string packageName, string destinationPath) {
			try {
				Console.WriteLine("Start download packages ({0}).", packageName);
				var packageNames = string.Format("\"{0}\"", packageName.Replace(" ", string.Empty).Replace(",", "\",\""));
				string requestData = "[" + packageNames + "]";
				BpmonlineClient.DownloadFile(GetZipPackageUrl, destinationPath, requestData);
				Console.WriteLine("Download packages ({0}) completed.", packageName);
			} catch (Exception) {
				Console.WriteLine("Download packages ({0}) not completed.", packageName);
			}
		}

		private static int ExecuteSqlScript(ExecuteSqlScriptOptions opts) {
			try {
				SetupAppConnection(opts);
				string result = string.Empty;
				if (!string.IsNullOrEmpty(opts.Script)) {
					result = ExecuteSqlScript(opts.Script);
				} else if (!string.IsNullOrEmpty(opts.File)) {
					var script = File.ReadAllText(opts.File);
					Console.WriteLine(script);
					script = script.Replace(Environment.NewLine, "|nl|");
					result = ExecuteSqlScript(script);
				} else {
					Console.WriteLine("Enter sql (Ctrl+C for exit): ");
					var sc = Console.ReadLine();
					result = ExecuteSqlScript(sc);
				}
				result = GetSqlScriptResult(result, opts.ViewType);
				Console.WriteLine(result);
				if (opts.DestPath != null) {
					File.WriteAllText(opts.DestPath, result);
				}
				Console.WriteLine("Done");
			} catch (Exception e) {
				Console.WriteLine(e);
			}
			return 0;
		}

		private static string GetSqlScriptResult(string result, string viewType) {
			viewType = viewType.ToLower();
			if (viewType == "table") {
				if (result == "[]") {
					return string.Empty;
				}
				if (int.TryParse(result, out var count)) {
					return $"({count} rows affected)";
				}
				var dataTable = JsonConvert.DeserializeObject<DataTable>(result);
				var table = CreateConsoleTable(dataTable);
				return table.ToString();
			}
			return result;
		}

		private static string CorrectJson(string body) {
			body = body.Replace("\\\\r\\\\n", Environment.NewLine);
			body = body.Replace("\\r\\n", Environment.NewLine);
			body = body.Replace("\\\\t", Convert.ToChar(9).ToString());
			body = body.Replace("\\\"", "\"");
			body = body.Replace("\\\\", "\\");
			body = body.Trim(new Char[] { '\"' });
			return body;
		}

		private static string ExecuteSqlScript(string script) {
			var scriptData = "{ \"script\":\"" + script + "\"}";
			string responseFormServer = BpmonlineClient.ExecutePostRequest(ExecuteSqlScriptUrl, scriptData);
			return CorrectJson(responseFormServer);
		}

		private static ConsoleTable CreateConsoleTable(DataTable dataTable) {
			var table = new ConsoleTable();
			foreach (var column in dataTable.Columns) {
				table.AddColumn(new [] { column.ToString() });
			}
			for (var i = 0; i < dataTable.Rows.Count; i++) {
				table.AddRow(dataTable.Rows[i].ItemArray);
			}
			return table;
		}

		private static void UnZipPackages(string zipFilePath) {
			var fileInfo = new FileInfo(zipFilePath);
			if (fileInfo.Length == 0) {
				throw new Exception("CompressionUtilities.Exception.FileIsEmpty");
			}
			string targetDirectoryPath = GetPackagePathFromZipFile(zipFilePath, ".zip");
			if (Directory.Exists(targetDirectoryPath)) {
				Directory.Delete(targetDirectoryPath, true);
			}
			ZipFile.ExtractToDirectory(zipFilePath, targetDirectoryPath);
			foreach (var filePath in Directory.GetFiles(targetDirectoryPath)) {
				string packageName = GetPackagePathFromZipFile(new FileInfo(filePath).Name, ".gz");
				string currentDirectoryPath = Path.Combine(Environment.CurrentDirectory, packageName);
				Console.WriteLine("Start unzip package ({0}).", packageName);
				using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None)) {
					using (var zipStream = new GZipStream(fileStream, CompressionMode.Decompress, true)) {
						while (CompressionUtilities.UnzipFile(currentDirectoryPath, zipStream)) {
						}
					}
				}
				Console.WriteLine("Unzip package ({0}) completed.", packageName);
			}
		}

		private static string GetPackagePathFromZipFile(string filePath, string zipFileExtention) {
			string targetDirectoryPath = filePath.Remove(filePath.IndexOf(zipFileExtention,
				StringComparison.Ordinal), zipFileExtention.Length);
			return targetDirectoryPath;
		}

		internal static void CopyProjectElement(string sourcePath, string destinationPath, string name) {
			string fromAssembliesPath = Path.Combine(sourcePath, name);
			if (Directory.Exists(fromAssembliesPath)) {
				string toAssembliesPath = Path.Combine(destinationPath, name);
				CopyDir(fromAssembliesPath, toAssembliesPath);
			}
		}

		internal static void CopyDir(string source, string dest) {
			if (String.IsNullOrEmpty(source) || String.IsNullOrEmpty(dest))
				return;
			Directory.CreateDirectory(dest);
			foreach (string fn in Directory.GetFiles(source)) {
				File.Copy(fn, Path.Combine(dest, Path.GetFileName(fn)), true);
			}
			foreach (string dirFn in Directory.GetDirectories(source)) {
				CopyDir(dirFn, Path.Combine(dest, Path.GetFileName(dirFn)));
			}
		}

		private static string InstallPackage(string filePath) {
			string fileName = string.Empty;
			try {
				fileName = UploadPackage(filePath);
			} catch (Exception e) {
				Console.WriteLine(e.Message);
				return e.Message;
			}
			Console.WriteLine($"Install {fileName} ...");
			var installResponse = BpmonlineClient.ExecutePostRequest(InstallUrl, "\"" + fileName + "\"", 600000);
			if (_settings.DeveloperModeEnabled.HasValue && _settings.DeveloperModeEnabled.Value) {
				UnlockMaintainerPackageInternal();
				RestartInternal();
			}
			var logText = GetLog();
			Console.WriteLine("Installation log:");
			Console.WriteLine(logText);
			return logText;
		}

		private static void DeletePackage(string code) {
			Console.WriteLine("Deleting...");
			string deleteRequestData = "\"" + code + "\"";
			BpmonlineClient.ExecutePostRequest(DeletePackageUrl, deleteRequestData);
			Console.WriteLine("Deleted.");
		}

		private static string UploadPackage(string filePath) {
			Console.WriteLine("Uploading...");
			FileInfo fileInfo = new FileInfo(filePath);
			string fileName = fileInfo.Name;
			BpmonlineClient.UploadFile(UploadUrl, filePath);
			Console.WriteLine("Uploaded");
			return fileName;
		}

		private static int Execute(ExecuteAssemblyOptions options) {
			Configure(options);
			ExecuteScript(options);
			return 0;
		}

		private static int Register(RegisterOptions options) {
			try {
				var bpmcliEnv = new BpmcliEnvironment();
				string path = string.IsNullOrEmpty(options.Path) ? Environment.CurrentDirectory : options.Path;
				IResult result = options.Target == "m"
					? bpmcliEnv.MachineRegisterPath(path)
					: bpmcliEnv.UserRegisterPath(path);
				result.ShowMessagesTo(Console.Out);
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e);
				return 1;
			}
		}

		private static int Restart(RestartOptions options) {
			try {
				options.Environment = options.Environment ?? options.Name;
				SetupAppConnection(options);
				RestartInternal();
				Console.WriteLine("Done");
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e);
				return 1;
			}
		}

		private static int ClearRedisDb(ClearRedisOptions options) {
			try {
				options.Environment = options.Environment ?? options.Name;
				SetupAppConnection(options);
				ClearRedisDbInternal();
				Console.WriteLine("Done");
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e);
				return 1;
			}
		}

		private static int DownloadZipPackages(PullPkgOptions options) {
			try {
				SetupAppConnection(options);
				string destPath = options.DestPath != null
					? options.DestPath
					: Path.Combine(Path.GetTempPath(), "packages.zip");
				DownloadZipPackagesInternal(options.Name, destPath);
				UnZipPackages(destPath);
				Console.WriteLine("Done");
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e);
				return 1;
			}
		}

		private static int Compression(GeneratePkgZipOptions options) {
			try {
				if (options.Packages == null) {
					var destinationPath = string.IsNullOrEmpty(options.DestinationPath) ? $"{options.Name}.gz" : options.DestinationPath;
					CompressionProject(options.Name, destinationPath, options.SkipPdb);
				} else {
					var packages = GetPackages(options.Packages);
					string zipFileName = $"packages_{DateTime.Now.ToString("yy.MM.dd_hh.mm.ss")}.zip";
					var destinationPath = string.IsNullOrEmpty(options.DestinationPath) ? zipFileName : options.DestinationPath;
					CompressionProjects(options.Name, destinationPath, packages, options.SkipPdb);
				}
				Console.WriteLine("Done");
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e.Message);
				return 1;
			}
		}

		private static int Install(PushPkgOptions options) {
			try {
				SetupAppConnection(options);
				if (options.Name == null) {
					options.Name = Environment.CurrentDirectory;
				}
				string logText = string.Empty;
				if (File.Exists(options.Name)) {
					logText = InstallPackage(options.Name);
				} else {
					if (Directory.Exists(options.Name)) {
						var folderPath = options.Name;
						var filePath = options.Name + ".gz";
						CompressionProject(folderPath, filePath, false);
						logText = InstallPackage(filePath);
						File.Delete(filePath);
					}
				}
				if (options.ReportPath != null) {
					SaveLogFile(logText, options.ReportPath);
				}
				Console.WriteLine("Done");
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e.Message);
				return 1;
			}
		}

		private static int Delete(DeletePkgOptions options) {
			try {
				SetupAppConnection(options);
				DeletePackage(options.Name);
				Console.WriteLine("Done");
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e.Message);
				return 1;
			}
		}

		private static string GetLog() {
			return BpmonlineClient.ExecuteGetRequest(LogUrl);
		}

		private static void SaveLogFile(string logText, string reportPath) {
			if (File.Exists(reportPath)) {
				File.Delete(reportPath);
				File.WriteAllText(reportPath, logText, Encoding.UTF8);
			} else if (Directory.Exists(reportPath)) {
				reportPath = Path.Combine(reportPath, DefLogFileName);
			}
			File.WriteAllText(reportPath, logText, Encoding.UTF8);
		}

		private static int Main(string[] args) {
			var autoupdate = new SettingsRepository().GetAutoupdate();
			if (autoupdate) {
				new Thread(CheckUpdate).Start();
			}

			var bpmcliEnvironment = new BpmcliEnvironment();
			string helpFolderName = $"help";
			string helpDirectoryPath = helpFolderName;
			var envPath = bpmcliEnvironment.GetRegisteredPath();
			helpDirectoryPath = Path.Combine(envPath ?? string.Empty, helpFolderName);
			Parser.Default.Settings.ShowHeader = false;
			Parser.Default.Settings.HelpDirectory = helpDirectoryPath;
			return Parser.Default.ParseArguments<ExecuteAssemblyOptions, RestartOptions, ClearRedisOptions, FetchOptions,
					RegAppOptions, AppListOptions, UnregAppOptions, GeneratePkgZipOptions, PushPkgOptions,
					DeletePkgOptions, ReferenceOptions, NewPkgOptions, ConvertOptions, RegisterOptions, PullPkgOptions,
					UpdateCliOptions, ExecuteSqlScriptOptions, InstallGateOptions, ItemOptions, DeveloperModeOptions,
					SysSettingsOptions, FeatureOptions>(args)
				.MapResult(
					(ExecuteAssemblyOptions opts) => Execute(opts),
					(RestartOptions opts) => Restart(opts),
					(ClearRedisOptions opts) => ClearRedisDb(opts),
					(FetchOptions opts) => Fetch(opts),
					(RegAppOptions opts) => RegAppCommand.RegApp(opts),
					(AppListOptions opts) => ShowAppListCommand.ShowAppList(opts),
					(UnregAppOptions opts) => UnregAppCommand.UnregApplication(opts),
					(GeneratePkgZipOptions opts) => Compression(opts),
					(PushPkgOptions opts) => Install(opts),
					(DeletePkgOptions opts) => Delete(opts),
					(ReferenceOptions opts) => ReferenceTo(opts),
					(NewPkgOptions opts) => NewPkg(opts),
					(ConvertOptions opts) => ConvertPackage(opts),
					(RegisterOptions opts) => Register(opts),
					(PullPkgOptions opts) => DownloadZipPackages(opts),
					(UpdateCliOptions opts) => UpdateCli(),
					(ExecuteSqlScriptOptions opts) => ExecuteSqlScript(opts),
					(InstallGateOptions opts) => UpdateGate(opts),
					(ItemOptions opts) => AddItem(opts),
					(DeveloperModeOptions opts) => SetDeveloperMode(opts),
					(SysSettingsOptions opts) => SetSysSettings(opts),
					(FeatureOptions opts) => SetFeatureState(opts),
					errs => 1);
		}

		private static int SetDeveloperMode(DeveloperModeOptions opts) {
			try {
				SetupAppConnection(opts);
				var repository = new SettingsRepository();
				_settings.DeveloperModeEnabled = true;
				repository.ConfigureEnvironment(_environmentName, _settings);
				ExecuteSqlScript($"UPDATE SysSettingsValue SET TextValue = '{_settings.Maintainer}' where SysSettingsId = (select Id from SysSettings where Code = 'Maintainer')");
				UnlockMaintainerPackageInternal();
				RestartInternal();
				Console.WriteLine("Done");
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e);
				return 1;
			}
		}

		private static void UnlockMaintainerPackageInternal() {
			ExecuteSqlScript($"UPDATE SysPackage SET InstallType = 0 WHERE Maintainer = '{_settings.Maintainer}'");
		}

		private static int AddModels(ItemOptions opts) {
			try {
				SetupAppConnection(opts);
				var models = GetClassModels(opts.ItemName);
				var project = new Project(opts.DestinationPath, opts.Namespace);
				foreach (var model in models) {
					project.AddFile(model.Key, model.Value);
				}
				project.Reload();
				Console.WriteLine("Done");
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e);
				return 1;
			}
		}

		public class Project {

			public string DestPath { get; set; }

			public string Namespace { get; set; }

			public string ProjFile { get; set; }

			public Project(string destPath = null, string @namespace = null) {
				DestPath = destPath;
				Namespace = @namespace;
				if (string.IsNullOrEmpty(Namespace)) {
					var curDir = Environment.CurrentDirectory;
					ProjFile = Directory.GetFiles(curDir, "*.csproj").FirstOrDefault();
					if (File.Exists(ProjFile)) {
						Console.WriteLine($"Detected projFile {ProjFile}");
						var fileText = File.ReadAllText(ProjFile);
						int start = fileText.IndexOf("<RootNamespace>");
						int end = fileText.IndexOf("</RootNamespace>");
						if (end > start) {
							Namespace = fileText.Substring(start + 15, end - start - 15);
							Console.WriteLine($"Detected namespace {@Namespace}");
						}
						if (string.IsNullOrEmpty(DestPath)) {
							DestPath = $"{curDir}\\Files\\cs";
						}
					}

				}
			}

			public void AddFile(string name, string body) {
					Console.WriteLine($"Save {name} class");
					if (!string.IsNullOrEmpty(Namespace)) {
						body = body.Replace("<Namespace>", Namespace);
					}
					File.WriteAllText($"{DestPath}\\{name}.cs", body);
			}

			public void Reload() {
				if (File.Exists(ProjFile)) {
					File.AppendAllText(ProjFile, " ");
					var content = File.ReadAllText(ProjFile);
					File.WriteAllText(ProjFile, content.Substring(0, content.Length - 1));
					Console.WriteLine($"Modified proj file {ProjFile}");
				}
			}
		}

		private static int AddItem(ItemOptions options) {
			if (options.ItemType.ToLower() == "model") {
				return AddModels(options);
			} else {
				return AddItemFromTemplate(options);
			} 
		}

		private static void CreateSysSetting(SysSettingsOptions opts) {
			Guid id = Guid.NewGuid();
			string requestData = "{" + string.Format("\"id\":\"{0}\",\"name\":\"{1}\",\"code\":\"{1}\",\"valueTypeName\":\"{2}\",\"isCacheable\":true",
				id, opts.Code, opts.Type) + "}";
			try {
				BpmonlineClient.ExecutePostRequest(InsertSysSettingsUrl, requestData);
				Console.WriteLine("SysSettings with code: {0} created.", opts.Code);
			} catch {
				Console.WriteLine("SysSettings with code: {0} already exists.", opts.Code);
			}
		}

		private static void UpdateSysSetting(SysSettingsOptions opts) {
			string requestData = "{\"isPersonal\":false,\"sysSettingsValues\":{" + string.Format("\"{0}\":{1}", opts.Code, opts.Value) + "}}";
			try {
				BpmonlineClient.ExecutePostRequest(PostSysSettingsValuesUrl, requestData);
				Console.WriteLine("SysSettings with code: {0} updated.", opts.Code);
			} catch {
				Console.WriteLine("SysSettings with code: {0} is not updated.", opts.Code);
			}
		}

		private static int SetSysSettings(SysSettingsOptions opts) {
			try {
				Configure(opts);
				CreateSysSetting(opts);
				UpdateSysSetting(opts);
			} catch (Exception ex) {
				return 1;
			}
			return 0;
		}

		private static int SetFeatureState(FeatureOptions options) {
			try {
				Configure(options);
				var fm = new FeatureModerator(BpmonlineClient);
				switch (options.State) {
					case 0: 
						fm.SwitchFeatureOff(options.Code);
						break;
					case 1:
						fm.SwitchFeatureOn(options.Code);
						break;
					default:
						throw new NotSupportedException($"You use not supported feature state type {options.State}");
				}
			} catch (Exception exception) {
				Console.WriteLine(exception.Message);
				return 1;
			}
			return 0;
		}

		private static int AddItemFromTemplate(ItemOptions options) {
			try {
				var project = new Project(options.DestinationPath, options.Namespace);
				var bpmcliEnvironment = new BpmcliEnvironment();
				string tplPath = $"tpl{Path.DirectorySeparatorChar}{options.ItemType}-template.tpl";
				if (!File.Exists(tplPath)) {
					var envPath = bpmcliEnvironment.GetRegisteredPath();
					if (!string.IsNullOrEmpty(envPath)) {
						tplPath = Path.Combine(envPath, tplPath);
					}
				}
				string templateBody = File.ReadAllText(tplPath);
				project.AddFile(options.ItemName, templateBody.Replace("<Name>", options.ItemName));
				project.Reload();
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e);
				return 1;
			}
		}

		private static Dictionary<string, string> GetClassModels(string entitySchemaName) {
			var url = string.Format(GetEntityModelsUrl, entitySchemaName);
			string responseFormServer = BpmonlineClient.ExecuteGetRequest(url);
			var result = CorrectJson(responseFormServer);
			return JsonConvert.DeserializeObject<Dictionary<string, string>>(result);
		}

		private static int ConvertPackage(ConvertOptions opts) {
			return PackageConverter.Convert(opts);
		}

		private static int Fetch(FetchOptions opts) {
			try {
				SetupAppConnection(opts);
				var packages = GetPackages(opts.Name);
				foreach (var package in packages) {
					if (opts.Operation == "load") {
						DownloadPackages(package);
					} else {
						UploadPackages(package);
					}
				}
				Console.WriteLine("Done");
			} catch (Exception e) {
				Console.WriteLine(e);
			}
			return 0;
		}

		private static int NewPkg(NewPkgOptions options) {
			var settings = new SettingsRepository().GetEnvironment();
			try {
				var packageName = options.Name;
				var packageDirectory = Directory.CreateDirectory(packageName);
				Directory.SetCurrentDirectory(packageDirectory.FullName);
				var pkg = BpmPkg.CreatePackage(options.Name, settings.Maintainer);
				pkg.Create();
				if (!String.IsNullOrEmpty(options.Rebase) && options.Rebase != "nuget") {
					ReferenceTo(new ReferenceOptions { ReferenceType = options.Rebase });
					pkg.RemovePackageConfig();
				}
				Console.WriteLine("Done");
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e);
				return 1;
			}
		}

		private static int ReferenceTo(ReferenceOptions options) {
			options.Path = options.Path ?? CurrentProj;
			if (string.IsNullOrEmpty(options.Path)) {
				throw new ArgumentNullException(nameof(options.Path));
			}
			if (!string.IsNullOrEmpty(options.RefPattern)) {
				options.ReferenceType = "custom";
			}
			try {
				switch (options.ReferenceType) {
					case "bin": {
							BpmPkgProject.LoadFromFile(options.Path)
							.RefToBin()
							.SaveChanges();
						}
						break;
					case "src": {
							BpmPkgProject.LoadFromFile(options.Path)
							.RefToCoreSrc()
							.SaveChanges();
						}
						break;
					case "custom": {
							BpmPkgProject.LoadFromFile(options.Path)
								.RefToCustomPath(options.RefPattern)
								.SaveChanges();
						}
						break;
					case "unit-bin": {
							BpmPkgProject.LoadFromFile(options.Path)
								.RefToUnitBin()
								.SaveChanges();
						}
						break;
					case "unit-src": {
							BpmPkgProject.LoadFromFile(options.Path)
								.RefToUnitCoreSrc()
								.SaveChanges();
						}
						break;
					default: {
							throw new NotSupportedException($"You use not supported option type {options.ReferenceType}");
						}
				}
				Console.WriteLine("Done");
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e);
				return 1;
			}
		}
	}
}
