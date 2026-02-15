using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ATF.Repository.Providers;
using Autofac;
using Clio.Command;
using Clio.Command.ApplicationCommand;
using Clio.Command.ChainItems;
using Clio.Command.CreatioInstallCommand;
using Clio.Command.PackageCommand;
using Clio.Command.ProcessModel;
using Clio.Command.SqlScriptCommand;
using Clio.Command.TIDE;
using Clio.Command.Update;
using Clio.Common;
using Clio.Common.db;
using Clio.Common.DeploymentStrategies;
using Clio.Common.SystemServices;
using Clio.Common.K8;
using Clio.Common.Kubernetes;
using Clio.Common.Database;
using Clio.Common.ScenarioHandlers;
using Clio.ComposableApplication;
using Clio.Package;
using Clio.Query;
using Clio.Requests;
using Clio.Requests.Validators;
using Clio.UserEnvironment;
using Clio.Utilities;
using Clio.Workspace;
using Clio.Workspaces;
using Clio.YAML;
using Creatio.Client;
using k8s;
using MediatR;
using MediatR.Extensions.Autofac.DependencyInjection;
using MediatR.Extensions.Autofac.DependencyInjection.Builder;
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

	public IContainer Register(EnvironmentSettings settings = null, Action<ContainerBuilder> additionalRegistrations = null){
		ContainerBuilder containerBuilder = new();

		containerBuilder
			.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
			.Where(t => t != typeof(ConsoleLogger))
			.AsImplementedInterfaces();

		containerBuilder.RegisterInstance(ConsoleLogger.Instance).As<ILogger>().SingleInstance();

		if (settings != null) {
			containerBuilder.Register(provider => {
				IApplicationClient creatioClientInstance = new ApplicationClientFactory().CreateClient(settings);
				containerBuilder.RegisterInstance(creatioClientInstance).As<IApplicationClient>();
				IDataProvider dataProvider = string.IsNullOrEmpty(settings.ClientId) switch {
												false => new RemoteDataProvider(settings.Uri, settings.AuthAppUri,
													settings.ClientId, settings.ClientSecret, settings.IsNetCore),
												true => new RemoteDataProvider(settings.Uri, settings.Login,
													settings.Password, settings.IsNetCore)
											};
				return dataProvider;
			});
			containerBuilder.RegisterInstance(settings);
		}
		else {

			SettingsRepository sr = new SettingsRepository(_fileSystem);
			string envName = sr.GetDefaultEnvironmentName();
			EnvironmentSettings defSettings = sr.FindEnvironment(envName);
			
			if (defSettings is not null) {
				containerBuilder.Register(provider => {
					IApplicationClient creatioClientInstance = new ApplicationClientFactory().CreateClient(defSettings);
					containerBuilder.RegisterInstance(creatioClientInstance).As<IApplicationClient>();
					IDataProvider dataProvider = string.IsNullOrEmpty(defSettings.ClientId) switch {
						false => new RemoteDataProvider(defSettings.Uri, defSettings.AuthAppUri,
							defSettings.ClientId, defSettings.ClientSecret, defSettings.IsNetCore),
						true => new RemoteDataProvider(defSettings.Uri, defSettings.Login,
							defSettings.Password, defSettings.IsNetCore)
					};
					return dataProvider;
				});
				settings = defSettings;
				containerBuilder.RegisterInstance(defSettings);
			}
		}

		containerBuilder.Register(provider => {

			IKubernetes k8Instance;
			try {
				KubernetesClientConfiguration config = KubernetesClientConfiguration.BuildConfigFromConfigFile();
				Uri.TryCreate(config.Host, UriKind.Absolute, out var uriResult);
				if (uriResult != null && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps)) {
					k8sDns = uriResult.Host;
				}
				else {
					throw new InvalidOperationException("Invalid Kubernetes configuration host.");
				}
				k8Instance = new Kubernetes(config);
				return k8Instance;
			}
			catch {
				k8Instance = new FakeKubernetes();
				return k8Instance;
			}
		}).As<IKubernetes>();
		
		containerBuilder.RegisterType<Common.Kubernetes.KubernetesClient>().As<IKubernetesClient>();
		containerBuilder.RegisterType<K8ContextValidator>();
		containerBuilder.RegisterType<K8ServiceResolver>().As<IK8ServiceResolver>();
		containerBuilder.RegisterType<K8DatabaseDiscovery>().As<IK8DatabaseDiscovery>();
		containerBuilder.RegisterType<DatabaseConnectivityChecker>().As<IDatabaseConnectivityChecker>();
		containerBuilder.RegisterType<DatabaseCapabilityChecker>().As<IDatabaseCapabilityChecker>();
		containerBuilder.RegisterType<K8DatabaseAssertion>();
		containerBuilder.RegisterType<K8RedisAssertion>();
		containerBuilder.RegisterType<Common.Assertions.FsPathAssertion>();
		containerBuilder.RegisterType<Common.Assertions.FsPermissionAssertion>();
		containerBuilder.RegisterType<k8Commands>();
		containerBuilder.RegisterType<InfrastructurePathProvider>().As<IInfrastructurePathProvider>();
	
		containerBuilder.RegisterType<InstallerCommand>();

		if (_fileSystem is not null) {
			containerBuilder.RegisterInstance(_fileSystem).As<IFileSystem>();
		}
		else {
			containerBuilder.RegisterType<FileSystem>().As<IFileSystem>();
		}

		IDeserializer deserializer = new DeserializerBuilder()
									.WithNamingConvention(UnderscoredNamingConvention.Instance)
									.IgnoreUnmatchedProperties()
									.Build();

		ISerializer serializer = new SerializerBuilder()
								.WithNamingConvention(UnderscoredNamingConvention.Instance)
								.ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults |
									DefaultValuesHandling.OmitEmptyCollections)
								.Build();

		#region Epiremental CreatioCLient

		if (settings is not null) {
			CreatioClient creatioClient = string.IsNullOrEmpty(settings.ClientId)
				? new CreatioClient(settings.Uri ?? "http://localhost", settings.Login ?? "Supervisor", settings.Password ?? "Supervisor", true, settings.IsNetCore)
				: CreatioClient.CreateOAuth20Client(settings.Uri, settings.AuthAppUri, settings.ClientId,
					settings.ClientSecret, settings.IsNetCore);
			IApplicationClient clientAdapter = new CreatioClientAdapter(creatioClient);
			containerBuilder.RegisterInstance(clientAdapter).As<IApplicationClient>();

			containerBuilder.RegisterType<SysSettingsManager>();
		}

		#endregion

		containerBuilder.RegisterType<DconfChainItem>().As<DconfChainItem>();
		containerBuilder.RegisterType<FollowUpChain>().As<IFollowUpChain>();
		containerBuilder.RegisterInstance(deserializer).As<IDeserializer>();
		containerBuilder.RegisterInstance(serializer).As<ISerializer>();
		containerBuilder.RegisterType<FeatureCommand>();
		containerBuilder.RegisterType<SysSettingsCommand>();
		containerBuilder.RegisterType<BuildInfoCommand>();
		containerBuilder.RegisterType<PushPackageCommand>();
		containerBuilder.RegisterType<InstallApplicationCommand>();
		containerBuilder.RegisterType<OpenCfgCommand>();
		containerBuilder.RegisterType<InstallGatePkgCommand>();
		containerBuilder.RegisterType<PingAppCommand>();
		containerBuilder.RegisterType<SqlScriptCommand>();
		containerBuilder.RegisterType<CompressPackageCommand>();
		containerBuilder.RegisterType<PushNuGetPackagesCommand>();
		containerBuilder.RegisterType<PackNuGetPackageCommand>();
		containerBuilder.RegisterType<RestoreNugetPackageCommand>();
		containerBuilder.RegisterType<InstallNugetPackageCommand>();
		containerBuilder.RegisterType<SetPackageVersionCommand>();
		containerBuilder.RegisterType<GetPackageVersionCommand>();
		containerBuilder.RegisterType<CheckNugetUpdateCommand>();
		containerBuilder.RegisterType<UpdateCliCommand>();
		containerBuilder.RegisterType<UserPromptService>().As<IUserPromptService>();
		containerBuilder.RegisterType<DeletePackageCommand>();
		containerBuilder.RegisterType<GetPkgListCommand>();
		containerBuilder.RegisterType<RestoreWorkspaceCommand>();
		containerBuilder.RegisterType<CreateWorkspaceCommand>();
		containerBuilder.RegisterType<PushWorkspaceCommand>();
		containerBuilder.RegisterType<WorkspaceMerger>().As<IWorkspaceMerger>();
		containerBuilder.RegisterType<WorkspacePackageFilter>().As<IWorkspacePackageFilter>();
		containerBuilder.RegisterType<MergeWorkspacesCommand>();
		containerBuilder.RegisterType<LoadPackagesToFileSystemCommand>();
		containerBuilder.RegisterType<LoadPackagesToDbCommand>();
		containerBuilder.RegisterType<UploadLicensesCommand>();
		containerBuilder.RegisterType<HealthCheckCommand>();
		containerBuilder.RegisterType<ShowLocalEnvironmentsCommand>();
		containerBuilder.RegisterType<ClearLocalEnvironmentCommand>();
		containerBuilder.RegisterType<AddPackageCommand>();
		containerBuilder.RegisterType<UnlockPackageCommand>();
		containerBuilder.RegisterType<LockPackageCommand>();
		containerBuilder.RegisterType<DataServiceQuery>();
		containerBuilder.RegisterType<CallServiceCommand>();
		containerBuilder.RegisterType<RestoreFromPackageBackupCommand>();
		containerBuilder.RegisterType<Marketplace>();
		containerBuilder.RegisterType<CreateUiProjectCommand>();
		containerBuilder.RegisterType<CreateUiProjectOptionsValidator>();
		containerBuilder.RegisterType<SetIconParametersValidator>();
		containerBuilder.RegisterType<DownloadConfigurationCommand>();
		containerBuilder.RegisterType<DeployCommand>();
		containerBuilder.RegisterType<InfoCommand>();
		containerBuilder.RegisterType<ExtractPackageCommand>();
		containerBuilder.RegisterType<ExternalLinkCommand>();
		containerBuilder.RegisterType<PowerShellFactory>();
		containerBuilder.RegisterType<RegAppCommand>();
		containerBuilder.RegisterType<RestartCommand>();
		containerBuilder.RegisterType<StartCommand>();
		containerBuilder.RegisterType<StopCommand>();
		containerBuilder.RegisterType<HostsCommand>();
		containerBuilder.RegisterType<RedisCommand>();
		containerBuilder.RegisterType<SetFsmConfigCommand>();
		containerBuilder.RegisterType<TurnFsmCommand>();
		containerBuilder.RegisterType<TurnFarmModeCommand>();
		containerBuilder.RegisterType<ScenarioRunnerCommand>();
		containerBuilder.RegisterType<CompressAppCommand>();
		containerBuilder.RegisterType<Scenario>();
		containerBuilder.RegisterType<ConfigureWorkspaceCommand>();
		containerBuilder.RegisterType<CreateInfrastructureCommand>();
		containerBuilder.RegisterType<DeployInfrastructureCommand>();
		containerBuilder.RegisterType<DeleteInfrastructureCommand>();
		containerBuilder.RegisterType<OpenInfrastructureCommand>();
		containerBuilder.RegisterType<CheckWindowsFeaturesCommand>();
		containerBuilder.RegisterType<ManageWindowsFeaturesCommand>();
		containerBuilder.RegisterType<CreateTestProjectCommand>();
		containerBuilder.RegisterType<ListenCommand>();
		containerBuilder.RegisterType<ShowPackageFileContentCommand>();
		containerBuilder.RegisterType<CompilePackageCommand>();
		containerBuilder.RegisterType<SwitchNugetToDllCommand>();
		containerBuilder.RegisterType<NugetMaterializer>();
		containerBuilder.RegisterType<PropsBuilder>();
		containerBuilder.RegisterType<UninstallAppCommand>();
		containerBuilder.RegisterType<DownloadAppCommand>();
		containerBuilder.RegisterType<DeployAppCommand>();
		containerBuilder.RegisterType<ApplicationManager>();
		containerBuilder.RegisterType<RestoreDbCommand>();
		containerBuilder.RegisterType<DbClientFactory>().As<IDbClientFactory>();
		containerBuilder.RegisterType<DbConnectionTester>().As<IDbConnectionTester>();
		containerBuilder.RegisterType<BackupFileDetector>().As<IBackupFileDetector>();
		containerBuilder.RegisterType<PostgresToolsPathDetector>().As<IPostgresToolsPathDetector>().SingleInstance();
		containerBuilder.RegisterType<SetWebServiceUrlCommand>();
		containerBuilder.RegisterType<ListInstalledAppsCommand>();
		containerBuilder.RegisterType<GetCreatioInfoCommand>();
		containerBuilder.RegisterType<SetApplicationVersionCommand>();
		containerBuilder.RegisterType<ApplyEnvironmentManifestCommand>();
		containerBuilder.RegisterType<EnvironmentManager>();
		containerBuilder.RegisterType<GetWebServiceUrlCommand>();
		containerBuilder.RegisterType<MockDataCommand>();
		containerBuilder.RegisterType<AssertCommand>();
		containerBuilder.RegisterType<ConsoleProgressbar>();
		containerBuilder.RegisterType<ApplicationLogProvider>();
		containerBuilder.RegisterType<LastCompilationLogCommand>();
		//containerBuilder.RegisterType<CreateEntityCommand>();
		containerBuilder.RegisterType<LinkWorkspaceWithTideRepositoryCommand>();
		containerBuilder.RegisterType<CheckWebFarmNodeConfigurationsCommand>();
		containerBuilder.RegisterType<GetAppHashCommand>();
		containerBuilder.RegisterType<ShowAppListCommand>();
		containerBuilder.RegisterType<EnvManageUiCommand>();
		containerBuilder.RegisterType<EnvManageUiService>().As<IEnvManageUiService>();
		containerBuilder.RegisterType<InstalledApplication>().As<IInstalledApplication>();
		
		containerBuilder.RegisterType<Link4RepoCommand>();
		containerBuilder.RegisterType<LinkPackageStoreCommand>();
		containerBuilder.RegisterType<LinkCoreSrcCommand>();
		

		MediatRConfiguration configuration = MediatRConfigurationBuilder
											.Create(typeof(BindingsModule).Assembly)
											.WithAllOpenGenericHandlerTypesRegistered()
											.Build();
		containerBuilder.RegisterMediatR(configuration);

		containerBuilder.RegisterGeneric(typeof(ValidationBehaviour<,>)).As(typeof(IPipelineBehavior<,>));

		//Validators
		containerBuilder.RegisterType<ExternalLinkOptionsValidator>();
		containerBuilder.RegisterType<SetFsmConfigOptionsValidator>();
		containerBuilder.RegisterType<TurnFarmModeOptionsValidator>();
		containerBuilder.RegisterType<UninstallCreatioCommandOptionsValidator>();
		containerBuilder.RegisterType<Link4RepoOptionsValidator>();
		containerBuilder.RegisterType<LinkPackageStoreOptionsValidator>();
		containerBuilder.RegisterType<DownloadConfigurationCommandOptionsValidator>();

		containerBuilder.RegisterType<CreatioUninstaller>().As<ICreatioUninstaller>();
		containerBuilder.RegisterType<UnzipRequestValidator>();
		containerBuilder.RegisterType<GitSyncCommand>();
		containerBuilder.RegisterType<DeactivatePackageCommand>();
		containerBuilder.RegisterType<PublishWorkspaceCommand>();
		containerBuilder.RegisterType<ActivatePackageCommand>();
		containerBuilder.RegisterType<PackageHotFixCommand>();
		containerBuilder.RegisterType<PackageEditableMutator>();
		containerBuilder.RegisterType<SaveSettingsToManifestCommand>();
		containerBuilder.RegisterType<ShowDiffEnvironmentsCommand>();
		containerBuilder.RegisterType<CloneEnvironmentCommand>();
		containerBuilder.RegisterType<PullPkgCommand>();
		containerBuilder.RegisterType<AssemblyCommand>();
		containerBuilder.RegisterType<UninstallCreatioCommand>();
		containerBuilder.RegisterType<InstallTideCommand>();
		containerBuilder.RegisterType<AddSchemaCommand>();
		containerBuilder.RegisterType<CreatioInstallerService>();
		containerBuilder.RegisterType<SetApplicationIconCommand>();
		containerBuilder.RegisterType<CustomizeDataProtectionCommand>();
		containerBuilder.RegisterType<GenerateProcessModelCommand>();
		containerBuilder.RegisterType<ZipFileWrapper>().As<IZipFile>();
		containerBuilder.RegisterType<ProcessModelGenerator>().As<IProcessModelGenerator>();
		containerBuilder.RegisterType<ProcessModelWriter>().As<IProcessModelWriter>();
		containerBuilder.RegisterType<ZipBasedApplicationDownloader>().As<IZipBasedApplicationDownloader>();

		// Register deployment strategies and system service managers
		containerBuilder.RegisterType<CreatioHostService>().As<ICreatioHostService>();
		containerBuilder.RegisterType<IISDeploymentStrategy>();
		containerBuilder.RegisterType<DotNetDeploymentStrategy>();
		containerBuilder.RegisterType<DeploymentStrategyFactory>();
		containerBuilder.RegisterType<OpenAppCommand>();

		// Register platform-specific system service managers
		containerBuilder.Register<ISystemServiceManager>(c =>
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
				return new LinuxSystemServiceManager();
			if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
				return new MacOSSystemServiceManager();
			return new WindowsSystemServiceManager();
		}).SingleInstance();

		// Register platform-specific IIS site detector
		containerBuilder.Register<Common.IIS.IIISSiteDetector>(c =>
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				return new Common.IIS.WindowsIISSiteDetector();
			return new Common.IIS.StubIISSiteDetector();
		}).SingleInstance();

		// Register platform-specific IIS app pool manager
		containerBuilder.Register<Common.IIS.IIISAppPoolManager>(c =>
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				return new Common.IIS.WindowsIISAppPoolManager();
			return new Common.IIS.StubIISAppPoolManager();
		}).SingleInstance();

		containerBuilder.RegisterType<ClioGateway>();
		containerBuilder.RegisterType<CompileConfigurationCommand>();

		containerBuilder.RegisterType<Mssql>().As<IMssql>();
		containerBuilder.RegisterType<Postgres>().As<IPostgres>();

		containerBuilder.RegisterType<LocalHelpViewer>();
		containerBuilder.RegisterType<WikiHelpViewer>();

		additionalRegistrations?.Invoke(containerBuilder);
		return containerBuilder.Build();
	}

	#endregion

}


