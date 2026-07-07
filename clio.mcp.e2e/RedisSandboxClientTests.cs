using Clio.Mcp.E2E.Support.Redis;
using FluentAssertions;
using StackExchange.Redis;

namespace Clio.Mcp.E2E;

/// <summary>
/// Unit tests for the pure <see cref="RedisSandboxClient.BuildConfigurationOptions"/> timeout wiring.
/// They build <see cref="ConfigurationOptions"/> in-memory (no Redis server, no stand, no network I/O),
/// so they validate the fail-fast connect-timeout contract locally and are categorized <c>Unit</c>
/// rather than <c>McpE2E.Sandbox</c>. The actual unreachable-Redis hang-vs-fail-fast behavior can only
/// be exercised against a stand, so it is not covered here.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class RedisSandboxClientTests {
	[Test]
	[Description("Applies a bounded connect timeout and AbortOnConnectFail=true to a Creatio-style 'host=...' connection string so an unreachable sandbox Redis fails fast instead of hanging.")]
	public void BuildConfigurationOptions_ShouldApplyBoundedConnectTimeout_WhenHostStyleConnectionString() {
		// Arrange
		string connectionString = "host=redis.sandbox.local;db=2;port=6380";

		// Act
		ConfigurationOptions options = RedisSandboxClient.BuildConfigurationOptions(connectionString);

		// Assert
		options.AbortOnConnectFail.Should().BeTrue(
			because: "ConnectAsync must throw on a failed connect instead of returning a multiplexer that retries forever and hangs the suite");
		options.ConnectTimeout.Should().Be((int)RedisSandboxClient.ConnectTimeout.TotalMilliseconds,
			because: "the connect must be bounded so an unreachable sandbox Redis surfaces a failure within a few seconds");
		options.SyncTimeout.Should().Be((int)RedisSandboxClient.ConnectTimeout.TotalMilliseconds,
			because: "synchronous Redis operations must also be bounded so a stuck endpoint cannot block indefinitely");
		options.AsyncTimeout.Should().Be((int)RedisSandboxClient.ConnectTimeout.TotalMilliseconds,
			because: "asynchronous Redis operations must also be bounded so a stuck endpoint cannot block indefinitely");
		options.DefaultDatabase.Should().Be(2,
			because: "the parsed db index must be preserved so reachable-Redis behavior is unchanged");
	}

	[Test]
	[Description("Applies a bounded connect timeout and AbortOnConnectFail=true to a standard StackExchange.Redis-style connection string so an unreachable sandbox Redis fails fast instead of hanging.")]
	public void BuildConfigurationOptions_ShouldApplyBoundedConnectTimeout_WhenStandardConnectionString() {
		// Arrange
		string connectionString = "redis.sandbox.local:6380,defaultDatabase=3";

		// Act
		ConfigurationOptions options = RedisSandboxClient.BuildConfigurationOptions(connectionString);

		// Assert
		options.AbortOnConnectFail.Should().BeTrue(
			because: "ConnectAsync must throw on a failed connect instead of returning a multiplexer that retries forever and hangs the suite");
		options.ConnectTimeout.Should().Be((int)RedisSandboxClient.ConnectTimeout.TotalMilliseconds,
			because: "the connect must be bounded regardless of the connection-string shape so an unreachable sandbox Redis fails fast");
		options.SyncTimeout.Should().Be((int)RedisSandboxClient.ConnectTimeout.TotalMilliseconds,
			because: "synchronous Redis operations must also be bounded so a stuck endpoint cannot block indefinitely");
		options.DefaultDatabase.Should().Be(3,
			because: "the parsed default database must be preserved so reachable-Redis behavior is unchanged");
	}
}
