using Clio.Command.McpServer.Resources;
using Clio.Command.Theming;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Unit tests for the theming MCP guidance resource.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class ThemingGuidanceResourceTests {
	[Test]
	[Description("The theming guide advertises the same Creatio version floor the theme tools enforce, so bumping ThemeServiceRequirement.MinVersion cannot leave the guide advertising a stale floor.")]
	public void ThemingGuidanceResource_Should_Advertise_The_Enforced_Creatio_Version_Floor() {
		// Arrange
		ThemingGuidanceResource resource = new();

		// Act
		TextResourceContents article = resource.GetGuide().Should().BeOfType<TextResourceContents>(
			because: "the theming guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Text.Should().Contain($"Creatio {ThemeServiceRequirement.MinVersion} or later",
			because: "the guide's version constraint must state the exact floor the theme tools enforce through ThemeServiceRequirement.MinVersion");
	}
}