public class FakeKubernetes : IKubernetes
{
	public FakeKubernetes()
	{
	}

	public void Dispose() {
		throw new NotImplementedException();
	}

	public Task<int> NamespacedPodExecAsync(string name, string @namespace, string container, IEnumerable<string> command, bool tty,
		ExecAsyncCallback action, CancellationToken cancellationToken) {
		throw new NotImplementedException();
	}

	public Task<WebSocket> WebSocketNamespacedPodExecAsync(string name, string @namespace = "default", string command = null,
		string container = null, bool stderr = true, bool stdin = true, bool stdout = true, bool tty = true,
		string webSocketSubProtocol = null, Dictionary<string, List<string>> customHeaders = null,
		CancellationToken cancellationToken = new CancellationToken()) {
		throw new NotImplementedException();
	}

	public Task<WebSocket> WebSocketNamespacedPodExecAsync(string name, string @namespace = "default", IEnumerable<string> command = null,
		string container = null, bool stderr = true, bool stdin = true, bool stdout = true, bool tty = true,
		string webSocketSubProtocol = null, Dictionary<string, List<string>> customHeaders = null,
		CancellationToken cancellationToken = new CancellationToken()) {
		throw new NotImplementedException();
	}

	public Task<IStreamDemuxer> MuxedStreamNamespacedPodExecAsync(string name, string @namespace = "default", IEnumerable<string> command = null,
		string container = null, bool stderr = true, bool stdin = true, bool stdout = true, bool tty = true,
		string webSocketSubProtocol = "v4.channel.k8s.io", Dictionary<string, List<string>> customHeaders = null,
		CancellationToken cancellationToken = new CancellationToken()) {
		throw new NotImplementedException();
	}

