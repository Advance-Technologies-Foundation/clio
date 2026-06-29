using System;
using System.Net;
using Clio.Common.Database;
using FluentAssertions;
using NUnit.Framework;
using StackExchange.Redis;

namespace Clio.Tests.Common.Database;

[TestFixture]
[Property("Module", "Common")]
[Category("Unit")]
public class RedisDatabaseSelectorTests
{
	[Test]
	[Description("Should wire AbortOnConnectFail, bounded timeouts and a single connect retry so an unreachable Redis fails fast instead of hanging")]
	public void BuildConfigurationOptions_ShouldWireFailFastConnect_WhenBuildingOptions()
	{
		// Arrange
		int expectedTimeoutMs = (int)RedisDatabaseSelector.ConnectTimeout.TotalMilliseconds;

		// Act
		ConfigurationOptions options = RedisDatabaseSelector.BuildConfigurationOptions("localhost", 6379, null, null);

		// Assert
		options.AbortOnConnectFail.Should().BeTrue(because: "a failed connect must throw a descriptive error rather than return a multiplexer that retries forever in the background");
		options.ConnectTimeout.Should().Be(expectedTimeoutMs, because: "the connect attempt must be bounded by the fail-fast ConnectTimeout");
		options.SyncTimeout.Should().Be(expectedTimeoutMs, because: "synchronous commands must be bounded instead of using the previous 500s timeout");
		options.AsyncTimeout.Should().Be(expectedTimeoutMs, because: "asynchronous commands must be bounded by the same fail-fast window");
		options.ConnectRetry.Should().Be(1, because: "a single retry keeps the total wait close to ConnectTimeout when Redis is unreachable");
	}

	[Test]
	[Description("Should keep the connect timeout within the 10-15s fail-fast budget")]
	public void BuildConfigurationOptions_ShouldBoundConnectTimeoutWithinFailFastBudget_WhenBuildingOptions()
	{
		// Act
		ConfigurationOptions options = RedisDatabaseSelector.BuildConfigurationOptions("localhost", 6379, null, null);

		// Assert
		options.ConnectTimeout.Should().BeInRange(10_000, 15_000, because: "the fail-fast budget for an unreachable Redis must be a few seconds, not minutes");
	}

	[Test]
	[Description("Should register exactly one endpoint for the supplied host and port")]
	public void BuildConfigurationOptions_ShouldRegisterSingleEndpoint_WhenHostAndPortProvided()
	{
		// Act
		ConfigurationOptions options = RedisDatabaseSelector.BuildConfigurationOptions("redis.example.com", 6380, null, null);

		// Assert
		options.EndPoints.Should().ContainSingle(because: "the selector targets exactly the requested Redis endpoint");
		DnsEndPoint endpoint = (DnsEndPoint)options.EndPoints[0];
		endpoint.Host.Should().Be("redis.example.com", because: "the configured host must match the requested hostname");
		endpoint.Port.Should().Be(6380, because: "the configured port must match the requested port");
	}

	[Test]
	[Description("Should apply credentials when both username and password are provided")]
	public void BuildConfigurationOptions_ShouldApplyCredentials_WhenUsernameAndPasswordProvided()
	{
		// Act
		ConfigurationOptions options = RedisDatabaseSelector.BuildConfigurationOptions("localhost", 6379, "redis-user", "redis-pass");

		// Assert
		options.User.Should().Be("redis-user", because: "a non-empty username must be forwarded to the Redis connection");
		options.Password.Should().Be("redis-pass", because: "a non-empty password must be forwarded to the Redis connection");
	}

	[Test]
	[Description("Should leave credentials unset when username and password are null or whitespace")]
	public void BuildConfigurationOptions_ShouldNotSetCredentials_WhenUsernameAndPasswordAreBlank()
	{
		// Act
		ConfigurationOptions options = RedisDatabaseSelector.BuildConfigurationOptions("localhost", 6379, null, "   ");

		// Assert
		options.User.Should().BeNull(because: "a null username must not configure an ACL user");
		options.Password.Should().BeNull(because: "a whitespace-only password must not be forwarded as a credential");
	}
}
