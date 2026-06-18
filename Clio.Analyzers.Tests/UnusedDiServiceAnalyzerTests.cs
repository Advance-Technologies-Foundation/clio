using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Analyzers.Tests;

/// <summary>
/// Verifies <see cref="UnusedDiServiceAnalyzer"/> (CLIO005) diagnostics for DI-registered services
/// that are never injected or resolved.
/// </summary>
public sealed class UnusedDiServiceAnalyzerTests {
	private const string DiStubs = """
	                    using Microsoft.Extensions.DependencyInjection;
	                    namespace Microsoft.Extensions.DependencyInjection {
	                    	public interface IServiceCollection { }
	                    	public sealed class ServiceCollection : IServiceCollection { }
	                    	public static class ServiceCollectionServiceExtensions {
	                    		public static IServiceCollection AddTransient<TService, TImplementation>(this IServiceCollection services) where TImplementation : TService { return services; }
	                    		public static IServiceCollection AddTransient<TService>(this IServiceCollection services) { return services; }
	                    		public static IServiceCollection AddTransient<TService>(this IServiceCollection services, System.Func<System.IServiceProvider, TService> factory) { return services; }
	                    		public static IServiceCollection AddScoped<TService, TImplementation>(this IServiceCollection services) where TImplementation : TService { return services; }
	                    		public static IServiceCollection AddScoped<TService>(this IServiceCollection services) { return services; }
	                    		public static IServiceCollection AddSingleton<TService, TImplementation>(this IServiceCollection services) where TImplementation : TService { return services; }
	                    		public static IServiceCollection AddSingleton<TService>(this IServiceCollection services) { return services; }
	                    		public static IServiceCollection AddTransient(this IServiceCollection services, System.Type serviceType, System.Type implementationType) { return services; }
	                    	}
	                    	public static class ServiceProviderServiceExtensions {
	                    		public static T GetRequiredService<T>(this System.IServiceProvider provider) { return default; }
	                    		public static T GetService<T>(this System.IServiceProvider provider) { return default; }
	                    		public static System.Collections.Generic.IEnumerable<T> GetServices<T>(this System.IServiceProvider provider) { return default; }
	                    	}
	                    }

	                    """;

	[Test]
	[Description("Reports CLIO005 when a registered service and its implementation are never injected or resolved.")]
	public async Task RunAnalyzerAsync_WhenRegisteredServiceIsNeverConsumed_ReturnsClio005Diagnostic() {
		// Arrange
		const string body = """
		                    namespace SampleApp {
		                    	public interface IFoo { }
		                    	public sealed class Foo : IFoo { }
		                    	public static class Program {
		                    		public static void Run() {
		                    			Microsoft.Extensions.DependencyInjection.IServiceCollection services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
		                    			services.AddTransient<IFoo, Foo>();
		                    		}
		                    	}
		                    }
		                    """;
		UnusedDiServiceAnalyzer analyzer = new();

		// Act
		var diagnostics = await AnalyzerTestRunner.RunAnalyzerAsync(DiStubs + body, analyzer);

		// Assert
		diagnostics.Should().ContainSingle(d => d.Id == "CLIO005",
			because: "a service whose interface and implementation are never injected or resolved is dead code");
	}

	[Test]
	[Description("Does not report CLIO005 when the service is constructor-injected somewhere.")]
	public async Task RunAnalyzerAsync_WhenServiceIsConstructorInjected_ReturnsNoClio005Diagnostic() {
		// Arrange
		const string body = """
		                    namespace SampleApp {
		                    	public interface IFoo { }
		                    	public sealed class Foo : IFoo { }
		                    	public sealed class Consumer {
		                    		public Consumer(IFoo foo) { }
		                    	}
		                    	public static class Program {
		                    		public static void Run() {
		                    			Microsoft.Extensions.DependencyInjection.IServiceCollection services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
		                    			services.AddTransient<IFoo, Foo>();
		                    		}
		                    	}
		                    }
		                    """;
		UnusedDiServiceAnalyzer analyzer = new();

		// Act
		var diagnostics = await AnalyzerTestRunner.RunAnalyzerAsync(DiStubs + body, analyzer);

		// Assert
		diagnostics.Should().NotContain(d => d.Id == "CLIO005",
			because: "constructor injection of the service type counts as consumption");
	}

