using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Clio.Command;
using Clio.Command.ApplicationCommand;
using Clio.Command.CreatioInstallCommand;
using Clio.Command.McpServer;
using Clio.Command.PackageCommand;
using Clio.Command.SqlScriptCommand;
using Clio.Command.TIDE;
using Clio.Command.Update;
using Clio.Common;
using Clio.Help;
using Clio.Package;
using Clio.Project;
using Clio.Query;
using Clio.UserEnvironment;
using CommandLine;
using Creatio.Client;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Clio;

internal class Program {

	#region Fields: Private

	private static bool? autoUpdate;

	private static bool useCreatioLogStreamer;

	private static readonly Type[] CommandOption = [
		typeof(RegAppOptions),
		typeof(UnregAppOptions),
		typeof(AppListOptions),
		typeof(ExecuteAssemblyOptions),
		typeof(CompressAppOptions),
		typeof(GeneratePkgZipOptions),
		typeof(UnzipPkgOptions),
		typeof(PushPkgOptions),
		typeof(PullPkgOptions),
		typeof(DeletePkgOptions),
		typeof(NewPkgOptions),
		typeof(ReferenceOptions),
		typeof(ConvertOptions),
		typeof(ExecuteSqlScriptOptions),
		typeof(InstallGateOptions),
		typeof(AddItemOptions),
		typeof(DeveloperModeOptions),
		typeof(SysSettingsOptions),
		typeof(FeatureOptions),
		typeof(PingAppOptions),
		typeof(ShowLocalEnvironmentsOptions),
		typeof(EnvManageUiOptions),
		typeof(ClearLocalEnvironmentOptions),
		typeof(OpenAppOptions),
		// Package development
		typeof(PkgListOptions),
		typeof(CompileOptions),
		typeof(PushNuGetPkgsOptions),
		typeof(PackNuGetPkgOptions),
		typeof(RestoreNugetPkgOptions),
		typeof(InstallNugetPkgOptions),
		typeof(SetPackageVersionOptions),
		typeof(GetPackageVersionOptions),
		typeof(CheckNugetUpdateOptions),
		typeof(UpdateCliOptions),
		typeof(CreateWorkspaceCommandOptions),
		typeof(RestoreWorkspaceOptions),
		typeof(PushWorkspaceCommandOptions),
		typeof(LoadPackagesToFileSystemOptions),
		typeof(UploadLicensesOptions),
		typeof(LoadPackagesToDbOptions),
		typeof(HealthCheckOptions),
		typeof(AddPackageOptions),
		typeof(CreateDataBindingOptions),
		typeof(AddDataBindingRowOptions),
		typeof(RemoveDataBindingRowOptions),
		typeof(CreateDataBindingDbOptions),
		typeof(UpsertDataBindingRowDbOptions),
		typeof(RemoveDataBindingRowDbOptions),
		typeof(UnlockPackageOptions),
		typeof(LockPackageOptions),
		typeof(DeactivatePkgOptions),
		typeof(CompilePackageOptions),
		typeof(CompileConfigurationOptions),
		typeof(DataServiceQueryOptions),
		typeof(CallServiceCommandOptions),
		typeof(RestoreFromPackageBackupOptions),
		typeof(CreateUiProjectOptions),
		typeof(DownloadConfigurationCommandOptions),
		typeof(DeployCommandOptions),
		typeof(InfoCommandOptions),
		typeof(ExternalLinkOptions),
		typeof(OpenCfgOptions),
		typeof(Link2RepoOptions),
		typeof(Link4RepoOptions),
		typeof(LinkPackageStoreOptions),
		typeof(TurnFsmCommandOptions),
		typeof(TurnFarmModeOptions),
		typeof(SetFsmConfigOptions),
		typeof(ScenarioRunnerOptions),
		typeof(InstallApplicationOptions),
		typeof(CreateAppSectionOptions),
		typeof(UpdateAppSectionOptions),
		typeof(DeleteAppSectionOptions),
		typeof(ApplicationSectionGetListOptions),
		typeof(CreateAppOptions),
		typeof(GetAppInfoOptions),
		typeof(CreateLookupOptions),
		typeof(PageListOptions),
		typeof(PageGetOptions),
		typeof(PageUpdateOptions),
		typeof(PageCreateOptions),
		typeof(PageTemplatesListOptions),
		typeof(ClientUnitSchemaUpdateOptions),
		typeof(ConfigureWorkspaceOptions),
		typeof(GitSyncOptions),
		typeof(BuildInfoOptions),
		typeof(BuildDockerImageOptions),
		typeof(InstallSkillsOptions),
		typeof(UpdateSkillOptions),
		typeof(DeleteSkillOptions),
		typeof(PfInstallerOptions),
		typeof(CreateInfrastructureOptions),
		typeof(DeployInfrastructureOptions),
		typeof(DeleteInfrastructureOptions),
		typeof(OpenInfrastructureOptions),
		typeof(CheckWindowsFeaturesOptions),
		typeof(ManageWindowsFeaturesOptions),
		typeof(CreateTestProjectOptions),
		typeof(ListenOptions),
		typeof(ShowPackageFileContentOptions),
		typeof(SwitchNugetToDllOptions),
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
		typeof(GetWebServiceUrlOptions),
		typeof(ApplyEnvironmentManifestOptions),
		typeof(SaveSettingsToManifestOptions),
		typeof(CloneEnvironmentOptions),
		typeof(ShowDiffEnvironmentsOptions),
		typeof(MockDataCommandOptions),
		typeof(UninstallCreatioCommandOptions),
		typeof(AddSchemaOptions),
		typeof(CreateEntitySchemaOptions),
		typeof(UpdateEntitySchemaOptions),
		typeof(ModifyEntitySchemaColumnOptions),
		typeof(GetEntitySchemaColumnPropertiesOptions),
		typeof(GetEntitySchemaPropertiesOptions),
		typeof(FindEntitySchemaOptions),
		typeof(CreateUserTaskOptions),
		typeof(ModifyUserTaskParametersOptions),
		typeof(DeleteSchemaOptions),
		typeof(SetApplicationVersionOption),
		typeof(SetApplicationIconOption),
		typeof(RestartOptions),
		typeof(StartOptions),
		typeof(StopOptions),
		typeof(HostsOptions),
		typeof(ClearRedisOptions),
		typeof(LastCompilationLogOptions),
		typeof(UploadLicenseCommandOptions),
		typeof(RegisterOptions),
		typeof(UnregisterOptions),
		typeof(InstallTideCommandOptions),
		typeof(LinkWorkspaceWithTideRepositoryOptions),
		typeof(CheckWebFarmNodeConfigurationsOptions),
		typeof(CustomizeDataProtectionCommandOptions),
		typeof(GetAppHashCommandOptions),
		typeof(MergeWorkspacesCommandOptions),
		typeof(GenerateProcessModelCommandOptions),
		typeof(LinkCoreSrcOptions),
		typeof(AssertOptions),
		typeof(McpServerCommandOptions),
		typeof(QuizCommandOptions),
		
		
	];
	private static readonly Lazy<IReadOnlyList<CommandSuggestionEntry>> CommandSuggestionsCatalog =
		new(CreateCommandSuggestionsCatalog);
	private const int CommandSuggestionLimit = 10;

	internal static bool IsCfgOpenCommand;
	internal static bool IsMcpServerMode { get; private set; }
	public static IAppUpdater _appUpdater;

	private sealed record CommandSuggestionEntry(string CanonicalName, IReadOnlyList<string> SearchTerms);
	private sealed record CommandSuggestionScore(string CanonicalName, int TokenOverlap, int EditDistance);

