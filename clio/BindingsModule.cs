#pragma warning disable CLIO001 // This is DI class, warning not applicable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
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
using Clio.ComposableApplication;
using Clio.Package;
using Clio.Package.NuGet;
using Clio.Project;
using Clio.Project.NuGet;
using Clio.Query;
using Clio.Requests;
using Clio.Requests.Validators;
using Clio.Utilities;
using Clio.Workspace;
using Clio.Workspaces;
using Clio.YAML;
using Creatio.Client;
using FluentValidation;
using k8s;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using FileSystem = System.IO.Abstractions.FileSystem;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio;

public class BindingsModule {

	#region Fields: Private

	public static string k8sDns = "127.0.0.1";
	private readonly IFileSystem _fileSystem;

	#endregion

	#region Constructors: Public

	public BindingsModule(IFileSystem fileSystem = null){
		_fileSystem = fileSystem;
	}

	#endregion

	#region Methods: Public

	public IServiceProvider Register(EnvironmentSettings settings = null,
		Action<IServiceCollection> additionalRegistrations = null){
		IServiceCollection services = new ServiceCollection();
		RegisterAssemblyInterfaceTypes(services);
		services.AddSingleton<IWorkspacePathBuilder, WorkspacePathBuilder>();
		services.AddTransient<IVsProjectFactory, VsProjectFactory>();
		services.AddSingleton<ILogger>(ConsoleLogger.Instance);
		services.AddSingleton<IDbOperationLogContextAccessor, DbOperationLogContextAccessor>();
		services.AddSingleton<IDbOperationLogSessionFactory, DbOperationLogSessionFactory>();

		EnvironmentSettings activeSettings = settings;
		if (activeSettings is null) {
			SettingsRepository settingsRepository = new(_fileSystem);
			string envName = settingsRepository.GetDefaultEnvironmentName();
			activeSettings = settingsRepository.FindEnvironment(envName);
		}

		if (activeSettings is not null) {
			services.AddSingleton(activeSettings);
			services.AddTransient<IDataProvider>(_ => string.IsNullOrEmpty(activeSettings.ClientId)
				? new RemoteDataProvider(activeSettings.Uri, activeSettings.Login, activeSettings.Password,
					activeSettings.IsNetCore)
				: new RemoteDataProvider(activeSettings.Uri, activeSettings.AuthAppUri, activeSettings.ClientId,
					activeSettings.ClientSecret, activeSettings.IsNetCore));
			CreatioClient creatioClient = string.IsNullOrEmpty(activeSettings.ClientId)
				? new CreatioClient(activeSettings.Uri ?? "http://localhost", activeSettings.Login ?? "Supervisor",
					activeSettings.Password ?? "Supervisor", true, activeSettings.IsNetCore)
				: CreatioClient.CreateOAuth20Client(activeSettings.Uri, activeSettings.AuthAppUri,
					activeSettings.ClientId, activeSettings.ClientSecret, activeSettings.IsNetCore);
			
			services.AddSingleton(creatioClient);
			services.AddSingleton<IApplicationClient>(new CreatioClientAdapter(creatioClient));
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
		services.AddTransient<IPackageUtilities, PackageUtilities>();
		services.AddKeyedTransient<IFollowupUpChainItem, DconfChainItem>(nameof(DconfChainItem));
		services.AddTransient<IFollowUpChain, FollowUpChain>();
		services.AddTransient<FeatureCommand>();
		services.AddTransient<SysSettingsCommand>();
		services.AddTransient<BuildInfoCommand>();
		services.AddTransient<PushPackageCommand>();
		services.AddTransient<InstallApplicationCommand>();
		services.AddTransient<PageListCommand>();
		services.AddTransient<PageGetCommand>();
		services.AddTransient<PageUpdateCommand>();
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
		services.AddTransient<CreateDataBindingCommand>();
		services.AddTransient<AddDataBindingRowCommand>();
		services.AddTransient<RemoveDataBindingRowCommand>();
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
		services.AddTransient<ExtractPackageCommand>();
		services.AddTransient<ExternalLinkCommand>();
		services.AddTransient<PowerShellFactory>();
		services.AddTransient<RegAppCommand>();
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
		services.AddTransient<ModifyEntitySchemaColumnCommand>();
		services.AddTransient<GetEntitySchemaColumnPropertiesCommand>();
		services.AddTransient<GetEntitySchemaPropertiesCommand>();
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
		services.AddSingleton<ISystemServiceManager>(_ =>
			RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? new LinuxSystemServiceManager() :
			RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? new MacOSSystemServiceManager() :
			new WindowsSystemServiceManager());
		services.AddSingleton<Common.IIS.IIISSiteDetector>(_ =>
			RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
				? new Common.IIS.WindowsIISSiteDetector()
				: new Common.IIS.StubIISSiteDetector());
		services.AddSingleton<Common.IIS.IPlatformDetector, Common.IIS.PlatformDetector>();
		services.AddSingleton<Common.IIS.ITcpPortReservationReader, Common.IIS.TcpPortReservationReader>();
		services.AddTransient<Common.IIS.IAvailableIisPortService, Common.IIS.AvailableIisPortService>();
		services.AddSingleton<Common.IIS.IIISAppPoolManager>(_ =>
			RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
				? new Common.IIS.WindowsIISAppPoolManager()
				: new Common.IIS.StubIISAppPoolManager());
		services.AddTransient<ClioGateway>();
		services.AddTransient<CompileConfigurationCommand>();
		services.AddTransient<IMssql, Mssql>();
		services.AddTransient<IPostgres, Postgres>();
		services.AddTransient<LocalHelpViewer>();
		services.AddTransient<WikiHelpViewer>();
		
		services.AddTransient<McpServerCommand>();
		services.AddMcpServer()
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

}
#pragma warning restore CLIO001 // Non-nullable field is uninitialized.
