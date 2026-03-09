using Clio.Command.McpServer.Tools;
using Clio.Common.Assertions;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using System.Threading.Tasks;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public sealed class AssertInfrastructureToolTests
{
	[Test]
	[Description("Returns the full infrastructure aggregate result from the MCP tool without wrapping it as generic command log output.")]
	[Category("Unit")]
	public async Task AssertInfrastructure_Should_Return_Aggregated_Result()
	{
		// Arrange
		IAssertInfrastructureAggregator aggregator = Substitute.For<IAssertInfrastructureAggregator>();
		AssertInfrastructureResult expectedResult = new(
			"partial",
			1,
			"Infrastructure assertion partial: k8=pass, local=fail, filesystem=pass, databaseCandidates=1.",
			new AssertInfrastructureSections(
				CreateResult(AssertionScope.K8, "pass"),
				CreateResult(AssertionScope.Local, "fail", AssertionPhase.DbDiscovery, "No local database"),
				CreateResult(AssertionScope.Fs, "pass")),
			[
				new AssertInfrastructureDatabaseCandidate("k8", "postgres", "pg-main", "pg.example", 5432, "PostgreSQL 16.5", true)
			]);
		aggregator.ExecuteAsync().Returns(expectedResult);
		AssertInfrastructureTool tool = new(aggregator);

		// Act
		AssertInfrastructureResult actualResult = await tool.AssertInfrastructure();

		// Assert
		actualResult.Should().BeSameAs(expectedResult,
			because: "the MCP tool should return the full structured infrastructure result produced by the aggregator");
	}

	[Test]
	[Description("Exposes the stable assert-infrastructure MCP tool name as a constant so unit and end-to-end tests use the same identifier.")]
	[Category("Unit")]
	public void AssertInfrastructureTool_Should_Expose_Stable_Tool_Name()
	{
		// Arrange

		// Act
		string toolName = AssertInfrastructureTool.AssertInfrastructureToolName;

		// Assert
		toolName.Should().Be("assert-infrastructure",
			because: "the MCP contract should keep a stable tool name that tests can reference directly");
	}

	private static AssertionResult CreateResult(
		AssertionScope scope,
		string status,
		AssertionPhase? failedAt = null,
		string reason = null)
	{
		return new AssertionResult
		{
			Status = status,
			Scope = scope,
			FailedAt = failedAt,
			Reason = reason
		};
	}
}
