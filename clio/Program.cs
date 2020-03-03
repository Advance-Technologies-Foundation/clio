using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using Clio.Command;
using Clio.Command.PackageCommand;
using Clio.Command.SqlScriptCommand;
using Clio.Command.SysSettingsCommand;
using Clio.Command.UpdateCliCommand;
using Clio.Common;
using Clio.Package;
using Clio.Project;
using Clio.Project.NuGet;
using Clio.UserEnvironment;
using Clio.WebApplication;
using CommandLine;
using Creatio.Client;
using Newtonsoft.Json;
using Сlio.Command.PackageCommand;

namespace Clio
{


	class Program
	{
		private static string UserName => CreatioEnvironment.Settings.Login;
		private static string UserPassword => CreatioEnvironment.Settings.Password;
		private static string Url => CreatioEnvironment.Settings.Uri; // Необходимо получить из конфига
		private static string AppUrl
		{
			get
			{
				if (CreatioEnvironment.IsNetCore) {
					return Url;
				} else {
					return Url + @"/0";
				}
			}
		}

		private static string GetZipPackageUrl => AppUrl + @"/ServiceModel/PackageInstallerService.svc/GetZipPackages";

		private static string ApiVersionUrl => AppUrl + @"/rest/CreatioApiGateway/GetApiVersion";


		private static string GetEntityModelsUrl => AppUrl + @"/rest/CreatioApiGateway/GetEntitySchemaModels/{0}";

		private static CreatioClient CreatioClient
		{
			get => new CreatioClient(Url, UserName, UserPassword, true, CreatioEnvironment.IsNetCore);
		}

		public static bool Safe { get; private set; } = true;

		private static void Configure(EnvironmentOptions options) {
			var settingsRepository = new SettingsRepository();
			CreatioEnvironment.EnvironmentName = options.Environment;
			CreatioEnvironment.Settings = settingsRepository.GetEnvironment(options);
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
			body = body.Replace("\\\\n", Environment.NewLine);
			body = body.Replace("\\n", Environment.NewLine);
			body = body.Replace("\\\\t", Convert.ToChar(9).ToString());
			body = body.Replace("\\\"", "\"");
			body = body.Replace("\\\\", "\\");
			body = body.Trim(new Char[] { '\"' });
			return body;
		}

		private static void UnZipPackages(string zipFilePath) {
			IPackageArchiver packageArchiver = CreatePackageArchiver();
			var fileInfo = new FileInfo(zipFilePath);
			packageArchiver.UnZipPackages(zipFilePath, true, false, fileInfo.DirectoryName);
		}

		private static int DownloadZipPackages(PullPkgOptions options) {
			try {
				SetupAppConnection(options);
				string destPath = options.DestPath ?? Path.Combine(Path.GetTempPath(), "packages.zip");
				DownloadZipPackagesInternal(options.Name, destPath);
				UnZipPackages(destPath);
				Console.WriteLine("Done");
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e);
				return 1;
			}
		}

		private static EnvironmentSettings GetEnvironmentSettings(EnvironmentOptions options) {
			var settingsRepository = new SettingsRepository();
			return settingsRepository.GetEnvironment(options);
		}

		//ToDo: move to factory
		private static TCommand CreateRemoteCommand<TCommand>(EnvironmentOptions options,
				params object[] additionalConstructorArgs) {
			var settings = GetEnvironmentSettings(options);
			var creatioClient = new CreatioClient(settings.Uri, settings.Login, settings.Password, true, settings.IsNetCore);
			var clientAdapter = new CreatioClientAdapter(creatioClient);
			var constructorArgs = new object[] { clientAdapter, settings }.Concat(additionalConstructorArgs).ToArray();
			return (TCommand)Activator.CreateInstance(typeof(TCommand), constructorArgs);
		}

		private static TCommand CreateBaseRemoteCommand<TCommand>(EnvironmentOptions options,
				params object[] additionalConstructorArgs) {
			var settings = GetEnvironmentSettings(options);
			var creatioClient = new CreatioClient(settings.Uri, settings.Login, settings.Password, true , settings.IsNetCore);
			var clientAdapter = new CreatioClientAdapter(creatioClient);
			var constructorArgs = new object[] { clientAdapter }.Concat(additionalConstructorArgs).ToArray();
			return (TCommand)Activator.CreateInstance(typeof(TCommand), constructorArgs);
		}

