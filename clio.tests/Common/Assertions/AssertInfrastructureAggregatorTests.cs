using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Clio.Common.Assertions;
using Clio.Common.Kubernetes;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.Assertions;

[TestFixture]
[Property("Module", "Common")]
public sealed class AssertInfrastructureAggregatorTests
{
	private IK8ContextValidator _contextValidator;
	private IK8DatabaseAssertion _k8DatabaseAssertion;
	private IK8RedisAssertion _k8RedisAssertion;
	private IFsPathAssertion _fsPathAssertion;
	private IFsPermissionAssertion _fsPermissionAssertion;
	private ILocalDatabaseAssertion _localDatabaseAssertion;
	private ILocalRedisAssertion _localRedisAssertion;
	private IKubernetesClient _kubernetesClient;

	[SetUp]
	public void Setup()
	{
		_contextValidator = Substitute.For<IK8ContextValidator>();
		_k8DatabaseAssertion = Substitute.For<IK8DatabaseAssertion>();
		_k8RedisAssertion = Substitute.For<IK8RedisAssertion>();
		_fsPathAssertion = Substitute.For<IFsPathAssertion>();
		_fsPermissionAssertion = Substitute.For<IFsPermissionAssertion>();
		_localDatabaseAssertion = Substitute.For<ILocalDatabaseAssertion>();
		_localRedisAssertion = Substitute.For<ILocalRedisAssertion>();
		_kubernetesClient = Substitute.For<IKubernetesClient>();
	}

	[Test]
	[Description("Returns pass when Kubernetes, local infrastructure, and filesystem assertions all succeed and databases are available for deployment selection.")]
	[Category("Unit")]
	public async Task ExecuteAsync_Should_Return_Pass_When_All_Sections_Succeed()
	{
		// Arrange
		ArrangePassingSections();
		AssertInfrastructureAggregator sut = CreateSut();

		// Act
		AssertInfrastructureResult result = await sut.ExecuteAsync();

		// Assert
		result.Status.Should().Be("pass",
			because: "the full sweep should pass when every infrastructure section succeeds");
		result.ExitCode.Should().Be(0,
			because: "the aggregate exit code should remain zero when every section passes");
		result.Sections.K8.Status.Should().Be("pass",
			because: "the Kubernetes section should preserve its successful assertion result");
		result.Sections.Local.Status.Should().Be("pass",
			because: "the local section should preserve its successful assertion result");
		result.Sections.Filesystem.Status.Should().Be("pass",
			because: "the filesystem section should preserve its successful assertion result");
		result.DatabaseCandidates.Should().HaveCount(2,
			because: "successful K8 and local sections should both contribute database candidates");
		result.DatabaseCandidates.Should().Contain(candidate => candidate.Source == "k8" && candidate.IsConnectable == true,
			because: "successful full-sweep K8 candidates should record that connectivity was validated");
		result.DatabaseCandidates.Should().Contain(candidate => candidate.Source == "local" && candidate.IsConnectable == true,
			because: "successful full-sweep local candidates should record that connectivity was validated");
	}

	[Test]
	[Description("Returns partial when one section fails but at least one successful section still contributes deployable database candidates.")]
	[Category("Unit")]
	public async Task ExecuteAsync_Should_Return_Partial_When_One_Section_Fails_And_Databases_Remain_Available()
	{
		// Arrange
		ArrangePassingSections();
		_localRedisAssertion.ExecuteAsync(true, true, null).Returns(Task.FromResult(
			AssertionResult.Failure(AssertionScope.Local, AssertionPhase.RedisDiscovery, "Local redis unavailable")));
		AssertInfrastructureAggregator sut = CreateSut();

		// Act
		AssertInfrastructureResult result = await sut.ExecuteAsync();

		// Assert
		result.Status.Should().Be("partial",
			because: "the full sweep should report partial when at least one section passes and database inventory remains available");
		result.ExitCode.Should().Be(1,
			because: "the aggregate exit code should indicate a failed section even when the result is partial");
		result.Sections.Local.Status.Should().Be("fail",
			because: "the local section should preserve the first failing local assertion result");
		result.Sections.K8.Status.Should().Be("pass",
			because: "a local failure must not stop the Kubernetes section from running");
		result.Sections.Filesystem.Status.Should().Be("pass",
			because: "a local failure must not stop the filesystem section from running");
		result.DatabaseCandidates.Should().ContainSingle(candidate => candidate.Source == "k8",
			because: "only successful sections should contribute database candidates");
	}

	[Test]
	[Description("Returns fail when all sections fail or no successful database inventory remains for deployment selection.")]
	[Category("Unit")]
	public async Task ExecuteAsync_Should_Return_Fail_When_No_Database_Candidates_Are_Available()
	{
		// Arrange
		_contextValidator.ValidateContextAsync(null, null, null, null).Returns(Task.FromResult(
			AssertionResult.Failure(AssertionScope.K8, AssertionPhase.K8Context, "K8 unavailable")));
		_localDatabaseAssertion.ExecuteAsync("postgres,mssql", 1, true, "version", null).Returns(Task.FromResult(
			AssertionResult.Failure(AssertionScope.Local, AssertionPhase.DbDiscovery, "No local databases")));
		_fsPathAssertion.Execute("iis-clio-root-path").Returns(
			AssertionResult.Failure(AssertionScope.Fs, AssertionPhase.FsPath, "Missing path"));
		AssertInfrastructureAggregator sut = CreateSut();

		// Act
		AssertInfrastructureResult result = await sut.ExecuteAsync();

		// Assert
		result.Status.Should().Be("fail",
			because: "the full sweep should fail when no successful database inventory remains");
		result.ExitCode.Should().Be(1,
			because: "a failed aggregate sweep should report a failing exit code");
		result.DatabaseCandidates.Should().BeEmpty(
			because: "failed K8 and local sections must not contribute unusable database candidates");
	}

