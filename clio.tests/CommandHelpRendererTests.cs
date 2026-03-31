using System;
using System.IO.Abstractions.TestingHelpers;
using Clio.Help;
using Clio.Tests.Command;
using Clio.Tests.Infrastructure;
using CommandLine;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests;

[TestFixture]
internal class CommandHelpRendererTests : BaseClioModuleTests {
	private string _helpDirectory;
	private CommandHelpRenderer _renderer;

	[SetUp]
	public override void Setup() {
		base.Setup();
		_helpDirectory = TestFileSystem.GetRootedPath("help");
		Parser.Default.Settings.HelpDirectory = _helpDirectory;
		_renderer = new CommandHelpRenderer(FileSystem, new CommandHelpCatalog());
	}

	[Test]
	[Description("Returns canonical command help when help is requested through an alias.")]
	public void TryRenderCommandHelp_WhenAliasRequested_ReturnsCanonicalHelp() {
		string output = _renderer.TryRenderCommandHelp("ping");

		output.Should().NotBeNullOrWhiteSpace(because: "known aliases should resolve through the shared help catalog");
		output.Should().Contain("ping-app - Verify connectivity to a Creatio environment",
			because: "alias help should render the canonical command heading");
		output.Should().Contain("clio ping-app [options]",
			because: "usage should use the canonical command name");
		output.Should().Contain("clio ping-app -e dev",
			because: "fallback examples should use the canonical command name");
	}

	[Test]
	[Description("Rewrites legacy help examples and usage lines to the canonical command name.")]
	public void TryRenderCommandHelp_WhenLegacyHelpUsesOldName_RewritesExamplesToCanonicalName() {
		FileSystem.AddDirectory(_helpDirectory);
		FileSystem.AddFile(
			System.IO.Path.Combine(_helpDirectory, "callservice.txt"),
			new MockFileData("""
USAGE
clio callservice [options]

EXAMPLES
clio callservice -e dev
"""));

		string output = _renderer.TryRenderCommandHelp("call-service");

		output.Should().Contain("clio call-service [options]",
			because: "legacy help files should be normalized to the canonical command name");
		output.Should().Contain("clio call-service -e dev",
			because: "examples should be normalized to the canonical command name");
		output.Should().NotContain("clio callservice",
			because: "legacy file names should not leak into the rendered help contract");
	}

	[Test]
	[Description("Renders the flat root help as one alphabetical canonical command list.")]
	public void RenderRootHelp_WhenCalled_UsesCanonicalAlphabeticalCommandList() {
		string output = _renderer.RenderRootHelp();

		output.Should().Contain("Commands:",
			because: "root help should render one flat top-level command list");
		output.Should().NotContain("Application Management",
			because: "root help should no longer render grouped sections");
		output.IndexOf("  activate-pkg", StringComparison.Ordinal).Should().BeLessThan(
			output.IndexOf("  add-data-binding-row", StringComparison.Ordinal),
			because: "root help should sort commands alphabetically across the whole list");
		output.Should().Contain("  ping-app",
			because: "root help should list canonical command names");
		output.Should().NotContain(Environment.NewLine + "  ping" + Environment.NewLine,
			because: "aliases should not appear as standalone top-level commands");
		output.Should().NotContain("execute-assembly-code",
			because: "hidden commands must not appear in root help");
	}

	[Test]
	[Description("Builds the grouped markdown index with canonical files and alias anchors.")]
	public void RenderCommandsMarkdown_WhenCalled_UsesCanonicalLinksAndAliasAnchors() {
		string output = _renderer.RenderCommandsMarkdown();

		output.Should().Contain("<a id=\"ping-app\"></a>",
			because: "the markdown index should include a canonical anchor for each command");
		output.Should().Contain("<a id=\"ping\"></a>",
			because: "the markdown index should preserve alias anchors for incoming links");
		output.Should().Contain("(docs/commands/ping-app.md)",
			because: "the markdown index should point to the canonical markdown file");
		output.Should().NotContain("(docs/commands/ping.md)",
			because: "alias-only markdown files should no longer be referenced");
	}
}
