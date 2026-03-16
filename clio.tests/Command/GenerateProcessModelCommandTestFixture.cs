using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

public class GenerateProcessModelCommandTestFixture : BaseCommandTests<GenerateProcessModelCommandOptions> {

	[Test]
	[Description("Uses initializer-backed defaults for destination path, namespace, and culture so non-CLI construction matches the command-line defaults.")]
	public void GenerateProcessModelCommandOptions_Should_Expose_Defaults() {
		// Arrange
		GenerateProcessModelCommandOptions options = new();

		// Act

		// Assert
		options.DestinationPath.Should().Be(".",
			because: "MCP-created command options should use the same destination-path default as CLI parsing");
		options.Namespace.Should().Be("AtfTIDE.ProcessModels",
			because: "MCP-created command options should use the same namespace default as CLI parsing");
		options.Culture.Should().Be("en-US",
			because: "MCP-created command options should use the same culture default as CLI parsing");
	}
}
