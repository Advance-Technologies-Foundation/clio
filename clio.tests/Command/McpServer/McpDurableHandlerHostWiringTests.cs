using System;
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
}
