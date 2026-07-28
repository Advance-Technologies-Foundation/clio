using System;
using System.Collections.Generic;
using Clio;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// The MCP host build must validate the durable-invocation surface EAGERLY: <c>ValidateOnBuild</c>
/// verifies the DI graph but does not instantiate services, so without the explicit eager resolution in
/// <c>BindingsModule.Register</c> (registerMcpHost path) a malformed compatibility catalog or a
/// duplicate tool name would surface only on the first <c>tools/call</c>. These tests pin that a
/// malformed catalog aborts host construction, and that the registry/catalog are host-lifetime
/// singletons (the registry's constructor reflects the full tool surface — far too expensive to rebuild
/// per call).
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class McpHostStartupValidationTests {

	private static McpToolCompatibilityEntry CollidingEntry(string canonical, params string[] aliases) =>
		new(
			CanonicalName: canonical,
			Aliases: aliases,
			Kind: McpToolCompatibilityKind.DeprecatedAlias,
			DeprecatedSince: null,
			Replacement: null,
			Owner: McpToolSurfaceOwner.Clio);

	[Test]
	[Category("Unit")]
	[Description("A malformed compatibility catalog (duplicate alias) aborts MCP HOST construction — startup fails fast instead of deferring the collision to the first tools/call (TC-U-16).")]
	public void Register_ShouldThrowAtHostBuild_WhenCompatibilityCatalogIsMalformed() {
		// Arrange — override the catalog registration with a factory producing a colliding catalog; the
		// factory runs during the host's eager validation resolve, not at registration time.
		BindingsModule module = new();
		Action act = () => module.Register(
			applyBootstrapRepairs: false,
			registerMcpHost: true,
			additionalRegistrations: services => services.AddSingleton<IMcpToolCompatibilityCatalog>(_ =>
				new McpToolCompatibilityCatalog(new List<McpToolCompatibilityEntry> {
					CollidingEntry("tool-a", "shared-alias"),
					CollidingEntry("tool-b", "shared-alias")
				})));

		// Act & Assert
		act.Should().Throw<InvalidOperationException>(
			because: "a catalog collision must abort host startup, not surface on the first unmatched tools/call");
	}

	[Test]
	[Category("Unit")]
	[Description("The MCP host resolves the invoker registry and compatibility catalog as host-lifetime singletons, so the expensive reflection scan runs once per process instead of once per call.")]
	public void Register_ShouldPinRegistryAndCatalogAsSingletons_WhenMcpHostIsRegistered() {
		// Arrange
		BindingsModule module = new();
		IServiceProvider provider = module.Register(
			applyBootstrapRepairs: false,
			registerMcpHost: true);

		// Act
		IMcpToolInvokerRegistry registryFirst = provider.GetRequiredService<IMcpToolInvokerRegistry>();
		IMcpToolInvokerRegistry registrySecond = provider.GetRequiredService<IMcpToolInvokerRegistry>();
		IMcpToolCompatibilityCatalog catalogFirst = provider.GetRequiredService<IMcpToolCompatibilityCatalog>();
		IMcpToolCompatibilityCatalog catalogSecond = provider.GetRequiredService<IMcpToolCompatibilityCatalog>();

		// Assert
		registrySecond.Should().BeSameAs(registryFirst,
			because: "the registry's constructor reflects and SDK-builds the full tool map — it must be built once per host");
		catalogSecond.Should().BeSameAs(catalogFirst,
			because: "the catalog is immutable and must be validated/built once per host");
	}
}