		//ToDo: move to factory
		private static TCommand CreateCommand<TCommand>(params object[] additionalConstructorArgs) {
			return (TCommand)Activator.CreateInstance(typeof(TCommand), additionalConstructorArgs);
		}

		private static IPackageUtilities CreatePackageUtilities() {
			var fileSystem = new FileSystem();
			return new PackageUtilities(fileSystem);
		}

		private static IPackageArchiver CreatePackageArchiver() {
			var fileSystem = new FileSystem();
			IWorkingDirectoriesProvider workingDirectoriesProvider = new WorkingDirectoriesProvider();
			ICompressionUtilities compressionUtilities = new CompressionUtilities(fileSystem);
			var logger = new ConsoleLogger();
			return new PackageArchiver(CreatePackageUtilities(), compressionUtilities, workingDirectoriesProvider, 
				fileSystem, logger);
		}

		private static INuGetManager CreateNuGetManager() {
			var logger = new ConsoleLogger();
			var dotnetExecutor = new DotnetExecutor();
			var fileSystem = new FileSystem();
			var workingDirectoriesProvider = new WorkingDirectoriesProvider();
			var templateProvider = new TemplateProvider(workingDirectoriesProvider);
			var nuspecFilesGenerator = new NuspecFilesGenerator(templateProvider);
			var nugetPacker = new NugetPacker(templateProvider, dotnetExecutor, workingDirectoriesProvider, fileSystem, 
				logger);
			var nugetPackageRestorer = new NugetPackageRestorer(CreatePackageArchiver(), templateProvider, 
				dotnetExecutor, workingDirectoriesProvider, fileSystem, logger);
			var packageInfoProvider = new PackageInfoProvider(); 
			return new NuGetManager(nuspecFilesGenerator, nugetPacker, nugetPackageRestorer, packageInfoProvider, 
				CreatePackageArchiver(), dotnetExecutor, logger);
		}

		private static IApplicationPackageListProvider CreateApplicationPackageListProvider(EnvironmentOptions options) {
			var jsonConverter = new Common.JsonConverter();
			EnvironmentSettings settings = GetEnvironmentSettings(options);
			var creatioClient = new CreatioClient(settings.Uri, settings.Login, settings.Password, 
				true, settings.IsNetCore);
			var applicationClient = new CreatioClientAdapter(creatioClient);
			var serviceUrlBuilder = new ServiceUrlBuilder(settings);
			return new ApplicationPackageListProvider(applicationClient, jsonConverter, serviceUrlBuilder);
		}

		private static IApplicationClient CreateApplicationClient(EnvironmentOptions options) {
			EnvironmentSettings settings = GetEnvironmentSettings(options);
			var creatioClient = new CreatioClient(settings.Uri, settings.Login, settings.Password, 
				true, settings.IsNetCore);
			return new CreatioClientAdapter(creatioClient);
		}

		private static IApplication CreateApplicationManager(EnvironmentOptions options) {
			EnvironmentSettings settings = GetEnvironmentSettings(options);
			var serviceUrlBuilder = new ServiceUrlBuilder(settings);
			return new Application(settings, CreateApplicationClient(options), serviceUrlBuilder);
		}
		
		private static IPackageInstaller CreatePackageInstaller(EnvironmentOptions options) {
			EnvironmentSettings settings = GetEnvironmentSettings(options);
			var serviceUrlBuilder = new ServiceUrlBuilder(settings);
			var fileSystem = new FileSystem();
			var logger = new ConsoleLogger();
			return new PackageInstaller(settings, CreateApplicationClient(options), CreateApplicationManager(options),
				CreatePackageArchiver(), new SqlScriptExecutor(), serviceUrlBuilder, fileSystem, logger);
		}

