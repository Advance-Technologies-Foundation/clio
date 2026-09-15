using Clio.Command.McpServer;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class McpServerCommandPresenceTests {
	[Test]
	[Description("The MCP host writes no presence marker when it runs as a short-lived worker child.")]
	public void RegisterHostPresence_Should_Write_No_Marker_For_A_Worker() {
		// Arrange
		IMcpHostPresenceRegistry registry = Substitute.For<IMcpHostPresenceRegistry>();
		McpServerCommandOptions options = new() { Worker = true };

		// Act
		string markerFilePath = McpServerCommand.RegisterHostPresence(options, registry);

		// Assert
		markerFilePath.Should().BeNull(
			because: "a worker lives for one call; one marker per spawned worker would defer updates for a host that is already gone");
		registry.DidNotReceive().Register();
	}

	[Test]
	[Description("The MCP host writes the presence marker when it is a real host, so a CLI process can see it before self-updating.")]
	public void RegisterHostPresence_Should_Write_A_Marker_For_A_Host() {
		// Arrange
		IMcpHostPresenceRegistry registry = Substitute.For<IMcpHostPresenceRegistry>();
		registry.Register().Returns("/tmp/clio/mcp-server.10.lock");
		McpServerCommandOptions options = new() { Worker = false };

		// Act
		string markerFilePath = McpServerCommand.RegisterHostPresence(options, registry);

		// Assert
		markerFilePath.Should().Be("/tmp/clio/mcp-server.10.lock",
			because: "the returned path is what the shutdown path removes, so it must be the one that was written");
		registry.Received(1).Register();
	}
}
