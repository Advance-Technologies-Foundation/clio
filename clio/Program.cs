using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using Clio.UserEnvironment;
using CommandLine;
using Newtonsoft.Json;
using Creatio.Client;
using Clio.Command;
using Clio.Command.UpdateCliCommand;

using Clio.Command.SqlScriptCommand;
using Clio.Command.SysSettingsCommand;
using Clio.Common;
using Clio.Project;
using Clio.Command.PackageCommand;

namespace Clio
{


	class Program {
		private static string _userName => _settings.Login;
		private static string _userPassword => _settings.Password;
		private static string _url => _settings.Uri; // Необходимо получить из конфига
		private static string _appUrl {
			get
			{
				if (_isNetCore) {
					return _url;
				} else	{
					return _url + @"/0";
				}
			}
		}
		private static bool _isNetCore => _settings.IsNetCore;
		private static EnvironmentSettings _settings;
		private static string _environmentName;
		private static string UploadUrl => _appUrl + @"/ServiceModel/PackageInstallerService.svc/UploadPackage";
		private static string InstallUrl => _appUrl + @"/ServiceModel/PackageInstallerService.svc/InstallPackage";
		private static string LogUrl => _appUrl + @"/ServiceModel/PackageInstallerService.svc/GetLogFile";
		private static string DeletePackageUrl => _appUrl + @"/ServiceModel/AppInstallerService.svc/DeletePackage";
		private static string GetZipPackageUrl => _appUrl + @"/ServiceModel/PackageInstallerService.svc/GetZipPackages";

		private static string ApiVersionUrl => _appUrl + @"/rest/CreatioApiGateway/GetApiVersion";

		private static string DefLogFileName => "cliolog.txt";

		private static string GetEntityModelsUrl => _appUrl + @"/rest/CreatioApiGateway/GetEntitySchemaModels/{0}";

		private static CreatioClient CreatioClient {
			get => new CreatioClient(_url, _userName, _userPassword, _isNetCore);
		}


		private static string CurrentProj =>
			new DirectoryInfo(Environment.CurrentDirectory).GetFiles("*.csproj").FirstOrDefault()?.FullName;

		public static bool _safe { get; private set; } = true;

		private static void Configure(EnvironmentOptions options) {
			var settingsRepository = new SettingsRepository();
			_environmentName = options.Environment;
			_settings = settingsRepository.GetEnvironment(options);
		}

		private static int UpdateGate(EnvironmentOptions options) {
			try {
				Configure(options);
				var dir = AppDomain.CurrentDomain.BaseDirectory;
				string packageFilePath = Path.Combine(dir, "cliogate", "cliogate.gz");
				InstallPackage(packageFilePath);
				new RestartCommand(new CreatioClientAdapter(CreatioClient)).Restart(_settings);
				return 0;
			} catch (Exception e) {
				Console.WriteLine($"Update error {e.Message}");
				return 1;
			}
		}


		private static void MessageToConsole(string text, ConsoleColor color) {
			var currentColor = Console.ForegroundColor;
			Console.ForegroundColor = color;
			Console.WriteLine(text);
			Console.ForegroundColor = currentColor;
		}


		public static void SetupAppConnection(EnvironmentOptions options) {
			Configure(options);
			CheckApiVersion();
		}


		public static void CheckApiVersion() {
			var dir = AppDomain.CurrentDomain.BaseDirectory;
			string versionFilePath = Path.Combine(dir, "cliogate", "version.txt");
			var localApiVersion = new Version(File.ReadAllText(versionFilePath));
			var appApiVersion = GetAppApiVersion();
			if (appApiVersion == new Version("0.0.0.0")) {
				MessageToConsole($"Your app does not contain clio API." +
				 $"{Environment.NewLine}You should consider install it via the \'clio install-gate\' command.", ConsoleColor.DarkYellow);
			} else if (localApiVersion > appApiVersion) {
				MessageToConsole($"You are using clio api version {appApiVersion}, however version {localApiVersion} is available." +
				 $"{Environment.NewLine}You should consider upgrading via the \'clio update-gate\' command.", ConsoleColor.DarkYellow);
			}
		}

	
		private static Version GetAppApiVersion() {
			var apiVersion = new Version("0.0.0.0");
			try {
				string appVersionResponse = CreatioClient.ExecuteGetRequest(ApiVersionUrl).Trim('"');
				apiVersion = new Version(appVersionResponse);
			} catch (Exception) {
			}
			return apiVersion;
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
				CreatioClient.DownloadFile(GetZipPackageUrl, destinationPath, requestData);
				Console.WriteLine("Download packages ({0}) completed.", packageName);
			} catch (Exception) {
				Console.WriteLine("Download packages ({0}) not completed.", packageName);
			}
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
			var installResponse = CreatioClient.ExecutePostRequest(InstallUrl, "\"" + fileName + "\"", 600000);
			if (_settings.DeveloperModeEnabled.HasValue && _settings.DeveloperModeEnabled.Value) {
				UnlockMaintainerPackageInternal();
				new RestartCommand(new CreatioClientAdapter(CreatioClient)).Restart(_settings);
			}
			var logText = GetLog();
			Console.WriteLine("Installation log:");
			Console.WriteLine(logText);
			return logText;
		}

