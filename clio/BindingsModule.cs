using Autofac;
using Clio.Command;
using Clio.Command.PackageCommand;
using Clio.Command.SqlScriptCommand;
using Clio.Common;
using Clio.Package;
using Clio.Project.NuGet;
using Clio.Querry;
using Clio.Utilities;
using Clio.Workspace;
using System.Reflection;

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
			containerBuilder.RegisterType<PushPackageCommand>();
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
			containerBuilder.RegisterType<DownloadConfigurationCommand>();
			containerBuilder.RegisterType<DeployCommand>();
			containerBuilder.RegisterType<GetVersionCommand>();
			return containerBuilder.Build();
		}
	}
}
