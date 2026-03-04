using System.Collections.Generic;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Common;
using Clio.Common.Assertions;
using Clio.Common.Database;
using Clio.Common.Kubernetes;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public class AssertCommandTests : BaseCommandTests<AssertOptions>
{
	private AssertCommand _sut;
	private ILogger _logger;
	private ILocalDatabaseAssertion _localDatabaseAssertion;
	private ILocalRedisAssertion _localRedisAssertion;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder)
	{
		base.AdditionalRegistrations(containerBuilder);

		_logger = Substitute.For<ILogger>();
		containerBuilder.AddTransient(_ => _logger);

		IKubernetesClient kubernetesClient = Substitute.For<IKubernetesClient>();
		containerBuilder.AddTransient(_ => kubernetesClient);

		IK8DatabaseDiscovery databaseDiscovery = Substitute.For<IK8DatabaseDiscovery>();
		IDatabaseConnectivityChecker databaseConnectivity = Substitute.For<IDatabaseConnectivityChecker>();
		IDatabaseCapabilityChecker databaseCapability = Substitute.For<IDatabaseCapabilityChecker>();
		IK8ServiceResolver k8ServiceResolver = Substitute.For<IK8ServiceResolver>();
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();

		containerBuilder.AddTransient(_ => new K8ContextValidator(kubernetesClient));
		containerBuilder.AddTransient(_ => new K8DatabaseAssertion(
			databaseDiscovery,
			databaseConnectivity,
			databaseCapability,
			kubernetesClient));
		containerBuilder.AddTransient(_ => new K8RedisAssertion(kubernetesClient, k8ServiceResolver));
		containerBuilder.AddTransient(_ => new FsPathAssertion(settingsRepository, _logger));
		containerBuilder.AddTransient(_ => new FsPermissionAssertion(settingsRepository, _logger));

		_localDatabaseAssertion = Substitute.For<ILocalDatabaseAssertion>();
		_localRedisAssertion = Substitute.For<ILocalRedisAssertion>();
		containerBuilder.AddTransient(_ => _localDatabaseAssertion);
		containerBuilder.AddTransient(_ => _localRedisAssertion);
	}

	[SetUp]
	public override void Setup()
	{
		base.Setup();
		_sut = Container.GetRequiredService<AssertCommand>();
	}

	[TearDown]
	public override void TearDown()
	{
		_logger.ClearReceivedCalls();
		_localDatabaseAssertion.ClearReceivedCalls();
		_localRedisAssertion.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("Should return invalid invocation when unsupported kubernetes options are passed with local scope")]
	public void Execute_WhenLocalScopeUsesContextOption_ShouldReturnInvalidInvocation()
	{
		// Arrange
		AssertOptions options = new()
		{
			Scope = "local",
			Context = "rancher-desktop"
		};

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(2, because: "local scope does not support kubernetes context options");
		_logger.Received().WriteError(Arg.Is<string>(s => s.Contains("Unsupported option")));
	}

	[Test]
	[Description("Should return invalid invocation when db checks are requested in local scope without db-server-name")]
	public void Execute_WhenLocalDbRequestedWithoutDbServerName_ShouldReturnInvalidInvocation()
	{
		// Arrange
		AssertOptions options = new()
		{
			Scope = "local",
			DatabaseEngines = "postgres"
		};

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(2, because: "local DB assertions require db server configuration name");
		_logger.Received().WriteError(Arg.Is<string>(s => s.Contains("--db-server-name")));
	}

	[Test]
	[Description("Should return invalid invocation when redis-connect or redis-ping is used without redis flag in local scope")]
	public void Execute_WhenRedisConnectSpecifiedWithoutRedis_ShouldReturnInvalidInvocation()
	{
		// Arrange
		AssertOptions options = new()
		{
			Scope = "local",
			RedisConnect = true
		};

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(2, because: "redis-connect must be explicitly paired with redis assertion flag");
		_logger.Received().WriteError(Arg.Is<string>(s => s.Contains("--redis parameter is required")));
	}

	[Test]
	[Description("Should return success when local db and redis assertions pass")]
	public void Execute_WhenLocalDbAndRedisPass_ShouldReturnSuccess()
	{
		// Arrange
		_localDatabaseAssertion.ExecuteAsync("postgres", 1, false, null, "local-postgres")
			.Returns(Task.FromResult(new AssertionResult
			{
				Status = "pass",
				Scope = AssertionScope.Local,
				Resolved = new Dictionary<string, object>
				{
					["databases"] = new List<Dictionary<string, object>>
					{
						new()
						{
							["engine"] = "postgres",
							["host"] = "localhost",
							["port"] = 5432
						}
					}
				}
			}));

		_localRedisAssertion.ExecuteAsync(true, true)
			.Returns(Task.FromResult(new AssertionResult
			{
				Status = "pass",
				Scope = AssertionScope.Local,
				Resolved = new Dictionary<string, object>
				{
					["redis"] = new { host = "localhost", port = 6379 }
				}
			}));

		AssertOptions options = new()
		{
			Scope = "local",
			DatabaseEngines = "postgres",
			DbServerName = "local-postgres",
			Redis = true,
			RedisConnect = true,
			RedisPing = true
		};

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(0, because: "all requested local assertions passed");
		_localDatabaseAssertion.Received(1).ExecuteAsync("postgres", 1, false, null, "local-postgres");
		_localRedisAssertion.Received(1).ExecuteAsync(true, true);
	}

	[Test]
	[Description("Should return failure when local redis assertion fails")]
	public void Execute_WhenLocalRedisFails_ShouldReturnFailure()
	{
		// Arrange
		_localRedisAssertion.ExecuteAsync(false, false)
			.Returns(Task.FromResult(AssertionResult.Failure(
				AssertionScope.Local,
				AssertionPhase.RedisDiscovery,
				"redis unavailable")));

		AssertOptions options = new()
		{
			Scope = "local",
			Redis = true
		};

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(1, because: "any failed assertion should return failure exit code");
	}
}
