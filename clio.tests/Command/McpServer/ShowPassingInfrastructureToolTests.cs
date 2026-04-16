using System.Linq;
using System.Threading.Tasks;
using Clio.Command.McpServer.Prompts;
using Clio.Command.McpServer.Tools;
using Clio.Common.Assertions;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class ShowPassingInfrastructureToolTests
{
	[Test]
	[Category("Unit")]
	[Description("Advertises the stable show-passing-infrastructure MCP tool name for test and client reuse.")]
	public void ShowPassingInfrastructure_Should_Advertise_Stable_Tool_Name()
	{
		// Arrange

		// Act
		string toolName = ShowPassingInfrastructureTool.ShowPassingInfrastructureToolName;

		// Assert
		toolName.Should().Be("show-passing-infrastructure",
			because: "the MCP contract should keep a stable passing-infrastructure tool name");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the structured passing-infrastructure result directly from the backing service.")]
	public async Task ShowPassingInfrastructure_Should_Return_Structured_Result()
	{
		// Arrange
		IPassingInfrastructureService service = Substitute.For<IPassingInfrastructureService>();
		ShowPassingInfrastructureResult expectedResult = new(
			"available",
			"Passing infrastructure available.",
			new ShowPassingInfrastructureKubernetes(
				true,
				[
					new ShowPassingInfrastructureDatabaseCandidate("k8", "postgres", "clio-postgres", "postgres.example", 5432, "PostgreSQL 16.5", null)
				],
				new ShowPassingInfrastructureRedisCandidate("k8", "clio-redis", "redis.example", 6379, 3, null)),
			new ShowPassingInfrastructureLocal([], []),
			new ShowPassingInfrastructureFilesystem(true, @"C:\inetpub\wwwroot\clio", @"BUILTIN\IIS_IUSRS", "full-control"),
			new ShowPassingInfrastructureRecommendation(
				"kubernetes",
				"postgres",
				null,
				null,
				new ShowPassingInfrastructureDeployCreatioArguments(null, null)),
			new ShowPassingInfrastructureRecommendationsByEngine(
				new ShowPassingInfrastructureRecommendation(
					"kubernetes",
					"postgres",
					null,
					null,
					new ShowPassingInfrastructureDeployCreatioArguments(null, null)),
				null));
		service.ExecuteAsync().Returns(expectedResult);
		ShowPassingInfrastructureTool tool = new(service);

		// Act
		ShowPassingInfrastructureResult actualResult = await tool.ShowPassingInfrastructure();

		// Assert
		actualResult.Should().BeSameAs(expectedResult,
			because: "the MCP tool should return the structured service result without converting it into command log output");
	}

	[Test]
	[Category("Unit")]
	[Description("Advertises show-passing-infrastructure as a read-only, idempotent MCP tool and describes the required preflight relationship with assert-infrastructure.")]
	public void ShowPassingInfrastructure_Should_Expose_ReadOnly_Metadata_And_Preflight_Guidance()
	{
		// Arrange
		var method = typeof(ShowPassingInfrastructureTool).GetMethod(nameof(ShowPassingInfrastructureTool.ShowPassingInfrastructure))!;
		var attribute = (ModelContextProtocol.Server.McpServerToolAttribute)method
			.GetCustomAttributes(typeof(ModelContextProtocol.Server.McpServerToolAttribute), false)
			.Single();
		var description = (System.ComponentModel.DescriptionAttribute)method
			.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
			.Single();

		// Act
		bool readOnly = attribute.ReadOnly;
		bool idempotent = attribute.Idempotent;
		string text = description.Description;

		// Assert
		readOnly.Should().BeTrue(
			because: "passing-infrastructure discovery should not mutate infrastructure");
		idempotent.Should().BeTrue(
			because: "re-running discovery should not change state and should expose the same semantics");
		text.Should().Contain("assert-infrastructure",
			because: "the description should tell the agent to review failing infrastructure before using passing-only recommendations");
		text.Should().Contain("deploy-creatio",
			because: "the description should explain that the result is intended to feed the deployment tool");
	}

	[Test]
	[Category("Unit")]
	[Description("Prompt guidance for show-passing-infrastructure tells the agent to run assert-infrastructure first and to use the returned recommendations for deploy-creatio.")]
	public void ShowPassingInfrastructurePrompt_Should_Mention_Preflight_Sequence()
	{
		// Arrange

		// Act
		string prompt = ShowPassingInfrastructurePrompt.Prompt();

		// Assert
		prompt.Should().Contain("assert-infrastructure",
			because: "the prompt should direct the agent to inspect the full infrastructure state first");
		prompt.Should().Contain("show-passing-infrastructure",
			because: "the prompt should call out the passing-only discovery tool by name");
		prompt.Should().Contain("deploy-creatio",
			because: "the prompt should explain that the recommendations feed the deployment tool");
	}
}
