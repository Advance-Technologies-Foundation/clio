#pragma warning disable CLIO001 // This is DI class, warning not applicable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using ATF.Repository;
using ATF.Repository.Providers;
using Clio.Command;
using Clio.Command.ApplicationCommand;
using Clio.Command.ChainItems;
using Clio.Command.CreatioInstallCommand;
using Clio.Command.EntitySchemaDesigner;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Resources;
using Clio.Command.PackageCommand;
using Clio.Command.ProcessModel;
using Clio.Command.SqlScriptCommand;
using Clio.Command.TIDE;
using Clio.Command.Update;
using Clio.Common;
using Clio.Common.Assertions;
using Clio.Common.db;
using Clio.Common.DeploymentStrategies;
using Clio.Common.SystemServices;
using Clio.Common.K8;
using Clio.Common.Kubernetes;
using Clio.Common.Database;
using Clio.Common.ScenarioHandlers;
using Clio.Common.DataForge;
using Clio.Common.EntitySchema;
using Clio.ComposableApplication;
using Clio.Help;
using Clio.Package;
using Clio.Package.NuGet;
using Clio.Project;
using Clio.Project.NuGet;
using Clio.Query;
using Clio.Requests;
using Clio.Requests.Validators;
using Clio.Utilities;
using Clio.Command.McpServer.Tools;
using Clio.Workspace;
using Clio.Workspaces;
using Clio.UserEnvironment;
using Clio.YAML;
using Creatio.Client;
using FluentValidation;
using k8s;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using FileSystem = System.IO.Abstractions.FileSystem;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio;

public enum BindingsModuleRegistrationProfile {
	Bootstrap,
	EnvironmentScoped
}

public class BindingsModule {

	#region Fields: Private

	public static string k8sDns = "127.0.0.1";
	private static readonly object BootstrapDiagnosticsSyncRoot = new();
	private static bool _bootstrapDiagnosticsLogged;
	private readonly IFileSystem _fileSystem;

	#endregion

	#region Constructors: Public

	public BindingsModule(IFileSystem fileSystem = null){
		_fileSystem = fileSystem;
	}

	#endregion

	#region Methods: Public

