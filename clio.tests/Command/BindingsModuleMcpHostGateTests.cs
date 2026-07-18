using System;
using System.IO.Abstractions.TestingHelpers;
using Clio;
using Clio.Command.McpServer;
using Clio.Tests.Infrastructure;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Covers the ENG-92563 Lever 1 gate: <see cref="BindingsModule.Register"/> only registers the MCP
/// stdio host (and the <see cref="McpServerCommand"/> that depends on the McpServer singleton) when
/// the caller explicitly opts in via <c>registerMcpHost:true</c>.
/// </summary>
[TestFixture]
[Property("Module", "Command")]
public class BindingsModuleMcpHostGateTests {
	[TestCase("0.0.0")]
	[TestCase("0.0.0.0")]
	[Category("Unit")]
	[Description("Maps source-build assembly-version sentinels to the external knowledge compatibility fallback.")]
	public void ResolveKnowledgeBundleClioVersion_ShouldUseProductFallback_WhenAssemblyVersionIsDevelopmentSentinel(
		string assemblyVersion) {
		// Arrange
		Version version = new(assemblyVersion);

		// Act
		Version result = BindingsModule.ResolveKnowledgeBundleClioVersion(version);

		// Assert
		result.Should().Be(new Version(8, 1, 0),
			because: "source builds without CI or tag versioning must remain compatible with the external bundle product range");
	}

	[Test]
	[Category("Unit")]
	[Description("Preserves a real assembly version when building external knowledge compatibility capabilities.")]
	public void ResolveKnowledgeBundleClioVersion_ShouldPreserveVersion_WhenAssemblyVersionIsReal() {
		// Arrange
		Version version = new(8, 1, 0, 86);

		// Act
		Version result = BindingsModule.ResolveKnowledgeBundleClioVersion(version);

		// Assert
		result.Should().Be(version,
			because: "published builds must advertise their actual assembly product version and revision");
	}

	[Test]
	[Category("Unit")]
	[Description("Register with registerMcpHost:false builds successfully under ValidateOnBuild and leaves the MCP host out of the graph.")]
	public void Register_Should_NotRegisterMcpHost_When_RegisterMcpHostIsFalse() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();

		// Act — Register itself runs BuildServiceProvider(ValidateOnBuild:true); a returned provider
		// proves the whole graph validated without the McpServer dependency.
		IServiceProvider provider = new BindingsModule(fileSystem)
			.Register(profile: BindingsModuleRegistrationProfile.Bootstrap, registerMcpHost: false);

		// Assert
		provider.GetService(typeof(ModelContextProtocol.Server.McpServer)).Should().BeNull(
			because: "the MCP server singleton must be registered only when the host is explicitly requested");
		provider.GetService(typeof(McpServerCommand)).Should().BeNull(
			because: "McpServerCommand depends on the gated McpServer singleton, so it must be absent from non-mcp builds");
	}

	[Test]
	[Category("Unit")]
	[Description("Register with registerMcpHost:true registers the MCP host so McpServer and McpServerCommand resolve from the container.")]
	public void Register_Should_RegisterMcpHost_When_RegisterMcpHostIsTrue() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();

		// Act
		IServiceProvider provider = new BindingsModule(fileSystem)
			.Register(profile: BindingsModuleRegistrationProfile.Bootstrap, registerMcpHost: true);

		// Assert
		provider.GetService(typeof(ModelContextProtocol.Server.McpServer)).Should().NotBeNull(
			because: "AddMcpServer registers the McpServer singleton when the host is requested");
		provider.GetRequiredService<McpServerCommand>().Should().NotBeNull(
			because: "the mcp-server command must resolve from the container that hosts the MCP server");
	}
}
