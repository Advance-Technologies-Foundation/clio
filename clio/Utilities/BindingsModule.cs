using Autofac;
using Clio.Command;
using Clio.Common;
using System.Reflection;

namespace Clio.Utilities
{
	public class BindingsModule
	{
		public IContainer Register(EnvironmentSettings settings) {
			var containerBuilder = new ContainerBuilder();
			containerBuilder
				.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
				.AsImplementedInterfaces();
			containerBuilder
				.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
				.Where(e => e.IsAssignableFrom(typeof(Command<>)));
			containerBuilder.RegisterInstance(new CreatioClientAdapter(settings.Uri, settings.Login, 
				settings.Password, settings.IsNetCore)).As<IApplicationClient>();
			containerBuilder.RegisterInstance(settings);
			containerBuilder.RegisterType<PushPackageCommand>();
			containerBuilder.RegisterType<PingAppCommand>();
			return containerBuilder.Build();
		}
	}
}
