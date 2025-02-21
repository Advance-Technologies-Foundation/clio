using ATF.Repository.Providers;
using Autofac;
using Clio.Command;
using Clio.Command.ApplicationCommand;
using Clio.Command.PackageCommand;
using Clio.Command.SqlScriptCommand;
using Clio.Common;
using Clio.Common.db;
using Clio.Common.K8;
using Clio.Common.ScenarioHandlers;
using Clio.Package;
using Clio.Querry;
using Clio.Requests;
using Clio.Requests.Validators;
using Clio.Utilities;
using Clio.YAML;
using Creatio.Client;
using k8s;
using MediatR;
using MediatR.Extensions.Autofac.DependencyInjection;
using MediatR.Extensions.Autofac.DependencyInjection.Builder;
using System;
using System.Reflection;
using Clio.Command.CreatioInstallCommand;
using Clio.ComposableApplication;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using FileSystem = System.IO.Abstractions.FileSystem;
using IFileSystem = System.IO.Abstractions.IFileSystem;
using Autofac.Builder;
using Clio.Command.TIDE;

namespace Clio
{
	public class BindingsModule {

		private readonly IFileSystem _fileSystem;
		public BindingsModule(IFileSystem fileSystem = null){
			_fileSystem = fileSystem;
		}
		
		public IContainer Register(EnvironmentSettings settings = null,
			Action<ContainerBuilder> additionalRegistrations = null) {
			
			var containerBuilder = new ContainerBuilder();
			
			containerBuilder
				.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
				.Where(t => t != typeof(ConsoleLogger))
				.AsImplementedInterfaces();
			
			containerBuilder.RegisterInstance(ConsoleLogger.Instance).As<ILogger>().SingleInstance();
			
			if (settings != null) {
				containerBuilder.Register<IDataProvider>(provider => {
					var creatioClientInstance = new ApplicationClientFactory().CreateClient(settings);
					containerBuilder.RegisterInstance(creatioClientInstance).As<IApplicationClient>();
					IDataProvider dataProvider = string.IsNullOrEmpty(settings.ClientId) switch {
						false => new RemoteDataProvider(settings.Uri, settings.AuthAppUri, settings.ClientId, settings.ClientSecret, settings.IsNetCore),
						true => new RemoteDataProvider(settings.Uri, settings.Login, settings.Password, settings.IsNetCore)
					};
					return dataProvider;
				});
				containerBuilder.RegisterInstance(settings);
			}

			containerBuilder.Register<Kubernetes>( provider => {
				KubernetesClientConfiguration config = KubernetesClientConfiguration.BuildConfigFromConfigFile();
				return new Kubernetes(config);
			}).As<IKubernetes>();
			containerBuilder.RegisterType<k8Commands>();
			containerBuilder.RegisterType<InstallerCommand>();
			
			if(_fileSystem is not null) {
				containerBuilder.RegisterInstance(_fileSystem).As<IFileSystem>();
			}else {
				containerBuilder.RegisterType<FileSystem>().As<IFileSystem>();
			}
			

			var deserializer = new DeserializerBuilder()
				.WithNamingConvention(UnderscoredNamingConvention.Instance)
				.IgnoreUnmatchedProperties()
				.Build();
			
			var serializer = new SerializerBuilder()
				.WithNamingConvention(UnderscoredNamingConvention.Instance)
				.ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults | DefaultValuesHandling.OmitEmptyCollections)
				
				.Build();


			#region Epiremental CreatioCLient

			if(settings is not null) {
				CreatioClient creatioClient = string.IsNullOrEmpty(settings.ClientId) 
					? new CreatioClient(settings.Uri, settings.Login, settings.Password, true, settings.IsNetCore) 
					: CreatioClient.CreateOAuth20Client(settings.Uri, settings.AuthAppUri, settings.ClientId, settings.ClientSecret, settings.IsNetCore);
				IApplicationClient clientAdapter = new CreatioClientAdapter(creatioClient);
				containerBuilder.RegisterInstance(clientAdapter).As<IApplicationClient>();
				
				containerBuilder.RegisterType<SysSettingsManager>();
			}
			#endregion
			
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
			containerBuilder.RegisterType<SetFsmConfigCommand>();
			containerBuilder.RegisterType<TurnFsmCommand>();
			containerBuilder.RegisterType<ScenarioRunnerCommand>();
			containerBuilder.RegisterType<CompressAppCommand>();
			containerBuilder.RegisterType<Scenario>();
			containerBuilder.RegisterType<ConfigureWorkspaceCommand>();
			containerBuilder.RegisterType<CreateInfrastructureCommand>();
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
			containerBuilder.RegisterType<DataServiceQuerry>();
			
			MediatRConfiguration configuration = MediatRConfigurationBuilder
												.Create(typeof(BindingsModule).Assembly)
												.WithAllOpenGenericHandlerTypesRegistered()
												.Build();
			containerBuilder.RegisterMediatR(configuration);

			containerBuilder.RegisterGeneric(typeof(ValidationBehaviour<,>)).As(typeof(IPipelineBehavior<,>));
			
			//Validators
			containerBuilder.RegisterType<ExternalLinkOptionsValidator>();
			containerBuilder.RegisterType<SetFsmConfigOptionsValidator>();
			containerBuilder.RegisterType<UninstallCreatioCommandOptionsValidator>();
			
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
			containerBuilder.RegisterType<ZipFileWrapper>().As<IZipFile>();
				
			
			containerBuilder.RegisterType<ClioGateway>();
			
			containerBuilder.RegisterType<Mssql>().As<IMssql>();
			containerBuilder.RegisterType<Postgres>().As<IPostgres>();
			
			
			additionalRegistrations?.Invoke(containerBuilder);
			return containerBuilder.Build();
		}
		
		
		
		
	}
}