	[Test]
	[Description("Converts unexpected section exceptions into section-level failures so the full sweep still returns all sections instead of crashing.")]
	[Category("Unit")]
	public async Task ExecuteAsync_Should_Capture_Unexpected_Section_Exceptions_As_Section_Failures()
	{
		// Arrange
		ArrangePassingSections();
		_fsPathAssertion.Execute("iis-clio-root-path").Returns(_ => throw new InvalidOperationException("filesystem exploded"));
		AssertInfrastructureAggregator sut = CreateSut();

		// Act
		AssertInfrastructureResult result = await sut.ExecuteAsync();

		// Assert
		result.Sections.Filesystem.Status.Should().Be("fail",
			because: "unexpected filesystem exceptions should be converted into a failed filesystem section result");
		result.Sections.Filesystem.Reason.Should().Contain("filesystem exploded",
			because: "the section-level failure should preserve the unexpected exception message for diagnostics");
		result.Sections.K8.Status.Should().Be("pass",
			because: "an unexpected filesystem exception must not erase successful Kubernetes results");
		result.Sections.Local.Status.Should().Be("pass",
			because: "an unexpected filesystem exception must not erase successful local results");
	}

	[Test]
	[Description("Preserves fs --all semantics by resolving the built-in IIS path key instead of requiring explicit filesystem arguments.")]
	[Category("Unit")]
	public async Task ExecuteAsync_Should_Use_FsAll_Preset_Semantics()
	{
		// Arrange
		ArrangePassingSections();
		AssertInfrastructureAggregator sut = CreateSut();

		// Act
		await sut.ExecuteAsync();

		// Assert
		_fsPathAssertion.Received(1).Execute("iis-clio-root-path");
	}

	private void ArrangePassingSections()
	{
		AssertionResult k8Context = AssertionResult.Success();
		k8Context.Context["name"] = "dev-cluster";
		k8Context.Context["namespace"] = "default";
		_contextValidator.ValidateContextAsync(null, null, null, null).Returns(Task.FromResult(k8Context));
		_kubernetesClient.NamespaceExistsAsync("clio-infrastructure").Returns(Task.FromResult(true));

		AssertionResult k8Database = AssertionResult.Success();
		k8Database.Resolved["databases"] = new List<Dictionary<string, object>>
		{
			new()
			{
				["engine"] = "postgres",
				["name"] = "k8-postgres",
				["host"] = "postgres.example",
				["port"] = 5432,
				["version"] = "PostgreSQL 16.5"
			}
		};
		_k8DatabaseAssertion.ExecuteAsync("postgres,mssql", 2, true, "version", "clio-infrastructure")
			.Returns(Task.FromResult(k8Database));

		AssertionResult k8Redis = AssertionResult.Success();
		k8Redis.Resolved["redis"] = new Dictionary<string, object> { ["host"] = "redis.example", ["port"] = 6379 };
		_k8RedisAssertion.ExecuteAsync(true, true, "clio-infrastructure")
			.Returns(Task.FromResult(k8Redis));

		AssertionResult localDatabase = AssertionResult.Success();
		localDatabase.Scope = AssertionScope.Local;
		localDatabase.Resolved["databases"] = new List<Dictionary<string, object>>
		{
			new()
			{
				["engine"] = "mssql",
				["name"] = "local-mssql",
				["host"] = "localhost",
				["port"] = 1433,
				["version"] = "SQL Server 2022"
			}
		};
		_localDatabaseAssertion.ExecuteAsync("postgres,mssql", 1, true, "version", null)
			.Returns(Task.FromResult(localDatabase));

		AssertionResult localRedis = AssertionResult.Success();
		localRedis.Scope = AssertionScope.Local;
		localRedis.Resolved["redis"] = new Dictionary<string, object> { ["host"] = "localhost", ["port"] = 6379 };
		_localRedisAssertion.ExecuteAsync(true, true, null).Returns(Task.FromResult(localRedis));

		AssertionResult filesystem = AssertionResult.Success();
		filesystem.Scope = AssertionScope.Fs;
		filesystem.Details["requestedPath"] = "iis-clio-root-path";
		filesystem.Resolved["path"] = @"C:\inetpub\wwwroot\clio";
		_fsPathAssertion.Execute("iis-clio-root-path").Returns(filesystem);
		_fsPermissionAssertion.Execute("iis-clio-root-path", Arg.Any<string>(), "full-control")
			.Returns(filesystem);
	}

	private AssertInfrastructureAggregator CreateSut()
	{
		return new AssertInfrastructureAggregator(
			_contextValidator,
			_k8DatabaseAssertion,
			_k8RedisAssertion,
			_fsPathAssertion,
			_fsPermissionAssertion,
			_localDatabaseAssertion,
			_localRedisAssertion,
			_kubernetesClient);
	}
}