	public IServiceProvider Register(EnvironmentSettings settings = null,
		Action<IServiceCollection> additionalRegistrations = null,
		BindingsModuleRegistrationProfile? profile = null,
		bool applyBootstrapRepairs = true){
		BindingsModuleRegistrationProfile registrationProfile = profile
			?? (settings is null ? BindingsModuleRegistrationProfile.Bootstrap : BindingsModuleRegistrationProfile.EnvironmentScoped);
		IServiceCollection services = new ServiceCollection();
		RegisterAssemblyInterfaceTypes(services);
		services.AddSingleton<IWorkspacePathBuilder, WorkspacePathBuilder>();
		services.AddTransient<IVsProjectFactory, VsProjectFactory>();
		services.AddSingleton<ILogger>(ConsoleLogger.Instance);
		services.AddSingleton<IDbOperationLogContextAccessor, DbOperationLogContextAccessor>();
		services.AddSingleton<IDbOperationLogSessionFactory, DbOperationLogSessionFactory>();
		services.AddTransient<IContainerRegistryCredentialProvider, ContainerRegistryCredentialProvider>();
		services.AddHttpClient();
		services.AddHttpClient<IContainerRegistryPreflightService, ContainerRegistryPreflightService>();
		
		ISettingsBootstrapService settingsBootstrapService = new SettingsBootstrapService(_fileSystem, applyBootstrapRepairs);
		SettingsBootstrapResult bootstrapResult = settingsBootstrapService.GetResult();
		SettingsRepository settingsRepository = new(_fileSystem, settingsBootstrapService);
		services.AddSingleton<ISettingsBootstrapService>(settingsBootstrapService);
		services.AddSingleton<ISettingsRepository>(settingsRepository);
		LogBootstrapDiagnostics(registrationProfile, bootstrapResult.Report);

		EnvironmentSettings activeSettings = ResolveActiveSettings(settings, registrationProfile, bootstrapResult);

		if (activeSettings is not null) {
			services.AddSingleton(activeSettings);
			services.AddTransient<IDataProvider>(_ => new LazyDataProvider(() =>
				string.IsNullOrEmpty(activeSettings.ClientId)
					? new RemoteDataProvider(activeSettings.Uri, activeSettings.Login, activeSettings.Password,
						activeSettings.IsNetCore)
					: new RemoteDataProvider(activeSettings.Uri, activeSettings.AuthAppUri, activeSettings.ClientId,
						activeSettings.ClientSecret, activeSettings.IsNetCore)));
			Lazy<CreatioClient> lazyCreatioClient = new(() => string.IsNullOrEmpty(activeSettings.ClientId)
				? new CreatioClient(activeSettings.Uri ?? "http://localhost", activeSettings.Login ?? "Supervisor",
					activeSettings.Password ?? "Supervisor", true, activeSettings.IsNetCore)
				: CreatioClient.CreateOAuth20Client(activeSettings.Uri, activeSettings.AuthAppUri,
					activeSettings.ClientId, activeSettings.ClientSecret, activeSettings.IsNetCore));
			services.AddSingleton<CreatioClient>(_ => lazyCreatioClient.Value);
			services.AddSingleton<IApplicationClient>(_ => new CreatioClientAdapter(lazyCreatioClient));
			services.AddTransient<SysSettingsManager>();
		}

		services.AddTransient<IKubernetes>(_ => {
			try {
				KubernetesClientConfiguration config = KubernetesClientConfiguration.BuildConfigFromConfigFile();
				Uri.TryCreate(config.Host, UriKind.Absolute, out Uri uriResult);
				if (uriResult is null || (uriResult.Scheme != Uri.UriSchemeHttp && uriResult.Scheme != Uri.UriSchemeHttps)) {
					throw new InvalidOperationException("Invalid Kubernetes configuration host.");
				}
				k8sDns = uriResult.Host;
				return new Kubernetes(config);
			}
			catch {
				return new FakeKubernetes();
			}
		});

		services.AddTransient<IKubernetesClient, KubernetesClient>();
		services.AddTransient<K8ContextValidator>();
		services.AddTransient<IK8ServiceResolver, K8ServiceResolver>();
		services.AddTransient<IK8DatabaseDiscovery, K8DatabaseDiscovery>();
		services.AddTransient<IDatabaseConnectivityChecker, DatabaseConnectivityChecker>();
		services.AddTransient<IDatabaseCapabilityChecker, DatabaseCapabilityChecker>();
		services.AddTransient<IRedisDatabaseSelector, RedisDatabaseSelector>();
		services.AddTransient<K8DatabaseAssertion>();
		services.AddTransient<K8RedisAssertion>();
		services.AddTransient<Common.Assertions.FsPathAssertion>();
		services.AddTransient<Common.Assertions.FsPermissionAssertion>();
		services.AddTransient<ILocalDatabaseAssertion, LocalDatabaseAssertion>();
		services.AddTransient<ILocalRedisAssertion, LocalRedisAssertion>();
		services.AddTransient<k8Commands>();
		services.AddTransient<IInfrastructurePathProvider, InfrastructurePathProvider>();
		services.AddTransient<InstallerCommand>();
		services.AddTransient<IDockerTemplatePathProvider, DockerTemplatePathProvider>();
		services.AddTransient<IBuildDockerImageService, BuildDockerImageService>();
		services.AddHttpClient<ICodeServerArchiveCache, CodeServerArchiveCache>();

		if (_fileSystem is not null) {
			services.AddSingleton(_fileSystem);
		}
		else {
			services.AddTransient<IFileSystem, FileSystem>();
		}

		services.AddTransient<Clio.Common.IFileSystem, Clio.Common.FileSystem>();
		IDeserializer deserializer = new DeserializerBuilder()
			.WithNamingConvention(UnderscoredNamingConvention.Instance)
			.IgnoreUnmatchedProperties()
			.Build();
		ISerializer serializer = new SerializerBuilder()
			.WithNamingConvention(UnderscoredNamingConvention.Instance)
			.ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults | DefaultValuesHandling.OmitEmptyCollections)
			.Build();
		services.AddSingleton(deserializer);
		services.AddSingleton(serializer);