	internal static IReadOnlyList<Type> GetCommandOptionTypes() => CommandOption;

	private static string[] NormalizeCommandLineArgs(string[] args) {
		if (args.Length >= 3 &&
			string.Equals(args[0], "create-data-binding", StringComparison.OrdinalIgnoreCase)) {
			string[] normalizedArgs = (string[])args.Clone();
			for (int index = 1; index < normalizedArgs.Length; index++) {
				if (string.Equals(normalizedArgs[index], "--environment", StringComparison.OrdinalIgnoreCase)) {
					normalizedArgs[index] = "-e";
				}
			}

			return normalizedArgs;
		}

		return args;
	}

	public static Func<object, int> ExecuteCommandWithOption = instance => {
		return instance switch {
					ExecuteAssemblyOptions opts => CreateRemoteCommand<AssemblyCommand>(opts).Execute(opts),
					RestartOptions opts => Resolve<RestartCommand>(opts).Execute(opts),
					StartOptions opts => Resolve<StartCommand>(opts).Execute(opts),
					ClearRedisOptions opts => Resolve<RedisCommand>(opts).Execute(opts),
					UploadLicenseCommandOptions opts => Resolve<UploadLicenseCommand>(opts).Execute(opts),
					RegAppOptions opts => Resolve<RegAppCommand>(opts).Execute(opts),
					AppListOptions opts => Resolve<ShowAppListCommand>().Execute(opts),
					UnregAppOptions opts => CreateCommand<UnregAppCommand>(Resolve<ISettingsRepository>(), ConsoleLogger.Instance).Execute(opts),
					GeneratePkgZipOptions opts => Resolve<CompressPackageCommand>().Execute(opts),
					PushPkgOptions opts => Resolve<PushPackageCommand>(opts).Execute(opts),
					InstallApplicationOptions opts => Resolve<InstallApplicationCommand>(opts).Execute(opts),
					CreateAppSectionOptions opts => Resolve<CreateAppSectionCommand>(opts).Execute(opts),
					UpdateAppSectionOptions opts => Resolve<UpdateAppSectionCommand>(opts).Execute(opts),
					DeleteAppSectionOptions opts => Resolve<DeleteAppSectionCommand>(opts).Execute(opts),
					ApplicationSectionGetListOptions opts => Resolve<GetAppSectionsCommand>(opts).Execute(opts),
					CreateAppOptions opts => Resolve<CreateAppCommand>(opts).Execute(opts),
					GetAppInfoOptions opts => Resolve<GetAppInfoCommand>(opts).Execute(opts),
					CreateLookupOptions opts => Resolve<CreateLookupCommand>(opts).Execute(opts),
					DeletePkgOptions opts => Resolve<DeletePackageCommand>(opts).Execute(opts),
					ReferenceOptions opts => CreateCommand<ReferenceCommand>(Resolve<ICreatioPkgProjectCreator>())
						.Execute(opts),
					NewPkgOptions opts => CreateCommand<NewPkgCommand>(Resolve<ISettingsRepository>(),
							CreateCommand<ReferenceCommand>(Resolve<ICreatioPkgProjectCreator>()), ConsoleLogger.Instance)
						.Execute(opts),
					ConvertOptions opts => ConvertPackage(opts),
					RegisterOptions opts => Resolve<RegisterCommand>().Execute(opts),
					UnregisterOptions opts => Resolve<UnregisterCommand>().Execute(opts),
					PullPkgOptions opts => DownloadZipPackages(opts),
					ExecuteSqlScriptOptions opts => Resolve<SqlScriptCommand>(opts).Execute(opts),
					InstallGateOptions opts => Resolve<InstallGatePkgCommand>(CreateClioGatePkgOptions(opts))
						.Execute(CreateClioGatePkgOptions(opts)),
					AddItemOptions opts => Resolve<AddItemCommand>(opts).Execute(opts),
					DeveloperModeOptions opts => SetDeveloperMode(opts),
					SysSettingsOptions opts => Resolve<SysSettingsCommand>(opts).Execute(opts),
					FeatureOptions opts => Resolve<FeatureCommand>(opts).Execute(opts),
					UnzipPkgOptions opts => Resolve<ExtractPackageCommand>().Execute(opts),
					PingAppOptions opts => CreateRemoteCommand<PingAppCommand>(opts).Execute(opts),
					OpenAppOptions opts => Resolve<OpenAppCommand>(opts).Execute(opts),
					PkgListOptions opts => Resolve<GetPkgListCommand>(opts).Execute(opts),
					ShowLocalEnvironmentsOptions opts => Resolve<ShowLocalEnvironmentsCommand>().Execute(opts),
					EnvManageUiOptions opts => Resolve<EnvManageUiCommand>().Execute(opts),
					ClearLocalEnvironmentOptions opts => Resolve<ClearLocalEnvironmentCommand>().Execute(opts),
					CompileOptions opts => Resolve<CompileWorkspaceCommand>(opts).Execute(opts),
					PushNuGetPkgsOptions opts => Resolve<PushNuGetPackagesCommand>(opts).Execute(opts),
					PackNuGetPkgOptions opts => Resolve<PackNuGetPackageCommand>(opts).Execute(opts),
					RestoreNugetPkgOptions opts => Resolve<RestoreNugetPackageCommand>(opts).Execute(opts),
					InstallNugetPkgOptions opts => Resolve<InstallNugetPackageCommand>(opts).Execute(opts),
					SetPackageVersionOptions opts => Resolve<SetPackageVersionCommand>().Execute(opts),
					GetPackageVersionOptions opts => Resolve<GetPackageVersionCommand>().Execute(opts),
					CheckNugetUpdateOptions opts => Resolve<CheckNugetUpdateCommand>(opts).Execute(opts),
					UpdateCliOptions opts => Resolve<UpdateCliCommand>(opts).Execute(opts),
					RestoreWorkspaceOptions opts => Resolve<RestoreWorkspaceCommand>(opts).Execute(opts),
					CreateWorkspaceCommandOptions opts => Resolve<CreateWorkspaceCommand>(opts).Execute(opts),
					PushWorkspaceCommandOptions opts => Resolve<PushWorkspaceCommand>(opts).Execute(opts),
					LoadPackagesToFileSystemOptions opts => Resolve<LoadPackagesToFileSystemCommand>(opts)
						.Execute(opts),
					LoadPackagesToDbOptions opts => Resolve<LoadPackagesToDbCommand>(opts).Execute(opts),
					UploadLicensesOptions opts => Resolve<UploadLicensesCommand>(opts).Execute(opts),
					HealthCheckOptions opts => Resolve<HealthCheckCommand>(opts).Execute(opts),
					AddPackageOptions opts => Resolve<AddPackageCommand>(opts).Execute(opts),
					CreateDataBindingOptions opts => Resolve<CreateDataBindingCommand>(opts).Execute(opts),
					AddDataBindingRowOptions opts => Resolve<AddDataBindingRowCommand>().Execute(opts),
					RemoveDataBindingRowOptions opts => Resolve<RemoveDataBindingRowCommand>().Execute(opts),				CreateDataBindingDbOptions opts => Resolve<CreateDataBindingDbCommand>(opts).Execute(opts),
				UpsertDataBindingRowDbOptions opts => Resolve<UpsertDataBindingRowDbCommand>(opts).Execute(opts),
				RemoveDataBindingRowDbOptions opts => Resolve<RemoveDataBindingRowDbCommand>(opts).Execute(opts),					UnlockPackageOptions opts => Resolve<UnlockPackageCommand>(opts).Execute(opts),
					LockPackageOptions opts => Resolve<LockPackageCommand>(opts).Execute(opts),
					DataServiceQueryOptions opts => Resolve<DataServiceQuery>(opts).Execute(opts),
					CallServiceCommandOptions opts => Resolve<CallServiceCommand>(opts).Execute(opts),
					RestoreFromPackageBackupOptions opts =>
						Resolve<RestoreFromPackageBackupCommand>(opts).Execute(opts),
					CreateUiProjectOptions opts => Resolve<CreateUiProjectCommand>(opts).Execute(opts),
					DownloadConfigurationCommandOptions opts => Resolve<DownloadConfigurationCommand>(opts)
						.Execute(opts),
					DeployCommandOptions opts => Resolve<DeployCommand>(opts).Execute(opts),
					InfoCommandOptions opts => Resolve<InfoCommand>(opts).Execute(opts),
					ExternalLinkOptions opts => Resolve<ExternalLinkCommand>(opts).Execute(opts),
					OpenCfgOptions opts => Resolve<OpenCfgCommand>().Execute(opts),
					CompileConfigurationOptions opts => Resolve<CompileConfigurationCommand>(opts)
						.Execute(opts),
					Link2RepoOptions opts => Resolve<Link2RepoCommand>().Execute(opts),
					Link4RepoOptions opts => Resolve<Link4RepoCommand>(opts).Execute(opts),
					TurnFsmCommandOptions opts => Resolve<TurnFsmCommand>(opts).Execute(opts),
					TurnFarmModeOptions opts => Resolve<TurnFarmModeCommand>(opts).Execute(opts),
					SetFsmConfigOptions opts => Resolve<SetFsmConfigCommand>(opts).Execute(opts),
					CompressAppOptions opts => Resolve<CompressAppCommand>().Execute(opts),
					ScenarioRunnerOptions opts => Resolve<ScenarioRunnerCommand>(opts).Execute(opts),
					ConfigureWorkspaceOptions opts => Resolve<ConfigureWorkspaceCommand>(opts).Execute(opts),
					GitSyncOptions opts => Resolve<GitSyncCommand>(opts).Execute(opts),
					BuildInfoOptions opts => Resolve<BuildInfoCommand>(opts).Execute(opts),
					BuildDockerImageOptions opts => Resolve<BuildDockerImageCommand>().Execute(opts),
					InstallSkillsOptions opts => Resolve<InstallSkillsCommand>().Execute(opts),
					UpdateSkillOptions opts => Resolve<UpdateSkillCommand>().Execute(opts),
					DeleteSkillOptions opts => Resolve<DeleteSkillCommand>().Execute(opts),
					PfInstallerOptions opts => Resolve<InstallerCommand>(opts).Execute(opts),
					CreateInfrastructureOptions opts => Resolve<CreateInfrastructureCommand>().Execute(opts),
					DeployInfrastructureOptions opts => Resolve<DeployInfrastructureCommand>().Execute(opts),
					DeleteInfrastructureOptions opts => Resolve<DeleteInfrastructureCommand>().Execute(opts),
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
					ApplyEnvironmentManifestOptions opts => ResolveEnvSettings<ApplyEnvironmentManifestCommand>(opts)
						.Execute(opts),
					GetWebServiceUrlOptions opts => Resolve<GetWebServiceUrlCommand>(opts).Execute(opts),
					SaveSettingsToManifestOptions opts => Resolve<SaveSettingsToManifestCommand>(opts).Execute(opts),
					CloneEnvironmentOptions opts => Resolve<CloneEnvironmentCommand>(opts).Execute(opts),
					ShowDiffEnvironmentsOptions opts => Resolve<ShowDiffEnvironmentsCommand>(opts).Execute(opts),
					MockDataCommandOptions opts => Resolve<MockDataCommand>(opts).Execute(opts),
					UninstallCreatioCommandOptions opts => Resolve<UninstallCreatioCommand>(opts).Execute(opts),
					AddSchemaOptions opts => Resolve<AddSchemaCommand>(opts).Execute(opts),
					CreateEntitySchemaOptions opts => Resolve<CreateEntitySchemaCommand>(opts).Execute(opts),
					UpdateEntitySchemaOptions opts => Resolve<UpdateEntitySchemaCommand>(opts).Execute(opts),
					ModifyEntitySchemaColumnOptions opts => Resolve<ModifyEntitySchemaColumnCommand>(opts).Execute(opts),
					GetEntitySchemaColumnPropertiesOptions opts =>
						Resolve<GetEntitySchemaColumnPropertiesCommand>(opts).Execute(opts),
					GetEntitySchemaPropertiesOptions opts =>
						Resolve<GetEntitySchemaPropertiesCommand>(opts).Execute(opts),
					FindEntitySchemaOptions opts =>
						Resolve<FindEntitySchemaCommand>(opts).Execute(opts),
					CreateUserTaskOptions opts => Resolve<CreateUserTaskCommand>(opts).Execute(opts),
					ModifyUserTaskParametersOptions opts => Resolve<ModifyUserTaskParametersCommand>(opts).Execute(opts),
					DeleteSchemaOptions opts => Resolve<DeleteSchemaCommand>(opts).Execute(opts),
					SetApplicationIconOption opts => Resolve<SetApplicationIconCommand>(opts).Execute(opts),
					LastCompilationLogOptions opts => Resolve<LastCompilationLogCommand>(opts).Execute(opts),
					InstallTideCommandOptions opts => Resolve<InstallTideCommand>(opts).Execute(opts),
					CustomizeDataProtectionCommandOptions opts => Resolve<CustomizeDataProtectionCommand>(opts).Execute(opts),
					LinkWorkspaceWithTideRepositoryOptions opts => Resolve<LinkWorkspaceWithTideRepositoryCommand>(opts)
						.Execute(opts),
					CheckWebFarmNodeConfigurationsOptions opts => Resolve<CheckWebFarmNodeConfigurationsCommand>(opts)
						.Execute(opts),
					GetAppHashCommandOptions opts => Resolve<GetAppHashCommand>(opts)
						.Execute(opts),
					MergeWorkspacesCommandOptions opts => Resolve<MergeWorkspacesCommand>(opts)
						.Execute(opts),
					GenerateProcessModelCommandOptions opts => Resolve<GenerateProcessModelCommand>(opts)
						.Execute(opts),
					StopOptions opts => Resolve<StopCommand>(opts).Execute(opts),
					HostsOptions opts => Resolve<HostsCommand>(opts).Execute(opts),
					LinkCoreSrcOptions opts => Resolve<LinkCoreSrcCommand>(opts).Execute(opts),
					AssertOptions opts => Resolve<AssertCommand>(opts).Execute(opts),
					LinkPackageStoreOptions opts => Resolve<LinkPackageStoreCommand>(opts).Execute(opts),
					McpServerCommandOptions opts => Resolve<McpServerCommand>(opts).Execute(opts),
					PageCreateOptions opts => Resolve<PageCreateCommand>(opts).Execute(opts),
					PageTemplatesListOptions opts => Resolve<PageTemplatesListCommand>(opts).Execute(opts),
					QuizCommandOptions opts => Resolve<QuizCommand>().Execute(opts),
					var _ => 1
				};
	};

