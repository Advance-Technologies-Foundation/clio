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
	[Description("The branding guide routes background activation through the dedicated set-background-image tool and no longer carries the raw gallery-registration recipe (PR #928 review).")]
	public void BrandingGuidanceResource_Should_Route_Background_Activation_Through_SetBackgroundImage_Tool() {
		// Arrange
		BrandingGuidanceResource resource = new();

		// Act
		TextResourceContents article = resource.GetGuide().Should().BeOfType<TextResourceContents>(
			because: "the branding guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Text.Should().Contain("set-background-image",
			because: "the dedicated tool encapsulates the gallery registration and background activation");
		article.Text.Should().NotContain("SysImageInTag",
			because: "the gallery-registration mechanics are owned by the set-background-image tool implementation, not hand-executed from the guide");
		article.Text.Should().NotContain("CrtBackgroundConfig",
			because: "the background-configuration setting is owned by the set-background-image tool implementation");
	}

	[Test]
	[Description("The branding guide warns that applying a logo cannot be automatically reverted by clio, so the agent warns the user before writing one (PR #928 review; verified live 2026-07-21: the platform accepts an empty-value clear but no clio surface can send one for a Binary setting).")]
	public void BrandingGuidanceResource_Should_Warn_That_Logos_Cannot_Be_Automatically_Reverted() {
		// Arrange
		BrandingGuidanceResource resource = new();

		// Act
		TextResourceContents article = resource.GetGuide().Should().BeOfType<TextResourceContents>(
			because: "the branding guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Text.Should().Contain("cannot be automatically reverted",
			because: "clio has no clear affordance for Binary sys settings, so the guide must not promise a restore");
		article.Text.Should().Contain("warn the user",
			because: "the agent must get the user's go-ahead before an irreversible write");
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
	[Description("The branding guide maps the two favicon system settings so the agent can replace the browser-tab icon through the existing sys-settings surface.")]
	public void BrandingGuidanceResource_Should_Map_The_Favicon_Settings() {
		// Arrange
		BrandingGuidanceResource resource = new();

		// Act
		TextResourceContents article = resource.GetGuide().Should().BeOfType<TextResourceContents>(
			because: "the branding guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Text.Should().Contain("`FaviconImage`",
			because: "the favicon binary slot is what the agent writes the icon into");
		article.Text.Should().Contain("`UseFaviconFromSysSettings`",
			because: "the boolean gate must be enabled or the platform ignores the uploaded favicon");
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