		services.AddTransient<IProcessExecutor, ProcessExecutor>();
		services.AddTransient<IDotnetExecutor, DotnetExecutor>();
		services.AddTransient<IPackageUtilities, PackageUtilities>();
		services.AddKeyedTransient<IFollowupUpChainItem, DconfChainItem>(nameof(DconfChainItem));
		services.AddTransient<IFollowUpChain, FollowUpChain>();
		services.AddTransient<FeatureCommand>();
		services.AddTransient<SysSettingsCommand>();
		services.AddTransient<BuildInfoCommand>();
		services.AddTransient<BuildDockerImageCommand>();
		services.AddTransient<InstallSkillsCommand>();
		services.AddTransient<UpdateSkillCommand>();
		services.AddTransient<DeleteSkillCommand>();
		services.AddTransient<PushPackageCommand>();
		services.AddTransient<InstallApplicationCommand>();
		services.AddTransient<IApplicationSectionCreateService, ApplicationSectionCreateService>();
		services.AddTransient<CreateAppSectionCommand>();
		services.AddTransient<IApplicationSectionUpdateService, ApplicationSectionUpdateService>();
		services.AddTransient<UpdateAppSectionCommand>();
		services.AddTransient<IApplicationSectionDeleteService, ApplicationSectionDeleteService>();
		services.AddTransient<DeleteAppSectionCommand>();
		services.AddTransient<IApplicationSectionGetListService, ApplicationSectionGetListService>();
		services.AddTransient<GetAppSectionsCommand>();
		services.AddTransient<CreateAppCommand>();
		services.AddTransient<GetAppInfoCommand>();
		services.AddTransient<CreateLookupCommand>();
		services.AddTransient<PageListCommand>();
		services.AddTransient<PageGetCommand>();
		services.AddTransient<PageUpdateCommand>();
		services.AddTransient<PageCreateCommand>();
		services.AddTransient<PageTemplatesListCommand>();
		services.AddTransient<ISchemaTemplateCatalog, SchemaTemplateCatalog>();
		services.AddTransient<IPageDesignerHierarchyClient, PageDesignerHierarchyClient>();
		services.AddTransient<IPageSchemaBodyParser, PageSchemaBodyParser>();
		services.AddTransient<IPageJsonDiffApplier, PageJsonDiffApplier>();
		services.AddTransient<IPageJsonPathDiffApplier, PageJsonPathDiffApplier>();
		services.AddTransient<IPageBundleBuilder, PageBundleBuilder>();
		services.AddSingleton<IComponentInfoCatalog, ComponentInfoCatalog>();
		
