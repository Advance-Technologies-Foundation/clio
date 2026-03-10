using System.Linq;
using System.Threading.Tasks;
using Clio.Command.McpServer.Prompts;
using Clio.Command.McpServer.Tools;
using Clio.Common.IIS;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public sealed class FindEmptyIisPortToolTests
{
	[Test]
	[Category("Unit")]
	[Description("Advertises the stable find-empty-iis-port MCP tool name for test and client reuse.")]
	public void FindEmptyIisPort_Should_Advertise_Stable_Tool_Name()
	{
		// Arrange

		// Act
		string toolName = FindEmptyIisPortTool.FindEmptyIisPortToolName;

		// Assert
		toolName.Should().Be("find-empty-iis-port",
			because: "the MCP contract should keep a stable IIS port discovery tool name");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the structured IIS port discovery result directly from the backing service.")]
	public async Task FindEmptyIisPort_Should_Return_Structured_Result()
	{
		// Arrange
		IAvailableIisPortService service = Substitute.For<IAvailableIisPortService>();
		FindAvailableIisPortResult expectedResult = new(
			"available",
			"Port 40095 is the first free IIS deployment port between 40000 and 42000.",
			40000,
			42000,
			40095,
			2,
			2);
		service.FindAsync(FindEmptyIisPortTool.RangeStart, FindEmptyIisPortTool.RangeEnd).Returns(expectedResult);
		FindEmptyIisPortTool tool = new(service);

		// Act
		FindAvailableIisPortResult actualResult = await tool.FindEmptyIisPort();

		// Assert
		actualResult.Should().BeSameAs(expectedResult,
			because: "the MCP tool should return the structured IIS port discovery payload without converting it into command logs");
	}

	[Test]
	[Category("Unit")]
	[Description("Advertises find-empty-iis-port as a read-only, idempotent MCP tool and describes its deploy-creatio preflight role.")]
	public void FindEmptyIisPort_Should_Expose_ReadOnly_Metadata_And_Preflight_Guidance()
	{
		// Arrange
		var method = typeof(FindEmptyIisPortTool).GetMethod(nameof(FindEmptyIisPortTool.FindEmptyIisPort))!;
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
			because: "IIS port discovery should not mutate infrastructure");
		idempotent.Should().BeTrue(
			because: "re-running the IIS port scan should not change infrastructure state");
		text.Should().Contain("deploy-creatio",
			because: "the description should explain that the result feeds deploy-creatio sitePort selection");
		text.Should().Contain("show-passing-infrastructure",
			because: "the description should keep the deployment preflight sequence visible to the agent");
	}

	[Test]
	[Category("Unit")]
	[Description("Prompt guidance for find-empty-iis-port tells the agent to run assertion and passing-infrastructure discovery before choosing a deploy-creatio sitePort.")]
	public void FindEmptyIisPortPrompt_Should_Mention_Preflight_Sequence()
	{
		// Arrange

		// Act
		string prompt = FindEmptyIisPortPrompt.Prompt();

		// Assert
		prompt.Should().Contain("assert-infrastructure",
			because: "the prompt should direct the agent to inspect the full infrastructure state first");
		prompt.Should().Contain("show-passing-infrastructure",
			because: "the prompt should direct the agent to pick passing DB and Redis targets before the port");
		prompt.Should().Contain("deploy-creatio",
			because: "the prompt should explain that the discovered port is intended for deploy-creatio");
	}
}
