using Clio.Command.McpServer;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class McpServerInstructionsTests
{
	[Test]
	[Category("Unit")]
	[Description("A request in business words about a record type's statuses is named as Creatio work, so an agent reads core-rules and routing before it answers: on a stand, \"A purchase request ends either Approved or Rejected; show them as two outcomes of the last step\" got a drawing and no tool call.")]
	public void McpServerInstructions_ShouldTreatBusinessWordingAsCreatioWork()
	{
		// Arrange
		string instructions = McpServerInstructions.Text;

		// Act / Assert
		instructions.Should().Contain("a request about a record type's statuses (their order or outcomes), stages, fields or pages is Creatio work",
			because: "the failing prompts named statuses and outcomes, never a tool, a schema or the word stage");
		instructions.Should().Contain("read both before answering it or offering a drawing",
			because: "the agent offered a diagram instead of reading the routing that leads to the object's case");
	}
}
