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
using Сlio.Command.PackageCommand;
using Clio.Common.ScenarioHandlers;
using Clio.YAML;
using k8s;
using FileSystem = System.IO.Abstractions.FileSystem;

namespace Clio
{
	public class BindingsModule
	{
		public IContainer Register(EnvironmentSettings settings = null) {
			var containerBuilder = new ContainerBuilder();
			containerBuilder
				.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
				.AsImplementedInterfaces();
			if (settings != null) {
				var creatioClientInstance = new ApplicationClientFactory().CreateClient(settings);
				containerBuilder.RegisterInstance(creatioClientInstance).As<IApplicationClient>();
				containerBuilder.RegisterInstance(settings);
			}

			try {
				KubernetesClientConfiguration config = KubernetesClientConfiguration.BuildConfigFromConfigFile();
				IKubernetes k8Client = new Kubernetes(config);
				containerBuilder.RegisterInstance(k8Client).As<IKubernetes>();
				containerBuilder.RegisterType<k8Commands>();
				containerBuilder.RegisterType<InstallerCommand>();
			} catch {

			}
			
			containerBuilder.RegisterType<FileSystem>().As<System.IO.Abstractions.IFileSystem>();
			
			
			var deserializer = new DeserializerBuilder()
				.WithNamingConvention(UnderscoredNamingConvention.Instance)
				.Build();
			containerBuilder.RegisterInstance(deserializer).As<IDeserializer>();

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
			containerBuilder.RegisterType<GetVersionCommand>();
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
			containerBuilder.RegisterType<CreateTestProjectCommand>();
			containerBuilder.RegisterType<ListenCommand>();
			containerBuilder.RegisterType<ShowPackageFileContentCommand>();
			containerBuilder.RegisterType<MaterializeNugetCommand>();
			containerBuilder.RegisterType<NugetMaterializer>();
			containerBuilder.RegisterType<PropsBuilder>();
			

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

			return containerBuilder.Build();
		}
	}
}
