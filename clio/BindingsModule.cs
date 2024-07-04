using System;
using Autofac;
using Clio.Command;
using Clio.Command.PackageCommand;
using Clio.Command.SqlScriptCommand;
using Clio.Common;
using Clio.Querry;
using Clio.Requests;
using Clio.Requests.Validators;
using Clio.Utilities;
using MediatR;
using MediatR.Extensions.Autofac.DependencyInjection;
using MediatR.Extensions.Autofac.DependencyInjection.Builder;
using System.Reflection;
using Clio.Common.K8;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization;
using Clio.Common.ScenarioHandlers;
using Clio.YAML;
using k8s;
using FileSystem = System.IO.Abstractions.FileSystem;
using ATF.Repository.Providers;
using Clio.Common.db;
using IFileSystem = System.IO.Abstractions.IFileSystem;
using Clio.Command.ApplicationCommand;
using Clio.Package;
using Clio.Project.NuGet;
using Creatio.Client;
using DocumentFormat.OpenXml.Math;

namespace Clio
{
	public class BindingsModule {

		private readonly IFileSystem _fileSystem;
		public BindingsModule(IFileSystem fileSystem = null){
			_fileSystem = fileSystem;
		}
		
		public IContainer Register(EnvironmentSettings settings = null, bool registerNullSettingsForTest = false,
			Action<ContainerBuilder> additionalRegistrations = null) {
			
			var containerBuilder = new ContainerBuilder();
			
			containerBuilder
				.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
				.Where(t => t != typeof(ConsoleLogger))
				.AsImplementedInterfaces();
			
			containerBuilder.RegisterInstance(ConsoleLogger.Instance).As<ILogger>().SingleInstance();
			
			if (settings != null || registerNullSettingsForTest) {
				containerBuilder.RegisterInstance(settings);
				if (!registerNullSettingsForTest) {
					var creatioClientInstance = new ApplicationClientFactory().CreateClient(settings);
					containerBuilder.RegisterInstance(creatioClientInstance).As<IApplicationClient>();
					IDataProvider provider = string.IsNullOrEmpty(settings.Login) switch {
						true => new RemoteDataProvider(settings.Uri, settings.AuthAppUri, settings.ClientId, settings.ClientSecret, settings.IsNetCore),
						false => new RemoteDataProvider(settings.Uri, settings.Login, settings.Password, settings.IsNetCore)
					};
					containerBuilder.RegisterInstance(provider).As<IDataProvider>();
				}
				
			}

			try {
				KubernetesClientConfiguration config = KubernetesClientConfiguration.BuildConfigFromConfigFile();
				IKubernetes k8Client = new Kubernetes(config);
				containerBuilder.RegisterInstance(k8Client).As<IKubernetes>();
				containerBuilder.RegisterType<k8Commands>();
				containerBuilder.RegisterType<InstallerCommand>();
			} catch {

			}
			
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
			
			containerBuilder.RegisterInstance(new CreatioSdkOnline()).As<ICreatioSdk>();
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
			containerBuilder.RegisterType<DataServiceQuerry>();
			containerBuilder.RegisterType<RestoreFromPackageBackupCommand>();
			containerBuilder.RegisterType<Marketplace>();
			containerBuilder.RegisterType<GetMarketplacecatalogCommand>();
			containerBuilder.RegisterType<CreateUiProjectCommand>();
			containerBuilder.RegisterType<CreateUiProjectOptionsValidator>();
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
			var configuration = MediatRConfigurationBuilder
				.Create(typeof(BindingsModule).Assembly)
				.WithAllOpenGenericHandlerTypesRegistered()
				.Build();
			containerBuilder.RegisterMediatR(configuration);

			containerBuilder.RegisterGeneric(typeof(ValidationBehaviour<,>)).As(typeof(IPipelineBehavior<,>));
			containerBuilder.RegisterType<ExternalLinkOptionsValidator>();
			containerBuilder.RegisterType<SetFsmConfigOptionsValidator>();
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
			
			containerBuilder.RegisterType<ClioGateway>();
			additionalRegistrations?.Invoke(containerBuilder);
			return containerBuilder.Build();
		}
		
		
		
		
	}
}
