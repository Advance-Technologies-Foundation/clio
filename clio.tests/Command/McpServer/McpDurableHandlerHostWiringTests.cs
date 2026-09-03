using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Clio;
using Clio.UserEnvironment;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Guards the security-exposure invariant (ADR adr-mcp-durable-invocation D1/B3, TC-I-03): the forgiving
/// unmatched-name handler is wired ONLY at the stdio call-site via <c>WithCallToolHandler</c>, and NEVER
/// by the transport-neutral <see cref="BindingsModule.RegisterMcpServer"/> that the (unreleased) mcp-http
/// host also calls — so the forgiving-execution surface can never leak onto the HTTP transport. A future
/// refactor that moved the handler into <c>RegisterMcpServer</c> would silently expose it on mcp-http;
/// this test fails if that happens.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class McpDurableHandlerHostWiringTests {

	// Reads McpServerHandlers.CallToolHandler regardless of getter visibility, so the assertion does not
	// depend on the SDK exposing a public getter.
	private static object ReadCallToolHandler(IServiceProvider provider) {
		McpServerOptions options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;
		object handlers = typeof(McpServerOptions)
			.GetProperty("Handlers", BindingFlags.Public | BindingFlags.Instance)!
			.GetValue(options);
		if (handlers is null) {
			return null;
		}
		return handlers.GetType()
			.GetProperty("CallToolHandler", BindingFlags.Public | BindingFlags.Instance)!
			.GetValue(handlers);
	}

	private static ISettingsRepository SettingsRepositoryStub() {
		ISettingsRepository repository = Substitute.For<ISettingsRepository>();
		repository.GetEnvironment(Arg.Any<string>()).Returns(new EnvironmentSettings());
		return repository;
	}

	[Test]
	[Category("Unit")]
	[Description("RegisterMcpServer alone (the transport-neutral builder the mcp-http host uses) does NOT set a custom CallToolHandler, so the forgiving handler is never exposed on the HTTP transport.")]
	public void RegisterMcpServer_ShouldNotWireDurableHandler_WhenCalledWithoutStdioCallSite() {
		// Arrange — replicate the mcp-http build path: RegisterMcpServer with NO WithCallToolHandler chain.
		IServiceCollection services = new ServiceCollection();
		BindingsModule.RegisterMcpServer(services, SettingsRepositoryStub());
		using ServiceProvider provider = services.BuildServiceProvider();

		// Act
		object callToolHandler = ReadCallToolHandler(provider);

		// Assert
		callToolHandler.Should().BeNull(
			because: "the transport-neutral registration must never wire the forgiving handler — " +
				"it is scoped to the stdio call-site, so the mcp-http host cannot reach it");
	}

	[Test]
	[Category("Unit")]
	[Description("Chaining WithCallToolHandler (exactly as the stdio host does) DOES set a custom CallToolHandler, confirming the invariant test above is meaningful and not vacuously passing.")]
	public void RegisterMcpServer_ShouldWireDurableHandler_WhenStdioCallSiteChainsIt() {
		// Arrange — replicate the stdio host build path: RegisterMcpServer + WithCallToolHandler.
		IServiceCollection services = new ServiceCollection();
		BindingsModule.RegisterMcpServer(services, SettingsRepositoryStub())
			.WithCallToolHandler((request, cancellationToken) =>
				ValueTask.FromResult(new CallToolResult()));
		using ServiceProvider provider = services.BuildServiceProvider();

		// Act
		object callToolHandler = ReadCallToolHandler(provider);

		// Assert
		callToolHandler.Should().NotBeNull(
			because: "the stdio call-site's WithCallToolHandler wires the forgiving handler — " +
				"this half of the invariant proves the null assertion above is a real guard, not a false positive");
	}

	[Test]
	[Category("Unit")]
	[Description("The sibling invariant (ENG-95262 Stage 4b): the MCP host pins ONE execution router for its whole lifetime, so the three dispatch sites can never be answered by different authorities.")]
	public void Register_ShouldPinExecutionRouterAsSingleton_WhenMcpHostIsRegistered() {
		// Arrange — the real host build, which is also what makes a mis-declared route abort startup: the
		// router is eagerly resolved there rather than on the first dispatch.
		BindingsModule module = new();
		IServiceProvider provider = module.Register(
			applyBootstrapRepairs: false,
			registerMcpHost: true);

		// Act
		Clio.Command.McpServer.IMcpExecutionRouter first =
			provider.GetRequiredService<Clio.Command.McpServer.IMcpExecutionRouter>();
		Clio.Command.McpServer.IMcpExecutionRouter second =
			provider.GetRequiredService<Clio.Command.McpServer.IMcpExecutionRouter>();

		// Assert
		second.Should().BeSameAs(first,
			because: "two routers would be two copies of one routing rule — exactly the drift the single-authority " +
				"design exists to prevent (ADR §9), and it would also rebuild the reflected metadata map per call");
	}

	[Test]
	[Category("Unit")]
	[Description("The same invariant on the OTHER transport: the mcp-http build (RegisterInto + RegisterMcpServer, no stdio call-site) also pins ONE execution router, so 'one authority per host' is not a stdio-only property.")]
	public void RegisterInto_ShouldPinExecutionRouterAsSingleton_OnTheMcpHttpBuildPath() {
		// Arrange — replicate McpHttpServerCommand.Run's graph: the shared registrations plus the
		// transport-neutral MCP server builder, and deliberately NOT the registerMcpHost block, which that
		// host never runs. Bootstrap repairs are off because this fixture must not write appsettings.json;
		// nothing about that flag touches the router's lifetime.
		IServiceCollection services = new ServiceCollection();
		ISettingsRepository settingsRepository = new BindingsModule()
			.RegisterInto(services, applyBootstrapRepairs: false);
		BindingsModule.RegisterMcpServer(services, settingsRepository);
		using ServiceProvider provider = services.BuildServiceProvider();

		// Act
		Clio.Command.McpServer.IMcpExecutionRouter first =
			provider.GetRequiredService<Clio.Command.McpServer.IMcpExecutionRouter>();
		Clio.Command.McpServer.IMcpExecutionRouter second =
			provider.GetRequiredService<Clio.Command.McpServer.IMcpExecutionRouter>();

		// Assert
		second.Should().BeSameAs(first,
			because: "mcp-http reached the router only through the assembly auto-scan, which registers it as a " +
				"TRANSIENT — every resolution would build its own copy of the routing rule, so the single-authority " +
				"property held on stdio and quietly did not hold on HTTP (ADR §9)");
	}

	[Test]
	[Category("Unit")]
	[Description("Both constructor-injected dispatch sites declare the routing authority as a dependency, so removing it from either one fails the build rather than silently un-routing that seam.")]
	public void DispatchSites_ShouldDeclareExecutionRouterDependency_ForBothConstructorInjectedSeams() {
		// Arrange — the third site (the static matched filter) has no constructor and is covered
		// behaviourally in McpExecutionRouterTests; these two are the ones a refactor could quietly drop.
		Type[] durableHandlerDependencies = typeof(Clio.Command.McpServer.McpDurableCallToolHandler)
			.GetConstructors()
			.SelectMany(constructor => constructor.GetParameters())
			.Select(parameter => parameter.ParameterType)
			.ToArray();
		Type[] clioRunDependencies = typeof(Clio.Command.McpServer.Tools.ClioRunExecutor)
			.GetConstructors()
			.SelectMany(constructor => constructor.GetParameters())
			.Select(parameter => parameter.ParameterType)
			.ToArray();

		// Act & Assert
		durableHandlerDependencies.Should().Contain(typeof(Clio.Command.McpServer.IMcpExecutionRouter),
			because: "the unmatched seam routes after its confirmation gate — an unrouted copy of it would let a " +
				"long-tail tool reached through a deprecated alias execute in a different place than its canonical sibling");
		clioRunDependencies.Should().Contain(typeof(Clio.Command.McpServer.IMcpExecutionRouter),
			because: "clio-run is the only place the UNWRAPPED inner name exists (ADR rule 7), so dropping the " +
				"dependency there would run the entire long tail on the wrapper's own in-process row");
	}
}
