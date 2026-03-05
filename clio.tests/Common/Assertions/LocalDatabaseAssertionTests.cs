using System.Threading.Tasks;
using Clio.Common.Assertions;
using Clio.Common.Database;
using Clio.Common.db;
using Clio.Common.Kubernetes;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.Assertions;

[TestFixture]
[Category("Unit")]
public class LocalDatabaseAssertionTests
{
	private IDatabaseCapabilityChecker _capabilityChecker;
	private IDbConnectionTester _connectionTester;
	private ISettingsRepository _settingsRepository;
	private LocalDatabaseAssertion _sut;

	[SetUp]
	public void SetUp()
	{
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_connectionTester = Substitute.For<IDbConnectionTester>();
		_capabilityChecker = Substitute.For<IDatabaseCapabilityChecker>();
		_sut = new LocalDatabaseAssertion(_settingsRepository, _connectionTester, _capabilityChecker);
	}

	[Test]
	[Description("Should fail when configured db server is missing")]
	public async Task ExecuteAsync_WhenServerNotFound_ShouldFailAtDiscovery()
	{
		// Arrange
		_settingsRepository.GetLocalDbServer("missing").Returns((LocalDbServerConfiguration)null);
		_settingsRepository.GetLocalDbServerNames().Returns(new[] { "primary-postgres" });

		// Act
		AssertionResult result = await _sut.ExecuteAsync("postgres", 1, false, null, "missing");

		// Assert
		result.Status.Should().Be("fail", because: "missing local server config must fail");
		result.Scope.Should().Be(AssertionScope.Local, because: "local scope should be used for local assertion");
		result.FailedAt.Should().Be(AssertionPhase.DbDiscovery, because: "missing server is a discovery failure");
		result.Details.Should().ContainKey("availableServers", because: "output should help users find valid config names");
	}

	[Test]
	[Description("Should fail with explicit message when no enabled local DB servers are configured")]
	public async Task ExecuteAsync_WhenNoEnabledServersConfigured_ShouldFailAtDiscovery()
	{
		// Arrange
		_settingsRepository.GetLocalDbServerNames().Returns(System.Array.Empty<string>());

		// Act
		AssertionResult result = await _sut.ExecuteAsync("postgres", 1, false, null, null);

		// Assert
		result.Status.Should().Be("fail", because: "local DB assertion requires at least one enabled local DB server");
		result.FailedAt.Should().Be(AssertionPhase.DbDiscovery, because: "absence of enabled local DB servers is a discovery failure");
		result.Reason.Should().Contain("No enabled local database server configurations found", because: "the error must clearly state that enabled servers are required");
	}

	[Test]
	[Description("Should fail when configured server engine does not match requested db engines")]
	public async Task ExecuteAsync_WhenEngineMismatch_ShouldFailAtDiscovery()
	{
		// Arrange
		_settingsRepository.GetLocalDbServer("primary-postgres").Returns(new LocalDbServerConfiguration
		{
			DbType = "postgres",
			Hostname = "localhost",
			Port = 5432
		});

		// Act
		AssertionResult result = await _sut.ExecuteAsync("mssql", 1, false, null, "primary-postgres");

		// Assert
		result.Status.Should().Be("fail", because: "requested engine set does not include configured server engine");
		result.FailedAt.Should().Be(AssertionPhase.DbDiscovery, because: "engine filtering happens during discovery");
		result.Details["found"].Should().Be(0, because: "no matching database should be discovered");
	}

	[Test]
	[Description("Should fail when connection check is requested and local DB connection fails")]
	public async Task ExecuteAsync_WhenConnectionFails_ShouldFailAtDbConnect()
	{
		// Arrange
		LocalDbServerConfiguration config = new()
		{
			DbType = "postgres",
			Hostname = "localhost",
			Port = 5432,
			Username = "postgres",
			Password = "postgres"
		};
		_settingsRepository.GetLocalDbServer("primary-postgres").Returns(config);
		_connectionTester.TestConnection(config).Returns(new ConnectionTestResult
		{
			Success = false,
			DetailedError = "Connection refused"
		});

		// Act
		AssertionResult result = await _sut.ExecuteAsync("postgres", 1, true, null, "primary-postgres");

		// Assert
		result.Status.Should().Be("fail", because: "failing local DB connection check should fail assertion");
		result.FailedAt.Should().Be(AssertionPhase.DbConnect, because: "connectivity check maps to DbConnect phase");
		result.Details["host"].Should().Be("localhost", because: "failure details should include target endpoint");
	}

	[Test]
	[Description("Should include database version in resolved output when version capability check passes")]
	public async Task ExecuteAsync_WhenVersionCheckPasses_ShouldIncludeVersionInResolved()
	{
		// Arrange
		LocalDbServerConfiguration config = new()
		{
			DbType = "postgres",
			Hostname = "localhost",
			Port = 5432,
			Username = "postgres",
			Password = "postgres"
		};
		_settingsRepository.GetLocalDbServer("primary-postgres").Returns(config);
		_capabilityChecker.CheckVersionAsync(Arg.Any<DiscoveredDatabase>(), Arg.Any<string>()).Returns(
			new CapabilityCheckResult
			{
				Success = true,
				Version = "PostgreSQL 16.2"
			});

		// Act
		AssertionResult result = await _sut.ExecuteAsync("postgres", 1, false, "version", "primary-postgres");

		// Assert
		result.Status.Should().Be("pass", because: "successful capability check should pass assertion");
		result.Scope.Should().Be(AssertionScope.Local, because: "local scope should be attached to success result");
		result.Resolved.Should().ContainKey("databases", because: "resolved databases should be present on success");
	}

