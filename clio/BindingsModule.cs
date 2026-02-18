using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ATF.Repository.Providers;
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
using FluentValidation;
using k8s;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
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

	public Autofac.IContainer Register(EnvironmentSettings settings = null,
		Action<Autofac.ContainerBuilder> additionalRegistrations = null){
		return RegisterInternal(settings, additionalRegistrations is null
			? null
			: services => additionalRegistrations(new Autofac.ContainerBuilder(services)));
	}

	private Autofac.IContainer RegisterInternal(EnvironmentSettings settings, Action<IServiceCollection> additionalRegistrations){
		IServiceCollection services = new ServiceCollection();
		RegisterAssemblyInterfaceTypes(services);
		services.AddSingleton<ILogger>(ConsoleLogger.Instance);

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

		services.AddTransient<IKubernetesClient, Common.Kubernetes.KubernetesClient>();
		services.AddTransient<K8ContextValidator>();
		services.AddTransient<IK8ServiceResolver, K8ServiceResolver>();
		services.AddTransient<IK8DatabaseDiscovery, K8DatabaseDiscovery>();
		services.AddTransient<IDatabaseConnectivityChecker, DatabaseConnectivityChecker>();
		services.AddTransient<IDatabaseCapabilityChecker, DatabaseCapabilityChecker>();
		services.AddTransient<K8DatabaseAssertion>();
		services.AddTransient<K8RedisAssertion>();
		services.AddTransient<Common.Assertions.FsPathAssertion>();
		services.AddTransient<Common.Assertions.FsPermissionAssertion>();
		services.AddTransient<k8Commands>();
		services.AddTransient<IInfrastructurePathProvider, InfrastructurePathProvider>();
		services.AddTransient<InstallerCommand>();

		if (_fileSystem is not null) {
			services.AddSingleton(_fileSystem);
		}
		else {
			services.AddTransient<IFileSystem, FileSystem>();
		}

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

		services.AddTransient<DconfChainItem>();
		services.AddTransient<IFollowUpChain, FollowUpChain>();
		services.AddTransient<FeatureCommand>();
		services.AddTransient<SysSettingsCommand>();
		services.AddTransient<BuildInfoCommand>();
		services.AddTransient<PushPackageCommand>();
		services.AddTransient<InstallApplicationCommand>();
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
		services.AddTransient<UpdateCliCommand>();
		services.AddTransient<IUserPromptService, UserPromptService>();
		services.AddTransient<DeletePackageCommand>();
		services.AddTransient<GetPkgListCommand>();
		services.AddTransient<RestoreWorkspaceCommand>();
		services.AddTransient<CreateWorkspaceCommand>();
		services.AddTransient<PushWorkspaceCommand>();
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
		services.AddTransient<LinkPackageStoreCommand>();
		services.AddTransient<LinkCoreSrcCommand>();

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
		services.AddTransient<CreatioInstallerService>();
		services.AddTransient<SetApplicationIconCommand>();
		services.AddTransient<CustomizeDataProtectionCommand>();
		services.AddTransient<GenerateProcessModelCommand>();
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
		RegisterFluentValidators(services);

		additionalRegistrations?.Invoke(services);
		IServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions {
			ValidateOnBuild = false,
			ValidateScopes = true
		});
		return new Autofac.Container(provider);
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
					|| !implementedInterface.Name.StartsWith("I", StringComparison.Ordinal)) {
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

