using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Autofac {
	public interface IContainer : IServiceProvider {
		T Resolve<T>();
	}

	public sealed class Container : IContainer {
		private readonly IServiceProvider _provider;

		public Container(IServiceProvider provider){
			_provider = provider;
		}

		public object GetService(Type serviceType){
			return _provider.GetService(serviceType);
		}

		public T Resolve<T>(){
			return _provider.GetRequiredService<T>();
		}
	}

	public sealed class ContainerBuilder {
		private readonly IServiceCollection _services;

		public ContainerBuilder(IServiceCollection services){
			_services = services;
		}

		public RegistrationBuilder<TImplementation> Register<TImplementation>(Func<IServiceProvider, TImplementation> factory){
			return new RegistrationBuilder<TImplementation>(_services, sp => factory(sp), ServiceLifetime.Transient);
		}

		public RegistrationBuilder<TImplementation> RegisterType<TImplementation>() where TImplementation : class {
			return new RegistrationBuilder<TImplementation>(_services,
				sp => ActivatorUtilities.CreateInstance<TImplementation>(sp),
				ServiceLifetime.Transient);
		}

		public RegistrationBuilder<TImplementation> RegisterInstance<TImplementation>(TImplementation instance){
			return new RegistrationBuilder<TImplementation>(_services, _ => instance, ServiceLifetime.Singleton);
		}
	}

	public sealed class RegistrationBuilder<TImplementation> {
		private readonly IServiceCollection _services;
		private readonly Func<IServiceProvider, TImplementation> _factory;
		private readonly List<Type> _serviceTypes = [typeof(TImplementation)];
		private ServiceLifetime _lifetime;

		public RegistrationBuilder(IServiceCollection services, Func<IServiceProvider, TImplementation> factory,
			ServiceLifetime lifetime){
			_services = services;
			_factory = factory;
			_lifetime = lifetime;
			RegisterCurrentTypes();
		}

		public RegistrationBuilder<TImplementation> As<TService>(){
			if (!_serviceTypes.Contains(typeof(TService))) {
				_serviceTypes.Add(typeof(TService));
			}
			RegisterDescriptor(typeof(TService));
			return this;
		}

		public RegistrationBuilder<TImplementation> SingleInstance(){
			_lifetime = ServiceLifetime.Singleton;
			RegisterCurrentTypes();
			return this;
		}

		private void RegisterCurrentTypes(){
			foreach (Type serviceType in _serviceTypes) {
				RegisterDescriptor(serviceType);
			}
		}

		private void RegisterDescriptor(Type serviceType){
			_services.Add(ServiceDescriptor.Describe(serviceType, sp => _factory(sp), _lifetime));
		}
	}

	public static class ResolutionExtensions {
		public static T Resolve<T>(this IServiceProvider serviceProvider){
			return serviceProvider.GetRequiredService<T>();
		}
	}
}

namespace Autofac.Core {
	public class DependencyResolutionException : Exception {
		public DependencyResolutionException(string message) : base(message){
		}

		public DependencyResolutionException(string message, Exception innerException) : base(message, innerException){
		}
	}
}
