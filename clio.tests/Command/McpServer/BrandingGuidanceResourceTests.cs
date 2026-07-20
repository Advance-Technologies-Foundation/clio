using Clio.Command.McpServer.Resources;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Unit tests for the branding MCP guidance resource.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class BrandingGuidanceResourceTests {
	[Test]
	[Description("The branding guide routes the theme part of branding (colours, fonts, custom themes) to the theming guide instead of duplicating it.")]
	public void BrandingGuidanceResource_Should_Route_Theme_Work_To_The_Theming_Guide() {
		// Arrange
		BrandingGuidanceResource resource = new();

		// Act
		TextResourceContents article = resource.GetGuide().Should().BeOfType<TextResourceContents>(
			because: "the branding guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Text.Should().Contain("get-guidance name=theming",
			because: "the theme part of branding is owned by the theming guide and the branding guide must route there, not restate it");
		article.Text.Should().NotContain("build-theme",
			because: "theme-building mechanics belong to the theming guide and must not be duplicated in the branding guide");
	}

	[Test]
	[Description("The branding guide routes the background image upload through the dedicated upload-image tool and does not carry the raw image-API recipe.")]
	public void BrandingGuidanceResource_Should_Route_Background_Upload_Through_UploadImage_Tool() {
		// Arrange
		BrandingGuidanceResource resource = new();

		// Act
		TextResourceContents article = resource.GetGuide().Should().BeOfType<TextResourceContents>(
			because: "the branding guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Text.Should().Contain("upload-image",
			because: "the SysImage binary cannot be written through OData JSON, so the guide must route the upload to the dedicated tool");
		article.Text.Should().NotContain("ImageAPIService",
			because: "the raw image-API endpoint, query literals, and headers are owned by the upload-image tool implementation, not hand-executed from the guide");
		article.Text.Should().NotContain("Content-Range",
			because: "the chunk-header mechanics are owned by the upload-image tool implementation");
	}

	[Test]
	[Description("The branding guide maps every logo slot of the acceptance criterion — all four Binary sys settings plus the splash and underlay companions — so dropping any slot from the guide fails this test.")]
	public void BrandingGuidanceResource_Should_Map_All_Four_Logo_Slots_And_Companion_Settings() {
		// Arrange
		BrandingGuidanceResource resource = new();

		// Act
		TextResourceContents article = resource.GetGuide().Should().BeOfType<TextResourceContents>(
			because: "the branding guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Text.Should().Contain("`LogoImage`",
			because: "the login-page logo slot is part of the ENG-92981 acceptance criterion");
		article.Text.Should().Contain("`MenuLogoImage`",
			because: "the main-menu logo slot is part of the ENG-92981 acceptance criterion");
		article.Text.Should().Contain("`ConfigurationPageLogoImage`",
			because: "the configuration-section logo slot is part of the ENG-92981 acceptance criterion");
		article.Text.Should().Contain("`CrtAppToolbarLogo`",
			because: "the Freedom UI top-panel logo slot is part of the ENG-92981 acceptance criterion");
		article.Text.Should().Contain("HideSplashScreenLogoImage",
			because: "applying logos must also hide the stock splash logo");
		article.Text.Should().Contain("CrtAppToolbarLogoUnderlayColor",
			because: "the agent must know the underlay-color setting exists but change it only on explicit request");
	}

	[Test]
	[Description("The branding guide states the provenance and stability of the shell-background SysImageTag id and gives a lookup fallback, so the literal is never trusted blindly (PR #928 review).")]
	public void BrandingGuidanceResource_Should_State_ShellBackground_Tag_Provenance() {
		// Arrange
		BrandingGuidanceResource resource = new();

		// Act
		TextResourceContents article = resource.GetGuide().Should().BeOfType<TextResourceContents>(
			because: "the branding guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Text.Should().Contain("273C2402-7CAE-456B-A9C4-067D2024F1A7",
			because: "the platform-seeded shell-background tag id is the one literal the registration step needs");
		article.Text.Should().Contain("shell_background",
			because: "the guide must name the SysImageTag record so the id's provenance is stated inline");
		article.Text.Should().Contain("same id on every installation",
			because: "the stability guarantee must be stated inline so a future edit does not silently drop the context");
	}

	[Test]
	[Description("The branding guide gates all branding writes on the CanCustomizeBranding license via check-theming-access.")]
	public void BrandingGuidanceResource_Should_Gate_Writes_On_The_Branding_License() {
		// Arrange
		BrandingGuidanceResource resource = new();

		// Act
		TextResourceContents article = resource.GetGuide().Should().BeOfType<TextResourceContents>(
			because: "the branding guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Text.Should().Contain("check-theming-access",
			because: "the agent must check the branding license up front with the existing access tool");
		article.Text.Should().Contain("CanCustomizeBranding",
			because: "the branding license is the gate for every branding write");
	}

	[Test]
	[Description("The routing map carries a branding row so an agent asked to change logos or the shell background is routed to the branding guide.")]
	public void RoutingGuidanceResource_Should_Route_Branding_Assets_To_The_Branding_Guide() {
		// Arrange
		RoutingGuidanceResource resource = new();

		// Act
		TextResourceContents routing = resource.GetGuide().Should().BeOfType<TextResourceContents>(
			because: "routing guidance should remain a plain-text resource").Subject;

		// Assert
		routing.Text.Should().Contain("name=branding",
			because: "the routing map must direct logo / shell-background work to the branding guide");
		routing.Text.Should().Contain("shell background",
			because: "the branding routing row must be keyed to the task wording an agent will see");
	}
}
