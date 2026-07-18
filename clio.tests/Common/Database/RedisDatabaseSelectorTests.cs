using System;
using System.Collections.Generic;
using System.Net;
using Clio.Common.Database;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using StackExchange.Redis;

namespace Clio.Tests.Common.Database;

[TestFixture]
[Property("Module", "Common")]
[Category("Unit")]
public class RedisDatabaseSelectorTests
{
	private static IConnectionMultiplexer BuildMultiplexerWithEmptyDatabase(int databaseCount, int firstEmptyDatabase)
	{
		IServer server = Substitute.For<IServer>();
		server.DatabaseCount.Returns(databaseCount);
		server.DatabaseSize(Arg.Any<int>(), Arg.Any<CommandFlags>())
			.Returns(callInfo => callInfo.ArgAt<int>(0) == firstEmptyDatabase ? 0L : 42L);

		IConnectionMultiplexer multiplexer = Substitute.For<IConnectionMultiplexer>();
		multiplexer.GetServer(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<object>()).Returns(server);
		return multiplexer;
	}

	[Test]
	[Description("Should retry a transient connect blip and succeed on a later attempt without hard-failing the deployment")]
	public void FindEmptyDatabase_ShouldRetryAndSucceed_WhenFirstConnectBlips()
	{
		// Arrange
		List<TimeSpan> backoffs = new();
		int attempts = 0;
		IConnectionMultiplexer multiplexer = BuildMultiplexerWithEmptyDatabase(databaseCount: 3, firstEmptyDatabase: 2);
		Func<ConfigurationOptions, IConnectionMultiplexer> connectionFactory = _ =>
		{
			attempts++;
			if (attempts == 1)
			{
				throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "transient blip");
			}

			return multiplexer;
		};
		RedisDatabaseSelector selector = new(connectionFactory, backoffs.Add);

		// Act
		RedisDatabaseSelectionResult result = selector.FindEmptyDatabase("localhost", 6379, null, null);

