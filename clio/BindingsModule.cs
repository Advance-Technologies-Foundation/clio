using System;
using System.Reflection;
using System.Runtime.InteropServices;
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
using Clio.Common;
using Clio.Common.db;
using Clio.Common.DeploymentStrategies;
using Clio.Common.SystemServices;
using Clio.Common.K8;
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

		containerBuilder.Register(provider =>
		{
			KubernetesClientConfiguration config = KubernetesClientConfiguration.BuildConfigFromConfigFile();
			Uri.TryCreate(config.Host, UriKind.Absolute, out var uriResult);
			if (uriResult != null && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps)) {
				k8sDns = uriResult.Host;
			}
			else {
				throw new InvalidOperationException("Invalid Kubernetes configuration host.");
			}
			return new Kubernetes(config);
		}).As<IKubernetes>();
		containerBuilder.RegisterType<k8Commands>();
	
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
		containerBuilder.RegisterType<SetWebServiceUrlCommand>();
		containerBuilder.RegisterType<ListInstalledAppsCommand>();
		containerBuilder.RegisterType<GetCreatioInfoCommand>();
		containerBuilder.RegisterType<SetApplicationVersionCommand>();
		containerBuilder.RegisterType<ApplyEnvironmentManifestCommand>();
		containerBuilder.RegisterType<EnvironmentManager>();
		containerBuilder.RegisterType<GetWebServiceUrlCommand>();
		containerBuilder.RegisterType<MockDataCommand>();
		containerBuilder.RegisterType<ConsoleProgressbar>();
		containerBuilder.RegisterType<ApplicationLogProvider>();
		containerBuilder.RegisterType<LastCompilationLogCommand>();
		//containerBuilder.RegisterType<CreateEntityCommand>();
		containerBuilder.RegisterType<LinkWorkspaceWithTideRepositoryCommand>();
		containerBuilder.RegisterType<CheckWebFarmNodeConfigurationsCommand>();
		containerBuilder.RegisterType<GetAppHashCommand>();
		
		containerBuilder.RegisterType<Link4RepoCommand>();
		containerBuilder.RegisterType<LinkPackageStoreCommand>();
		
		

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

		// Register platform-specific system service managers
		containerBuilder.Register<ISystemServiceManager>(c =>
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
				return new LinuxSystemServiceManager();
			if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
				return new MacOSSystemServiceManager();
			return new WindowsSystemServiceManager();
		}).SingleInstance();

		containerBuilder.RegisterType<ClioGateway>();
		containerBuilder.RegisterType<CompileConfigurationCommand>();

		containerBuilder.RegisterType<Mssql>().As<IMssql>();
		containerBuilder.RegisterType<Postgres>().As<IPostgres>();

		additionalRegistrations?.Invoke(containerBuilder);
		return containerBuilder.Build();
	}

	#endregion

}
