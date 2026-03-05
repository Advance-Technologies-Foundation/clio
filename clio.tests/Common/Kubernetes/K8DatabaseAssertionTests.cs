using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Clio.Common.Assertions;
using Clio.Common.Database;
using Clio.Common.Kubernetes;
using FluentAssertions;
using k8s.Models;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.Kubernetes;

[TestFixture]
[Category("Unit")]
public class K8DatabaseAssertionTests
{
	[Test]
	[Description("Should fail at DbCheck when discovered PostgreSQL server major version is below 16 even without explicit db-check version.")]
	public async Task ExecuteAsync_WhenPostgresVersionBelow16AndNoVersionCheckRequested_ShouldFailAtDbCheck()
	{
		// Arrange
		IK8DatabaseDiscovery discovery = Substitute.For<IK8DatabaseDiscovery>();
		IDatabaseConnectivityChecker connectivityChecker = Substitute.For<IDatabaseConnectivityChecker>();
		IDatabaseCapabilityChecker capabilityChecker = Substitute.For<IDatabaseCapabilityChecker>();
		IKubernetesClient kubernetesClient = Substitute.For<IKubernetesClient>();
		discovery.DiscoverDatabasesAsync(Arg.Any<List<DatabaseEngine>>(), "clio-infrastructure").Returns(
			[
				new DiscoveredDatabase
				{
					Engine = DatabaseEngine.Postgres,
					Name = "clio-postgres",
					Host = "clio-postgres",
					Port = 5432
				}
			]);
		kubernetesClient.GetSecretAsync("clio-infrastructure", "clio-postgres-secret")
			.Returns(Task.FromResult(new V1Secret
			{
				Data = new Dictionary<string, byte[]>
				{
					["POSTGRES_USER"] = Encoding.UTF8.GetBytes("postgres"),
					["POSTGRES_PASSWORD"] = Encoding.UTF8.GetBytes("postgres")
				}
			}));
		capabilityChecker.CheckVersionAsync(Arg.Any<DiscoveredDatabase>(), Arg.Any<string>()).Returns(
			new CapabilityCheckResult
			{
				Success = true,
				Version = "PostgreSQL 15.9"
			});
		K8DatabaseAssertion sut = new(discovery, connectivityChecker, capabilityChecker, kubernetesClient);

		// Act
		AssertionResult result = await sut.ExecuteAsync("postgres", 1, false, null, "clio-infrastructure");

		// Assert
		result.Status.Should().Be("fail", because: "PostgreSQL version policy requires major version 16 or higher");
		result.Scope.Should().Be(AssertionScope.K8, because: "k8 database assertion failures must keep k8 scope");
		result.FailedAt.Should().Be(AssertionPhase.DbCheck, because: "version floor violations are capability check failures");
		result.Details["requiredMajorVersion"].Should().Be(16, because: "minimum PostgreSQL major version must be explicit in output");
	}

	[Test]
	[Description("Should pass when discovered PostgreSQL server major version is at least 16 without explicit db-check version.")]
	public async Task ExecuteAsync_WhenPostgresVersionAtLeast16AndNoVersionCheckRequested_ShouldPass()
	{
		// Arrange
		IK8DatabaseDiscovery discovery = Substitute.For<IK8DatabaseDiscovery>();
		IDatabaseConnectivityChecker connectivityChecker = Substitute.For<IDatabaseConnectivityChecker>();
		IDatabaseCapabilityChecker capabilityChecker = Substitute.For<IDatabaseCapabilityChecker>();
		IKubernetesClient kubernetesClient = Substitute.For<IKubernetesClient>();
		discovery.DiscoverDatabasesAsync(Arg.Any<List<DatabaseEngine>>(), "clio-infrastructure").Returns(
			[
				new DiscoveredDatabase
				{
					Engine = DatabaseEngine.Postgres,
					Name = "clio-postgres",
					Host = "clio-postgres",
					Port = 5432
				}
			]);
		kubernetesClient.GetSecretAsync("clio-infrastructure", "clio-postgres-secret")
			.Returns(Task.FromResult(new V1Secret
			{
				Data = new Dictionary<string, byte[]>
				{
					["POSTGRES_USER"] = Encoding.UTF8.GetBytes("postgres"),
					["POSTGRES_PASSWORD"] = Encoding.UTF8.GetBytes("postgres")
				}
			}));
		capabilityChecker.CheckVersionAsync(Arg.Any<DiscoveredDatabase>(), Arg.Any<string>()).Returns(
			new CapabilityCheckResult
			{
				Success = true,
				Version = "PostgreSQL 16.4"
			});
		K8DatabaseAssertion sut = new(discovery, connectivityChecker, capabilityChecker, kubernetesClient);

		// Act
		AssertionResult result = await sut.ExecuteAsync("postgres", 1, false, null, "clio-infrastructure");

		// Assert
		result.Status.Should().Be("pass", because: "supported PostgreSQL major versions must pass floor validation");
		result.Resolved.Should().ContainKey("databases", because: "successful assertion should include discovered databases");
	}

	[Test]
	[Description("Should include version in resolved output when explicit db-check version is requested.")]
	public async Task ExecuteAsync_WhenVersionCheckRequested_ShouldIncludeVersionInResolved()
	{
		// Arrange
		IK8DatabaseDiscovery discovery = Substitute.For<IK8DatabaseDiscovery>();
		IDatabaseConnectivityChecker connectivityChecker = Substitute.For<IDatabaseConnectivityChecker>();
		IDatabaseCapabilityChecker capabilityChecker = Substitute.For<IDatabaseCapabilityChecker>();
		IKubernetesClient kubernetesClient = Substitute.For<IKubernetesClient>();
		discovery.DiscoverDatabasesAsync(Arg.Any<List<DatabaseEngine>>(), "clio-infrastructure").Returns(
			[
				new DiscoveredDatabase
				{
					Engine = DatabaseEngine.Postgres,
					Name = "clio-postgres",
					Host = "clio-postgres",
					Port = 5432
				}
			]);
		kubernetesClient.GetSecretAsync("clio-infrastructure", "clio-postgres-secret")
			.Returns(Task.FromResult(new V1Secret
			{
				Data = new Dictionary<string, byte[]>
				{
					["POSTGRES_USER"] = Encoding.UTF8.GetBytes("postgres"),
					["POSTGRES_PASSWORD"] = Encoding.UTF8.GetBytes("postgres")
				}
			}));
		capabilityChecker.CheckVersionAsync(Arg.Any<DiscoveredDatabase>(), Arg.Any<string>()).Returns(
			new CapabilityCheckResult
			{
				Success = true,
				Version = "PostgreSQL 16.2"
			});
		K8DatabaseAssertion sut = new(discovery, connectivityChecker, capabilityChecker, kubernetesClient);

		// Act
		AssertionResult result = await sut.ExecuteAsync("postgres", 1, false, "version", "clio-infrastructure");

		// Assert
		result.Status.Should().Be("pass", because: "successful explicit version check should pass assertion");
		List<Dictionary<string, object>> databases = result.Resolved["databases"] as List<Dictionary<string, object>>;
		databases.Should().NotBeNull(because: "resolved payload should expose discovered databases");
		databases![0].Should().ContainKey("version", because: "explicit db-check version should include version in resolved output");
		databases[0]["version"].Should().Be("PostgreSQL 16.2", because: "reported version should come from capability checker");
	}
}
