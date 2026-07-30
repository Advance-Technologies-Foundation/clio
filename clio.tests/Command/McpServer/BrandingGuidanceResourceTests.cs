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
	[Description("The branding guide routes all logo work through the set-logo tool and maps every argument — the all-slots shortcut, the three light-background slots, and the dark-background toolbar slot — so dropping any of them from the guide fails this test.")]
	public void BrandingGuidanceResource_Should_Route_Logos_Through_SetLogo_And_Map_Every_Slot() {
		// Arrange
		BrandingGuidanceResource resource = new();

		// Act
		TextResourceContents article = resource.GetGuide().Should().BeOfType<TextResourceContents>(
			because: "the branding guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Text.Should().Contain("set-logo",
			because: "the dedicated tool owns the logo apply-and-bind flow");
		article.Text.Should().Contain("`logo`",
			because: "the all-slots shortcut is what makes branding every slot a single call, and an agent that does not know it exists falls back to four");
		article.Text.Should().Contain("`login-logo`",
			because: "the login-page logo slot is part of the acceptance criterion");
		article.Text.Should().Contain("`menu-logo`",
			because: "the main-menu logo slot is part of the acceptance criterion");
		article.Text.Should().NotContain("header-logo",
			because: "the slot set is the canonical four (login, menu, configuration, dark toolbar); a header-logo slot does not exist on the tool");
		article.Text.Should().Contain("`configuration-logo`",
			because: "the configuration-section logo slot is part of the acceptance criterion");
		article.Text.Should().Contain("`dark-logo`",
			because: "the dark-background (Freedom UI top panel) logo slot is part of the acceptance criterion");
		article.Text.Should().Contain("splash-screen logo automatically",
			because: "the tool suppresses the stock splash logo itself, so the guide must not send the agent to do it by hand");
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
	[Description("The branding guide carries the package-delivery contract of the apply tools: both take a package argument, fall back to the environment's CurrentPackageId when it is omitted, and their warnings are the delivery-gap channel to relay.")]
	public void BrandingGuidanceResource_Should_Describe_Package_Delivery_Through_The_Apply_Tools() {
		// Arrange
		BrandingGuidanceResource resource = new();

		// Act
		TextResourceContents article = resource.GetGuide().Should().BeOfType<TextResourceContents>(
			because: "the branding guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Text.Should().Contain("`package` argument",
			because: "the guide must say the apply tools themselves bind the branding into a package");
		article.Text.Should().Contain("CurrentPackageId",
			because: "omitting the package delivers into the environment's current package, and an agent that does not know that cannot tell the user where the branding will land");
		article.Text.Should().NotContain("bind-branding",
			because: "the standalone bind step no longer exists; naming it would send the agent to a tool that is not there");
		article.Text.Should().Contain("`warnings`",
			because: "the warnings are the only place a delivery gap is reported, and the guide must name the exact result field so the agent relays it instead of hunting for a channel called something else");
	}

	[Test]
	[Description("The branding guide carries the package-notification contract so the agent tells the user which package the branding data is added to (ENG-93848 acceptance criterion 1).")]
	public void BrandingGuidanceResource_Should_Require_Naming_The_Target_Package() {
		// Arrange
		BrandingGuidanceResource resource = new();

		// Act
		TextResourceContents article = resource.GetGuide().Should().BeOfType<TextResourceContents>(
			because: "the branding guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Text.Should().Contain("which package",
			because: "the agent must tell the user which package the new branding data will be added to");
	}

	[Test]
	[Description("The branding guide states that an unbranded logo slot is never delivered, so the package cannot overwrite the target's own logo with this environment's stock value.")]
	public void BrandingGuidanceResource_Should_State_That_Unbranded_Slots_Never_Ship() {
		// Arrange
		BrandingGuidanceResource resource = new();

		// Act
		TextResourceContents article = resource.GetGuide().Should().BeOfType<TextResourceContents>(
			because: "the branding guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Text.Should().Contain("a slot nobody branded stays out of the package",
			because: "shipping a slot the user never branded would replace an install target's own logo with this environment's image");
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