	[Test]
	[Description("Does not report CLIO005 when the service is resolved via GetRequiredService<T>.")]
	public async Task RunAnalyzerAsync_WhenServiceIsResolvedViaGetRequiredService_ReturnsNoClio005Diagnostic() {
		// Arrange
		const string body = """
		                    namespace SampleApp {
		                    	public interface IFoo { }
		                    	public sealed class Foo : IFoo { }
		                    	public static class Program {
		                    		public static void Run(System.IServiceProvider provider) {
		                    			Microsoft.Extensions.DependencyInjection.IServiceCollection services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
		                    			services.AddTransient<IFoo, Foo>();
		                    			IFoo foo = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IFoo>(provider);
		                    		}
		                    	}
		                    }
		                    """;
		UnusedDiServiceAnalyzer analyzer = new();

		// Act
		var diagnostics = await AnalyzerTestRunner.RunAnalyzerAsync(DiStubs + body, analyzer);

		// Assert
		diagnostics.Should().NotContain(d => d.Id == "CLIO005",
			because: "GetRequiredService<IFoo> resolves the service and counts as consumption");
	}

	[Test]
	[Description("Does not report CLIO005 when the service is resolved via GetServices<T>.")]
	public async Task RunAnalyzerAsync_WhenServiceIsResolvedViaGetServices_ReturnsNoClio005Diagnostic() {
		// Arrange
		const string body = """
		                    namespace SampleApp {
		                    	public interface IFoo { }
		                    	public sealed class Foo : IFoo { }
		                    	public static class Program {
		                    		public static void Run(System.IServiceProvider provider) {
		                    			Microsoft.Extensions.DependencyInjection.IServiceCollection services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
		                    			services.AddTransient<IFoo, Foo>();
		                    			var all = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetServices<IFoo>(provider);
		                    		}
		                    	}
		                    }
		                    """;
		UnusedDiServiceAnalyzer analyzer = new();

		// Act
		var diagnostics = await AnalyzerTestRunner.RunAnalyzerAsync(DiStubs + body, analyzer);

		// Assert
		diagnostics.Should().NotContain(d => d.Id == "CLIO005",
			because: "GetServices<IFoo> resolves the service collection and counts as consumption");
	}

	[Test]
	[Description("Does not report CLIO005 when the service is injected as IEnumerable<T>.")]
	public async Task RunAnalyzerAsync_WhenServiceIsInjectedAsIEnumerable_ReturnsNoClio005Diagnostic() {
		// Arrange
		const string body = """
		                    namespace SampleApp {
		                    	public interface IFoo { }
		                    	public sealed class Foo : IFoo { }
		                    	public sealed class Consumer {
		                    		public Consumer(System.Collections.Generic.IEnumerable<IFoo> foos) { }
		                    	}
		                    	public static class Program {
		                    		public static void Run() {
		                    			Microsoft.Extensions.DependencyInjection.IServiceCollection services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
		                    			services.AddTransient<IFoo, Foo>();
		                    		}
		                    	}
		                    }
		                    """;
		UnusedDiServiceAnalyzer analyzer = new();

		// Act
		var diagnostics = await AnalyzerTestRunner.RunAnalyzerAsync(DiStubs + body, analyzer);

		// Assert
		diagnostics.Should().NotContain(d => d.Id == "CLIO005",
			because: "an IEnumerable<IFoo> constructor parameter consumes the element type IFoo");
	}

	[Test]
	[Description("Does not report CLIO005 for a self-registered type that is resolved by its concrete type.")]
	public async Task RunAnalyzerAsync_WhenSelfRegisteredTypeIsResolved_ReturnsNoClio005Diagnostic() {
		// Arrange
		const string body = """
		                    namespace SampleApp {
		                    	public sealed class Foo { }
		                    	public static class Program {
		                    		public static void Run(System.IServiceProvider provider) {
		                    			Microsoft.Extensions.DependencyInjection.IServiceCollection services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
		                    			services.AddTransient<Foo>();
		                    			Foo foo = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Foo>(provider);
		                    		}
		                    	}
		                    }
		                    """;
		UnusedDiServiceAnalyzer analyzer = new();

		// Act
		var diagnostics = await AnalyzerTestRunner.RunAnalyzerAsync(DiStubs + body, analyzer);

		// Assert
		diagnostics.Should().NotContain(d => d.Id == "CLIO005",
			because: "a self-registered type resolved by its concrete type is consumed");
	}