	private static string[] OriginalArgs;

	#endregion

	#region Properties: Private

	private static CreatioClient _creatioClientInstance {
		get {
			if (string.IsNullOrEmpty(ClientId)) {
				return new CreatioClient(Url, UserName, UserPassword, true, CreatioEnvironment.IsNetCore);
			}
			return CreatioClient.CreateOAuth20Client(Url, AuthAppUrl, ClientId, ClientSecret,
				CreatioEnvironment.IsNetCore);
		}
	}

	private static string ApiVersionUrl => AppUrl + @"/rest/CreatioApiGateway/GetApiVersion";

	private static string AppUrl {
		get {
			if (CreatioEnvironment.IsNetCore) {
				return Url;
			}
			return Url + @"/0";
		}
	}

	private static string AuthAppUrl => CreatioEnvironment.Settings.AuthAppUri;

	private static string ClientId => CreatioEnvironment.Settings.ClientId;

	private static string ClientSecret => CreatioEnvironment.Settings.ClientSecret;

	private static string DeleteExistsPackagesZipUrl => AppUrl + @"/rest/PackagesGateway/DeleteExistsPackagesZip";

	private static string DownloadExistsPackageZipUrl => AppUrl + @"/rest/PackagesGateway/DownloadExistsPackageZip";

	private static string ExistsPackageZipUrl => AppUrl + @"/rest/PackagesGateway/ExistsPackageZip";