		private static void DeletePackage(string code) {
			Console.WriteLine("Deleting...");
			string deleteRequestData = "\"" + code + "\"";
			CreatioClient.ExecutePostRequest(DeletePackageUrl, deleteRequestData);
			Console.WriteLine("Deleted.");
		}

		private static string UploadPackage(string filePath) {
			Console.WriteLine("Uploading...");
			FileInfo fileInfo = new FileInfo(filePath);
			string fileName = fileInfo.Name;
			CreatioClient.UploadFile(UploadUrl, filePath);
			Console.WriteLine("Uploaded");
			return fileName;
		}

		private static int Register(RegisterOptions options) {
			try {
				var creatioEnv = new CreatioEnvironment();
				string path = string.IsNullOrEmpty(options.Path) ? Environment.CurrentDirectory : options.Path;
				IResult result = options.Target == "m"
					? creatioEnv.MachineRegisterPath(path)
					: creatioEnv.UserRegisterPath(path);
				result.ShowMessagesTo(Console.Out);
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
					var packages = StringParser.ParseArray(options.Packages);
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

		private static string GetLog() {
			return CreatioClient.ExecuteGetRequest(LogUrl);
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

		//ToDo: move to factory
		private static TCommand CreateRemoteCommand<TCommand>(EnvironmentOptions options, 
				params object[] additionalConstructorArgs) {
			var settingsRepository = new SettingsRepository();
			var settings = settingsRepository.GetEnvironment(options);
			var creatioClient = new CreatioClient(settings.Uri, settings.Login, settings.Password, settings.IsNetCore);
			var clientAdapter = new CreatioClientAdapter(creatioClient);
			var constructorArgs = new object[] { clientAdapter }.Concat(additionalConstructorArgs).ToArray();
			return (TCommand)Activator.CreateInstance(typeof(TCommand), constructorArgs);
		}

		//ToDo: move to factory
		private static TCommand CreateCommand<TCommand>(params object[] additionalConstructorArgs) {
			return (TCommand)Activator.CreateInstance(typeof(TCommand), additionalConstructorArgs);
		}

		private static int Main(string[] args) {
			var autoupdate = new SettingsRepository().GetAutoupdate();
			if (autoupdate) {
				new Thread(UpdateCliCommand.CheckUpdate).Start();
			}
			var creatioEnv = new CreatioEnvironment();
			string helpFolderName = $"help";
			string helpDirectoryPath = helpFolderName;
			var envPath = creatioEnv.GetRegisteredPath();
			helpDirectoryPath = Path.Combine(envPath ?? string.Empty, helpFolderName);
			Parser.Default.Settings.ShowHeader = false;
			Parser.Default.Settings.HelpDirectory = helpDirectoryPath;
			return Parser.Default.ParseArguments<ExecuteAssemblyOptions, RestartOptions, ClearRedisOptions,
					RegAppOptions, AppListOptions, UnregAppOptions, GeneratePkgZipOptions, PushPkgOptions,
					DeletePkgOptions, ReferenceOptions, NewPkgOptions, ConvertOptions, RegisterOptions, PullPkgOptions,
					UpdateCliOptions, ExecuteSqlScriptOptions, InstallGateOptions, ItemOptions, DeveloperModeOptions,
					SysSettingsOptions, FeatureOptions>(args)
				.MapResult(
					(ExecuteAssemblyOptions opts) => AssemblyCommand.ExecuteCodeFromAssmebly(opts),
					(RestartOptions opts) => CreateRemoteCommand<RestartCommand>(opts).Restart(opts),
					(ClearRedisOptions opts) => CreateRemoteCommand<RedisCommand>(opts).ClearRedisDb(opts),
					(RegAppOptions opts) => CreateRemoteCommand<RegAppCommand>(opts, new SettingsRepository()).Execute(opts),
					(AppListOptions opts) => CreateCommand<ShowAppListCommand>(new SettingsRepository()).Execute(opts),
					(UnregAppOptions opts) => CreateCommand<UnregAppCommand>(new SettingsRepository()).Execute(opts),
					(GeneratePkgZipOptions opts) => Compression(opts),
					(PushPkgOptions opts) => Install(opts),
					(DeletePkgOptions opts) => CreateRemoteCommand<DeletePackageCommand>(opts).Delete(opts),
					(ReferenceOptions opts) => ReferenceTo(opts),
					(NewPkgOptions opts) => NewPkg(opts),
					(ConvertOptions opts) => ConvertPackage(opts),
					(RegisterOptions opts) => Register(opts),
					(PullPkgOptions opts) => DownloadZipPackages(opts),
					(UpdateCliOptions opts) => UpdateCliCommand.UpdateCli(),
					(ExecuteSqlScriptOptions opts) => SqlScriptCommand.ExecuteSqlScript(opts),
					(InstallGateOptions opts) => UpdateGate(opts),
					(ItemOptions opts) => AddItem(opts),
					(DeveloperModeOptions opts) => SetDeveloperMode(opts),
					(SysSettingsOptions opts) => SysSettingsCommand.SetSysSettings(opts),
					(FeatureOptions opts) => FeatureCommand.SetFeatureState(opts),
					errs => 1);
		}

		private static int SetDeveloperMode(DeveloperModeOptions opts) {
			try {
				SetupAppConnection(opts);
				var repository = new SettingsRepository();
				_settings.DeveloperModeEnabled = true;
				repository.ConfigureEnvironment(_environmentName, _settings);
				var sysSettingOptions = new SysSettingsOptions() {
					Code = "Maintainer",
					Value = _settings.Maintainer
				};
				SysSettingsCommand.UpdateSysSetting(sysSettingOptions, CreatioClient);
				UnlockMaintainerPackageInternal();
				new RestartCommand(new CreatioClientAdapter(CreatioClient)).Restart(_settings);
				Console.WriteLine("Done");
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e);
				return 1;
			}
		}

		private static void UnlockMaintainerPackageInternal() {
			var script = $"UPDATE SysPackage SET InstallType = 0 WHERE Maintainer = '{_settings.Maintainer}'";
			SqlScriptCommand.ExecuteSqlScript(script, CreatioClient);
		}

		private static int AddModels(ItemOptions opts) {
			try {
				SetupAppConnection(opts);
				var models = GetClassModels(opts.ItemName);
				var project = new VSProject(opts.DestinationPath, opts.Namespace);
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

		private static int AddItem(ItemOptions options) {
			if (options.ItemType.ToLower() == "model") {
				return AddModels(options);
			} else {
				return AddItemFromTemplate(options);
			} 
		}

		private static int AddItemFromTemplate(ItemOptions options) {
			try {
				var project = new VSProject(options.DestinationPath, options.Namespace);
				var creatioEnv = new CreatioEnvironment();
				string tplPath = $"tpl{Path.DirectorySeparatorChar}{options.ItemType}-template.tpl";
				if (!File.Exists(tplPath)) {
					var envPath = creatioEnv.GetRegisteredPath();
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
			string responseFormServer = CreatioClient.ExecuteGetRequest(url);
			var result = CorrectJson(responseFormServer);
			return JsonConvert.DeserializeObject<Dictionary<string, string>>(result);
		}

		private static int ConvertPackage(ConvertOptions opts) {
			return PackageConverter.Convert(opts);
		}

		private static int NewPkg(NewPkgOptions options) {
			var settings = new SettingsRepository().GetEnvironment();
			try {
				var packageName = options.Name;
				var packageDirectory = Directory.CreateDirectory(packageName);
				Directory.SetCurrentDirectory(packageDirectory.FullName);
				var pkg = CreatioPackage.CreatePackage(options.Name, settings.Maintainer);
				pkg.Create();
				if (!string.IsNullOrEmpty(options.Rebase) && options.Rebase != "nuget") {
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
							CreatioPkgProject.LoadFromFile(options.Path)
							.RefToBin()
							.SaveChanges();
						}
						break;
					case "src": {
							CreatioPkgProject.LoadFromFile(options.Path)
							.RefToCoreSrc()
							.SaveChanges();
						}
						break;
					case "custom": {
							CreatioPkgProject.LoadFromFile(options.Path)
								.RefToCustomPath(options.RefPattern)
								.SaveChanges();
						}
						break;
					case "unit-bin": {
							CreatioPkgProject.LoadFromFile(options.Path)
								.RefToUnitBin()
								.SaveChanges();
						}
						break;
					case "unit-src": {
							CreatioPkgProject.LoadFromFile(options.Path)
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