	[Test]
	[Description("Does not report CLIO005 when only the concrete implementation is injected (service interface unconsumed).")]
	public async Task RunAnalyzerAsync_WhenOnlyImplementationIsInjected_ReturnsNoClio005Diagnostic() {
		// Arrange
		const string body = """
		                    namespace SampleApp {
		                    	public interface IFoo { }
		                    	public sealed class Foo : IFoo { }
		                    	public sealed class Consumer {
		                    		public Consumer(Foo foo) { }
		                    	}
		                    	public static class Program {
		                    		public static void Run() {
		                    			Microsoft.Extensions.DependencyInjection.IServiceCollection services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
		                    			services.AddTransient<IFoo, Foo>();
		                    		}
		                    	}
		                    }
		                    """;
		UnusedDiServiceAnalyzer analyzer = new();

		// Act
		var diagnostics = await AnalyzerTestRunner.RunAnalyzerAsync(DiStubs + body, analyzer);

		// Assert
		diagnostics.Should().NotContain(d => d.Id == "CLIO005",
			because: "consumption of the implementation type means the registration is not dead even if the interface is unused");
	}

	[Test]
	[Description("Does not report CLIO005 when the service type is marked [ResolvedDynamically].")]
	public async Task RunAnalyzerAsync_WhenServiceIsMarkedResolvedDynamically_ReturnsNoClio005Diagnostic() {
		// Arrange
		const string body = """
		                    namespace SampleApp {
		                    	public sealed class ResolvedDynamicallyAttribute : System.Attribute { }
		                    	[ResolvedDynamically]
		                    	public interface IFoo { }
		                    	public sealed class Foo : IFoo { }
		                    	public static class Program {
		                    		public static void Run() {
		                    			Microsoft.Extensions.DependencyInjection.IServiceCollection services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
		                    			services.AddTransient<IFoo, Foo>();
		                    		}
		                    	}
		                    }
		                    """;
		UnusedDiServiceAnalyzer analyzer = new();

		// Act
		var diagnostics = await AnalyzerTestRunner.RunAnalyzerAsync(DiStubs + body, analyzer);

		// Assert
		diagnostics.Should().NotContain(d => d.Id == "CLIO005",
			because: "[ResolvedDynamically] opts the service out of the dead-registration diagnostic");
	}

	[Test]
	[Description("Does not report CLIO005 when the registered tool type has a method decorated with [McpServerTool] (reflection-scanned MCP tool).")]
	public async Task RunAnalyzerAsync_WhenRegisteredTypeHasMcpServerToolMethod_ReturnsNoClio005Diagnostic() {
		// Arrange
		const string body = """
		                    namespace ModelContextProtocol.Server {
		                    	[System.AttributeUsage(System.AttributeTargets.Method)]
		                    	public sealed class McpServerToolAttribute : System.Attribute {
		                    		public string Name { get; set; }
		                    	}
		                    }
		                    namespace SampleApp {
		                    	public sealed class InstallGateTool {
		                    		[ModelContextProtocol.Server.McpServerTool(Name = "install-gate")]
		                    		public string InstallGate() => null;
		                    	}
		                    	public static class Program {
		                    		public static void Run() {
		                    			Microsoft.Extensions.DependencyInjection.IServiceCollection services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
		                    			services.AddTransient<InstallGateTool>();
		                    		}
		                    	}
		                    }
		                    """;
		UnusedDiServiceAnalyzer analyzer = new();

		// Act
		var diagnostics = await AnalyzerTestRunner.RunAnalyzerAsync(DiStubs + body, analyzer);

		// Assert
		diagnostics.Should().NotContain(d => d.Id == "CLIO005",
			because: "a tool type whose method carries [McpServerTool] is instantiated by the MCP reflection scan and is not dead code");
	}

	[Test]
	[Description("Does not report CLIO005 when the service is resolved indirectly via a generic dispatch helper.")]
	public async Task RunAnalyzerAsync_WhenServiceIsResolvedViaGenericDispatchHelper_ReturnsNoClio005Diagnostic() {
		// Arrange
		const string body = """
		                    namespace SampleApp {
		                    	public sealed class Foo { }
		                    	public static class Dispatcher {
		                    		public static T Resolve<T>() => default;
		                    	}
		                    	public static class Program {
		                    		public static void Run() {
		                    			Microsoft.Extensions.DependencyInjection.IServiceCollection services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
		                    			services.AddTransient<Foo>();
		                    			Foo foo = Dispatcher.Resolve<Foo>();
		                    		}
		                    	}
		                    }
		                    """;
		UnusedDiServiceAnalyzer analyzer = new();

		// Act
		var diagnostics = await AnalyzerTestRunner.RunAnalyzerAsync(DiStubs + body, analyzer);

		// Assert
		diagnostics.Should().NotContain(d => d.Id == "CLIO005",
			because: "Foo appears as a generic type argument of the custom Resolve<Foo>() dispatch helper, which counts as indirect resolution");
	}