	private static string GetZipPackageUrl => AppUrl + @"/ServiceModel/PackageInstallerService.svc/GetZipPackages";

	private static string Url => CreatioEnvironment.Settings.Uri; // Should be obtained from config

	private static string UserName => CreatioEnvironment.Settings.Login;

	private static string UserPassword => CreatioEnvironment.Settings.Password;

	#endregion

	#region Properties: Internal

	internal static IServiceProvider Container { get; set; }

	#endregion

	#region Properties: Public

	public static bool AddTimeStampToOutput { get; internal set; }

	public static IAppUpdater AppUpdater {
		get {
			if (_appUpdater == null) {
				_appUpdater = Container.GetRequiredService<IAppUpdater>();
			}
			return _appUpdater;
		}
		set { _appUpdater = value; }
	}

	public static bool AutoUpdate {
	get { return autoUpdate.HasValue ? autoUpdate.Value : Resolve<ISettingsRepository>().GetAutoupdate(); }
	set { autoUpdate = value; }
}

	public static bool IsDebugMode { get; set; }

	public static bool IsEnvironmentReported { get; set; }

	public static bool Safe { get; private set; } = true;

	#endregion

	#region Methods: Private

	/// <summary>
	/// Configures the environment with the specified options.
	/// </summary>
	/// <param name="options">Environment configuration options</param>
	/// <param name="checkEnvExist">If true, verifies that the environment exists before proceeding</param>
	/// <exception cref="ArgumentException">Thrown when the environment doesn't exist and checkEnvExist is true</exception>
	private static void Configure(EnvironmentOptions options, bool checkEnvExist = false){
		ISettingsRepository settingsRepository = Resolve<ISettingsRepository>();
		if (string.IsNullOrWhiteSpace(options.Environment) && string.IsNullOrEmpty(options.Uri)) {
			string activeEnvName = settingsRepository.GetDefaultEnvironmentName();
			if (!string.IsNullOrWhiteSpace(activeEnvName) && settingsRepository.IsEnvironmentExists(activeEnvName)) {
				options.Environment = activeEnvName;
			}
		}
		CreatioEnvironment.EnvironmentName = options.Environment;
		if (checkEnvExist) {
			bool isEnvironmentExists = settingsRepository.IsEnvironmentExists(options.Environment);
			if (!isEnvironmentExists) {
				throw new ArgumentException($"Cannot find environment with name {options.Environment}",
					nameof(options.Environment));
			}
		}
		CreatioEnvironment.Settings = settingsRepository.GetEnvironment(options);
		ICreatioEnvironment creatioEnvironment = Resolve<ICreatioEnvironment>();
	}

	/// <summary>
	/// Converts a package using the specified options.
	/// </summary>
	/// <param name="opts">Package conversion options</param>
	/// <returns>Result code from the conversion operation</returns>
	private static int ConvertPackage(ConvertOptions opts){
		return Resolve<IPackageConverter>().Convert(opts);
	}

	/// <summary>
	/// Creates package options specifically for Clio Gate installation.
	/// </summary>
	/// <param name="opts">Gate installation options</param>
	/// <returns>Configured package options</returns>
	internal static PushPkgOptions CreateClioGatePkgOptions(InstallGateOptions opts){
		PushPkgOptions pushPackageOptions = CreatePushPkgOptions(opts);
		pushPackageOptions.DeveloperModeEnabled = false;
		pushPackageOptions.RestartEnvironment = true;
		return pushPackageOptions;
	}

	/// <summary>
	/// Creates a command of the specified type with the provided constructor arguments.
	/// </summary>
	/// <typeparam name="TCommand">Type of command to create</typeparam>
	/// <param name="additionalConstructorArgs">Additional arguments to pass to the constructor</param>
	/// <returns>Instantiated command</returns>
	private static TCommand CreateCommand<TCommand>(params object[] additionalConstructorArgs){
		return (TCommand)Activator.CreateInstance(typeof(TCommand), additionalConstructorArgs);
	}

