using System;
using Clio.Command;
using Clio.Help;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class CommandHelpRendererFeatureToggleTests {

	private IFeatureToggleService _featureToggleService;
	private CommandHelpCatalog _catalog;

	[SetUp]
	public void SetUp() {
		_featureToggleService = Substitute.For<IFeatureToggleService>();
		_featureToggleService.IsEnabled(Arg.Any<Type>()).Returns(true);
		_catalog = new CommandHelpCatalog();
	}

	private CommandHelpRenderer CreateRenderer() =>
		new(new System.IO.Abstractions.FileSystem(), _catalog, _featureToggleService, () => false);

	[Test]
	[Description("Runtime root help omits a command whose feature flag is reported disabled by the service.")]
	public void RenderRootHelp_ShouldOmitCommand_WhenFeatureDisabledInRuntimeMode() {
		// Arrange
		_featureToggleService.IsEnabled(typeof(RegAppOptions)).Returns(false);
		CommandHelpRenderer renderer = CreateRenderer();

		// Act
		string help = renderer.RenderRootHelp(RootHelpRenderMode.Runtime);

		// Assert (match the row prefix to avoid matching substrings such as "unreg-web-app")
		help.Should().NotContain("  reg-web-app ",
			because: "a command whose feature flag is off must be absent from the runtime root help listing");
	}

	[Test]
	[Description("Runtime root help still lists a command when its feature flag is reported enabled.")]
	public void RenderRootHelp_ShouldListCommand_WhenFeatureEnabledInRuntimeMode() {
		// Arrange
		CommandHelpRenderer renderer = CreateRenderer();

		// Act
		string help = renderer.RenderRootHelp(RootHelpRenderMode.Runtime);

		// Assert
		help.Should().Contain("  reg-web-app ",
			because: "an enabled command must remain visible in the runtime root help listing");
	}

	[Test]
	[Description("Export root help omits a command whose feature flag is reported disabled so gated commands are not advertised in committed docs.")]
	public void RenderRootHelp_ShouldOmitCommand_WhenFeatureDisabledInExportMode() {
		// Arrange
		_featureToggleService.IsEnabled(typeof(RegAppOptions)).Returns(false);
		CommandHelpRenderer renderer = CreateRenderer();

		// Act
		string help = renderer.RenderRootHelp(RootHelpRenderMode.Export);

		// Assert
		help.Should().NotContain("  reg-web-app ",
			because: "a gated-off command must not be advertised in generated public help artifacts");
	}

	[Test]
	[Description("Export root help still lists a command whose feature flag is enabled.")]
	public void RenderRootHelp_ShouldListCommand_WhenFeatureEnabledInExportMode() {
		// Arrange
		CommandHelpRenderer renderer = CreateRenderer();

		// Act
		string help = renderer.RenderRootHelp(RootHelpRenderMode.Export);

		// Assert
		help.Should().Contain("  reg-web-app ",
			because: "an enabled (or ungated) command must remain present in generated public help artifacts");
	}

	[Test]
	[Description("RenderCommandsMarkdown omits a gated-off command's index entry from the committed Commands.md.")]
	public void RenderCommandsMarkdown_ShouldOmitCommand_WhenFeatureDisabled() {
		// Arrange
		_featureToggleService.IsEnabled(typeof(RegAppOptions)).Returns(false);
		CommandHelpRenderer renderer = CreateRenderer();

		// Act
		string markdown = renderer.RenderCommandsMarkdown();

		// Assert
		markdown.Should().NotContain("docs/commands/reg-web-app.md",
			because: "a gated-off command must not be linked from the generated Commands.md index");
	}

	[Test]
	[Description("RenderWikiAnchors omits a gated-off command's anchor mapping from the committed WikiAnchors.txt.")]
	public void RenderWikiAnchors_ShouldOmitCommand_WhenFeatureDisabled() {
		// Arrange
		_featureToggleService.IsEnabled(typeof(RegAppOptions)).Returns(false);
		CommandHelpRenderer renderer = CreateRenderer();

		// Act
		string anchors = renderer.RenderWikiAnchors();
		string[] lines = anchors.Split('\n');

		// Assert (match the canonical anchor at line start to avoid matching "unreg-web-app:")
		lines.Should().NotContain(line => line.StartsWith("reg-web-app:", StringComparison.Ordinal),
			because: "a gated-off command must not produce a wiki anchor mapping in generated artifacts");
	}

	[Test]
	[Description("Runtime root help does not throw when every command is gated off, returning a header-only listing.")]
	public void RenderRootHelp_ShouldNotThrow_WhenAllCommandsGatedOff() {
		// Arrange
		_featureToggleService.IsEnabled(Arg.Any<Type>()).Returns(false);
		CommandHelpRenderer renderer = CreateRenderer();

		// Act
		Action act = () => renderer.RenderRootHelp(RootHelpRenderMode.Runtime);

		// Assert
		act.Should().NotThrow(
			because: "an empty visible-command set must use a safe default column width instead of throwing on Max");
	}

	[Test]
	[Description("TryRenderCommandHelp returns null for a gated command so it is indistinguishable from a typo.")]
	public void TryRenderCommandHelp_ShouldReturnNull_WhenFeatureDisabled() {
		// Arrange
		_featureToggleService.IsEnabled(typeof(RegAppOptions)).Returns(false);
		CommandHelpRenderer renderer = CreateRenderer();

		// Act
		string help = renderer.TryRenderCommandHelp("reg-web-app");

		// Assert
		help.Should().BeNull(
			because: "a gated-off command must behave exactly like an unknown verb in per-command help");
	}

	[Test]
	[Description("TryRenderCommandHelp renders the command page when its feature flag is enabled.")]
	public void TryRenderCommandHelp_ShouldRenderHelp_WhenFeatureEnabled() {
		// Arrange
		CommandHelpRenderer renderer = CreateRenderer();

		// Act
		string help = renderer.TryRenderCommandHelp("reg-web-app");

		// Assert
		help.Should().NotBeNull(
			because: "an enabled command must still render its per-command help page");
	}
}
