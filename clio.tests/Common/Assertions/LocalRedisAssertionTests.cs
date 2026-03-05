using System.Threading.Tasks;
using Clio.Common.Assertions;
using Clio.Common.Database;
using Clio.Common.db;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.Assertions;

[TestFixture]
[Category("Unit")]
public class LocalRedisAssertionTests
{
	private IRedisAuthenticationValidator _redisAuthenticationValidator;
	private ILocalRedisServerResolver _localRedisServerResolver;
	private IRedisDatabaseSelector _redisDatabaseSelector;
	private LocalRedisAssertion _sut;

	[SetUp]
	public void SetUp()
	{
		_redisAuthenticationValidator = Substitute.For<IRedisAuthenticationValidator>();
		_localRedisServerResolver = Substitute.For<ILocalRedisServerResolver>();
		_redisDatabaseSelector = Substitute.For<IRedisDatabaseSelector>();
		_sut = new LocalRedisAssertion(_localRedisServerResolver, _redisDatabaseSelector, _redisAuthenticationValidator);
	}

	[Test]
	[Description("Should fail at discovery when local Redis cannot be discovered")]
	public async Task ExecuteAsync_WhenRedisDiscoveryFails_ShouldReturnRedisDiscoveryFailure()
	{
		// Arrange
		ResolvedLocalRedisServer resolvedServer = null;
		string resolveError = null;
		_localRedisServerResolver.TryResolve(Arg.Any<string>(), out resolvedServer, out resolveError)
			.Returns(x =>
			{
				x[1] = new ResolvedLocalRedisServer { Name = "local-redis", Host = "localhost", Port = 6379 };
				x[2] = null;
				return true;
			});
		_redisDatabaseSelector.FindEmptyDatabase("localhost", 6379, null, null).Returns(new RedisDatabaseSelectionResult
		{
			Success = false,
			ErrorMessage = "redis not reachable"
		});

		// Act
		AssertionResult result = await _sut.ExecuteAsync(false, false, null);

		// Assert
		result.Status.Should().Be("fail", because: "local Redis discovery failure should fail assertion");
		result.Scope.Should().Be(AssertionScope.Local, because: "local assertion must report local scope");
		result.FailedAt.Should().Be(AssertionPhase.RedisDiscovery, because: "failure originates from discovery step");
	}

	[Test]
	[Description("Should pass and include redis endpoint when discovery succeeds and no active checks requested")]
	public async Task ExecuteAsync_WhenDiscoverySucceedsAndNoChecksRequested_ShouldReturnSuccess()
	{
		// Arrange
		ResolvedLocalRedisServer resolvedServer = null;
		string resolveError = null;
		_localRedisServerResolver.TryResolve(Arg.Any<string>(), out resolvedServer, out resolveError)
			.Returns(x =>
			{
				x[1] = new ResolvedLocalRedisServer
				{
					Name = "redis-dev",
					Host = "redis.local",
					Port = 6380,
					Username = "user",
					Password = "password"
				};
				x[2] = null;
				return true;
			});
		_redisDatabaseSelector.FindEmptyDatabase("redis.local", 6380, null, null).Returns(new RedisDatabaseSelectionResult
		{
			Success = true,
			DatabaseNumber = 3
		});
		_redisDatabaseSelector.FindEmptyDatabase("redis.local", 6380, "user", "password").Returns(new RedisDatabaseSelectionResult
		{
			Success = true,
			DatabaseNumber = 3
		});
		_redisAuthenticationValidator.ValidateAuthenticationIsEnforcedAsync(Arg.Any<ResolvedLocalRedisServer>())
			.Returns(Task.FromResult(RedisAuthValidationResult.Enforced()));

		// Act
		AssertionResult result = await _sut.ExecuteAsync(false, false, null);

		// Assert
		result.Status.Should().Be("pass", because: "successful discovery without extra checks should pass");
		result.Scope.Should().Be(AssertionScope.Local, because: "success should still indicate local scope");
		result.Resolved.Should().ContainKey("redis", because: "resolved redis info should be present on success");
		string json = result.ToJson();
		json.Should().Contain("\"firstAvailableDb\": 3",
			because: "resolved redis data should expose the first available database index explicitly");
		json.Should().Contain("\"name\": \"redis-dev\"",
			because: "resolved redis payload should expose resolved server name");
		json.Should().NotContain("\"db\":",
			because: "ambiguous redis database field name should no longer be used");
	}

	[Test]
	[Description("Should fail when credentials are configured but Redis allows anonymous access")]
	public async Task ExecuteAsync_WhenCredentialsConfiguredAndAnonymousAccessAllowed_ShouldFail()
	{
		// Arrange
		ResolvedLocalRedisServer resolvedServer = null;
		string resolveError = null;
		_localRedisServerResolver.TryResolve(Arg.Any<string>(), out resolvedServer, out resolveError)
			.Returns(x =>
			{
				x[1] = new ResolvedLocalRedisServer
				{
					Name = "local-redis",
					Host = "localhost",
					Port = 6379,
					Username = "a",
					Password = "a"
				};
				x[2] = null;
				return true;
			});
		_redisDatabaseSelector.FindEmptyDatabase("localhost", 6379, "a", "a").Returns(new RedisDatabaseSelectionResult
		{
			Success = true,
			DatabaseNumber = 1
		});
		_redisAuthenticationValidator.ValidateAuthenticationIsEnforcedAsync(Arg.Any<ResolvedLocalRedisServer>())
			.Returns(Task.FromResult(new RedisAuthValidationResult
			{
				IsAuthenticationEnforced = false,
				ErrorMessage = "anonymous access is allowed"
			}));

		// Act
		AssertionResult result = await _sut.ExecuteAsync(false, false, null);

		// Assert
		result.Status.Should().Be("fail", because: "configured redis credentials should imply strict auth enforcement");
		result.FailedAt.Should().Be(AssertionPhase.RedisConnect, because: "auth enforcement is validated during connectivity policy check");
		result.Reason.Should().Contain("anonymous access is allowed", because: "failure reason should explain why strict validation failed");
	}
}
