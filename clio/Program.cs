using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Autofac;
using Clio.Command;
using Clio.Command.ApplicationCommand;
using Clio.Command.CreatioInstallCommand;
using Clio.Command.PackageCommand;
using Clio.Command.SqlScriptCommand;
using Clio.Command.UpdateCliCommand;
using Clio.Common;
using Clio.Package;
using Clio.Project;
using Clio.Querry;
using Clio.UserEnvironment;
using Clio.Utilities;
using CommandLine;
using Creatio.Client;
using Newtonsoft.Json;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization;

namespace Clio;

class Program
{

	private static string UserName => CreatioEnvironment.Settings.Login;
	private static string UserPassword => CreatioEnvironment.Settings.Password;
	private static string Url => CreatioEnvironment.Settings.Uri; // Необходимо получить из конфига
	private static string ClientId => CreatioEnvironment.Settings.ClientId;
	private static string ClientSecret => CreatioEnvironment.Settings.ClientSecret;
	private static string AuthAppUrl => CreatioEnvironment.Settings.AuthAppUri;
	private static string AppUrl {
		get {
			if (CreatioEnvironment.IsNetCore) {
				return Url;
			} else {
				return Url + @"/0";
			}
		}
	}

	private static string GetZipPackageUrl => AppUrl + @"/ServiceModel/PackageInstallerService.svc/GetZipPackages";

	private static string DeleteExistsPackagesZipUrl => AppUrl + @"/rest/PackagesGateway/DeleteExistsPackagesZip";

	private static string ExistsPackageZipUrl => AppUrl + @"/rest/PackagesGateway/ExistsPackageZip";

	private static string DownloadExistsPackageZipUrl => AppUrl + @"/rest/PackagesGateway/DownloadExistsPackageZip";

	private static string ApiVersionUrl => AppUrl + @"/rest/CreatioApiGateway/GetApiVersion";

	private static string GetEntityModelsUrl => AppUrl + @"/rest/CreatioApiGateway/GetEntitySchemaModels/{0}/{1}";

	public static bool IsEnvironmentReported {
		get; set;
	}

	private static CreatioClient _creatioClientInstance {
		get {
			if (string.IsNullOrEmpty(ClientId)) {
				return new CreatioClient(Url, UserName, UserPassword, true, CreatioEnvironment.IsNetCore);
			} else {
				return CreatioClient.CreateOAuth20Client(Url, AuthAppUrl, ClientId, ClientSecret, CreatioEnvironment.IsNetCore);
			}
		}
	}

	public static bool Safe { get; private set; } = true;
	internal static IContainer Container { get; set; }

	public static IAppUpdater _appUpdater = null;
	public static IAppUpdater AppUpdater {
		get {
			if (_appUpdater == null) {
				_appUpdater = Container.Resolve<IAppUpdater>();
			}
			return _appUpdater;
		}
		set {
			_appUpdater = value;
		}
	}

	private static bool? autoUpdate;
	public static bool AutoUpdate {
		get {
			return autoUpdate.HasValue ? autoUpdate.Value : new SettingsRepository().GetAutoupdate();
		}
		set {
			autoUpdate = value;
		}
	}

	private static void Configure(EnvironmentOptions options, bool checkEnvExist = false) {
		var settingsRepository = new SettingsRepository();
		CreatioEnvironment.EnvironmentName = options.Environment;
		if (checkEnvExist) {
			var isEnvironmentExists = settingsRepository.IsEnvironmentExists(options.Environment);
			if (!isEnvironmentExists) {
				throw new ArgumentException($"Cannot find environment with name {options.Environment}", nameof(options.Environment));
			}
		}
		CreatioEnvironment.Settings = settingsRepository.GetEnvironment(options);
		ICreatioEnvironment creatioEnvironment = Resolve<ICreatioEnvironment>();
	}