		// Assert
		result.Success.Should().BeTrue(because: "a single transient connect blip must be absorbed by the bounded retry instead of aborting");
		result.DatabaseNumber.Should().Be(2, because: "the first empty database discovered after reconnecting must be selected");
		attempts.Should().Be(2, because: "the selector must reconnect once after the first blip");
		backoffs.Should().ContainSingle(because: "exactly one backoff must be applied between the failed and successful attempt");
	}

	[Test]
	[Description("Should fail after exhausting the bounded number of attempts when Redis stays unreachable")]
	public void FindEmptyDatabase_ShouldFailAfterMaxAttempts_WhenRedisStaysUnreachable()
	{
		// Arrange
		List<TimeSpan> backoffs = new();
		int attempts = 0;
		Func<ConfigurationOptions, IConnectionMultiplexer> connectionFactory = _ =>
		{
			attempts++;
			throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "still down");
		};
		RedisDatabaseSelector selector = new(connectionFactory, backoffs.Add);

		// Act
		RedisDatabaseSelectionResult result = selector.FindEmptyDatabase("localhost", 6379, null, null);

		// Assert
		result.Success.Should().BeFalse(because: "an unreachable Redis after every attempt must surface a connection failure");
		result.DatabaseNumber.Should().Be(-1, because: "no database can be selected when Redis never connects");
		result.ErrorMessage.Should().Contain("still down", because: "the last connect error must be reported to the user");
		attempts.Should().Be(RedisDatabaseSelector.MaxConnectAttempts, because: "the retry loop must stop after the bounded attempt count");
		backoffs.Should().HaveCount(RedisDatabaseSelector.MaxConnectAttempts - 1, because: "a backoff is applied between attempts but not after the final one");
		backoffs.Should().Equal(
			new[] { RedisDatabaseSelector.RetryBackoff, TimeSpan.FromMilliseconds(RedisDatabaseSelector.RetryBackoff.TotalMilliseconds * 2) },
			because: "the backoff must grow exponentially (500ms then 1000ms) rather than stay constant or linear");
	}

	[Test]
	[Description("Should not retry a definitive non-transient error and fail on the first attempt")]
	public void FindEmptyDatabase_ShouldNotRetry_WhenErrorIsNotTransient()
	{
		// Arrange
		List<TimeSpan> backoffs = new();
		int attempts = 0;
		Func<ConfigurationOptions, IConnectionMultiplexer> connectionFactory = _ =>
		{
			attempts++;
			throw new InvalidOperationException("unexpected failure");
		};
		RedisDatabaseSelector selector = new(connectionFactory, backoffs.Add);

		// Act
		RedisDatabaseSelectionResult result = selector.FindEmptyDatabase("localhost", 6379, null, null);

		// Assert
		result.Success.Should().BeFalse(because: "a definitive error must still surface as a failure");
		attempts.Should().Be(1, because: "a non-transient error must not be retried");
		backoffs.Should().BeEmpty(because: "no backoff is applied when the error is not retryable");
	}

	[Test]
	[Description("Should not retry an authentication failure even though it surfaces as a RedisConnectionException")]
	public void FindEmptyDatabase_ShouldNotRetry_WhenAuthenticationFails()
	{
		// Arrange
		List<TimeSpan> backoffs = new();
		int attempts = 0;
		Func<ConfigurationOptions, IConnectionMultiplexer> connectionFactory = _ =>
		{
			attempts++;
			throw new RedisConnectionException(ConnectionFailureType.AuthenticationFailure, "wrong password");
		};
		RedisDatabaseSelector selector = new(connectionFactory, backoffs.Add);

		// Act
		RedisDatabaseSelectionResult result = selector.FindEmptyDatabase("localhost", 6379, "user", "wrong-pass");

		// Assert
		result.Success.Should().BeFalse(because: "a bad credential must still surface as a failure");
		attempts.Should().Be(1, because: "an authentication failure is a definitive misconfiguration and must fail fast, not consume the whole retry budget");
		backoffs.Should().BeEmpty(because: "no backoff is applied when the failure is not a transient connect blip");
	}

	[Test]
	[Description("Should report all databases in use without retrying when the reachable server has no empty database")]
	public void FindEmptyDatabase_ShouldReportAllInUse_WhenNoEmptyDatabaseExists()
	{
		// Arrange
		List<TimeSpan> backoffs = new();
		int attempts = 0;
		IServer server = Substitute.For<IServer>();
		server.DatabaseCount.Returns(3);
		server.DatabaseSize(Arg.Any<int>(), Arg.Any<CommandFlags>()).Returns(99L);
		IConnectionMultiplexer multiplexer = Substitute.For<IConnectionMultiplexer>();
		multiplexer.GetServer(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<object>()).Returns(server);
		Func<ConfigurationOptions, IConnectionMultiplexer> connectionFactory = _ =>
		{
			attempts++;
			return multiplexer;
		};
		RedisDatabaseSelector selector = new(connectionFactory, backoffs.Add);

		// Act
		RedisDatabaseSelectionResult result = selector.FindEmptyDatabase("localhost", 6379, null, null);

		// Assert
		result.Success.Should().BeFalse(because: "a reachable server with no empty database is a definitive configuration failure");
		result.ErrorMessage.Should().Contain("are in use", because: "the user must be told every database is occupied");
		attempts.Should().Be(1, because: "a reachable-but-full server is not a transient failure and must not be retried");
		backoffs.Should().BeEmpty(because: "no retry occurs when the connect succeeds");
	}

	[Test]
	[Description("Should dispose the Redis connection after probing databases")]
	public void FindEmptyDatabase_ShouldDisposeConnection_WhenProbingCompletes()
	{
		// Arrange
		IConnectionMultiplexer multiplexer = BuildMultiplexerWithEmptyDatabase(databaseCount: 3, firstEmptyDatabase: 1);
		RedisDatabaseSelector selector = new(_ => multiplexer, _ => { });

		// Act
		selector.FindEmptyDatabase("localhost", 6379, null, null);

		// Assert
		// NSubstitute's Received() verification cannot carry a because argument; the intent is that the
		// Redis connection must be disposed exactly once so probing does not leak a connection per call.
		multiplexer.Received(1).Dispose();
	}

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
