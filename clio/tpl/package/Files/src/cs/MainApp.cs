using System;
using System.Collections.Generic;
using System.Linq;
using ATF.Repository.Providers;
using Common.Logging;
using Microsoft.Extensions.DependencyInjection;
using Terrasoft.Core;
using Terrasoft.Core.Factories;

namespace #RootNameSpace# {

	/// <summary>
	/// This is a class that provides access to the application services.
	/// This is the MAIN entry point into the #RootNameSpace# package.
	/// </summary>
	public sealed class #RootNameSpace# {

		#region Fields: Private

		private static Lazy<#RootNameSpace#> _instance = new Lazy<#RootNameSpace#>(() => new #RootNameSpace#());
		private readonly Lazy<ServiceProvider> _serviceProvider = new Lazy<ServiceProvider>(Init);

		/// <summary>
		/// Instance of a UserCennection from CLassFactory
		/// </summary>
		internal static UserConnection UserConnection => ClassFactory.Get<UserConnection>();

		#endregion

		#region Fields: Internal

		internal static IEnumerable<Func<IServiceCollection, IServiceCollection>> InjectedServices;

		#endregion

		#region Properties: Public

		public static #RootNameSpace# Instance => _instance.Value;

		#endregion

		#region Methods: Private

		private static ServiceProvider Init(){

			ServiceCollection serviceCollection = new ServiceCollection();
			serviceCollection.AddSingleton<ILog>(LogManager.GetLogger(Constants.LoggerName));

			// UserConnection is owned by the Creatio platform (the per-request connection).
			// Never register it as a container-managed (scoped/transient) service: the DI container
			// disposes any IDisposable it resolves from a factory when the scope closes, which would
			// tear down the platform connection's DB executors and clear UserConnection.Current
			// mid-request. Expose it through a Func accessor so the container never owns its lifetime.
			serviceCollection.AddTransient<Func<UserConnection>>(sp => () => UserConnection);

			InjectedServices?.ToList().ForEach(service => service(serviceCollection));
			return serviceCollection.BuildServiceProvider();
		}

		#endregion

		#region Methods: Internal

		/// <summary>
		/// This method is meant to be used for testing purposes only.
		/// WARNING: Not thread-safe. Should not be used in production.
		/// This allows test bootstrap to reset the instance between tests
		/// </summary>
		/// <returns></returns>
		internal #RootNameSpace# Reset(){
			_serviceProvider.Value?.Dispose();
			_instance = null;
			_instance = new Lazy<#RootNameSpace#>(() => new #RootNameSpace#());
			return _instance.Value;
		}

		#endregion

		#region Methods: Public

		public T GetKeyedService<T>(object serviceKey) => _serviceProvider.Value.GetKeyedService<T>(serviceKey);

		 public T GetRequiredKeyedService<T>(object serviceKey) =>
		 	_serviceProvider.Value.GetRequiredKeyedService<T>(serviceKey);

		public T GetRequiredService<T>() => _serviceProvider.Value.GetRequiredService<T>();

		public T GetService<T>() => _serviceProvider.Value.GetService<T>();

		public IEnumerable<T> GetServices<T>() => _serviceProvider.Value.GetServices<T>();

		/// <summary>
		/// Creates a new service scope. Caller is responsible for disposing the scope.
		/// Use this for package-owned scoped services. Do NOT use it to obtain UserConnection:
		/// that connection is owned by the platform — resolve Func&lt;UserConnection&gt; instead.
		/// </summary>
		/// <returns>A new service scope that must be disposed.</returns>
		public IServiceScope CreateScope() {
			IServiceScope scope = _serviceProvider.Value.CreateScope();
			return scope;
		}

		#endregion

		// Private constructor to prevent external instantiation
		private #RootNameSpace#() { }
	}
}