		private static IInstallNugetPackage CreateInstallNugetPackage(EnvironmentOptions options) {
			var fileSystem = new FileSystem();
			IWorkingDirectoriesProvider workingDirectoriesProvider = new WorkingDirectoriesProvider();
			var logger = new ConsoleLogger();
			return new InstallNugetPackage(CreateNuGetManager(), CreatePackageInstaller(options), 
				CreatePackageArchiver(), workingDirectoriesProvider, fileSystem, logger);
		}

		private static PushNuGetPackagesCommand CreatePushNuGetPkgsCommand() {
			return CreateCommand<PushNuGetPackagesCommand>(CreateNuGetManager());
		}

		private static PackNuGetPackageCommand CreatePackNuGetPackageCommand() {
			return CreateCommand<PackNuGetPackageCommand>(CreateNuGetManager());
		}

		private static PushPackageCommand CreatePushPackageCommand(EnvironmentOptions options) {
			return CreateRemoteCommand<PushPackageCommand>(options, CreatePackageArchiver(), 
				new SettingsRepository(), new SqlScriptExecutor());
		}

		private static PushPkgOptions CreatePushPkgOptions(InstallGateOptions options) {
				var dir = AppDomain.CurrentDomain.BaseDirectory;
				var settingsRepository = new SettingsRepository();
				var settings = settingsRepository.GetEnvironment(options);
				string packageFolder = settings.IsNetCore ? "netstandard" : "netframework";
				string packageFilePath = Path.Combine(dir, "cliogate", packageFolder, "cliogate.gz");
				return new PushPkgOptions {
					Name = packageFilePath
				};
		}

		private static RestoreNugetPackageCommand CreateRestoreNugetPackageCommand(EnvironmentOptions options) {
			return CreateCommand<RestoreNugetPackageCommand>(CreateNuGetManager());
		}

		private static InstallNugetPackageCommand CreateInstallNugetPackageCommand(EnvironmentOptions options) {
			return CreateCommand<InstallNugetPackageCommand>(CreateInstallNugetPackage(options));
		}

