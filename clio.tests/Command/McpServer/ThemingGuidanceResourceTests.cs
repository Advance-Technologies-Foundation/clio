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

	[Test]
	[Description("The theming guide directs the agent to auto-apply a newly created no-code theme to the current user via set-user-theme, satisfying FR-4.")]
	public void ThemingGuidanceResource_Should_Instruct_AutoApply_After_NoCode_Create() {
		// Arrange
		ThemingGuidanceResource resource = new();

		// Act
		TextResourceContents article = resource.GetGuide().Should().BeOfType<TextResourceContents>(
			because: "the theming guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Text.Should().Contain("set-user-theme",
			because: "the guide must name the set-user-theme tool so the agent applies the theme to the current user after create-theme");
		article.Text.Should().Contain("by default",
			because: "FR-4 requires applying the new theme to the current user by default after a successful no-code create-theme");
		article.Text.Should().Contain("Skip the apply step",
			because: "FR-4 requires an explicit opt-out when the user does not want to switch themes now");
	}

	[Test]
	[Description("The theming guide keeps the global DefaultTheme change confirmation-gated and distinct from the per-user apply, so auto-apply never silently changes the theme for everyone.")]
	public void ThemingGuidanceResource_Should_Keep_DefaultTheme_Change_ConfirmationGated() {
		// Arrange
		ThemingGuidanceResource resource = new();

		// Act
		TextResourceContents article = resource.GetGuide().Should().BeOfType<TextResourceContents>(
			because: "the theming guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Text.Should().Contain("confirm before changing it",
			because: "changing the global DefaultTheme affects all users and must stay confirmation-gated, separate from the per-user apply");
	}
}