	/// <summary>
	/// Creates package options based on installation options.
	/// </summary>
	/// <param name="options">Gate installation options</param>
	/// <returns>Configured package options</returns>
	private static PushPkgOptions CreatePushPkgOptions(InstallGateOptions options){
	ISettingsRepository settingsRepository = Resolve<ISettingsRepository>();
	EnvironmentSettings settings = settingsRepository.GetEnvironment(options);
	IWorkingDirectoriesProvider workingDirectoriesProvider = Resolve<IWorkingDirectoriesProvider>(options);
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

	/// <summary>
	/// Creates a remote command with a client connection to the Creatio environment.
	/// </summary>
	/// <typeparam name="TCommand">Type of command to create</typeparam>
	/// <param name="options">Environment options</param>
	/// <param name="additionalConstructorArgs">Additional arguments to pass to the constructor</param>
	/// <returns>Instantiated command with connection to remote environment</returns>
	private static TCommand CreateRemoteCommand<TCommand>(EnvironmentOptions options,
		params object[] additionalConstructorArgs){
		EnvironmentSettings settings = GetEnvironmentSettings(options);
		CreatioClient creatioClient = string.IsNullOrEmpty(settings.ClientId) ? new CreatioClient(settings.Uri,
				settings.Login, settings.Password, true, settings.IsNetCore) :
			CreatioClient.CreateOAuth20Client(settings.Uri, settings.AuthAppUri, settings.ClientId,
				settings.ClientSecret, settings.IsNetCore);
		CreatioClientAdapter clientAdapter = new(creatioClient);
		object[] constructorArgs = new object[] {clientAdapter, settings}.Concat(additionalConstructorArgs).ToArray();
		return (TCommand)Activator.CreateInstance(typeof(TCommand), constructorArgs);
	}

	/// <summary>
	/// Creates a remote command without a client connection to the Creatio environment.
	/// </summary>
	/// <typeparam name="TCommand">Type of command to create</typeparam>
	/// <param name="options">Environment options</param>
	/// <param name="additionalConstructorArgs">Additional arguments to pass to the constructor</param>
	/// <returns>Instantiated command without connection to remote environment</returns>
	private static TCommand CreateRemoteCommandWithoutClient<TCommand>(EnvironmentOptions options,
		params object[] additionalConstructorArgs){
		EnvironmentSettings settings = GetEnvironmentSettings(options);
		object[] constructorArgs = new object[] {settings}.Concat(additionalConstructorArgs).ToArray();
		return (TCommand)Activator.CreateInstance(typeof(TCommand), constructorArgs);
	}

	/// <summary>
	/// Downloads packages from the Creatio environment to the specified destination.
	/// </summary>
	/// <param name="packageName">Name of the package to download</param>
	/// <param name="destinationPath">Path where the downloaded package will be saved</param>
	/// <param name="_async">If true, performs the download asynchronously</param>
	private static void DownloadZipPackagesInternal(string packageName, string destinationPath, bool _async){
		try {
			Console.WriteLine("Start download packages ({0}).", packageName);
			int count = 0;
			string packageNames
				= string.Format("\"{0}\"", packageName.Replace(" ", string.Empty).Replace(",", "\",\""));
			string requestData = "[" + packageNames + "]";
			if (!_async) {
				_creatioClientInstance.DownloadFile(GetZipPackageUrl, destinationPath, requestData, 600000);
			}
			else {
				_creatioClientInstance.ExecutePostRequest(DeleteExistsPackagesZipUrl, string.Empty);
				new Thread(() => {
					try {
						_creatioClientInstance.DownloadFile(GetZipPackageUrl, Path.GetTempFileName(), requestData,
							2000);
					}
					catch { }
				}).Start();
				bool again = false;
				do {
					Thread.Sleep(2000);
					again = !bool.Parse(_creatioClientInstance.ExecutePostRequest(ExistsPackageZipUrl, string.Empty));
					if (++count > 600) {
						throw new TimeoutException("Timeout exception");
					}
				} while (again);
				Thread.Sleep(1000);
				_creatioClientInstance.DownloadFile(DownloadExistsPackageZipUrl, destinationPath, requestData, 60000);
			}
			Console.WriteLine("Download packages ({0}) completed.", packageName);
		}
		catch (Exception) {
			Console.WriteLine("Download packages ({0}) not completed.", packageName);
		}
	}

	/// <summary>
	/// Finds environment settings based on the environment name in the options.
	/// </summary>
	/// <param name="options">Environment options containing the environment name</param>
	/// <returns>Environment settings if found, null otherwise</returns>
	private static EnvironmentSettings FindEnvironmentSettings(EnvironmentOptions options){
	SettingsRepository settingsRepository = new();
	return settingsRepository.FindEnvironment(options.Environment);
}

	/// <summary>
	/// Gets the API version from the configured Creatio environment.
	/// </summary>
	/// <returns>API version, or 0.0.0.0 if the version cannot be determined</returns>
	private static Version GetAppApiVersion(){
		Version apiVersion = new("0.0.0.0");
		try {
			string appVersionResponse = _creatioClientInstance.ExecuteGetRequest(ApiVersionUrl).Trim('"');
			apiVersion = new Version(appVersionResponse);
		}
		catch (Exception) { }
		return apiVersion;
	}

	/// <summary>
	/// Gets environment settings based on the provided options.
	/// </summary>
	/// <param name="options">Environment options</param>
	/// <returns>Environment settings</returns>
	private static EnvironmentSettings GetEnvironmentSettings(EnvironmentOptions options){
	SettingsRepository settingsRepository = new();
	if (string.IsNullOrWhiteSpace(options.Environment) && string.IsNullOrEmpty(options.Uri)) {
		string activeEnvName = settingsRepository.GetDefaultEnvironmentName();
		if (!string.IsNullOrWhiteSpace(activeEnvName) && settingsRepository.IsEnvironmentExists(activeEnvName)) {
			options.Environment = activeEnvName;
		}
	}
	return settingsRepository.GetEnvironment(options);
}

	/// <summary>
	/// Handles errors that occur during command-line parsing.
	/// </summary>
	/// <param name="errs">Collection of parsing errors</param>
	/// <returns>Exit code based on the type of errors encountered</returns>
	private static int HandleParseError(IEnumerable<Error> errs){
		Error[] errors = errs.ToArray();
		int exitCode = 1;

		List<ErrorType> notRealErrors = new() {
			ErrorType.VersionRequestedError,
			ErrorType.HelpRequestedError,
			ErrorType.HelpVerbRequestedError
		};

		bool isNotRealError = errors.Select(err => err.Tag)
								.Intersect(notRealErrors)
								.Any();

		if (isNotRealError) {
			exitCode = 0;
		}
		else {
			BadVerbSelectedError badVerbError = errors.OfType<BadVerbSelectedError>().FirstOrDefault();
			if (badVerbError != null) {
				WriteUnknownCommandSuggestions(badVerbError.Token);
			}
		}

		return exitCode;
	}

	private static void WriteUnknownCommandSuggestions(string requestedCommand) {
		string[] suggestions = GetUnknownCommandSuggestions(requestedCommand);
		TextWriter output = Console.Out;
		output.WriteLine();
		if (suggestions.Length > 0) {
			output.WriteLine("Maybe you meant:");
			foreach (string suggestion in suggestions) {
				output.WriteLine($"  clio {suggestion}");
			}
			output.WriteLine();
		}
		output.WriteLine("See all commands: clio help");
		output.WriteLine("See command help: clio <command> --help");
	}

	private static string[] GetUnknownCommandSuggestions(string requestedCommand) {
		if (string.IsNullOrWhiteSpace(requestedCommand)) {
			return [];
		}
		CommandSuggestionScore[] scores = CommandSuggestionsCatalog.Value
			.Select(entry => BuildCommandSuggestionScore(requestedCommand, entry))
			.OrderByDescending(score => score.TokenOverlap)
			.ThenBy(score => score.EditDistance)
			.ThenBy(score => score.CanonicalName, StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (scores.Length == 0) {
			return [];
		}
		string comparableRequestedCommand = NormalizeComparableCommandName(requestedCommand);
		int suggestionDistanceThreshold = GetSuggestionDistanceThreshold(comparableRequestedCommand.Length);
		CommandSuggestionScore[] relevantScores = scores
			.Where(score => score.TokenOverlap > 0 || score.EditDistance <= suggestionDistanceThreshold)
			.ToArray();
		if (relevantScores.Length == 0) {
			return [];
		}
		return relevantScores
			.Take(CommandSuggestionLimit)
			.Select(score => score.CanonicalName)
			.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}

	private static CommandSuggestionScore BuildCommandSuggestionScore(string requestedCommand,
		CommandSuggestionEntry entry) {
		string[] requestedTokens = TokenizeCommandName(requestedCommand);
		string comparableRequestedCommand = NormalizeComparableCommandName(requestedCommand);
		int bestTokenOverlap = 0;
		int bestEditDistance = int.MaxValue;
		foreach (string searchTerm in entry.SearchTerms) {
			string normalizedSearchTerm = NormalizeComparableCommandName(searchTerm);
			int tokenOverlap = CountTokenOverlap(requestedTokens, TokenizeCommandName(searchTerm));
			int editDistance = ComputeDistance(comparableRequestedCommand, normalizedSearchTerm);
			editDistance = GetEffectiveEditDistance(comparableRequestedCommand, entry.CanonicalName, searchTerm,
				normalizedSearchTerm, tokenOverlap, editDistance);
			if (tokenOverlap > bestTokenOverlap || tokenOverlap == bestTokenOverlap && editDistance < bestEditDistance) {
				bestTokenOverlap = tokenOverlap;
				bestEditDistance = editDistance;
			}
		}
		return new CommandSuggestionScore(entry.CanonicalName, bestTokenOverlap, bestEditDistance);
	}

	private static int GetEffectiveEditDistance(string comparableRequestedCommand, string canonicalName, string searchTerm,
		string normalizedSearchTerm, int tokenOverlap, int editDistance) {
		if (tokenOverlap > 0 || editDistance <= 1 || string.Equals(searchTerm, canonicalName, StringComparison.OrdinalIgnoreCase)) {
			return editDistance;
		}
		if (comparableRequestedCommand.Length < 5 || normalizedSearchTerm.Length > 4) {
			return editDistance;
		}
		return editDistance + comparableRequestedCommand.Length;
	}

	private static IReadOnlyList<CommandSuggestionEntry> CreateCommandSuggestionsCatalog() {
		Dictionary<string, HashSet<string>> searchTermsByCanonicalName = new(StringComparer.OrdinalIgnoreCase);
		foreach (Type optionType in CommandOption) {
			VerbAttribute verbAttribute = optionType.GetCustomAttribute<VerbAttribute>();
			if (verbAttribute == null || verbAttribute.Hidden || string.IsNullOrWhiteSpace(verbAttribute.Name)) {
				continue;
			}
			if (!searchTermsByCanonicalName.TryGetValue(verbAttribute.Name, out HashSet<string> searchTerms)) {
				searchTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				searchTermsByCanonicalName[verbAttribute.Name] = searchTerms;
			}
			searchTerms.Add(verbAttribute.Name);
			if (verbAttribute.Aliases == null) {
				continue;
			}
			foreach (string alias in verbAttribute.Aliases.Where(alias => !string.IsNullOrWhiteSpace(alias) && !alias.Any(char.IsWhiteSpace))) {
				searchTerms.Add(alias);
			}
		}
		return searchTermsByCanonicalName
			.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
			.Select(entry => new CommandSuggestionEntry(entry.Key,
				entry.Value.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray()))
			.ToArray();
	}

	private static int CountTokenOverlap(IEnumerable<string> requestedTokens, IEnumerable<string> candidateTokens) {
		HashSet<string> requested = requestedTokens.ToHashSet(StringComparer.OrdinalIgnoreCase);
		HashSet<string> candidate = candidateTokens.ToHashSet(StringComparer.OrdinalIgnoreCase);
		requested.IntersectWith(candidate);
		return requested.Count;
	}

	private static string[] TokenizeCommandName(string commandName) {
		if (string.IsNullOrWhiteSpace(commandName)) {
			return [];
		}
		List<string> tokens = [];
		StringBuilder currentToken = new();
		for (int index = 0; index < commandName.Length; index++) {
			char current = commandName[index];
			if (!char.IsLetterOrDigit(current)) {
				FlushToken(tokens, currentToken);
				continue;
			}
			if (currentToken.Length > 0 && char.IsUpper(current) && char.IsLower(currentToken[currentToken.Length - 1])) {
				FlushToken(tokens, currentToken);
			}
			currentToken.Append(char.ToLowerInvariant(current));
		}
		FlushToken(tokens, currentToken);
		return tokens.ToArray();
	}

	private static void FlushToken(ICollection<string> tokens, StringBuilder currentToken) {
		if (currentToken.Length == 0) {
			return;
		}
		tokens.Add(NormalizeCommandToken(currentToken.ToString()));
		currentToken.Clear();
	}

	private static string NormalizeCommandToken(string token) {
		if (string.IsNullOrWhiteSpace(token) || token.Length <= 3) {
			return token;
		}
		if (token.EndsWith("ies", StringComparison.OrdinalIgnoreCase) && token.Length > 4) {
			return string.Concat(token.AsSpan(0, token.Length - 3), "y");
		}
		if (token.EndsWith('s') && !token.EndsWith("ss", StringComparison.OrdinalIgnoreCase)) {
			return token[..^1];
		}
		return token;
	}

	private static string NormalizeComparableCommandName(string commandName) {
		if (string.IsNullOrWhiteSpace(commandName)) {
			return string.Empty;
		}
		StringBuilder normalized = new(commandName.Length);
		foreach (char current in commandName.Where(char.IsLetterOrDigit)) {
			normalized.Append(char.ToLowerInvariant(current));
		}
		return normalized.ToString();
	}

	private static int GetSuggestionDistanceThreshold(int commandLength) {
		return commandLength switch {
			<= 2 => 0,
			<= 6 => 1,
			<= 10 => 2,
			_ => 3
		};
	}

	private static int ComputeDistance(string source, string target) {
		if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase)) {
			return 0;
		}
		int[,] matrix = new int[source.Length + 1, target.Length + 1];
		for (int row = 0; row <= source.Length; row++) {
			matrix[row, 0] = row;
		}
		for (int column = 0; column <= target.Length; column++) {
			matrix[0, column] = column;
		}
		for (int row = 1; row <= source.Length; row++) {
			for (int column = 1; column <= target.Length; column++) {
				int cost = source[row - 1] == target[column - 1] ? 0 : 1;
				matrix[row, column] = Math.Min(
					Math.Min(matrix[row - 1, column] + 1, matrix[row, column - 1] + 1),
					matrix[row - 1, column - 1] + cost);
			}
		}
		return matrix[source.Length, target.Length];
	}

