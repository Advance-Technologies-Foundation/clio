using System.Collections.Generic;
using System.Threading.Tasks;
using Clio.Common.Assertions;
using Clio.Common.db;
using Clio.Common.Kubernetes;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.Assertions;

[TestFixture]
public sealed class PassingInfrastructureServiceTests
{
	private IK8ContextValidator _contextValidator;
	private IK8DatabaseAssertion _k8DatabaseAssertion;
	private IK8RedisAssertion _k8RedisAssertion;
	private ILocalDatabaseAssertion _localDatabaseAssertion;
	private ILocalRedisAssertion _localRedisAssertion;
	private IFsPathAssertion _fsPathAssertion;
	private IFsPermissionAssertion _fsPermissionAssertion;
	private IKubernetesClient _kubernetesClient;
	private ISettingsRepository _settingsRepository;

	[SetUp]
	public void Setup()
	{
		_contextValidator = Substitute.For<IK8ContextValidator>();
		_k8DatabaseAssertion = Substitute.For<IK8DatabaseAssertion>();
		_k8RedisAssertion = Substitute.For<IK8RedisAssertion>();
		_localDatabaseAssertion = Substitute.For<ILocalDatabaseAssertion>();
		_localRedisAssertion = Substitute.For<ILocalRedisAssertion>();
		_fsPathAssertion = Substitute.For<IFsPathAssertion>();
		_fsPermissionAssertion = Substitute.For<IFsPermissionAssertion>();
		_kubernetesClient = Substitute.For<IKubernetesClient>();
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_settingsRepository.GetLocalDbServerNames().Returns([]);
		_settingsRepository.GetLocalRedisServerNames().Returns([]);
		_settingsRepository.GetDefaultLocalRedisServerName().Returns((string?)null);
		_settingsRepository.HasLocalRedisServersConfiguration().Returns(false);
	}

	[Test]
	[Category("Unit")]
	[Description("Prefers a fully passing Kubernetes deployment target and propagates the discovered Redis database index into the recommended deploy-creatio arguments.")]
	public async Task ExecuteAsync_Should_Recommend_Kubernetes_When_K8_Database_And_Redis_Pass()
	{
		// Arrange
		ArrangePassingFilesystem();
		ArrangePassingKubernetes();
		PassingInfrastructureService sut = CreateSut();

		// Act
		ShowPassingInfrastructureResult result = await sut.ExecuteAsync();

		// Assert
		result.Status.Should().Be("available",
			because: "passing Kubernetes infrastructure should make deployment infrastructure available");
		result.Kubernetes.IsAvailable.Should().BeTrue(
			because: "passing Kubernetes DB and Redis checks should produce an available Kubernetes deployment target");
		result.RecommendedDeployment.Should().NotBeNull(
			because: "a passing Kubernetes target should produce a deployment recommendation");
		result.RecommendedDeployment!.DeploymentMode.Should().Be("kubernetes",
			because: "Kubernetes should be preferred when both Kubernetes database and Redis are passing");
		result.RecommendedDeployment.RedisDb.Should().Be(2,
			because: "the discovered first available Redis database index should flow into the recommendation");
		result.RecommendedDeployment.DeployCreatioArguments.Db.Should().Be("pg",
			because: "PostgreSQL recommendations should be normalized to the deploy-creatio pg argument");
		result.RecommendedByEngine.Postgres.Should().NotBeNull(
			because: "the service should provide a per-engine recommendation for PostgreSQL when Kubernetes PostgreSQL is passing");
	}

