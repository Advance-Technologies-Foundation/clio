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
	[Description("The font rules are written about build-theme, but create-theme brand mode drives the same engine and returns the same warnings on its own result. Without this bridge a no-code agent reads rules addressed to a tool it never calls, and has nowhere to apply the suppression and could-not-verify outcomes it actually receives.")]
	public void ThemingGuidanceResource_Should_Apply_The_Font_Rules_To_Brand_Mode_Too() {
		// Arrange
		ThemingGuidanceResource resource = new();

		// Act
		TextResourceContents article = resource.GetGuide().Should().BeOfType<TextResourceContents>(
			because: "the theming guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Text.Should().Contain("applies to `create-theme` brand mode too",
			because: "the font rules name build-theme throughout, so the no-code entry point needs an explicit bridge to them");
		article.Text.Should().Contain("read those warnings off the create call",
			because: "in brand mode there is no separate build step whose warnings the agent could read instead");
	}

	[Test]
	[Description("The no-code create step keeps the retry protocol the ADR's B-D3 decision leans on: the create call is not idempotent, so the guide must tell agents to pass an explicit id when a retry is possible and to confirm with list-themes before retrying after a transport timeout. The sentence was dropped once as collateral of a prose trim; this pin keeps the ADR and the shipped guidance telling the same story.")]
	public void ThemingGuidanceResource_Should_Instruct_The_Id_Retry_Protocol_For_NoCode_Create() {
		// Arrange
		ThemingGuidanceResource resource = new();

		// Act
		TextResourceContents article = resource.GetGuide().Should().BeOfType<TextResourceContents>(
			because: "the theming guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Text.Should().Contain("pass an explicit `id` whenever a retry is possible",
			because: "an omitted id leaves a transport-timeout retry free to create a duplicate theme");
		article.Text.Should().Contain("confirm with `list-themes` before retrying",
			because: "the recovery path is to look before calling again, not to call again blindly");
	}

	[Test]
	[Description("The theming guide keeps the explicit user confirmation before building with a family Google Fonts does not publish, and hands the user the catalogue search as the resolver. With --local-font-families gone, this prose is the only control for that gate.")]
	public void ThemingGuidanceResource_Should_Keep_NonGoogleFamily_Confirmation_Gated() {
		// Arrange
		ThemingGuidanceResource resource = new();

		// Act
		TextResourceContents article = resource.GetGuide().Should().BeOfType<TextResourceContents>(
			because: "the theming guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Text.Should().Contain("explicit confirmation",
			because: "an unpublished family renders only where it is installed, so the agent must not decide that for the user");
		article.Text.Should().Contain("https://fonts.google.com/?query=",
			because: "the user is the resolver for a spelling the agent could not settle, so the guide hands them the catalogue search");
	}

	[Test]
	[Description("The theming guide describes the fail-open behaviour for an unverifiable family: the import is kept, and the remedy is to restyle once connectivity is back.")]
	public void ThemingGuidanceResource_Should_Describe_The_Unverified_FailOpen() {
		// Arrange
		ThemingGuidanceResource resource = new();

		// Act
		TextResourceContents article = resource.GetGuide().Should().BeOfType<TextResourceContents>(
			because: "the theming guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Text.Should().Contain("could not verify",
			because: "the agent branches on this warning text, so the guide must name it verbatim");
		article.Text.Should().Contain("restyle once connectivity is back",
			because: "a kept import for a family that turns out to be local is fixed by rebuilding once the catalogue is reachable — the bare verb also appears in unrelated flow prose, so it would pin nothing");
	}

	[Test]
	[Description("The theming guide states the family-name contract and that a malformed name FAILS the build, unlike the availability outcomes which only warn.")]
	public void ThemingGuidanceResource_Should_State_The_FamilyName_Contract() {
		// Arrange
		ThemingGuidanceResource resource = new();

		// Act
		TextResourceContents article = resource.GetGuide().Should().BeOfType<TextResourceContents>(
			because: "the theming guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Text.Should().Contain("INVALID_FONT_FAMILY",
			because: "the agent must know which font outcome is fatal rather than advisory");
		article.Text.Should().Contain("100 characters",
			because: "the guide states the same cap the builder enforces, so the agent checks the name before probing it");
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
	[Description("The no-code flow leads with create-theme's brand mode as a single call and tells the agent not to build the CSS first — the whole point of ENG-93989 is that the stylesheet never crosses the model boundary, and a future rewrite must not quietly reinstate the two-call pipe.")]
	public void ThemingGuidanceResource_Should_Lead_NoCode_Flow_With_SingleCall_BrandMode() {
		// Arrange
		ThemingGuidanceResource resource = new();

		// Act
		TextResourceContents article = resource.GetGuide().Should().BeOfType<TextResourceContents>(
			because: "the theming guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Text.Should().Contain("1. Create with `create-theme` in brand mode — one call",
			because: "the brand mode must be step 1 of the no-code flow, stated as a single call");
		article.Text.Should().Contain("do not call `build-theme` first and do not put CSS in the conversation",
			because: "the instruction that keeps the CSS out of the agent context is the load-bearing sentence of this change");
		article.Text.Should().Contain("the server builds the CSS and creates the theme in that same call",
			because: "the agent must understand the build happens server-side inside the create call");
	}

	[Test]
	[Description("The 'Building the theme CSS' section names both entry points, so an agent reading it in isolation learns that create-theme builds server-side and does not fall back to the build-theme pipe by default.")]
	public void ThemingGuidanceResource_Should_Describe_Both_Build_Entry_Points() {
		// Arrange
		ThemingGuidanceResource resource = new();

		// Act
		TextResourceContents article = resource.GetGuide().Should().BeOfType<TextResourceContents>(
			because: "the theming guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Text.Should().Contain("It has two entry points",
			because: "the section must present create-theme and build-theme as the two ways into the same engine");
		article.Text.Should().Contain("`create-theme` builds it server-side inside the create call",
			because: "the no-code entry point must be named explicitly where an agent looks up how the CSS is produced");
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