	private static bool IsMcpCommand(string[] args) {
		string commandName = args.FirstOrDefault(arg => !string.IsNullOrWhiteSpace(arg) && !arg.StartsWith("-", StringComparison.Ordinal));
		return string.Equals(commandName, "mcp-server", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(commandName, "mcp", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Main entry point for the application.
	/// </summary>
	/// <param name="args">Command line arguments</param>
	/// <returns>Exit code indicating success (0) or failure (non-zero)</returns>
	public static int Main(string[] args){
		bool loggerStarted = false;
		try {
			string logTarget = string.Empty;
			bool isLog = args.Contains("--log");
			if (isLog) {
				int logIndex = Array.IndexOf(args, "--log");
				logTarget = args[logIndex + 1];
				args = args.Where(x => x != "--log" && x != logTarget).ToArray();
			}

			string[] clearArgs = args.Where(x => x.ToLower() != "--debug" && x.ToLower() != "--ts").ToArray();
			if (clearArgs.Length > 0 && string.Equals(clearArgs[0], "__generate-help-artifacts", StringComparison.OrdinalIgnoreCase)) {
				return ExportHelpArtifacts();
			}
			if (TryHandleBuiltInVersion(clearArgs, out int versionExitCode)) {
				return versionExitCode;
			}
			bool isMcp = IsMcpCommand(clearArgs);
			IsMcpServerMode = isMcp;
			IsDebugMode = args.Any(x => x.ToLower() == "--debug");
			AddTimeStampToOutput = args.Any(x => x.ToLower() == "--ts");
			OriginalArgs = args;
			
			// Set IsCfgOpenCommand based on input arguments
			IsCfgOpenCommand = (args.Length >= 2 && args[0] == "cfg" && args[1] == "open");
			
			if (isMcp) {
				ConsoleLogger.Instance.PreserveMessages = true;
			}
			
				if (logTarget.ToLower() == "creatio") {
					useCreatioLogStreamer = true;
					ConsoleLogger.Instance.StartWithStream();
					loggerStarted = true;
				}  
				else {
					ConsoleLogger.Instance.Start(logTarget);
					loggerStarted = true;
				}
				return ExecuteCommands(clearArgs);
		}
		catch (Exception e) {
			ConsoleLogger.Instance.WriteError(e.GetReadableMessageException(IsDebugMode));
			return 1;
			}
			finally {
				if (loggerStarted) {
					ConsoleLogger.Instance.Stop();
				}
			}
		}

	/// <summary>
	/// Displays a colored message to the console.
	/// </summary>
	/// <param name="text">Text to display</param>
	/// <param name="color">Color to use for the text</param>
	private static void MessageToConsole(string text, ConsoleColor color){
		ConsoleColor currentColor = Console.ForegroundColor;
		Console.ForegroundColor = color;
		Console.WriteLine(text);
		Console.ForegroundColor = currentColor;
	}

	/// <summary>
	/// Resolves environment settings from a manifest file and creates an instance of the specified type.
	/// </summary>
	/// <typeparam name="T">Type to resolve</typeparam>
	/// <param name="options">Options containing the manifest file path</param>
	/// <returns>Resolved instance</returns>
	private static T ResolveEnvSettings<T>(ApplyEnvironmentManifestOptions options = null){
		EnvironmentOptions optionFromFile = ReadEnvironmentOptionsFromManifestFile(options.ManifestFilePath);
		EnvironmentOptions combinedOption = CombinedOption(optionFromFile, options);
		return Resolve<T>(combinedOption, true);
	}

	/// <summary>
	/// Enables developer mode for the specified environment.
	/// </summary>
	/// <param name="opts">Developer mode options</param>
	/// <returns>0 if the operation succeeds, 1 otherwise</returns>
	private static int SetDeveloperMode(DeveloperModeOptions opts){
	try {
		SetupAppConnection(opts, true);
		ISettingsRepository repository = Resolve<ISettingsRepository>();
		CreatioEnvironment.Settings.DeveloperModeEnabled = true;
		repository.ConfigureEnvironment(CreatioEnvironment.EnvironmentName, CreatioEnvironment.Settings);
		SysSettingsOptions sysSettingOptions = new() {
			Code = "Maintainer",
			Value = CreatioEnvironment.Settings.Maintainer
		};
		SysSettingsCommand sysSettingsCommand = Resolve<SysSettingsCommand>(opts);
		sysSettingsCommand.TryUpdateSysSetting(sysSettingOptions, CreatioEnvironment.Settings);
		UnlockMaintainerPackageInternal(opts);
		Resolve<RestartCommand>(opts).Execute(new RestartOptions());
		Console.WriteLine("Done");
		return 0;
	}
	catch (Exception e) {
		Console.WriteLine(e);
		return 1;
	}
}

	/// <summary>
	/// Unlocks the maintainer package in the specified environment.
	/// </summary>
	/// <param name="environmentOptions">Environment options</param>
	private static void UnlockMaintainerPackageInternal(EnvironmentOptions environmentOptions){
		IPackageLockManager packageLockManager = Resolve<IPackageLockManager>(environmentOptions);
		packageLockManager.Unlock();
	}

	/// <summary>
	/// Unzips a package file to the default location.
	/// </summary>
	/// <param name="zipFilePath">Path to the zip file</param>
	// private static void UnZip(string zipFilePath){
	// 	IPackageArchiver packageArchiver = Resolve<IPackageArchiver>();
	// 	packageArchiver.UnZip(zipFilePath, true);
	// }

	/// <summary>
	/// Extracts packages from a zip file to the specified destination.
	/// </summary>
	/// <param name="zipFilePath">Path to the zip file containing packages</param>
	/// <param name="destinationPath">Destination directory for extracted packages</param>
	private static void UnZipPackages(string zipFilePath, string destinationPath){
		IPackageArchiver packageArchiver = Resolve<IPackageArchiver>();
		packageArchiver.ExtractPackages(zipFilePath, true, true, true, false, destinationPath);
	}

	#endregion

	#region Methods: Internal

	/// <summary>
	/// Downloads and optionally extracts packages from the Creatio environment.
	/// </summary>
	/// <param name="options">Options specifying which packages to download and how to process them</param>
	/// <returns>0 if the operation succeeds, 1 otherwise</returns>
	internal static int DownloadZipPackages(PullPkgOptions options){
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
			}
			else {
				string destPath = options.DestPath ?? Path.Combine(Environment.CurrentDirectory, $"{packageName}.zip");
				if (Directory.Exists(destPath)) {
					destPath = Path.Combine(destPath, $"{packageName}.zip");
				}
				DownloadZipPackagesInternal(packageName, destPath, options.Async);
			}
			Console.WriteLine("Done");
			return 0;
		}
		catch (Exception e) {
			Console.WriteLine(e);
			return 1;
		}
	}

	/// <summary>
	/// Executes commands based on the provided command line arguments.
	/// Sets up the command-line parser with appropriate settings and processes the arguments.
	/// </summary>
	/// <param name="args">Command line arguments to process</param>
	/// <returns>Exit code from the executed command, or a parse error code</returns>
	internal static int ExecuteCommands(string[] args){
		CreatioEnvironment creatioEnv = new();
		const string helpFolderName = "help";
		string envPath = creatioEnv.GetAssemblyFolderPath();
		string helpDirectoryPath = Path.Combine(envPath ?? string.Empty, helpFolderName);
		Parser.Default.Settings.ShowHeader = false;
		Parser.Default.Settings.HelpDirectory = helpDirectoryPath;
		IServiceProvider bm = new BindingsModule().Register(applyBootstrapRepairs: false);
		if (TryHandleBuiltInHelp(args, bm, out int helpExitCode)) {
			return helpExitCode;
		}
		if(args.Length >= 2 && (args[1] == "--WEB" || args[1] == "-W")) {
			Parser.Default.Settings.CustomHelpViewer = bm.GetRequiredService<WikiHelpViewer>();
		}
		else {
			Parser.Default.Settings.CustomHelpViewer = bm.GetRequiredService<LocalHelpViewer>();
		}
		
		string[] normalizedArgs = NormalizeCommandLineArgs(args);
		ParserResult<object> parserResult = Parser.Default.ParseArguments(normalizedArgs, CommandOption);
		if (parserResult is Parsed<object> parsed) {
			return ExecuteCommandWithOption(parsed.Value);
		}
		return HandleParseError(((NotParsed<object>)parserResult).Errors);
	}

	private static bool TryHandleBuiltInHelp(string[] args, IServiceProvider serviceProvider, out int exitCode) {
		CommandHelpRenderer renderer = serviceProvider.GetRequiredService<CommandHelpRenderer>();
		string[] normalizedArgs = NormalizeCommandLineArgs(args);
		if (normalizedArgs.Length == 0
			|| normalizedArgs.Length == 1 && IsRootHelpToken(normalizedArgs[0])) {
			Console.Out.Write(renderer.RenderRootHelp(RootHelpRenderMode.Runtime));
			exitCode = 0;
			return true;
		}
		if (normalizedArgs.Length >= 2 && string.Equals(normalizedArgs[0], "help", StringComparison.OrdinalIgnoreCase)) {
			if (renderer.TryRenderCommandHelp(normalizedArgs[1]) is string commandHelp) {
				Console.Out.Write(commandHelp);
				exitCode = 0;
				return true;
			}
			Console.Out.Write(renderer.RenderRootHelp(RootHelpRenderMode.Runtime));
			exitCode = 0;
			return true;
		}
		exitCode = 1;
		return false;
	}

	private static bool TryHandleBuiltInVersion(string[] args, out int exitCode) {
		string[] normalizedArgs = NormalizeCommandLineArgs(args);
		if (normalizedArgs.Length == 1 && IsRootVersionToken(normalizedArgs[0])) {
			Console.Out.WriteLine(GetBuiltInVersionOutput());
			exitCode = 0;
			return true;
		}
		exitCode = 1;
		return false;
	}

	private static bool IsRootHelpToken(string value) =>
		string.Equals(value, "help", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase);

	private static bool IsRootVersionToken(string value) =>
		string.Equals(value, "--version", StringComparison.OrdinalIgnoreCase);

	private static string GetBuiltInVersionOutput() {
		Assembly clioAssembly = Assembly.GetExecutingAssembly();
		FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(clioAssembly.Location);
		return versionInfo.FileVersion;
	}

	private static int ExportHelpArtifacts() {
		BindingsModule bindingsModule = new();
		IServiceProvider serviceProvider = bindingsModule.Register();
		IWorkingDirectoriesProvider workingDirectoriesProvider = serviceProvider.GetRequiredService<IWorkingDirectoriesProvider>();
		string repositoryRoot = FindRepositoryRoot(workingDirectoriesProvider.ExecutingDirectory);
		HelpArtifactExporter exporter = serviceProvider.GetRequiredService<HelpArtifactExporter>();
		return exporter.Export(repositoryRoot);
	}

	private static string FindRepositoryRoot(string startDirectory) {
		string currentDirectory = Path.GetFullPath(startDirectory);
		while (!string.IsNullOrWhiteSpace(currentDirectory)) {
			if (Directory.Exists(Path.Combine(currentDirectory, "clio"))
				&& Directory.Exists(Path.Combine(currentDirectory, "clio.tests"))
				&& File.Exists(Path.Combine(currentDirectory, "clio", "Commands.md"))) {
				return currentDirectory;
			}
			string parentDirectory = Path.GetDirectoryName(currentDirectory);
			if (string.IsNullOrWhiteSpace(parentDirectory) || string.Equals(parentDirectory, currentDirectory, StringComparison.Ordinal)) {
				break;
			}
			currentDirectory = parentDirectory;
		}
		return Path.GetFullPath(startDirectory);
	}

	/// <summary>
	/// Resolves an instance of the specified type from the dependency injection container.
	/// If needed, configures the environment settings based on the provided options.
	/// </summary>
	/// <typeparam name="T">Type to resolve from the container</typeparam>
	/// <param name="options">Options used to configure the environment settings</param>
	/// <param name="logAndSettings">If true, logs the environment URI</param>
	/// <returns>Resolved instance of the specified type</returns>
	internal static T Resolve<T>(object options = null, bool logAndSettings = false){
		EnvironmentSettings settings = null;
		if (options is EnvironmentOptions environmentOptions && !IsCfgOpenCommand) {
			if (environmentOptions.RequiredEnvironment || !string.IsNullOrEmpty(environmentOptions.Uri)) {
				settings = GetEnvironmentSettings(environmentOptions);
			}
			else {
				settings = FindEnvironmentSettings(environmentOptions)
					?? new EnvironmentSettings {
						Login = "default"
					};
			}
		}
		if (logAndSettings) {
			ConsoleLogger.Instance.WriteInfo(settings.Uri);
		}
		if (Container == null) {
			BindingsModuleRegistrationProfile profile = settings is null
				? BindingsModuleRegistrationProfile.Bootstrap
				: BindingsModuleRegistrationProfile.EnvironmentScoped;
			Container = new BindingsModule().Register(settings, profile: profile);
		}
		if (useCreatioLogStreamer) {
			ConsoleLogger.Instance.SetCreatioLogStreamer(Container.GetRequiredService<ILogStreamer>());
		}
		return Container.GetRequiredService<T>();
	}

	#endregion

	#region Methods: Public

	/// <summary>
	/// Checks the API version of the connected Creatio environment against the local API version.
	/// Displays warning messages if the API is missing or outdated.
	/// </summary>
	public static void CheckApiVersion(){
		string dir = AppDomain.CurrentDomain.BaseDirectory;
		string versionFilePath = Path.Combine(dir, "cliogate", "version.txt");
		Version localApiVersion = new(File.ReadAllText(versionFilePath));
		Version appApiVersion = GetAppApiVersion();
		if (appApiVersion == new Version("0.0.0.0")) {
			MessageToConsole($"Your app does not contain clio API." +
				$"{Environment.NewLine}You should consider install it via the \'clio install-gate\' command.",
				ConsoleColor.DarkYellow);
		}
		else if (localApiVersion > appApiVersion) {
			MessageToConsole(
				$"You are using clio api version {appApiVersion}, however version {localApiVersion} is available." +
				$"{Environment.NewLine}You should consider upgrading via the \'clio update-gate\' command.",
				ConsoleColor.DarkYellow);
		}
	}

	/// <summary>
	/// Combines environment options from a file and from command line arguments,
	/// giving priority to command line values when both are specified.
	/// </summary>
	/// <param name="optionFromFile">Environment options from a file</param>
	/// <param name="optionsFromCommandLine">Environment options from the command line</param>
	/// <returns>Combined environment options</returns>
	public static EnvironmentOptions CombinedOption(EnvironmentOptions optionFromFile,
		EnvironmentOptions optionsFromCommandLine){
		if (optionFromFile == null && optionsFromCommandLine == null) {
			return null;
		}
		if (optionFromFile == null && optionsFromCommandLine.IsEmpty()) {
			return optionsFromCommandLine;
		}
		if (string.IsNullOrEmpty(optionsFromCommandLine.Environment)) {
			EnvironmentNameOptions result = new();
			result.Uri = optionsFromCommandLine.Uri ?? optionFromFile.Uri;
			result.Login = optionsFromCommandLine.Login ?? optionFromFile.Login;
			result.Password = optionsFromCommandLine.Password ?? optionFromFile.Password;
			result.AuthAppUri = optionsFromCommandLine.AuthAppUri ?? optionFromFile.AuthAppUri;
			result.ClientId = optionsFromCommandLine.ClientId ?? optionFromFile.ClientId;
			result.ClientSecret = optionsFromCommandLine.ClientSecret ?? optionFromFile.ClientSecret;
			result.IsNetCore = optionsFromCommandLine.IsNetCore.HasValue ? optionsFromCommandLine.IsNetCore
				: optionFromFile.IsNetCore;
			return result;
		}
		return optionsFromCommandLine;
	}

	/// <summary>
	/// Reads environment options from a manifest file.
	/// </summary>
	/// <param name="manifestFilePath">Path to the manifest file</param>
	/// <param name="fileSystem">Optional file system for reading the manifest file</param>
	/// <returns>Environment options extracted from the manifest file</returns>
	public static EnvironmentOptions ReadEnvironmentOptionsFromManifestFile(string manifestFilePath,
		IFileSystem fileSystem = null){
		IDeserializer deserializer = new DeserializerBuilder()
									.WithNamingConvention(UnderscoredNamingConvention.Instance)
									.IgnoreUnmatchedProperties()
									.Build();
		string manifest = fileSystem is null ? File.ReadAllText(manifestFilePath)
			: fileSystem.ReadAllText(manifestFilePath);
		EnvironmentManifest envManifest = deserializer.Deserialize<EnvironmentManifest>(manifest);
		EnvironmentSettings envManifestSettings = envManifest.EnvironmentSettings;
		if (envManifestSettings == null) {
			return null;
		}
		EnvironmentOptions environmnetOptions = new() {
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

	/// <summary>
	/// Sets up the connection to the Creatio application with the specified options.
	/// </summary>
	/// <param name="options">Environment options for connecting to the application</param>
	/// <param name="checkEnvExist">If true, verifies that the environment exists before proceeding</param>
	public static void SetupAppConnection(EnvironmentOptions options, bool checkEnvExist = false){
		Configure(options, checkEnvExist);
		CheckApiVersion();
	}

	#endregion

}