	[Test]
	[Category("Unit")]
	[Description("Falls back to local passing infrastructure when Kubernetes is unavailable and prefers the configured default Redis server for deploy-creatio recommendations.")]
	public async Task ExecuteAsync_Should_Recommend_Local_When_Kubernetes_Is_Not_Available()
	{
		// Arrange
		ArrangePassingFilesystem();
		ArrangeFailingKubernetes();
		_settingsRepository.GetLocalDbServerNames().Returns(["pg-main", "sql-main"]);
		_settingsRepository.GetLocalDbServer("pg-main").Returns(new LocalDbServerConfiguration
		{
			DbType = "postgres",
			Hostname = "pg.local",
			Port = 5432
		});
		_settingsRepository.GetLocalDbServer("sql-main").Returns(new LocalDbServerConfiguration
		{
			DbType = "mssql",
			Hostname = "sql.local",
			Port = 1433
		});
		_settingsRepository.HasLocalRedisServersConfiguration().Returns(true);
		_settingsRepository.GetLocalRedisServerNames().Returns(["cache-b", "cache-a"]);
		_settingsRepository.GetDefaultLocalRedisServerName().Returns("cache-b");
		_localDatabaseAssertion.ExecuteAsync("postgres", 1, true, "version", "pg-main").Returns(Task.FromResult(
			CreateDatabaseAssertion("postgres", "pg-main", "pg.local", 5432, "PostgreSQL 16.5")));
		_localDatabaseAssertion.ExecuteAsync("mssql", 1, true, "version", "sql-main").Returns(Task.FromResult(
			CreateDatabaseAssertion("mssql", "sql-main", "sql.local", 1433, "SQL Server 2022")));
		_localRedisAssertion.ExecuteAsync(true, true, "cache-a").Returns(Task.FromResult(
			CreateRedisAssertion("cache-a", "redis-a.local", 6379, 4)));
		_localRedisAssertion.ExecuteAsync(true, true, "cache-b").Returns(Task.FromResult(
			CreateRedisAssertion("cache-b", "redis-b.local", 6380, 7)));
		PassingInfrastructureService sut = CreateSut();

		// Act
		ShowPassingInfrastructureResult result = await sut.ExecuteAsync();

		// Assert
		result.Kubernetes.IsAvailable.Should().BeFalse(
			because: "the test scenario deliberately removes Kubernetes as a passing deployment target");
		result.Local.Databases.Should().HaveCount(2,
			because: "each passing local database server configuration should be surfaced as a deployment candidate");
		result.Local.RedisServers.Should().HaveCount(2,
			because: "each passing local Redis server configuration should be surfaced as a deployment candidate");
		result.RecommendedDeployment.Should().NotBeNull(
			because: "passing local database and Redis infrastructure should still yield a deployment recommendation");
		result.RecommendedDeployment!.DeploymentMode.Should().Be("local",
			because: "local infrastructure should be recommended when Kubernetes is not fully available");
		result.RecommendedDeployment.DbServerName.Should().Be("pg-main",
			because: "the recommended local deployment should preserve the selected local DB server name");
		result.RecommendedDeployment.RedisServerName.Should().Be("cache-b",
			because: "the configured default local Redis server should be preferred");
		result.RecommendedDeployment.RedisDb.Should().Be(7,
			because: "the recommended local Redis database should come from the discovered first available DB on the selected Redis server");
		result.RecommendedByEngine.Postgres!.DbServerName.Should().Be("pg-main",
			because: "per-engine PostgreSQL recommendations should use the matching passing local database");
		result.RecommendedByEngine.Mssql!.DbServerName.Should().Be("sql-main",
			because: "per-engine MSSQL recommendations should use the matching passing local database");
	}