	[Test]
	[Description("Does not report CLIO005 when the service implementation is referenced via a typeof expression.")]
	public async Task RunAnalyzerAsync_WhenImplementationIsReferencedViaTypeof_ReturnsNoClio005Diagnostic() {
		// Arrange
		const string body = """
		                    namespace SampleApp {
		                    	public interface IFoo { }
		                    	public sealed class Foo : IFoo { }
		                    	public static class Program {
		                    		public static void Run() {
		                    			Microsoft.Extensions.DependencyInjection.IServiceCollection services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
		                    			services.AddTransient<IFoo, Foo>();
		                    			System.Type fooType = typeof(Foo);
		                    		}
		                    	}
		                    }
		                    """;
		UnusedDiServiceAnalyzer analyzer = new();

		// Act
		var diagnostics = await AnalyzerTestRunner.RunAnalyzerAsync(DiStubs + body, analyzer);

		// Assert
		diagnostics.Should().NotContain(d => d.Id == "CLIO005",
			because: "typeof(Foo) is a reflection-resolution signal that consumes the implementation type Foo");
	}

	[Test]
	[Description("Does not report CLIO005 for AddSingleton and AddScoped registrations that are consumed.")]
	public async Task RunAnalyzerAsync_WhenSingletonAndScopedAreConsumed_ReturnsNoClio005Diagnostic() {
		// Arrange
		const string body = """
		                    namespace SampleApp {
		                    	public interface ISingletonService { }
		                    	public sealed class SingletonService : ISingletonService { }
		                    	public interface IScopedService { }
		                    	public sealed class ScopedService : IScopedService { }
		                    	public sealed class Consumer {
		                    		public Consumer(ISingletonService s, IScopedService sc) { }
		                    	}
		                    	public static class Program {
		                    		public static void Run() {
		                    			Microsoft.Extensions.DependencyInjection.IServiceCollection services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
		                    			services.AddSingleton<ISingletonService, SingletonService>();
		                    			services.AddScoped<IScopedService, ScopedService>();
		                    		}
		                    	}
		                    }
		                    """;
		UnusedDiServiceAnalyzer analyzer = new();

		// Act
		var diagnostics = await AnalyzerTestRunner.RunAnalyzerAsync(DiStubs + body, analyzer);

		// Assert
		diagnostics.Should().NotContain(d => d.Id == "CLIO005",
			because: "AddSingleton and AddScoped registrations are tracked just like AddTransient and are consumed here");
	}

	[Test]
	[Description("Does not report CLIO005 for a concrete type registered by itself that is alive via a consumed constructed generic interface.")]
	public async Task RunAnalyzerAsync_WhenConcreteRegisteredTypeImplementsConsumedGenericInterface_ReturnsNoClio005Diagnostic() {
		// Arrange
		const string body = """
		                    namespace SampleApp {
		                    	public interface IValidator<T> { }
		                    	public sealed class FooOptions { }
		                    	public sealed class FooValidator : IValidator<FooOptions> { }
		                    	public sealed class Consumer {
		                    		public Consumer(IValidator<FooOptions> validator) { }
		                    	}
		                    	public static class Program {
		                    		public static void Run() {
		                    			Microsoft.Extensions.DependencyInjection.IServiceCollection services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
		                    			services.AddTransient<FooValidator>();
		                    		}
		                    	}
		                    }
		                    """;
		UnusedDiServiceAnalyzer analyzer = new();

		// Act
		var diagnostics = await AnalyzerTestRunner.RunAnalyzerAsync(DiStubs + body, analyzer);

		// Assert
		diagnostics.Should().NotContain(d => d.Id == "CLIO005",
			because: "the concrete validator is alive via the consumed constructed interface IValidator<FooOptions> that consumers inject and the reflection registration loop wires");
	}

	[Test]
	[Description("Does not report CLIO005 for a concrete type registered by itself that is alive via a consumed non-generic interface.")]
	public async Task RunAnalyzerAsync_WhenConcreteRegisteredTypeImplementsConsumedInterface_ReturnsNoClio005Diagnostic() {
		// Arrange
		const string body = """
		                    namespace SampleApp {
		                    	public interface IFoo { }
		                    	public sealed class Foo : IFoo { }
		                    	public sealed class Consumer {
		                    		public Consumer(IFoo foo) { }
		                    	}
		                    	public static class Program {
		                    		public static void Run() {
		                    			Microsoft.Extensions.DependencyInjection.IServiceCollection services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
		                    			services.AddTransient<Foo>();
		                    		}
		                    	}
		                    }
		                    """;
		UnusedDiServiceAnalyzer analyzer = new();

		// Act
		var diagnostics = await AnalyzerTestRunner.RunAnalyzerAsync(DiStubs + body, analyzer);

		// Assert
		diagnostics.Should().NotContain(d => d.Id == "CLIO005",
			because: "the concrete type Foo is alive via the consumed interface IFoo even though Foo itself is never injected directly");
	}