	private static void MessageToConsole(string text, ConsoleColor color) {
		var currentColor = Console.ForegroundColor;
		Console.ForegroundColor = color;
		Console.WriteLine(text);
		Console.ForegroundColor = currentColor;
	}
	public static void SetupAppConnection(EnvironmentOptions options, bool checkEnvExist = false) {
		Configure(options, checkEnvExist);
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
			string appVersionResponse = _creatioClientInstance.ExecuteGetRequest(ApiVersionUrl).Trim('"');
			apiVersion = new Version(appVersionResponse);
		} catch (Exception) {
		}
		return apiVersion;
	}

	private static void DownloadZipPackagesInternal(string packageName, string destinationPath, bool _async) {
		try {
			Console.WriteLine("Start download packages ({0}).", packageName);
			int count = 0;
			var packageNames = string.Format("\"{0}\"", packageName.Replace(" ", string.Empty).Replace(",", "\",\""));
			string requestData = "[" + packageNames + "]";
			if (!_async) {
				_creatioClientInstance.DownloadFile(GetZipPackageUrl, destinationPath, requestData, 600000);
			} else {
				_creatioClientInstance.ExecutePostRequest(DeleteExistsPackagesZipUrl, string.Empty, 10000);
				new Thread(() => {
					try {
						_creatioClientInstance.DownloadFile(GetZipPackageUrl, Path.GetTempFileName(), requestData, 2000);
					} catch { }
				}).Start();
				bool again = false;
				do {
					Thread.Sleep(2000);
					again = !bool.Parse(_creatioClientInstance.ExecutePostRequest(ExistsPackageZipUrl, string.Empty, 10000));
					if (++count > 600) {
						throw new TimeoutException("Timeout exception");
					}
				} while (again);
				Thread.Sleep(1000);
				_creatioClientInstance.DownloadFile(DownloadExistsPackageZipUrl, destinationPath, requestData, 60000);
			}
			Console.WriteLine("Download packages ({0}) completed.", packageName);
		} catch (Exception e) {
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

	private static void UnZipPackages(string zipFilePath, string destinationPath) {
		IPackageArchiver packageArchiver = Resolve<IPackageArchiver>();
		packageArchiver.ExtractPackages(zipFilePath, true, true, true, false, destinationPath);
	}

	private static void UnZip(string zipFilePath) {
		IPackageArchiver packageArchiver = Resolve<IPackageArchiver>();
		packageArchiver.UnZip(zipFilePath, true, null);
	}

	internal static int DownloadZipPackages(PullPkgOptions options) {
		try {
			SetupAppConnection(options);
			string packageName = options.Name;
			if (options.Unzip) {
				string destPath = options.DestPath ?? Environment.CurrentDirectory;
				IWorkingDirectoriesProvider workingDirectoriesProvider = Resolve<IWorkingDirectoriesProvider>();
				workingDirectoriesProvider.CreateTempDirectory(tempDirectory => {
					string zipFilePath = Path.Combine(tempDirectory, $"{packageName}.zip");
					DownloadZipPackagesInternal(packageName, zipFilePath, options.Async);
					UnZipPackages(zipFilePath, destPath);
				});
			} else {
				string destPath = options.DestPath ?? Path.Combine(Environment.CurrentDirectory, $"{packageName}.zip");
				if (Directory.Exists(destPath)) {
					destPath = Path.Combine(destPath, $"{packageName}.zip");
				}
				DownloadZipPackagesInternal(packageName, destPath, options.Async);
			}
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

	private static EnvironmentSettings FindEnvironmentSettings(EnvironmentOptions options) {
		var settingsRepository = new SettingsRepository();
		return settingsRepository.FindEnvironment(options.Environment);
	}

	private static TCommand CreateRemoteCommand<TCommand>(EnvironmentOptions options,
		params object[] additionalConstructorArgs) {
		var settings = GetEnvironmentSettings(options);
		var creatioClient = string.IsNullOrEmpty(settings.ClientId) ? new CreatioClient(settings.Uri, settings.Login, settings.Password, true, settings.IsNetCore) :
			CreatioClient.CreateOAuth20Client(settings.Uri, settings.AuthAppUri, settings.ClientId, settings.ClientSecret, settings.IsNetCore);
		var clientAdapter = new CreatioClientAdapter(creatioClient);
		var constructorArgs = new object[] { clientAdapter, settings }.Concat(additionalConstructorArgs).ToArray();
		return (TCommand)Activator.CreateInstance(typeof(TCommand), constructorArgs);
	}

	private static TCommand CreateRemoteCommandWithoutClient<TCommand>(EnvironmentOptions options,
		params object[] additionalConstructorArgs) {
		var settings = GetEnvironmentSettings(options);
		var constructorArgs = new object[] { settings }.Concat(additionalConstructorArgs).ToArray();
		return (TCommand)Activator.CreateInstance(typeof(TCommand), constructorArgs);
	}

	//ToDo: move to factory
	private static TCommand CreateCommand<TCommand>(params object[] additionalConstructorArgs) {
		return (TCommand)Activator.CreateInstance(typeof(TCommand), additionalConstructorArgs);
	}

	private static PushPkgOptions CreatePushPkgOptions(InstallGateOptions options) {
		var settingsRepository = new SettingsRepository();
		var settings = settingsRepository.GetEnvironment(options);
		var workingDirectoriesProvider = Resolve<IWorkingDirectoriesProvider>(options);
		string packageName = settings.IsNetCore ? "cliogate_netcore" : "cliogate";
		string packagePath = Path.Combine(workingDirectoriesProvider.ExecutingDirectory, "cliogate",
			$"{packageName}.gz");
		return new PushPkgOptions {
			Environment = options.Environment,
			Name = packagePath,
			Login = options.Login,
			Uri = options.Uri,
			Password = options.Password,
			Maintainer = options.Maintainer,
			IsNetCore = options.IsNetCore,
			AuthAppUri = options.AuthAppUri,
			ClientSecret = options.ClientSecret,
			ClientId = options.ClientId
		};
	}

	private static T ResolveEnvSettings<T>(ApplyEnvironmentManifestOptions options = null) {
		EnvironmentOptions optionFromFile = ReadEnvironmentOptionsFromManifestFile(options.ManifestFilePath);
		EnvironmentOptions combinedOption = CombinedOption(optionFromFile, options);
		return Resolve<T>(combinedOption, true);
	}

	public static EnvironmentOptions CombinedOption(EnvironmentOptions optionFromFile, EnvironmentOptions optionsFromCommandLine) {
		if (optionFromFile == null && optionsFromCommandLine == null) {
			return null;
		}
		if (optionFromFile == null && optionsFromCommandLine.IsEmpty()) {
			return optionsFromCommandLine;

		}
		if (string.IsNullOrEmpty(optionsFromCommandLine.Environment)) {
			var result = new EnvironmentNameOptions();
			result.Uri = optionsFromCommandLine.Uri ?? optionFromFile.Uri;
			result.Login = optionsFromCommandLine.Login ?? optionFromFile.Login;
			result.Password = optionsFromCommandLine.Password ?? optionFromFile.Password;
			result.AuthAppUri = optionsFromCommandLine.AuthAppUri ?? optionFromFile.AuthAppUri;
			result.ClientId = optionsFromCommandLine.ClientId ?? optionFromFile.ClientId;
			result.ClientSecret = optionsFromCommandLine.ClientSecret ?? optionFromFile.ClientSecret;
			result.IsNetCore = optionsFromCommandLine.IsNetCore.HasValue ? optionsFromCommandLine.IsNetCore : optionFromFile.IsNetCore;
			return result;
		} else {
			return optionsFromCommandLine;
		}

	}

	public static EnvironmentOptions ReadEnvironmentOptionsFromManifestFile(string manifestFilePath, IFileSystem fileSystem = null) {
		var deserializer = new DeserializerBuilder()
				.WithNamingConvention(UnderscoredNamingConvention.Instance)
				.IgnoreUnmatchedProperties()
				.Build();
		var manifest = fileSystem is null ? File.ReadAllText(manifestFilePath) : fileSystem.ReadAllText(manifestFilePath);
		var envManifest = deserializer.Deserialize<EnvironmentManifest>(manifest);
		var envManifestSettings = envManifest.EnvironmentSettings;
		if (envManifestSettings == null) {
			return null;
		}
		var environmnetOptions = new EnvironmentOptions() {
			Uri = envManifestSettings.Uri,
			Login = envManifestSettings.Login,
			Password = envManifestSettings.Password,
			ClientId = envManifestSettings.ClientId,
			ClientSecret = envManifestSettings.ClientSecret,
			AuthAppUri = envManifestSettings.AuthAppUri,
			IsNetCore = envManifestSettings.IsNetCore
		};
		return environmnetOptions;
	}

	internal static T Resolve<T>(object options = null, bool logAndSettings = false) {
		EnvironmentSettings settings = null;
		if (options is EnvironmentOptions environmentOptions) {
			if (environmentOptions.RequiredEnvironment) {
				settings = GetEnvironmentSettings(environmentOptions as EnvironmentOptions);
			} else {
				settings = FindEnvironmentSettings(environmentOptions as EnvironmentOptions)
					?? new EnvironmentSettings() {
						Login = "default"
					};
			}
		}
		if (logAndSettings) {
			ConsoleLogger.Instance.WriteInfo(settings.Uri);
		}
		if (Container == null) {
			Container = new BindingsModule().Register(settings);
		}
		TryCheckUpdateOnStartCommand();
		return Container.Resolve<T>();
	}

	private static void TryCheckForUpdate() {
		try {
			new Thread(AppUpdater.CheckUpdate).Start();
		} catch (Exception ex) {

		}
	}

	private static int Main(string[] args) {
		try {
			var clearArgs = args.Where(x => x.ToLower() != "--debug").ToArray();
			IsDebugMode = args.Any(x => x.ToLower() == "--debug");
			OriginalArgs = args;
			ConsoleLogger.Instance.Start();
			return ExecuteCommands(clearArgs);
		}  catch (Exception e) {
			ConsoleLogger.Instance.WriteError(e.GetReadableMessageException(IsDebugMode));
			return 1;
		} finally {
			ConsoleLogger.Instance.Stop();
		}
	}

	internal static int ExecuteCommands(string[] args) {
		var creatioEnv = new CreatioEnvironment();
		string helpFolderName = $"help";
		var envPath = creatioEnv.GetAssemblyFolderPath();
		string helpDirectoryPath = Path.Combine(envPath ?? string.Empty, helpFolderName);
		Parser.Default.Settings.ShowHeader = false;
		Parser.Default.Settings.HelpDirectory = helpDirectoryPath;
		Parser.Default.Settings.CustomHelpViewer = new WikiHelpViewer();
		ParserResult<object> parserResult = Parser.Default.ParseArguments(args, CommandOption);
		if (parserResult is Parsed<object> parsed) {
			return ExecuteCommandWithOption(parsed.Value);
		}
		return HandleParseError(((NotParsed<object>)parserResult).Errors);
	}

	internal static bool IsCfgOpenCommand = false;

	private static void TryCheckUpdateOnStartCommand() {
		IsCfgOpenCommand = OriginalArgs?.Length switch {
			2 when OriginalArgs[0] == "cfg" && OriginalArgs[1] == "open" => true,
			_ => IsCfgOpenCommand
		};

		if (!IsCfgOpenCommand) {
			var needCheck = AutoUpdate;
			if (needCheck) {
				TryCheckForUpdate();
			}
		}
	}

	private static PushPkgOptions CreateClioGatePkgOptions(InstallGateOptions opts) {
		var pushPackageOptions = CreatePushPkgOptions(opts);
		pushPackageOptions.DeveloperModeEnabled = false;
		pushPackageOptions.RestartEnvironment = true;
		return pushPackageOptions;
	}

	private static int HandleParseError(IEnumerable<Error> errs) {
		var exitCode = 1;

		var notRealErrors = new List<ErrorType>()
		{
			ErrorType.VersionRequestedError,
			ErrorType.HelpRequestedError,
			ErrorType.HelpVerbRequestedError,
		};

		var isNotRealError = errs.Select(err => err.Tag)
			.Intersect(notRealErrors)
			.Any();

		if (isNotRealError)
			exitCode = 0;

		return exitCode;
	}

	private static int SetDeveloperMode(DeveloperModeOptions opts) {
		try {
			SetupAppConnection(opts, true);
			var repository = new SettingsRepository();
			CreatioEnvironment.Settings.DeveloperModeEnabled = true;
			repository.ConfigureEnvironment(CreatioEnvironment.EnvironmentName, CreatioEnvironment.Settings);
			var sysSettingOptions = new SysSettingsOptions() {
				Code = "Maintainer",
				Value = CreatioEnvironment.Settings.Maintainer
			};
			var sysSettingsCommand = Resolve<SysSettingsCommand>(opts);
			sysSettingsCommand.TryUpdateSysSetting(sysSettingOptions, CreatioEnvironment.Settings);
			UnlockMaintainerPackageInternal(opts);
			new RestartCommand(new CreatioClientAdapter(_creatioClientInstance), CreatioEnvironment.Settings).Execute(new RestartOptions());
			Console.WriteLine("Done");
			return 0;
		} catch (Exception e) {
			Console.WriteLine(e);
			return 1;
		}
	}

	private static void UnlockMaintainerPackageInternal(EnvironmentOptions environmentOptions) {
		IPackageLockManager packageLockManager = Resolve<IPackageLockManager>(environmentOptions);
		packageLockManager.Unlock();
	}

	private static int AddModels(ItemOptions opts) {

		if (opts.CreateAll) {
			Console.WriteLine("Generating models...");
			SetupAppConnection(opts);

			var workingDirectoryProvider = Resolve<IWorkingDirectoriesProvider>();
			ModelBuilder mb = new ModelBuilder(_creatioClientInstance, AppUrl, opts, workingDirectoryProvider);
			mb.GetModels();
			return 0;
		}

		try {
			SetupAppConnection(opts);
			var models = GetClassModels(opts.ItemName, opts.Fields);
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
	// clio add-item csschema "MySchema" -p "MyPkg" -n "MyNamespace"
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

	private static Dictionary<string, string> GetClassModels(string entitySchemaName, string fields) {
		var url = string.Format(GetEntityModelsUrl, entitySchemaName, fields);
		string responseFormServer = _creatioClientInstance.ExecuteGetRequest(url);
		var result = CorrectJson(responseFormServer);
		return JsonConvert.DeserializeObject<Dictionary<string, string>>(result);
	}

	private static int ConvertPackage(ConvertOptions opts) {
		return PackageConverter.Convert(opts);
	}


	private static Type[] CommandOption = new[]{
		typeof(ExecuteAssemblyOptions),
		typeof(RestartOptions),
		typeof(ClearRedisOptions),
		typeof(RegAppOptions),
		typeof(AppListOptions),
		typeof(UnregAppOptions),
		typeof(GeneratePkgZipOptions),
		typeof(PushPkgOptions),
		typeof(DeletePkgOptions),
		typeof(ReferenceOptions),
		typeof(NewPkgOptions),
		typeof(ConvertOptions),
		typeof(RegisterOptions),
		typeof(UnregisterOptions),
		typeof(PullPkgOptions),
		typeof(ExecuteSqlScriptOptions),
		typeof(InstallGateOptions),
		typeof(ItemOptions),
		typeof(DeveloperModeOptions),
		typeof(SysSettingsOptions),
		typeof(FeatureOptions),
		typeof(UnzipPkgOptions),
		typeof(PingAppOptions),
		typeof(OpenAppOptions),
		typeof(PkgListOptions),
		typeof(CompileOptions),
		typeof(PushNuGetPkgsOptions),
		typeof(PackNuGetPkgOptions),
		typeof(RestoreNugetPkgOptions),
		typeof(InstallNugetPkgOptions),
		typeof(SetPackageVersionOptions),
		typeof(GetPackageVersionOptions),
		typeof(CheckNugetUpdateOptions),
		typeof(RestoreWorkspaceOptions),
		typeof(CreateWorkspaceCommandOptions),
		typeof(PushWorkspaceCommandOptions),
		typeof(LoadPackagesToFileSystemOptions),
		typeof(UploadLicensesOptions),
		typeof(LoadPackagesToDbOptions),
		typeof(HealthCheckOptions),
		typeof(AddPackageOptions),
		typeof(UnlockPackageOptions),
		typeof(LockPackageOptions),
		typeof(DataServiceQuerryOptions),
		typeof(RestoreFromPackageBackupOptions),
		typeof(GetMarketplaceCatalogOptions),
		typeof(CreateUiProjectOptions),
		typeof(DownloadConfigurationCommandOptions),
		typeof(DeployCommandOptions),
		typeof(InfoCommandOptions),
		typeof(ExternalLinkOptions),
		typeof(OpenCfgOptions),
		typeof(CompileConfigurationOptions),
		typeof(Link2RepoOptions),
		typeof(Link4RepoOptions),
		typeof(TurnFsmCommandOptions),
		typeof(SetFsmConfigOptions),
		typeof(ScenarioRunnerOptions),
		typeof(CompressAppOptions),
		typeof(InstallApplicationOptions),
		typeof(ConfigureWorkspaceOptions),
		typeof(GitSyncOptions),
		typeof(BuildInfoOptions),
		typeof(PfInstallerOptions),
		typeof(CreateInfrastructureOptions),
		typeof(OpenInfrastructureOptions),
		typeof(CheckWindowsFeaturesOptions),
		typeof(ManageWindowsFeaturesOptions),
		typeof(CreateTestProjectOptions),
		typeof(DeactivatePkgOptions),
		typeof(ListenOptions),
		typeof(ShowPackageFileContentOptions),
		typeof(SwitchNugetToDllOptions),
		typeof(CompilePackageOptions),
		typeof(UninstallAppOptions),
		typeof(DownloadAppOptions),
		typeof(DeployAppOptions),
		typeof(ListInstalledAppsOptions),
		typeof(RestoreDbCommandOptions),
		typeof(SetWebServiceUrlOptions),
		typeof(ActivatePkgOptions),
		typeof(PackageHotFixCommandOptions),
		typeof(PublishWorkspaceCommandOptions),
		typeof(GetCreatioInfoCommandOptions),
		typeof(SetApplicationVersionOption),
		typeof(ApplyEnvironmentManifestOptions),
		typeof(GetWebServiceUrlOptions),
		typeof(SaveSettingsToManifestOptions),
		typeof(CloneEnvironmentOptions),
		typeof(ShowDiffEnvironmentsOptions),
		typeof(MockDataCommandOptions),
		typeof(UninstallCreatioCommandOptions),
		typeof(AddSchemaOptions),
		typeof(SetApplicationIconOption),

	};
	public static Func<object, int> ExecuteCommandWithOption = (instance) => {
		return instance switch {
			ExecuteAssemblyOptions opts => CreateRemoteCommand<AssemblyCommand>(opts).Execute(opts),
			RestartOptions opts => CreateRemoteCommand<RestartCommand>(opts).Execute(opts),
			ClearRedisOptions opts => CreateRemoteCommand<RedisCommand>(opts).Execute(opts),
			RegAppOptions opts => CreateCommand<RegAppCommand>(
				new SettingsRepository(), new ApplicationClientFactory(), new PowerShellFactory()).Execute(opts),
			AppListOptions opts => CreateCommand<ShowAppListCommand>(new SettingsRepository()).Execute(opts),
			UnregAppOptions opts => CreateCommand<UnregAppCommand>(new SettingsRepository()).Execute(opts),
			GeneratePkgZipOptions opts => Resolve<CompressPackageCommand>().Execute(opts),
			PushPkgOptions opts => Resolve<PushPackageCommand>(opts).Execute(opts),
			InstallApplicationOptions opts => Resolve<InstallApplicationCommand>(opts).Execute(opts),
			DeletePkgOptions opts => Resolve<DeletePackageCommand>(opts).Execute(opts),
			ReferenceOptions opts => CreateCommand<ReferenceCommand>(new CreatioPkgProjectCreator()).Execute(opts),
			NewPkgOptions opts => CreateCommand<NewPkgCommand>(new SettingsRepository(), CreateCommand<ReferenceCommand>(
				new CreatioPkgProjectCreator())).Execute(opts),
			ConvertOptions opts => ConvertPackage(opts),
			RegisterOptions opts => CreateCommand<RegisterCommand>().Execute(opts),
			UnregisterOptions opts => CreateCommand<UnregisterCommand>().Execute(opts),
			PullPkgOptions opts => DownloadZipPackages(opts),
			ExecuteSqlScriptOptions opts => Resolve<SqlScriptCommand>(opts).Execute(opts),
			InstallGateOptions opts => Resolve<InstallGatePkgCommand>(CreateClioGatePkgOptions(opts))
				.Execute(CreateClioGatePkgOptions(opts)),
			ItemOptions opts => AddItem(opts),
			DeveloperModeOptions opts => SetDeveloperMode(opts),
			//SysSettingsOptions opts => CreateRemoteCommand<SysSettingsCommand>(opts).Execute(opts),
			SysSettingsOptions opts => Resolve<SysSettingsCommand>(opts).Execute(opts),
			FeatureOptions opts => CreateRemoteCommand<FeatureCommand>(opts).Execute(opts),
			UnzipPkgOptions opts => Resolve<ExtractPackageCommand>().Execute(opts),
			PingAppOptions opts => CreateRemoteCommand<PingAppCommand>(opts).Execute(opts),
			OpenAppOptions opts => CreateRemoteCommandWithoutClient<OpenAppCommand>(opts).Execute(opts),
			PkgListOptions opts => Resolve<GetPkgListCommand>(opts).Execute(opts),
			CompileOptions opts => CreateRemoteCommand<CompileWorkspaceCommand>(opts).Execute(opts),
			PushNuGetPkgsOptions opts => Resolve<PushNuGetPackagesCommand>(opts).Execute(opts),
			PackNuGetPkgOptions opts => Resolve<PackNuGetPackageCommand>(opts).Execute(opts),
			RestoreNugetPkgOptions opts => Resolve<RestoreNugetPackageCommand>(opts).Execute(opts),
			InstallNugetPkgOptions opts => Resolve<InstallNugetPackageCommand>(opts).Execute(opts),
			SetPackageVersionOptions opts => Resolve<SetPackageVersionCommand>().Execute(opts),
			GetPackageVersionOptions opts => Resolve<GetPackageVersionCommand>().Execute(opts),
			CheckNugetUpdateOptions opts => Resolve<CheckNugetUpdateCommand>(opts).Execute(opts),
			RestoreWorkspaceOptions opts => Resolve<RestoreWorkspaceCommand>(opts).Execute(opts),
			CreateWorkspaceCommandOptions opts => Resolve<CreateWorkspaceCommand>(opts).Execute(opts),
			PushWorkspaceCommandOptions opts => Resolve<PushWorkspaceCommand>(opts).Execute(opts),
			//(UploadLicenseCommandOptions opts) => Resolve<UploadLicenseCommand>(opts).Execute(opts),
			LoadPackagesToFileSystemOptions opts => Resolve<LoadPackagesToFileSystemCommand>(opts)
				.Execute(opts),
			LoadPackagesToDbOptions opts => Resolve<LoadPackagesToDbCommand>(opts).Execute(opts),
			UploadLicensesOptions opts => Resolve<UploadLicensesCommand>(opts).Execute(opts),
			HealthCheckOptions opts => Resolve<HealthCheckCommand>(opts).Execute(opts),
			AddPackageOptions opts => Resolve<AddPackageCommand>(opts).Execute(opts),
			UnlockPackageOptions opts => Resolve<UnlockPackageCommand>(opts).Execute(opts),
			LockPackageOptions opts => Resolve<LockPackageCommand>(opts).Execute(opts),
			DataServiceQuerryOptions opts => Resolve<DataServiceQuerry>(opts).Execute(opts),
			RestoreFromPackageBackupOptions opts => Resolve<RestoreFromPackageBackupCommand>(opts).Execute(opts),
			GetMarketplaceCatalogOptions opts => Resolve<GetMarketplacecatalogCommand>(opts).Execute(opts),
			CreateUiProjectOptions opts => Resolve<CreateUiProjectCommand>(opts).Execute(opts),
			DownloadConfigurationCommandOptions opts => Resolve<DownloadConfigurationCommand>(opts).Execute(opts),
			DeployCommandOptions opts => Resolve<DeployCommand>(opts).Execute(opts),
			InfoCommandOptions opts => Resolve<InfoCommand>(opts).Execute(opts),
			ExternalLinkOptions opts => Resolve<ExternalLinkCommand>(opts).Execute(opts),
			OpenCfgOptions opts => Resolve<OpenCfgCommand>().Execute(opts),
			CompileConfigurationOptions opts => CreateRemoteCommand<CompileConfigurationCommand>(opts).Execute(opts),
			Link2RepoOptions opts => CreateCommand<Link2RepoCommand>().Execute(opts),
			Link4RepoOptions opts => CreateCommand<Link4RepoCommand>().Execute(opts),
			TurnFsmCommandOptions opts => Resolve<TurnFsmCommand>(opts).Execute(opts),
			SetFsmConfigOptions opts => Resolve<SetFsmConfigCommand>(opts).Execute(opts),
			CompressAppOptions opts => Resolve<CompressAppCommand>().Execute(opts),
			ScenarioRunnerOptions opts => Resolve<ScenarioRunnerCommand>(opts).Execute(opts),
			ConfigureWorkspaceOptions opts => Resolve<ConfigureWorkspaceCommand>(opts).Execute(opts),
			GitSyncOptions opts => Resolve<GitSyncCommand>(opts).Execute(opts),
			BuildInfoOptions opts => Resolve<BuildInfoCommand>(opts).Execute(opts),
			PfInstallerOptions opts => Resolve<InstallerCommand>(opts).Execute(opts),
			CreateInfrastructureOptions opts => Resolve<CreateInfrastructureCommand>().Execute(opts),
			OpenInfrastructureOptions opts => Resolve<OpenInfrastructureCommand>().Execute(opts),
			CheckWindowsFeaturesOptions opts => Resolve<CheckWindowsFeaturesCommand>().Execute(opts),
			ManageWindowsFeaturesOptions opts => Resolve<ManageWindowsFeaturesCommand>().Execute(opts),
			CreateTestProjectOptions opts => Resolve<CreateTestProjectCommand>(opts).Execute(opts),
			DeactivatePkgOptions opts => Resolve<DeactivatePackageCommand>(opts).Execute(opts),
			ListenOptions opts => Resolve<ListenCommand>(opts).Execute(opts),
			ShowPackageFileContentOptions opts => Resolve<ShowPackageFileContentCommand>(opts).Execute(opts),
			SwitchNugetToDllOptions opts => Resolve<SwitchNugetToDllCommand>(opts).Execute(opts),
			CompilePackageOptions opts => Resolve<CompilePackageCommand>(opts).Execute(opts),
			UninstallAppOptions opts => Resolve<UninstallAppCommand>(opts).Execute(opts),
			DownloadAppOptions opts => Resolve<DownloadAppCommand>(opts).Execute(opts),
			DeployAppOptions opts => Resolve<DeployAppCommand>(opts).Execute(opts),
			ListInstalledAppsOptions opts => Resolve<ListInstalledAppsCommand>(opts).Execute(opts),
			RestoreDbCommandOptions opts => Resolve<RestoreDbCommand>(opts).Execute(opts),
			SetWebServiceUrlOptions opts => Resolve<SetWebServiceUrlCommand>(opts).Execute(opts),
			PublishWorkspaceCommandOptions opts => Resolve<PublishWorkspaceCommand>(opts).Execute(opts),
			GetCreatioInfoCommandOptions opts => Resolve<GetCreatioInfoCommand>(opts).Execute(opts),
			ActivatePkgOptions opts => Resolve<ActivatePackageCommand>(opts).Execute(opts),
			PackageHotFixCommandOptions opts => Resolve<PackageHotFixCommand>(opts).Execute(opts),
			SetApplicationVersionOption opts => Resolve<SetApplicationVersionCommand>(opts).Execute(opts),
			ApplyEnvironmentManifestOptions opts => ResolveEnvSettings<ApplyEnvironmentManifestCommand>(opts).Execute(opts),
			GetWebServiceUrlOptions opts => Resolve<GetWebServiceUrlCommand>(opts).Execute(opts),
			SaveSettingsToManifestOptions opts => Resolve<SaveSettingsToManifestCommand>(opts).Execute(opts),
			CloneEnvironmentOptions opts => Resolve<CloneEnvironmentCommand>(opts).Execute(opts),
			ShowDiffEnvironmentsOptions opts => Resolve<ShowDiffEnvironmentsCommand>(opts).Execute(opts),
			MockDataCommandOptions opts => Resolve<MockDataCommand>(opts).Execute(opts),
			UninstallCreatioCommandOptions opts => Resolve<UninstallCreatioCommand>(opts).Execute(opts),
			AddSchemaOptions opts => Resolve<AddSchemaCommand>(opts).Execute(opts),
			SetApplicationIconOption opts => Resolve<SetApplicationIconCommand>(opts).Execute(opts),
			_ => 1,
		};
	};

	public static bool IsDebugMode { get; private set; }

	private static string[] OriginalArgs;
}

