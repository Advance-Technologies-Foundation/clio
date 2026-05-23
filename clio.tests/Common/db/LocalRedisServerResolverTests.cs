using Clio.Common.db;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.db;

[Category("Unit")]
[TestFixture]
[Property("Module", "Common")]
public class LocalRedisServerResolverTests
{
	private ISettingsRepository _settingsRepository;
	private LocalRedisServerResolver _sut;

	[SetUp]
	public void SetUp()
	{
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_sut = new LocalRedisServerResolver(_settingsRepository);
	}

	[Test]
	[Description("Should return localhost fallback when no redis configuration exists and no explicit server requested")]
	public void TryResolve_WhenNoConfigurationExists_ShouldUseLegacyLocalhostFallback()
	{
		// Arrange
		_settingsRepository.HasLocalRedisServersConfiguration().Returns(false);
		_settingsRepository.GetLocalRedisServerNames().Returns(System.Array.Empty<string>());

		// Act
		bool result = _sut.TryResolve(null, out ResolvedLocalRedisServer server, out string errorMessage);

		// Assert
		result.Should().BeTrue(because: "legacy behavior must be preserved when redis section is absent");
		errorMessage.Should().BeNull(because: "successful fallback should not produce error messages");
		server.Name.Should().Be("local-redis", because: "legacy resolved redis name should remain stable");
		server.Host.Should().Be("localhost", because: "legacy fallback endpoint uses localhost");
		server.Port.Should().Be(6379, because: "legacy fallback endpoint uses default redis port");
		server.IsFromConfiguration.Should().BeFalse(because: "fallback server is not loaded from appsettings");
	}

	[Test]
	[Description("Should fail when multiple redis servers are enabled and no default is defined")]
	public void TryResolve_WhenMultipleEnabledAndNoDefault_ShouldFail()
	{
		// Arrange
		_settingsRepository.HasLocalRedisServersConfiguration().Returns(true);
		_settingsRepository.GetLocalRedisServerNames().Returns(new[] { "redis-a", "redis-b" });
		_settingsRepository.GetDefaultLocalRedisServerName().Returns((string)null);

		// Act
		bool result = _sut.TryResolve(null, out ResolvedLocalRedisServer _, out string errorMessage);

		// Assert
		result.Should().BeFalse(because: "ambiguous redis selection must require explicit default or option override");
		errorMessage.Should().Contain("Multiple enabled Redis servers", because: "error should clearly explain ambiguity");
	}

	[Test]
	[Description("Should resolve default configured redis server when multiple servers are enabled")]
	public void TryResolve_WhenDefaultConfigured_ShouldResolveDefaultServer()
	{
		// Arrange
		_settingsRepository.HasLocalRedisServersConfiguration().Returns(true);
		_settingsRepository.GetLocalRedisServerNames().Returns(new[] { "redis-a", "redis-b" });
		_settingsRepository.GetDefaultLocalRedisServerName().Returns("redis-b");
		_settingsRepository.GetLocalRedisServer("redis-b").Returns(new LocalRedisServerConfiguration
		{
			Hostname = "redis.b.local",
			Port = 6381,
			Username = "svc",
			Password = "secret",
			Enabled = true
		});

		// Act
		bool result = _sut.TryResolve(null, out ResolvedLocalRedisServer server, out string errorMessage);

		// Assert
		result.Should().BeTrue(because: "default redis server should be used when option is omitted");
		errorMessage.Should().BeNull(because: "successful resolution should not produce an error");
		server.Name.Should().Be("redis-b", because: "resolver should return configured default server key");
		server.Host.Should().Be("redis.b.local", because: "resolved endpoint should come from configuration");
		server.Port.Should().Be(6381, because: "resolved endpoint should come from configuration");
		server.Username.Should().Be("svc", because: "credentials should be preserved for authenticated redis deployments");
		server.Password.Should().Be("secret", because: "credentials should be preserved for authenticated redis deployments");
		server.IsFromConfiguration.Should().BeTrue(because: "server is resolved from appsettings");
	}

	[Test]
	[Description("Should resolve explicitly requested redis server when it is enabled")]
	public void TryResolve_WhenServerNameProvided_ShouldResolveNamedServer()
	{
		// Arrange
		_settingsRepository.GetLocalRedisServer("redis-dev").Returns(new LocalRedisServerConfiguration
		{
			Hostname = "redis.dev.local",
			Port = 6379,
			Enabled = true
		});
		_settingsRepository.GetLocalRedisServerNames().Returns(new[] { "redis-dev" });
		_settingsRepository.HasLocalRedisServersConfiguration().Returns(true);

		// Act
		bool result = _sut.TryResolve("redis-dev", out ResolvedLocalRedisServer server, out string errorMessage);

		// Assert
		result.Should().BeTrue(because: "explicit redis selection should map directly to matching enabled configuration");
		errorMessage.Should().BeNull(because: "successful resolution should not produce an error");
		server.Name.Should().Be("redis-dev", because: "selected server name should match requested value");
		server.Host.Should().Be("redis.dev.local", because: "host should come from selected configuration");
		server.Port.Should().Be(6379, because: "port should come from selected configuration");
	}
}
