using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
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
	private IK8DatabaseDiscovery _databaseDiscovery;
	private IDatabaseCapabilityChecker _databaseCapabilityChecker;
	private IDatabaseConnectivityChecker _databaseConnectivityChecker;
	private IK8ServiceResolver _k8ServiceResolver;
	private IKubernetesClient _kubernetesClient;
	private ILogger _logger;
	private ILocalDatabaseAssertion _localDatabaseAssertion;
	private ILocalRedisAssertion _localRedisAssertion;
	private IRedisDatabaseSelector _redisDatabaseSelector;
	private ISettingsRepository _settingsRepository;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder)
	{
		base.AdditionalRegistrations(containerBuilder);

		_logger = Substitute.For<ILogger>();
		containerBuilder.AddTransient(_ => _logger);

		_kubernetesClient = Substitute.For<IKubernetesClient>();
		containerBuilder.AddTransient(_ => _kubernetesClient);

		_databaseDiscovery = Substitute.For<IK8DatabaseDiscovery>();
		_databaseConnectivityChecker = Substitute.For<IDatabaseConnectivityChecker>();
		_databaseCapabilityChecker = Substitute.For<IDatabaseCapabilityChecker>();
		_k8ServiceResolver = Substitute.For<IK8ServiceResolver>();
		_redisDatabaseSelector = Substitute.For<IRedisDatabaseSelector>();
		_settingsRepository = Substitute.For<ISettingsRepository>();
		containerBuilder.AddTransient(_ => _settingsRepository);

		containerBuilder.AddTransient<IK8ContextValidator>(_ => new K8ContextValidator(_kubernetesClient));
		containerBuilder.AddTransient<IK8DatabaseAssertion>(_ => new K8DatabaseAssertion(
			_databaseDiscovery,
			_databaseConnectivityChecker,
			_databaseCapabilityChecker,
			_kubernetesClient));
		containerBuilder.AddTransient<IK8RedisAssertion>(_ => new K8RedisAssertion(_kubernetesClient, _k8ServiceResolver, _redisDatabaseSelector));
		containerBuilder.AddTransient<IFsPathAssertion>(_ => new FsPathAssertion(_settingsRepository, _logger));
		containerBuilder.AddTransient<IFsPermissionAssertion>(_ => new FsPermissionAssertion(_settingsRepository, _logger));

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
		_kubernetesClient.ClearReceivedCalls();
		_databaseDiscovery.ClearReceivedCalls();
		_databaseConnectivityChecker.ClearReceivedCalls();
		_databaseCapabilityChecker.ClearReceivedCalls();
		_k8ServiceResolver.ClearReceivedCalls();
		_redisDatabaseSelector.ClearReceivedCalls();
		_settingsRepository.ClearReceivedCalls();
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
	[Description("Should execute local DB assertion when db checks are requested in local scope without db-server-name")]
	public void Execute_WhenLocalDbRequestedWithoutDbServerName_ShouldExecuteAssertion()
	{
		// Arrange
		_localDatabaseAssertion.ExecuteAsync("postgres", 1, false, null, null)
			.Returns(Task.FromResult(new AssertionResult
			{
				Status = "pass",
				Scope = AssertionScope.Local
			}));

		AssertOptions options = new()
		{
			Scope = "local",
			DatabaseEngines = "postgres"
		};

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(0, because: "local DB assertion should allow discovering local DB servers from configuration");
		_localDatabaseAssertion.Received(1).ExecuteAsync("postgres", 1, false, null, null);
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
		_localDatabaseAssertion.ExecuteAsync("postgres", 1, false, null, null)
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

		_localRedisAssertion.ExecuteAsync(true, true, null)
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
			Redis = true,
			RedisConnect = true,
			RedisPing = true
		};

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(0, because: "all requested local assertions passed");
		_localDatabaseAssertion.Received(1).ExecuteAsync("postgres", 1, false, null, null);
		_localRedisAssertion.Received(1).ExecuteAsync(true, true, null);
	}

	[Test]
	[Description("Should execute exhaustive local assertions when --all is specified")]
	public void Execute_WhenLocalAllSpecified_ShouldExecuteExhaustiveChecks()
	{
		// Arrange
		_localDatabaseAssertion.ExecuteAsync("postgres,mssql", 1, true, "version", null)
			.Returns(Task.FromResult(new AssertionResult
			{
				Status = "pass",
				Scope = AssertionScope.Local,
				Resolved = new Dictionary<string, object>()
			}));
		_localRedisAssertion.ExecuteAsync(true, true, null)
			.Returns(Task.FromResult(new AssertionResult
			{
				Status = "pass",
				Scope = AssertionScope.Local,
				Resolved = new Dictionary<string, object>()
			}));

		AssertOptions options = new()
		{
			Scope = "local",
			All = true
		};

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(0, because: "--all should run exhaustive local database and redis checks");
		_localDatabaseAssertion.Received(1).ExecuteAsync("postgres,mssql", 1, true, "version", null);
		_localRedisAssertion.Received(1).ExecuteAsync(true, true, null);
	}

	[Test]
	[Description("Should return invalid invocation when --all is combined with explicit local options")]
	public void Execute_WhenLocalAllIsCombinedWithExplicitOptions_ShouldReturnInvalidInvocation()
	{
		// Arrange
		AssertOptions options = new()
		{
			Scope = "local",
			All = true,
			Redis = true
		};

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(2, because: "--all must not be combined with explicit scope assertion options");
		_logger.Received().WriteError(Arg.Is<string>(s => s.Contains("--all cannot be combined")));
	}

	[Test]
	[Description("Should return invalid invocation when --all is combined with explicit k8 options")]
	public void Execute_WhenK8AllIsCombinedWithExplicitOptions_ShouldReturnInvalidInvocation()
	{
		// Arrange
		AssertOptions options = new()
		{
			Scope = "k8",
			All = true,
			DatabaseEngines = "postgres"
		};

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(2, because: "--all must not be combined with explicit scope assertion options");
		_logger.Received().WriteError(Arg.Is<string>(s => s.Contains("--all cannot be combined")));
	}

	[Test]
	[Description("Should return invalid invocation when --all is combined with explicit filesystem options")]
	public void Execute_WhenFsAllIsCombinedWithExplicitOptions_ShouldReturnInvalidInvocation()
	{
		// Arrange
		AssertOptions options = new()
		{
			Scope = "fs",
			All = true,
			Path = "C:\\temp"
		};

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(2, because: "--all must not be combined with explicit scope assertion options");
		_logger.Received().WriteError(Arg.Is<string>(s => s.Contains("--all cannot be combined")));
	}

	[Test]
	[Description("Should use IIS_IUSRS identity for filesystem --all assertions on Windows")]
	public void Execute_WhenFsAllSpecifiedOnWindows_ShouldUseIisIusrsIdentity()
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			Assert.Inconclusive("This test only runs on Windows");
			return;
		}

		// Arrange
		string tempDir = Path.Combine(Path.GetTempPath(), $"clio-assert-fs-{System.Guid.NewGuid()}");
		Directory.CreateDirectory(tempDir);
		_settingsRepository.GetIISClioRootPath().Returns(tempDir);
		AssertOptions options = new()
		{
			Scope = "fs",
			All = true
		};

		try
		{
			// Act
			int result = _sut.Execute(options);

			// Assert
			result.Should().NotBe(2, because: "filesystem --all should execute assertion flow without invalid invocation");
			_logger.Received().WriteLine(Arg.Is<string>(s => s.Contains("IIS_IUSRS")));
		}
		finally
		{
			Directory.Delete(tempDir, true);
		}
	}

	[Test]
	[Description("Should perform path-only validation for filesystem --all on non-Windows platforms")]
	public void Execute_WhenFsAllSpecifiedOnNonWindows_ShouldValidatePathOnly()
	{
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			Assert.Inconclusive("This test only runs on non-Windows platforms");
			return;
		}

		// Arrange
		IFsPathAssertion fsPathAssertion = Substitute.For<IFsPathAssertion>();
		IFsPermissionAssertion fsPermissionAssertion = Substitute.For<IFsPermissionAssertion>();
		fsPathAssertion.Execute("iis-clio-root-path").Returns(new AssertionResult {
			Status = "pass",
			Scope = AssertionScope.Fs,
			Resolved = new Dictionary<string, object> { ["path"] = "/tmp/clio-assert-fs" },
			Details = new Dictionary<string, object> { ["requestedPath"] = "iis-clio-root-path" }
		});
		AssertCommand command = new(
			_logger,
			_kubernetesClient,
			Container.GetRequiredService<IK8ContextValidator>(),
			Container.GetRequiredService<IK8DatabaseAssertion>(),
			Container.GetRequiredService<IK8RedisAssertion>(),
			fsPathAssertion,
			fsPermissionAssertion,
			_localDatabaseAssertion,
			_localRedisAssertion);
		AssertOptions options = new()
		{
			Scope = "fs",
			All = true
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, because: "non-Windows filesystem --all should only require successful path assertion");
		fsPathAssertion.Received(1).Execute("iis-clio-root-path");
		fsPermissionAssertion.DidNotReceive().Execute(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Description("Should return failure when local redis assertion fails")]
	public void Execute_WhenLocalRedisFails_ShouldReturnFailure()
	{
		// Arrange
		_localRedisAssertion.ExecuteAsync(false, false, null)
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

	[Test]
	[Description("Should return invalid invocation when redis-server-name is used without redis flag in local scope")]
	public void Execute_WhenRedisServerNameSpecifiedWithoutRedis_ShouldReturnInvalidInvocation()
	{
		// Arrange
		AssertOptions options = new()
		{
			Scope = "local",
			RedisServerName = "redis-dev"
		};

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(2, because: "redis server selection must be bound to redis assertions explicitly");
		_logger.Received().WriteError(Arg.Is<string>(s => s.Contains("--redis parameter is required")));
	}

	[Test]
	[Description("Should pass redis-server-name to local redis assertion in local scope")]
	public void Execute_WhenRedisServerNameSpecified_ShouldPassServerNameToAssertion()
	{
		// Arrange
		_localRedisAssertion.ExecuteAsync(false, false, "redis-dev")
			.Returns(Task.FromResult(new AssertionResult
			{
				Status = "pass",
				Scope = AssertionScope.Local,
				Resolved = new Dictionary<string, object>()
			}));

		AssertOptions options = new()
		{
			Scope = "local",
			Redis = true,
			RedisServerName = "redis-dev"
		};

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(0, because: "local redis assertion should support server selection from settings");
		_localRedisAssertion.Received(1).ExecuteAsync(false, false, "redis-dev");
	}
}