		// MCP Tools
		services.AddTransient<PageListTool>();
		services.AddTransient<ApplicationGetListTool>();
		services.AddTransient<ApplicationGetInfoTool>();
		services.AddTransient<ApplicationCreateTool>();
		services.AddTransient<ApplicationSectionCreateTool>();
		services.AddTransient<ApplicationSectionUpdateTool>();
		services.AddTransient<ApplicationSectionDeleteTool>();
		services.AddTransient<ApplicationSectionGetListTool>();
		services.AddTransient<ApplicationDeleteTool>();
		services.AddTransient<ToolContractGetTool>();
		services.AddTransient<PageGetTool>();
		services.AddTransient<PageUpdateTool>();
		services.AddTransient<PageCreateTool>();
		services.AddTransient<PageTemplatesListTool>();
		services.AddTransient<PageSyncTool>();
		services.AddTransient<ComponentInfoTool>();
		services.AddTransient<DataForgeTool>();
		services.AddTransient<IDataForgeEnrichmentBuilder, DataForgeEnrichmentBuilder>();
		services.AddTransient<IApplicationCreateEnrichmentService, ApplicationCreateEnrichmentService>();
		services.AddTransient<ISchemaEnrichmentService, SchemaEnrichmentService>();
		services.AddTransient<IToolCommandResolver, ToolCommandResolver>();
		services.AddHttpClient<IDataForgeClient, DataForgeClient>();
		services.AddTransient<IDataForgeSysSettingDirectReader, DataForgeSysSettingDirectReader>();
		services.AddSingleton<IDataForgeProxySafeExecutor, DataForgeProxySafeExecutor>();
		services.AddTransient<IDataForgeConfigResolver, DataForgeConfigResolver>();
		services.AddTransient<IDataForgeMaintenanceClient, DataForgeMaintenanceClient>();
		services.AddTransient<IRuntimeEntitySchemaReader, RuntimeEntitySchemaReader>();
		services.AddTransient<IDataForgeContextService, DataForgeContextService>();
		services.AddTransient<OpenCfgCommand>();
		services.AddTransient<InstallGatePkgCommand>();
		services.AddTransient<PingAppCommand>();
		services.AddTransient<SqlScriptCommand>();
		services.AddTransient<CompressPackageCommand>();
		services.AddTransient<PushNuGetPackagesCommand>();
		services.AddTransient<PackNuGetPackageCommand>();
		services.AddTransient<RestoreNugetPackageCommand>();
		services.AddTransient<InstallNugetPackageCommand>();
		services.AddTransient<SetPackageVersionCommand>();
		services.AddTransient<GetPackageVersionCommand>();
		services.AddTransient<CheckNugetUpdateCommand>();
		services.AddHttpClient<INugetPackagesProvider, NugetPackagesProvider>();
		services.AddTransient<UpdateCliCommand>();
		services.AddTransient<RegisterCommand>();
		services.AddTransient<UnregisterCommand>();
		
