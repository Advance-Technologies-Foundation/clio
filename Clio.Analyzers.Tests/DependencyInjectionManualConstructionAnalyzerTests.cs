using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Analyzers.Tests;

/// <summary>
/// Verifies <see cref="DependencyInjectionManualConstructionAnalyzer"/> diagnostics for DI-registered types.
/// </summary>
public sealed class DependencyInjectionManualConstructionAnalyzerTests {
	[Test]
	[Description("Reports CLIO001 when a DI-registered implementation is manually constructed.")]
	public async Task RunAnalyzerAsync_WhenRegisteredTypeIsConstructedWithNew_ReturnsClio001Diagnostic() {
		// Arrange
		const string source = """
		                    namespace Microsoft.Extensions.DependencyInjection {
		                    	public interface IServiceCollection { }
		                    	public sealed class ServiceCollection : IServiceCollection { }
		                    	public static class ServiceCollectionServiceExtensions {
		                    		public static IServiceCollection AddTransient<TService, TImplementation>(
		                    			this IServiceCollection services)
		                    			where TImplementation : TService {
		                    			return services;
		                    		}
		                    	}
		                    }

		                    namespace SampleApp {
		                    	public interface IFoo { }
		                    	public sealed class Foo : IFoo { }

		                    	public static class Program {
		                    		public static void Run() {
		                    			Microsoft.Extensions.DependencyInjection.IServiceCollection services =
		                    				new Microsoft.Extensions.DependencyInjection.ServiceCollection();
		                    			services.AddTransient<IFoo, Foo>();
		                    			Foo foo = new Foo();
		                    		}
		                    	}
		                    }
		                    """;
		DependencyInjectionManualConstructionAnalyzer analyzer = new();

		// Act
		var diagnostics = await AnalyzerTestRunner.RunAnalyzerAsync(source, analyzer);

		// Assert
		diagnostics.Should().ContainSingle(d => d.Id == "CLIO001",
			because: "manual construction of a registered implementation should be reported");
	}

	[Test]
	[Description("Reports CLIO001 when target-typed new is used for a DI-registered implementation.")]
	public async Task RunAnalyzerAsync_WhenRegisteredTypeIsConstructedWithTargetTypedNew_ReturnsClio001Diagnostic() {
		// Arrange
		const string source = """
		                    namespace Microsoft.Extensions.DependencyInjection {
		                    	public interface IServiceCollection { }
		                    	public sealed class ServiceCollection : IServiceCollection { }
		                    	public static class ServiceCollectionServiceExtensions {
		                    		public static IServiceCollection AddTransient<TService, TImplementation>(
		                    			this IServiceCollection services)
		                    			where TImplementation : TService {
		                    			return services;
		                    		}
		                    	}
		                    }

		                    namespace SampleApp {
		                    	public interface IFoo { }
		                    	public sealed class Foo : IFoo { }

		                    	public static class Program {
		                    		public static void Run() {
		                    			Microsoft.Extensions.DependencyInjection.IServiceCollection services =
		                    				new Microsoft.Extensions.DependencyInjection.ServiceCollection();
		                    			services.AddTransient<IFoo, Foo>();
		                    			Foo foo = new();
		                    		}
		                    	}
		                    }
		                    """;
		DependencyInjectionManualConstructionAnalyzer analyzer = new();

		// Act
		var diagnostics = await AnalyzerTestRunner.RunAnalyzerAsync(source, analyzer);

		// Assert
		diagnostics.Should().ContainSingle(d => d.Id == "CLIO001",
			because: "target-typed manual construction should be treated the same as explicit new");
	}

	[Test]
	[Description("Does not report CLIO001 for manually constructed unregistered types.")]
	public async Task RunAnalyzerAsync_WhenTypeIsNotRegistered_ReturnsNoClio001Diagnostic() {
		// Arrange
		const string source = """
		                    namespace SampleApp {
		                    	public sealed class Foo { }

		                    	public static class Program {
		                    		public static void Run() {
		                    			Foo foo = new Foo();
		                    		}
		                    	}
		                    }
		                    """;
		DependencyInjectionManualConstructionAnalyzer analyzer = new();

		// Act
		var diagnostics = await AnalyzerTestRunner.RunAnalyzerAsync(source, analyzer);

		// Assert
		diagnostics.Should().NotContain(d => d.Id == "CLIO001",
			because: "the analyzer should focus on DI-registered or inferred service types");
	}

	[Test]
	[Description("Does not report CLIO001 for object creation inside DI registration factories.")]
	public async Task RunAnalyzerAsync_WhenObjectCreationIsInsideRegistrationInvocation_ReturnsNoClio001Diagnostic() {
		// Arrange
		const string source = """
		                    namespace Microsoft.Extensions.DependencyInjection {
		                    	public interface IServiceCollection { }
		                    	public sealed class ServiceCollection : IServiceCollection { }
		                    	public static class ServiceCollectionServiceExtensions {
		                    		public static IServiceCollection AddTransient<TService>(
		                    			this IServiceCollection services,
		                    			System.Func<System.IServiceProvider, TService> factory) {
		                    			return services;
		                    		}
		                    	}
		                    }

		                    namespace SampleApp {
		                    	public interface IFoo { }
		                    	public sealed class Foo : IFoo { }

		                    	public static class Program {
		                    		public static void Run() {
		                    			Microsoft.Extensions.DependencyInjection.IServiceCollection services =
		                    				new Microsoft.Extensions.DependencyInjection.ServiceCollection();
		                    			services.AddTransient<IFoo>(_ => new Foo());
		                    		}
		                    	}
		                    }
		                    """;
		DependencyInjectionManualConstructionAnalyzer analyzer = new();

		// Act
		var diagnostics = await AnalyzerTestRunner.RunAnalyzerAsync(source, analyzer);

		// Assert
		diagnostics.Should().NotContain(d => d.Id == "CLIO001",
			because: "factory object creation in registration is intentionally excluded");
	}