		private static int Main(string[] args) {
			var autoupdate = new SettingsRepository().GetAutoupdate();
			if (autoupdate) {
				new Thread(UpdateCliCommand.CheckUpdate).Start();
			}
			var creatioEnv = new CreatioEnvironment();
			string helpFolderName = $"help";
			string helpDirectoryPath = helpFolderName;
			var envPath = creatioEnv.GetAssemblyFolderPath();
			helpDirectoryPath = Path.Combine(envPath ?? string.Empty, helpFolderName);
			Parser.Default.Settings.ShowHeader = false;
			Parser.Default.Settings.HelpDirectory = helpDirectoryPath;
			Parser.Default.Settings.CustomHelpViewer = new WikiHelpViewer();
			return Parser.Default.ParseArguments<ExecuteAssemblyOptions, RestartOptions, ClearRedisOptions,
					RegAppOptions, AppListOptions, UnregAppOptions, GeneratePkgZipOptions, PushPkgOptions,
					DeletePkgOptions, ReferenceOptions, NewPkgOptions, ConvertOptions, RegisterOptions, UnregisterOptions,
					PullPkgOptions,	UpdateCliOptions, ExecuteSqlScriptOptions, InstallGateOptions, ItemOptions,
					DeveloperModeOptions, SysSettingsOptions, FeatureOptions, UnzipPkgOptions, PingAppOptions,
					OpenAppOptions, PkgListOptions, CompileOptions, PushNuGetPkgsOptions, PackNuGetPkgOptions,
					RestoreNugetPkgOptions, InstallNugetPkgOptions>(args)
				.MapResult(
					(ExecuteAssemblyOptions opts) => AssemblyCommand.ExecuteCodeFromAssembly(opts),
					(RestartOptions opts) => CreateRemoteCommand<RestartCommand>(opts).Execute(opts),
					(ClearRedisOptions opts) => CreateRemoteCommand<RedisCommand>(opts).Execute(opts),
					(RegAppOptions opts) => CreateCommand<RegAppCommand>(
						new SettingsRepository(), new ApplicationClientFactory()).Execute(opts),
					(AppListOptions opts) => CreateCommand<ShowAppListCommand>(new SettingsRepository()).Execute(opts),
					(UnregAppOptions opts) => CreateCommand<UnregAppCommand>(new SettingsRepository()).Execute(opts),
					(GeneratePkgZipOptions opts) => CreateCommand<CompressPackageCommand>(CreatePackageArchiver()).Execute(opts),
					(PushPkgOptions opts) => CreatePushPackageCommand(opts).Execute(opts),
					(DeletePkgOptions opts) => CreateBaseRemoteCommand<DeletePackageCommand>(opts).Delete(opts),
					(ReferenceOptions opts) => CreateCommand<ReferenceCommand>(new CreatioPkgProjectCreator()).Execute(opts),
					(NewPkgOptions opts) => CreateCommand<NewPkgCommand>(new SettingsRepository(), CreateCommand<ReferenceCommand>(
						new CreatioPkgProjectCreator())).Execute(opts),
					(ConvertOptions opts) => ConvertPackage(opts),
					(RegisterOptions opts) => CreateCommand<RegisterCommand>().Execute(opts),
					(UnregisterOptions opts) => CreateCommand<UnregisterCommand>().Execute(opts),
					(PullPkgOptions opts) => DownloadZipPackages(opts),
					(UpdateCliOptions opts) => UpdateCliCommand.UpdateCli(opts),
					(ExecuteSqlScriptOptions opts) => CreateRemoteCommand<SqlScriptCommand>(opts, new SqlScriptExecutor()).Execute(opts),
					(InstallGateOptions opts) => CreatePushPackageCommand(opts)
							.Execute(CreatePushPkgOptions(opts)),
					(ItemOptions opts) => AddItem(opts),
					(DeveloperModeOptions opts) => SetDeveloperMode(opts),
					(SysSettingsOptions opts) => SysSettingsCommand.SetSysSettings(opts),
					(FeatureOptions opts) => FeatureCommand.SetFeatureState(opts),
					(UnzipPkgOptions opts) => ExtractPackageCommand.ExtractPackage(opts, 
						CreatePackageArchiver(), CreatePackageUtilities(), new FileSystem()),
					(PingAppOptions opts) => CreateRemoteCommand<PingAppCommand>(opts).Execute(opts),
					(OpenAppOptions opts) => CreateRemoteCommand<OpenAppCommand>(opts).Execute(opts),
					(PkgListOptions opts) => GetPkgListCommand.GetPkgList(opts),
					(CompileOptions opts) => CreateRemoteCommand<CompileWorkspaceCommand>(opts).Execute(opts),
					(PushNuGetPkgsOptions opts) => CreatePushNuGetPkgsCommand().Execute(opts),
					(PackNuGetPkgOptions opts) => CreatePackNuGetPackageCommand().Execute(opts),
					(RestoreNugetPkgOptions opts) => CreateRestoreNugetPackageCommand(opts).Execute(opts),
					(InstallNugetPkgOptions opts) => CreateInstallNugetPackageCommand(opts).Execute(opts),
					errs => 1);
		}

		private static int SetDeveloperMode(DeveloperModeOptions opts) {
			try {
				SetupAppConnection(opts);
				var repository = new SettingsRepository();
				CreatioEnvironment.Settings.DeveloperModeEnabled = true;
				repository.ConfigureEnvironment(CreatioEnvironment.EnvironmentName, CreatioEnvironment.Settings);
				var sysSettingOptions = new SysSettingsOptions() {
					Code = "Maintainer",
					Value = CreatioEnvironment.Settings.Maintainer
				};
				SysSettingsCommand.UpdateSysSetting(sysSettingOptions, CreatioEnvironment.Settings);
				UnlockMaintainerPackageInternal();
				new RestartCommand(new CreatioClientAdapter(CreatioClient), CreatioEnvironment.Settings).Execute(new RestartOptions());
				Console.WriteLine("Done");
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e);
				return 1;
			}
		}

		private static void UnlockMaintainerPackageInternal() {
			var script = $"UPDATE SysPackage SET InstallType = 0 WHERE Maintainer = '{CreatioEnvironment.Settings.Maintainer}'";
			new SqlScriptExecutor().Execute(script, new CreatioClientAdapter(CreatioClient), CreatioEnvironment.Settings);
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
					var envPath = creatioEnv.GetAssemblyFolderPath();
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
	}
}