		services.AddTransient<IUserPromptService, UserPromptService>();
		services.AddTransient<DeletePackageCommand>();
		services.AddTransient<GetPkgListCommand>();
		services.AddTransient<RestoreWorkspaceCommand>();
		services.AddTransient<CreateWorkspaceCommand>();
		services.AddTransient<PushWorkspaceCommand>();
		services.AddSingleton<IDataBindingTemplateSchemaCatalog, DataBindingTemplateCatalog>();
		services.AddSingleton<IDataBindingTemplateCatalog>(provider =>
			provider.GetRequiredService<IDataBindingTemplateSchemaCatalog>());
		services.AddTransient<IDataBindingSchemaClient, DataBindingSchemaClient>();
		services.AddTransient<IDataBindingSchemaResolver, DataBindingSchemaResolver>();
		services.AddTransient<IDataBindingSerializer, DataBindingSerializer>();
		services.AddTransient<IDataBindingValueConverter, DataBindingValueConverter>();
		services.AddTransient<IDataBindingDisplayValueResolver, DataBindingDisplayValueResolver>();
		services.AddTransient<IDataBindingService, DataBindingService>();
		services.AddTransient<ILookupRegistrationService, LookupRegistrationService>();
		services.AddTransient<CreateDataBindingCommand>();
		services.AddTransient<AddDataBindingRowCommand>();
		services.AddTransient<RemoveDataBindingRowCommand>();
		services.AddTransient<IDataBindingDbService, DataBindingDbService>();
		services.AddTransient<CreateDataBindingDbCommand>();
		services.AddTransient<UpsertDataBindingRowDbCommand>();
		services.AddTransient<RemoveDataBindingRowDbCommand>();
		services.AddTransient<IWorkspaceMerger, WorkspaceMerger>();
		services.AddTransient<IWorkspacePackageFilter, WorkspacePackageFilter>();
		services.AddTransient<MergeWorkspacesCommand>();
		services.AddTransient<LoadPackagesToFileSystemCommand>();
		services.AddTransient<LoadPackagesToDbCommand>();
		services.AddTransient<UploadLicensesCommand>();
		services.AddTransient<HealthCheckCommand>();
		services.AddTransient<ShowLocalEnvironmentsCommand>();
		services.AddTransient<ClearLocalEnvironmentCommand>();
		services.AddTransient<AddPackageCommand>();
		services.AddTransient<UnlockPackageCommand>();
		services.AddTransient<LockPackageCommand>();
		services.AddTransient<DataServiceQuery>();
		services.AddTransient<CallServiceCommand>();
		services.AddTransient<RestoreFromPackageBackupCommand>();
		services.AddTransient<Marketplace>();
		services.AddTransient<CreateUiProjectCommand>();
		services.AddTransient<CreateUiProjectOptionsValidator>();
		services.AddTransient<SetIconParametersValidator>();
		services.AddTransient<DownloadConfigurationCommand>();
		services.AddTransient<DeployCommand>();
		services.AddTransient<InfoCommand>();
		services.AddTransient<QuizCommand>();
		services.AddTransient<ExtractPackageCommand>();
		services.AddTransient<ExternalLinkCommand>();
		services.AddTransient<PowerShellFactory>();
		services.AddTransient<IEnvironmentRuntimeDetectionService, EnvironmentRuntimeDetectionService>();
		services.AddTransient<IIisEnvironmentDiscoveryService, IisEnvironmentDiscoveryService>();
		services.AddTransient<RegAppCommand>();
		services.AddTransient<UnregAppCommand>();
		services.AddTransient<RestartCommand>();
		services.AddTransient<StartCommand>();
		services.AddTransient<StopCommand>();
		services.AddTransient<HostsCommand>();
		services.AddTransient<RedisCommand>();
		services.AddTransient<IFsmModeStatusService, FsmModeStatusService>();
		services.AddTransient<SetFsmConfigCommand>();
		services.AddTransient<TurnFsmCommand>();
		services.AddTransient<TurnFarmModeCommand>();
		services.AddTransient<ScenarioRunnerCommand>();
		services.AddTransient<CompressAppCommand>();
		services.AddTransient<Scenario>();
		services.AddTransient<ConfigureWorkspaceCommand>();
		services.AddTransient<CreateInfrastructureCommand>();
		services.AddTransient<DeployInfrastructureCommand>();
		services.AddTransient<DeleteInfrastructureCommand>();
		services.AddTransient<OpenInfrastructureCommand>();
		services.AddTransient<CheckWindowsFeaturesCommand>();
		services.AddTransient<ManageWindowsFeaturesCommand>();
		services.AddTransient<CreateTestProjectCommand>();
		services.AddTransient<ListenCommand>();
		services.AddTransient<ShowPackageFileContentCommand>();
		services.AddTransient<CompilePackageCommand>();
		services.AddTransient<SwitchNugetToDllCommand>();
		services.AddTransient<NugetMaterializer>();
		services.AddTransient<PropsBuilder>();
		services.AddTransient<UninstallAppCommand>();
		services.AddTransient<DownloadAppCommand>();
		services.AddTransient<DeployAppCommand>();
		services.AddTransient<ApplicationManager>();
		services.AddTransient<RestoreDbCommand>();
		services.AddTransient<IDbClientFactory, DbClientFactory>();
		services.AddTransient<IDbConnectionTester, DbConnectionTester>();
		services.AddTransient<IBackupFileDetector, BackupFileDetector>();
		services.AddSingleton<IPostgresToolsPathDetector, PostgresToolsPathDetector>();
		services.AddTransient<SetWebServiceUrlCommand>();
		services.AddTransient<ListInstalledAppsCommand>();
		services.AddTransient<GetCreatioInfoCommand>();
		services.AddTransient<SetApplicationVersionCommand>();
		services.AddTransient<ApplyEnvironmentManifestCommand>();
		services.AddTransient<EnvironmentManager>();
		services.AddTransient<GetWebServiceUrlCommand>();
		services.AddTransient<MockDataCommand>();
		services.AddTransient<AssertCommand>();
		services.AddTransient<ConsoleProgressbar>();
		services.AddTransient<ApplicationLogProvider>();
		services.AddTransient<LastCompilationLogCommand>();
		services.AddTransient<LinkWorkspaceWithTideRepositoryCommand>();
		services.AddTransient<CheckWebFarmNodeConfigurationsCommand>();
		services.AddTransient<GetAppHashCommand>();
		services.AddTransient<ShowAppListCommand>();
		services.AddTransient<EnvManageUiCommand>();
		services.AddTransient<IEnvManageUiService, EnvManageUiService>();
		services.AddTransient<IInstalledApplication, InstalledApplication>();
		services.AddTransient<Link4RepoCommand>();
		services.AddTransient<Link2RepoCommand>();
		services.AddTransient<LinkPackageStoreCommand>();
		services.AddTransient<LinkCoreSrcCommand>();
		services.AddTransient<RfsEnvironment>();