	[Test]
	[Category("Unit")]
	[Description("Excludes failing local database and Redis candidates from the passing-only payload while still returning the passing survivors.")]
	public async Task ExecuteAsync_Should_Exclude_Failing_Local_Candidates()
	{
		// Arrange
		ArrangePassingFilesystem();
		ArrangeFailingKubernetes();
		_settingsRepository.GetLocalDbServerNames().Returns(["pg-main", "sql-main"]);
		_settingsRepository.GetLocalDbServer("pg-main").Returns(new LocalDbServerConfiguration
		{
			DbType = "postgres",
			Hostname = "pg.local",
			Port = 5432
		});
		_settingsRepository.GetLocalDbServer("sql-main").Returns(new LocalDbServerConfiguration
		{
			DbType = "mssql",
			Hostname = "sql.local",
			Port = 1433
		});
		_settingsRepository.HasLocalRedisServersConfiguration().Returns(true);
		_settingsRepository.GetLocalRedisServerNames().Returns(["cache-a", "cache-b"]);
		_localDatabaseAssertion.ExecuteAsync("postgres", 1, true, "version", "pg-main").Returns(Task.FromResult(
			CreateDatabaseAssertion("postgres", "pg-main", "pg.local", 5432, "PostgreSQL 16.5")));
		_localDatabaseAssertion.ExecuteAsync("mssql", 1, true, "version", "sql-main").Returns(Task.FromResult(
			AssertionResult.Failure(AssertionScope.Local, AssertionPhase.DbConnect, "sql-main unavailable")));
		_localRedisAssertion.ExecuteAsync(true, true, "cache-a").Returns(Task.FromResult(
			CreateRedisAssertion("cache-a", "redis-a.local", 6379, 4)));
		_localRedisAssertion.ExecuteAsync(true, true, "cache-b").Returns(Task.FromResult(
			AssertionResult.Failure(AssertionScope.Local, AssertionPhase.RedisPing, "cache-b unavailable")));
		PassingInfrastructureService sut = CreateSut();

		// Act
		ShowPassingInfrastructureResult result = await sut.ExecuteAsync();

		// Assert
		result.Local.Databases.Should().ContainSingle(
			candidate => candidate.DbServerName == "pg-main",
			because: "only passing local database assertions should be exposed to the MCP caller");
		result.Local.Databases.Should().NotContain(
			candidate => candidate.DbServerName == "sql-main",
			because: "failing local database assertions must be excluded from the passing-only payload");
		result.Local.RedisServers.Should().ContainSingle(
			candidate => candidate.RedisServerName == "cache-a",
			because: "only passing local Redis assertions should be exposed to the MCP caller");
		result.Local.RedisServers.Should().NotContain(
			candidate => candidate.RedisServerName == "cache-b",
			because: "failing local Redis assertions must be excluded from the passing-only payload");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns unavailable when no passing infrastructure remains and omits deployment recommendations.")]
	public async Task ExecuteAsync_Should_Return_Unavailable_When_No_Passing_Infrastructure_Exists()
	{
		// Arrange
		ArrangeFailingKubernetes();
		_fsPathAssertion.Execute("iis-clio-root-path").Returns(
			AssertionResult.Failure(AssertionScope.Fs, AssertionPhase.FsPath, "Missing path"));
		_localRedisAssertion.ExecuteAsync(true, true, null).Returns(Task.FromResult(
			AssertionResult.Failure(AssertionScope.Local, AssertionPhase.RedisDiscovery, "No local redis")));
		PassingInfrastructureService sut = CreateSut();

		// Act
		ShowPassingInfrastructureResult result = await sut.ExecuteAsync();

		// Assert
		result.Status.Should().Be("unavailable",
			because: "the service should report unavailable when no passing deployment infrastructure remains");
		result.RecommendedDeployment.Should().BeNull(
			because: "without passing deployment infrastructure there should be no recommended deploy-creatio bundle");
		result.Kubernetes.IsAvailable.Should().BeFalse(
			because: "Kubernetes should not be available in the no-passing-infrastructure scenario");
		result.Local.Databases.Should().BeEmpty(
			because: "failing or absent local database infrastructure should yield no passing candidates");
		result.Local.RedisServers.Should().BeEmpty(
			because: "failing or absent local Redis infrastructure should yield no passing candidates");
	}

	[Test]
	[Category("Unit")]
	[Description("Uses the fs --all preset semantics by resolving the built-in IIS path key instead of requiring MCP callers to pass explicit filesystem arguments.")]
	public async Task ExecuteAsync_Should_Use_FsAll_Preset_Semantics()
	{
		// Arrange
		ArrangePassingFilesystem();
		ArrangeFailingKubernetes();
		PassingInfrastructureService sut = CreateSut();

		// Act
		await sut.ExecuteAsync();

		// Assert
		_fsPathAssertion.Received(1).Execute("iis-clio-root-path");
	}

	private PassingInfrastructureService CreateSut()
	{
		return new PassingInfrastructureService(
			_contextValidator,
			_k8DatabaseAssertion,
			_k8RedisAssertion,
			_localDatabaseAssertion,
			_localRedisAssertion,
			_fsPathAssertion,
			_fsPermissionAssertion,
			_kubernetesClient,
			_settingsRepository);
	}

	private void ArrangePassingKubernetes()
	{
		_contextValidator.ValidateContextAsync().Returns(Task.FromResult(AssertionResult.Success()));
		_kubernetesClient.NamespaceExistsAsync("clio-infrastructure").Returns(Task.FromResult(true));
		_k8DatabaseAssertion.ExecuteAsync("postgres,mssql", 2, true, "version", "clio-infrastructure").Returns(
			Task.FromResult(CreateDatabaseAssertion("postgres", "clio-postgres", "postgres.example", 5432, "PostgreSQL 16.5")));
		_k8RedisAssertion.ExecuteAsync(true, true, "clio-infrastructure").Returns(Task.FromResult(
			CreateRedisAssertion("clio-redis", "redis.example", 6379, 2)));
	}

	private void ArrangeFailingKubernetes()
	{
		_contextValidator.ValidateContextAsync().Returns(Task.FromResult(
			AssertionResult.Failure(AssertionScope.K8, AssertionPhase.K8Context, "Kubernetes unavailable")));
	}

	private void ArrangePassingFilesystem()
	{
		AssertionResult pathResult = AssertionResult.Success();
		pathResult.Scope = AssertionScope.Fs;
		pathResult.Resolved["path"] = @"C:\inetpub\wwwroot\clio";
		_fsPathAssertion.Execute("iis-clio-root-path").Returns(pathResult);
		_fsPermissionAssertion.Execute("iis-clio-root-path", Arg.Any<string>(), "full-control").Returns(pathResult);
	}

	private static AssertionResult CreateDatabaseAssertion(string engine, string name, string host, int port, string version)
	{
		AssertionResult result = AssertionResult.Success();
		result.Scope = AssertionScope.Local;
		result.Resolved["databases"] = new List<Dictionary<string, object>>
		{
			new()
			{
				["engine"] = engine,
				["name"] = name,
				["host"] = host,
				["port"] = port,
				["version"] = version
			}
		};
		return result;
	}

	private static AssertionResult CreateRedisAssertion(string name, string host, int port, int firstAvailableDb)
	{
		AssertionResult result = AssertionResult.Success();
		result.Scope = AssertionScope.Local;
		result.Resolved["redis"] = new RedisAssertionResolvedDto
		{
			Name = name,
			Host = host,
			Port = port,
			FirstAvailableDb = firstAvailableDb
		};
		return result;
	}
}