	public Task<WebSocket> WebSocketNamespacedPodPortForwardAsync(string name, string @namespace, IEnumerable<int> ports,
		string webSocketSubProtocol = null, Dictionary<string, List<string>> customHeaders = null,
		CancellationToken cancellationToken = new CancellationToken()) {
		throw new NotImplementedException();
	}

	public Task<WebSocket> WebSocketNamespacedPodAttachAsync(string name, string @namespace, string container = null, bool stderr = true,
		bool stdin = false, bool stdout = true, bool tty = false, string webSocketSubProtocol = null,
		Dictionary<string, List<string>> customHeaders = null, CancellationToken cancellationToken = new CancellationToken()) {
		throw new NotImplementedException();
	}

	public Uri BaseUri { get; set; }
	public ICoreOperations Core { get; }
	public ICoreV1Operations CoreV1 { get; }
	public IApisOperations Apis { get; }
	public IAdmissionregistrationOperations Admissionregistration { get; }
	public IAdmissionregistrationV1Operations AdmissionregistrationV1 { get; }
	public IAdmissionregistrationV1alpha1Operations AdmissionregistrationV1alpha1 { get; }
	public IAdmissionregistrationV1beta1Operations AdmissionregistrationV1beta1 { get; }
	public IApiextensionsOperations Apiextensions { get; }
	public IApiextensionsV1Operations ApiextensionsV1 { get; }
	public IApiregistrationOperations Apiregistration { get; }
	public IApiregistrationV1Operations ApiregistrationV1 { get; }
	public IAppsOperations Apps { get; }
	public IAppsV1Operations AppsV1 { get; }
	public IAuthenticationOperations Authentication { get; }
	public IAuthenticationV1Operations AuthenticationV1 { get; }
	public IAuthorizationOperations Authorization { get; }
	public IAuthorizationV1Operations AuthorizationV1 { get; }
	public IAutoscalingOperations Autoscaling { get; }
	public IAutoscalingV1Operations AutoscalingV1 { get; }
	public IAutoscalingV2Operations AutoscalingV2 { get; }
	public IBatchOperations Batch { get; }
	public IBatchV1Operations BatchV1 { get; }
	public ICertificatesOperations Certificates { get; }
	public ICertificatesV1Operations CertificatesV1 { get; }
	public ICertificatesV1alpha1Operations CertificatesV1alpha1 { get; }
	public ICertificatesV1beta1Operations CertificatesV1beta1 { get; }
	public ICoordinationOperations Coordination { get; }
	public ICoordinationV1Operations CoordinationV1 { get; }
	public ICoordinationV1alpha2Operations CoordinationV1alpha2 { get; }
	public ICoordinationV1beta1Operations CoordinationV1beta1 { get; }
	public IDiscoveryOperations Discovery { get; }
	public IDiscoveryV1Operations DiscoveryV1 { get; }
	public IEventsOperations Events { get; }
	public IEventsV1Operations EventsV1 { get; }
	public IFlowcontrolApiserverOperations FlowcontrolApiserver { get; }
	public IFlowcontrolApiserverV1Operations FlowcontrolApiserverV1 { get; }
	public IInternalApiserverOperations InternalApiserver { get; }
	public IInternalApiserverV1alpha1Operations InternalApiserverV1alpha1 { get; }
	public INetworkingOperations Networking { get; }
	public INetworkingV1Operations NetworkingV1 { get; }
	public INetworkingV1beta1Operations NetworkingV1beta1 { get; }
	public INodeOperations Node { get; }
	public INodeV1Operations NodeV1 { get; }
	public IPolicyOperations Policy { get; }
	public IPolicyV1Operations PolicyV1 { get; }
	public IRbacAuthorizationOperations RbacAuthorization { get; }
	public IRbacAuthorizationV1Operations RbacAuthorizationV1 { get; }
	public IResourceOperations Resource { get; }
	public IResourceV1Operations ResourceV1 { get; }
	public IResourceV1alpha3Operations ResourceV1alpha3 { get; }
	public IResourceV1beta1Operations ResourceV1beta1 { get; }
	public IResourceV1beta2Operations ResourceV1beta2 { get; }
	public ISchedulingOperations Scheduling { get; }
	public ISchedulingV1Operations SchedulingV1 { get; }
	public IStorageOperations Storage { get; }
	public IStorageV1Operations StorageV1 { get; }
	public IStorageV1alpha1Operations StorageV1alpha1 { get; }
	public IStorageV1beta1Operations StorageV1beta1 { get; }
	public IStoragemigrationOperations Storagemigration { get; }
	public IStoragemigrationV1alpha1Operations StoragemigrationV1alpha1 { get; }
	public ILogsOperations Logs { get; }
	public IVersionOperations Version { get; }
	public ICustomObjectsOperations CustomObjects { get; }
	public IWellKnownOperations WellKnown { get; }
	public IOpenidOperations Openid { get; }
}