		services.AddMediatR(cfg => {
			cfg.RegisterServicesFromAssembly(typeof(BindingsModule).Assembly);
			cfg.AddOpenBehavior(typeof(ValidationBehaviour<,>));
		});

		services.AddTransient<ExternalLinkOptionsValidator>();
		services.AddTransient<SetFsmConfigOptionsValidator>();
		services.AddTransient<TurnFarmModeOptionsValidator>();
		services.AddTransient<UninstallCreatioCommandOptionsValidator>();
		services.AddTransient<Link4RepoOptionsValidator>();
		services.AddTransient<LinkPackageStoreOptionsValidator>();
		services.AddTransient<DownloadConfigurationCommandOptionsValidator>();
		services.AddTransient<AddItemOptionsValidator>();
		services.AddTransient<ICreatioUninstaller, CreatioUninstaller>();
		services.AddTransient<UnzipRequestValidator>();
		services.AddTransient<GitSyncCommand>();
		services.AddTransient<DeactivatePackageCommand>();
		services.AddTransient<PublishWorkspaceCommand>();
		services.AddTransient<ActivatePackageCommand>();
		services.AddTransient<PackageHotFixCommand>();
		services.AddTransient<PackageEditableMutator>();
		services.AddTransient<SaveSettingsToManifestCommand>();
		services.AddTransient<ShowDiffEnvironmentsCommand>();
		services.AddTransient<CloneEnvironmentCommand>();
		services.AddTransient<PullPkgCommand>();
		services.AddTransient<AssemblyCommand>();
		services.AddTransient<UninstallCreatioCommand>();
		services.AddTransient<InstallTideCommand>();
		services.AddTransient<AddSchemaCommand>();
		services.AddTransient<CreateEntitySchemaCommand>();
		services.AddTransient<UpdateEntitySchemaCommand>();
		services.AddTransient<ModifyEntitySchemaColumnCommand>();
		services.AddTransient<GetEntitySchemaColumnPropertiesCommand>();
		services.AddTransient<GetEntitySchemaPropertiesCommand>();
		services.AddTransient<FindEntitySchemaCommand>();
		services.AddTransient<CreateUserTaskCommand>();
		services.AddTransient<ModifyUserTaskParametersCommand>();
		services.AddTransient<DeleteSchemaCommand>();
		services.AddTransient<CreatioInstallerService>();
		services.AddTransient<SetApplicationIconCommand>();
		services.AddTransient<CustomizeDataProtectionCommand>();
		services.AddTransient<GenerateProcessModelCommand>();
		services.AddTransient<AddItemCommand>();
		services.AddTransient<IZipFile, ZipFileWrapper>();
		services.AddTransient<IProcessModelGenerator, ProcessModelGenerator>();
		services.AddTransient<IProcessModelWriter, ProcessModelWriter>();
		services.AddTransient<IZipBasedApplicationDownloader, ZipBasedApplicationDownloader>();
		services.AddTransient<ICreatioHostService, CreatioHostService>();
		services.AddTransient<IISDeploymentStrategy>();
		services.AddTransient<DotNetDeploymentStrategy>();
		services.AddTransient<DeploymentStrategyFactory>();
		services.AddTransient<OpenAppCommand>();
		services.AddSingleton<ISystemServiceManager>(sp =>
			RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? new LinuxSystemServiceManager() :
			RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? new MacOSSystemServiceManager(sp.GetRequiredService<IProcessExecutor>()) :
			new WindowsSystemServiceManager());
		services.AddSingleton<Common.IIS.IIISSiteDetector>(_ =>
			RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
				? new Common.IIS.WindowsIISSiteDetector()
				: new Common.IIS.StubIISSiteDetector());
		services.AddSingleton<Common.IIS.IPlatformDetector, Common.IIS.PlatformDetector>();
		services.AddSingleton<Common.IIS.ITcpPortReservationReader, Common.IIS.TcpPortReservationReader>();
		services.AddTransient<Common.IIS.IAvailableIisPortService, Common.IIS.AvailableIisPortService>();
		services.AddSingleton<Common.IIS.IIISAppPoolManager>(sp =>
			RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
				? new Common.IIS.WindowsIISAppPoolManager(sp.GetRequiredService<IProcessExecutor>())
				: new Common.IIS.StubIISAppPoolManager());
		services.AddTransient<ClioGateway>();
		services.AddTransient<CompileConfigurationCommand>();
		services.AddTransient<CompileWorkspaceCommand>();
		services.AddTransient<IMssql, Mssql>();
		services.AddTransient<IPostgres, Postgres>();
		services.AddSingleton<CommandHelpCatalog>();
		services.AddTransient<CommandHelpRenderer>();
		services.AddTransient<HelpArtifactExporter>();
		services.AddTransient<LocalHelpViewer>();
		services.AddTransient<WikiHelpViewer>();
		