	[Test]
	[Description("Reports CLIO005 for an interface+implementation registration whose interface is never consumed anywhere (the Unzip handler case).")]
	public async Task RunAnalyzerAsync_WhenInterfaceImplementationRegistrationIsNeverConsumed_ReportsClio005Diagnostic() {
		// Arrange
		const string body = """
		                    namespace SampleApp {
		                    	public interface IBar { }
		                    	public sealed class Bar : IBar { }
		                    	public static class Program {
		                    		public static void Run() {
		                    			Microsoft.Extensions.DependencyInjection.IServiceCollection services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
		                    			services.AddTransient<IBar, Bar>();
		                    		}
		                    	}
		                    }
		                    """;
		UnusedDiServiceAnalyzer analyzer = new();

		// Act
		var diagnostics = await AnalyzerTestRunner.RunAnalyzerAsync(DiStubs + body, analyzer);

		// Assert
		diagnostics.Should().ContainSingle(d => d.Id == "CLIO005",
			because: "Bar's only interface IBar is never injected anywhere, so the registration remains dead and interface-liveness must not suppress it");
	}

	[Test]
	[Description("Reports CLIO005 for a dead validator whose constructed interface differs from the only consumed constructed interface (constructed-to-constructed matching, not open-generic).")]
	public async Task RunAnalyzerAsync_WhenValidatorImplementsDifferentConstructedInterface_ReportsClio005Diagnostic() {
		// Arrange
		const string body = """
		                    namespace SampleApp {
		                    	public interface IValidator<T> { }
		                    	public sealed class FooOptions { }
		                    	public sealed class BarOptions { }
		                    	public sealed class FooValidator : IValidator<FooOptions> { }
		                    	public sealed class BarValidator : IValidator<BarOptions> { }
		                    	public sealed class Consumer {
		                    		public Consumer(IValidator<FooOptions> validator) { }
		                    	}
		                    	public static class Program {
		                    		public static void Run() {
		                    			Microsoft.Extensions.DependencyInjection.IServiceCollection services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
		                    			services.AddTransient<FooValidator>();
		                    			services.AddTransient<BarValidator>();
		                    		}
		                    	}
		                    }
		                    """;
		UnusedDiServiceAnalyzer analyzer = new();

		// Act
		var diagnostics = await AnalyzerTestRunner.RunAnalyzerAsync(DiStubs + body, analyzer);

		// Assert
		diagnostics.Should().ContainSingle(d => d.Id == "CLIO005",
			because: "only IValidator<FooOptions> is consumed; BarValidator implements IValidator<BarOptions> which is never consumed, so constructed-to-constructed matching must keep BarValidator flagged while FooValidator is suppressed");
	}

	[Test]
	[Description("Reports CLIO005 for unconsumed AddSingleton, AddScoped, self-registration, factory and typeof registrations.")]
	public async Task RunAnalyzerAsync_WhenAllRegistrationFormsAreUnconsumed_ReportsClio005PerRegistration() {
		// Arrange
		const string body = """
		                    namespace SampleApp {
		                    	public interface ISingletonService { }
		                    	public sealed class SingletonService : ISingletonService { }
		                    	public interface IScopedService { }
		                    	public sealed class ScopedService : IScopedService { }
		                    	public sealed class SelfRegistered { }
		                    	public interface IFactoryService { }
		                    	public sealed class FactoryService : IFactoryService { }
		                    	public interface ITypeofService { }
		                    	public sealed class TypeofService : ITypeofService { }
		                    	public static class Program {
		                    		public static void Run() {
		                    			Microsoft.Extensions.DependencyInjection.IServiceCollection services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
		                    			services.AddSingleton<ISingletonService, SingletonService>();
		                    			services.AddScoped<IScopedService, ScopedService>();
		                    			services.AddTransient<SelfRegistered>();
		                    			services.AddTransient<IFactoryService>(_ => new FactoryService());
		                    			services.AddTransient(typeof(ITypeofService), typeof(TypeofService));
		                    		}
		                    	}
		                    }
		                    """;
		UnusedDiServiceAnalyzer analyzer = new();

		// Act
		var diagnostics = await AnalyzerTestRunner.RunAnalyzerAsync(DiStubs + body, analyzer);

		// Assert
		diagnostics.Count(d => d.Id == "CLIO005")
			.Should()
			.Be(5, because: "each of the five unconsumed registration forms (Singleton, Scoped, self-reg, factory, typeof) is independently dead code");
	}
}