	[Test]
	[Description("Should fail at DbCheck when local PostgreSQL server major version is below 16 even without explicit db-check version")]
	public async Task ExecuteAsync_WhenPostgresVersionBelow16WithoutExplicitVersionCheck_ShouldFailAtDbCheck()
	{
		// Arrange
		LocalDbServerConfiguration config = new()
		{
			DbType = "postgres",
			Hostname = "localhost",
			Port = 5432,
			Username = "postgres",
			Password = "postgres"
		};
		_settingsRepository.GetLocalDbServerNames().Returns(new[] { "primary-postgres" });
		_settingsRepository.GetLocalDbServer("primary-postgres").Returns(config);
		_capabilityChecker.CheckVersionAsync(Arg.Any<DiscoveredDatabase>(), Arg.Any<string>()).Returns(
			new CapabilityCheckResult
			{
				Success = true,
				Version = "PostgreSQL 15.10"
			});

		// Act
		AssertionResult result = await _sut.ExecuteAsync("postgres", 1, false, null, "primary-postgres");

		// Assert
		result.Status.Should().Be("fail", because: "PostgreSQL 15 does not satisfy the required version floor");
		result.FailedAt.Should().Be(AssertionPhase.DbCheck, because: "version floor validation is a capability check");
		result.Details["requiredMajorVersion"].Should().Be(16, because: "failure payload should include required major version");
	}

	[Test]
	[Description("Should pass when local PostgreSQL server major version is at least 16 without explicit db-check version")]
	public async Task ExecuteAsync_WhenPostgresVersionAtLeast16WithoutExplicitVersionCheck_ShouldPass()
	{
		// Arrange
		LocalDbServerConfiguration config = new()
		{
			DbType = "postgres",
			Hostname = "localhost",
			Port = 5432,
			Username = "postgres",
			Password = "postgres"
		};
		_settingsRepository.GetLocalDbServerNames().Returns(new[] { "primary-postgres" });
		_settingsRepository.GetLocalDbServer("primary-postgres").Returns(config);
		_capabilityChecker.CheckVersionAsync(Arg.Any<DiscoveredDatabase>(), Arg.Any<string>()).Returns(
			new CapabilityCheckResult
			{
				Success = true,
				Version = "PostgreSQL 16.3"
			});

		// Act
		AssertionResult result = await _sut.ExecuteAsync("postgres", 1, false, null, "primary-postgres");

		// Assert
		result.Status.Should().Be("pass", because: "PostgreSQL 16 satisfies the required version floor");
		result.Scope.Should().Be(AssertionScope.Local, because: "successful local assertion should report local scope");
	}

	[Test]
	[Description("Should discover matching databases across all configured local DB servers when db-server-name is omitted")]
	public async Task ExecuteAsync_WhenDbServerNameIsOmitted_ShouldDiscoverAcrossAllConfiguredServers()
	{
		// Arrange
		_settingsRepository.GetLocalDbServerNames().Returns(new[] { "local-postgres", "local-mssql" });
		_settingsRepository.GetLocalDbServer("local-postgres").Returns(new LocalDbServerConfiguration
		{
			DbType = "postgres",
			Hostname = "localhost",
			Port = 5432
		});
		_settingsRepository.GetLocalDbServer("local-mssql").Returns(new LocalDbServerConfiguration
		{
			DbType = "mssql",
			Hostname = "localhost",
			Port = 1433
		});
		_capabilityChecker.CheckVersionAsync(Arg.Any<DiscoveredDatabase>(), Arg.Any<string>()).Returns(
			new CapabilityCheckResult
			{
				Success = true,
				Version = "PostgreSQL 16.2"
			});

		// Act
		AssertionResult result = await _sut.ExecuteAsync("postgres,mssql", 2, false, null, null);

		// Assert
		result.Status.Should().Be("pass", because: "both configured engines should be discovered across local DB server configs");
		result.Resolved.Should().ContainKey("databases", because: "discovered databases should be present in resolved payload");
		var databases = result.Resolved["databases"] as System.Collections.ICollection;
		databases.Should().NotBeNull(because: "resolved database list should be emitted");
		databases.Count.Should().Be(2, because: "both configured local DB servers match requested engines");
	}

	[Test]
	[Description("Should fail discovery when db minimum cannot be satisfied across all configured local DB servers")]
	public async Task ExecuteAsync_WhenDbMinimumIsNotMetAcrossAllConfiguredServers_ShouldFailAtDiscovery()
	{
		// Arrange
		_settingsRepository.GetLocalDbServerNames().Returns(new[] { "local-postgres" });
		_settingsRepository.GetLocalDbServer("local-postgres").Returns(new LocalDbServerConfiguration
		{
			DbType = "postgres",
			Hostname = "localhost",
			Port = 5432
		});

		// Act
		AssertionResult result = await _sut.ExecuteAsync("postgres,mssql", 2, false, null, null);

		// Assert
		result.Status.Should().Be("fail", because: "requested minimum of two databases cannot be satisfied with one configured server");
		result.FailedAt.Should().Be(AssertionPhase.DbDiscovery, because: "minimum count is validated during discovery");
		result.Details["found"].Should().Be(1, because: "only one matching configured server should be discovered");
		result.Details["expected"].Should().Be(2, because: "requested db minimum should be included in failure details");
	}
}