		services.AddTransient<McpServerCommand>();
		services.AddMcpServer(options => {
					options.Capabilities ??= new();
					options.Capabilities.Logging = new();
					options.ServerInstructions = McpServerInstructions.Text;
				})
				.WithStdioServerTransport()
				.WithResourcesFromAssembly(Assembly.GetExecutingAssembly())
				.WithToolsFromAssembly(Assembly.GetExecutingAssembly())
				.WithPromptsFromAssembly(Assembly.GetExecutingAssembly());
		
		RegisterFluentValidators(services);
		additionalRegistrations?.Invoke(services);
		return services.BuildServiceProvider(new ServiceProviderOptions {
			ValidateOnBuild = true,
			ValidateScopes = true
		});
	}

	private static EnvironmentSettings ResolveActiveSettings(
		EnvironmentSettings settings,
		BindingsModuleRegistrationProfile profile,
		SettingsBootstrapResult bootstrapResult) {
		if (settings is not null) {
			return settings;
		}
		if (profile == BindingsModuleRegistrationProfile.EnvironmentScoped) {
			return bootstrapResult.ResolvedEnvironment ?? CreateBootstrapPlaceholderEnvironment();
		}
		return CreateBootstrapPlaceholderEnvironment();
	}

	private static EnvironmentSettings CreateBootstrapPlaceholderEnvironment() {
		return new EnvironmentSettings {
			Uri = CreateBootstrapPlaceholderUri(),
			Login = string.Empty,
			Password = string.Empty
		};
	}

	private static string CreateBootstrapPlaceholderUri() {
		return new UriBuilder(Uri.UriSchemeHttp, "localhost")
			.Uri
			.GetComponents(UriComponents.SchemeAndServer, UriFormat.Unescaped);
	}

	private static void LogBootstrapDiagnostics(
		BindingsModuleRegistrationProfile profile,
		SettingsBootstrapReport report) {
		if (profile != BindingsModuleRegistrationProfile.Bootstrap) {
			return;
		}
		lock (BootstrapDiagnosticsSyncRoot) {
			if (_bootstrapDiagnosticsLogged) {
				return;
			}
			if (report.RepairsApplied.Count > 0) {
				string repairs = string.Join("; ", report.RepairsApplied.Select(repair => repair.Message));
				ConsoleLogger.Instance.WriteWarning(
					$"clio settings bootstrap repaired {repairs}. Active environment: {report.ResolvedActiveEnvironmentKey ?? "<none>"}.");
				_bootstrapDiagnosticsLogged = true;
				return;
			}
			if (string.Equals(report.Status, "broken", StringComparison.OrdinalIgnoreCase)) {
				string issue = report.Issues.FirstOrDefault()?.Message
					?? "appsettings.json is unreadable.";
				ConsoleLogger.Instance.WriteWarning(
					$"clio settings bootstrap is degraded. {issue} File path: {report.SettingsFilePath}");
				_bootstrapDiagnosticsLogged = true;
			}
		}
	}
	
	
	private static void RegisterAssemblyInterfaceTypes(IServiceCollection services){
		Type[] types = Assembly.GetExecutingAssembly().GetTypes();
		foreach (Type type in types) {
			if (!type.IsClass || type.IsAbstract || type.IsGenericTypeDefinition || type == typeof(ConsoleLogger)) {
				continue;
			}
			foreach (Type implementedInterface in type.GetInterfaces()) {
				if (implementedInterface.Namespace is null
					|| !implementedInterface.Namespace.StartsWith("Clio", StringComparison.Ordinal)
					|| !implementedInterface.Name.StartsWith("I", StringComparison.Ordinal)
					|| implementedInterface == typeof(IDbOperationLogSession)) {
					continue;
				}
				services.AddTransient(implementedInterface, type);
			}
		}
	}

	private static void RegisterFluentValidators(IServiceCollection services){
		Type validatorInterfaceType = typeof(IValidator<>);
		Type[] types = Assembly.GetExecutingAssembly().GetTypes();
		foreach (Type type in types) {
			if (!type.IsClass || type.IsAbstract) {
				continue;
			}
			Type[] validatorInterfaces = type.GetInterfaces();
			foreach (Type validatorInterface in validatorInterfaces) {
				if (!validatorInterface.IsGenericType
					|| validatorInterface.GetGenericTypeDefinition() != validatorInterfaceType) {
					continue;
				}
				services.AddTransient(validatorInterface, type);
			}
		}
	}

	#endregion

	private sealed class LazyDataProvider : IDataProvider {
		private readonly Lazy<IDataProvider> _lazy;
		internal LazyDataProvider(Func<IDataProvider> factory) => _lazy = new(factory);
		public IDefaultValuesResponse GetDefaultValues(string entitySchemaName) => _lazy.Value.GetDefaultValues(entitySchemaName);
		public IItemsResponse GetItems(ISelectQuery selectQuery) => _lazy.Value.GetItems(selectQuery);
		public IExecuteResponse BatchExecute(List<IBaseQuery> queries) => _lazy.Value.BatchExecute(queries);
		public T GetSysSettingValue<T>(string sysSettingCode) => _lazy.Value.GetSysSettingValue<T>(sysSettingCode);
		public bool GetFeatureEnabled(string featureCode) => _lazy.Value.GetFeatureEnabled(featureCode);
		public IExecuteProcessResponse ExecuteProcess(IExecuteProcessRequest request) => _lazy.Value.ExecuteProcess(request);
	}

}
#pragma warning restore CLIO001 // Non-nullable field is uninitialized.