	[Test]
	[Description("Does not report CLIO001 for test assemblies.")]
	public async Task RunAnalyzerAsync_WhenAssemblyNameContainsTest_ReturnsNoClio001Diagnostic() {
		// Arrange
		const string source = """
		                    namespace Microsoft.Extensions.DependencyInjection {
		                    	public interface IServiceCollection { }
		                    	public sealed class ServiceCollection : IServiceCollection { }
		                    	public static class ServiceCollectionServiceExtensions {
		                    		public static IServiceCollection AddTransient<TService, TImplementation>(
		                    			this IServiceCollection services)
		                    			where TImplementation : TService {
		                    			return services;
		                    		}
		                    	}
		                    }

		                    namespace SampleApp {
		                    	public interface IFoo { }
		                    	public sealed class Foo : IFoo { }

		                    	public static class Program {
		                    		public static void Run() {
		                    			Microsoft.Extensions.DependencyInjection.IServiceCollection services =
		                    				new Microsoft.Extensions.DependencyInjection.ServiceCollection();
		                    			services.AddTransient<IFoo, Foo>();
		                    			Foo foo = new Foo();
		                    		}
		                    	}
		                    }
		                    """;
		DependencyInjectionManualConstructionAnalyzer analyzer = new();

		// Act
		var diagnostics = await AnalyzerTestRunner.RunAnalyzerAsync(source, analyzer, "sample.tests");

		// Assert
		diagnostics.Should().NotContain(d => d.Id == "CLIO001",
			because: "the analyzer intentionally ignores test assemblies");
	}

	[Test]
	[Description("Reports one CLIO001 diagnostic for one offending manual construction site.")]
	public async Task RunAnalyzerAsync_WhenSingleOffendingConstructionExists_ReturnsSingleClio001Diagnostic() {
		// Arrange
		const string source = """
		                    namespace Microsoft.Extensions.DependencyInjection {
		                    	public interface IServiceCollection { }
		                    	public sealed class ServiceCollection : IServiceCollection { }
		                    	public static class ServiceCollectionServiceExtensions {
		                    		public static IServiceCollection AddScoped<TService, TImplementation>(
		                    			this IServiceCollection services)
		                    			where TImplementation : TService {
		                    			return services;
		                    		}
		                    	}
		                    }

		                    namespace SampleApp {
		                    	public interface IBar { }
		                    	public sealed class Bar : IBar { }

		                    	public static class Program {
		                    		public static void Run() {
		                    			Microsoft.Extensions.DependencyInjection.IServiceCollection services =
		                    				new Microsoft.Extensions.DependencyInjection.ServiceCollection();
		                    			services.AddScoped<IBar, Bar>();
		                    			Bar bar = new Bar();
		                    		}
		                    	}
		                    }
		                    """;
		DependencyInjectionManualConstructionAnalyzer analyzer = new();

		// Act
		var diagnostics = await AnalyzerTestRunner.RunAnalyzerAsync(source, analyzer);

		// Assert
		diagnostics.Count(d => d.Id == "CLIO001")
			.Should()
			.Be(1, because: "one explicit offending construction should produce one diagnostic");
	}

	[Test]
	[Description("Does not report CLIO001 for Clio.EnvironmentSettings manual construction (DTO exempt).")]
	public async Task RunAnalyzerAsync_WhenEnvironmentSettingsIsConstructedWithNew_ReturnsNoClio001Diagnostic() {
		const string source = """
		                    namespace Microsoft.Extensions.DependencyInjection {
		                    	public interface IServiceCollection { }
		                    	public sealed class ServiceCollection : IServiceCollection { }
		                    	public static class ServiceCollectionServiceExtensions {
		                    		public static IServiceCollection AddSingleton<TService>(
		                    			this IServiceCollection services, TService instance) {
		                    			return services;
		                    		}
		                    	}
		                    }

		                    namespace Clio {
		                    	public sealed class EnvironmentSettings {
		                    		public string Uri { get; set; }
		                    	}

		                    	public static class Program {
		                    		public static void Run() {
		                    			Microsoft.Extensions.DependencyInjection.IServiceCollection services =
		                    				new Microsoft.Extensions.DependencyInjection.ServiceCollection();
		                    			services.AddSingleton(new EnvironmentSettings());
		                    			EnvironmentSettings dto = new EnvironmentSettings { Uri = "http://x" };
		                    		}
		                    	}
		                    }
		                    """;
		DependencyInjectionManualConstructionAnalyzer analyzer = new();

		var diagnostics = await AnalyzerTestRunner.RunAnalyzerAsync(source, analyzer);

		diagnostics.Should().NotContain(d => d.Id == "CLIO001",
			because: "EnvironmentSettings is a DTO whose single DI-registered instance represents the active environment, while consumer code legitimately builds new instances for persistence, detection, or manifest assembly");
	}
}
