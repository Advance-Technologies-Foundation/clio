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
	private CommandHelpRenderer _exportRenderer;

	[SetUp]
	public override void Setup() {
		base.Setup();
		_helpDirectory = TestFileSystem.GetRootedPath("help");
		Parser.Default.Settings.HelpDirectory = _helpDirectory;
		_exportRenderer = CreateRenderer(() => false);
	}

	[Test]
	[Description("Returns canonical command help when help is requested through an alias.")]
	public void TryRenderCommandHelp_WhenAliasRequested_ReturnsCanonicalHelp() {
		string output = _exportRenderer.TryRenderCommandHelp("ping");

		output.Should().NotBeNullOrWhiteSpace(because: "known aliases should resolve through the shared help catalog");
		output.Should().Contain("ping-app - Verify connectivity to a Creatio environment",
			because: "alias help should render the canonical command heading");
		output.Should().Contain("clio ping-app [options]",
			because: "usage should use the canonical command name");
		output.Should().Contain("clio ping-app -e dev",
			because: "fallback examples should use the canonical command name");
	}

	[Test]
	[Description("Preserves manual custom sections when help is requested through an alias.")]
	public void TryRenderCommandHelp_WhenAliasTargetsManualHelp_PreservesManualSections() {
		FileSystem.AddDirectory(_helpDirectory);
		FileSystem.AddFile(
			System.IO.Path.Combine(_helpDirectory, "ping-app.txt"),
			new MockFileData("""
NAME
    ping-app - Manual ping help

DETAIL COLLECTIONS
    This line proves the raw manual file is returned.
"""));

		string output = _exportRenderer.TryRenderCommandHelp("ping");

		output.Should().Contain("ping-app - Manual ping help",
			because: "alias lookups should resolve to the canonical manual help file");
		output.Should().Contain("DETAIL COLLECTIONS",
			because: "manual custom sections should remain visible in runtime help");
	}

	[Test]
	[Description("Appends generated syntax sections when a manual help file omits arguments and options.")]
	public void TryRenderCommandHelp_WhenManualHelpOmitsSyntaxSections_AppendsGeneratedFallback() {
		FileSystem.AddDirectory(_helpDirectory);
		FileSystem.AddFile(
			System.IO.Path.Combine(_helpDirectory, "set-pkg-version.txt"),
			new MockFileData("""
COMMAND TYPE
    Service commands

NAME
    set-pkg-version - set package version

DESCRIPTION
    Set specified package version into descriptor.json by specified package path.

EXAMPLE
    clio set-pkg-version <PACKAGE PATH> -v <PACKAGE VERSION>
"""));

		string output = _exportRenderer.TryRenderCommandHelp("set-pkg-version");

		output.Should().Contain("COMMAND TYPE",
			because: "manual sections should still be preserved for runtime help");
		output.Should().Contain("ARGUMENTS",
			because: "missing syntax sections should be filled from command metadata");
		output.Should().Contain("PackagePath",
			because: "required positional arguments should be reflected when the manual file does not define them");
		output.Should().Contain("OPTIONS",
			because: "missing options should be appended from the option attributes");
		output.Should().Contain("-v, --PackageVersion <VALUE>",
			because: "reflected option metadata should surface the required package version switch");
	}

	[Test]
	[Description("Renders export root help as one alphabetical canonical command list with one physical row per command.")]
	public void RenderRootHelp_WhenExported_UsesCanonicalAlphabeticalCommandList() {
		string output = _exportRenderer.RenderRootHelp(RootHelpRenderMode.Export);

		output.Should().Contain("Commands:",
			because: "root help should render one flat top-level command list");
		output.Should().NotContain("Application Management",
			because: "root help should no longer render grouped sections");
		output.IndexOf("  activate-pkg", StringComparison.Ordinal).Should().BeLessThan(
			output.IndexOf("  add-data-binding-row", StringComparison.Ordinal),
			because: "root help should sort commands alphabetically across the whole list");
		output.Should().Contain("Register a Creatio environment, cfg, reg",
			because: "export help should keep aliases on the same description line without extra labels");
		output.Should().Contain("List registered Creatio environments, env, envs, show-web-app",
			because: "long alias groups should stay attached to the command description in export help");
		output.Should().Contain("Compare file content across web farm nodes, check-farm, check-web-farm-node, cwf, farm-check",
			because: "root help should keep long descriptions and alias groups on one physical line");
		output.Should().Contain("Publish a workspace to a ZIP archive or hub folder, ph, publish-hub, publish-workspace, publishw",
			because: "commands that previously wrapped should now render as a single line");
		output.Should().NotContain("aliases:",
			because: "export help should not use dedicated alias rows anymore");
		output.Should().NotContain("cwf," + Environment.NewLine,
			because: "root help should not insert manual line breaks inside long command descriptions");
		output.Should().NotContain("publish-hub," + Environment.NewLine,
			because: "root help should keep every command entry on one physical line");
		output.Should().NotContain(Environment.NewLine + "  ping" + Environment.NewLine,
			because: "aliases should not appear as standalone top-level commands");
		output.Should().NotContain("execute-assembly-code",
			because: "hidden commands must not appear in root help");
		output.Should().NotContain("\u001b[",
			because: "generated export help must not contain ANSI color codes");
	}

	[Test]
	[Description("Renders runtime root help with a dim alias suffix when ANSI output is supported.")]
	public void RenderRootHelp_WhenRuntimeAndAnsiSupported_ColorizesAliasSuffix() {
		CommandHelpRenderer runtimeRenderer = CreateRenderer(() => true);

		string output = runtimeRenderer.RenderRootHelp(RootHelpRenderMode.Runtime);

		output.Should().Contain("Register a Creatio environment\u001b[90m · cfg, reg\u001b[0m",
			because: "runtime help should keep aliases beside the description with lower visual emphasis");
		output.Should().Contain("List registered Creatio environments\u001b[90m · env, envs, show-web-app\u001b[0m",
			because: "runtime help should colorize longer alias suffixes too");
		output.Should().Contain("Compare file content across web farm nodes\u001b[90m · check-farm, check-web-farm-node, cwf, farm-check\u001b[0m",
			because: "runtime help should keep long alias groups on the same physical line");
		output.Should().NotContain("aliases:",
			because: "runtime help should not emit dedicated alias labels");
	}

	[Test]
	[Description("Falls back to the plain export-style alias tail when runtime ANSI output is unavailable.")]
	public void RenderRootHelp_WhenRuntimeAndAnsiUnsupported_FallsBackToPlainAliasSuffix() {
		CommandHelpRenderer runtimeRenderer = CreateRenderer(() => false);

		string output = runtimeRenderer.RenderRootHelp(RootHelpRenderMode.Runtime);

		output.Should().Contain("Register a Creatio environment, cfg, reg",
			because: "runtime help should still expose aliases even when color is unavailable");
		output.Should().NotContain("\u001b[",
			because: "runtime fallback should not emit ANSI escape codes");
	}

	[Test]
	[Description("Builds the grouped markdown index with canonical files and same-line alias tails.")]
	public void RenderCommandsMarkdown_WhenCalled_UsesCanonicalLinksAndAliasAnchors() {
		string output = _exportRenderer.RenderCommandsMarkdown();

		output.Should().Contain("<a id=\"ping-app\"></a>",
			because: "the markdown index should include a canonical anchor for each command");
		output.Should().Contain("<a id=\"ping\"></a>",
			because: "the markdown index should preserve alias anchors for incoming links");
		output.Should().Contain("(docs/commands/ping-app.md)",
			because: "the markdown index should point to the canonical markdown file");
		output.Should().Contain("- [`reg-web-app`](docs/commands/reg-web-app.md) - Register a Creatio environment, `cfg`, `reg`",
			because: "the markdown index should keep aliases on the same line as the description");
		output.Should().NotContain("Aliases:",
			because: "the markdown index should no longer render alias continuation lines");
		output.Should().NotContain("(docs/commands/ping.md)",
			because: "alias-only markdown files should no longer be referenced");
	}

	[Test]
	[Description("Preserves manual custom headings in markdown docs instead of dropping them during parsing.")]
	public void RenderMarkdownDoc_WhenManualHelpContainsCustomSections_PreservesThemInOrder() {
		FileSystem.AddDirectory(_helpDirectory);
		FileSystem.AddFile(
			System.IO.Path.Combine(_helpDirectory, "add-item.txt"),
			new MockFileData("""
NAME
    add-item - Generate package item models from Creatio metadata

USAGE
    clio add-item model [options]

DESCRIPTION
    Manual description.

DETAIL COLLECTIONS
    Custom section line.

MODEL VALIDATION
    Another custom section line.
"""));
		CommandHelpCatalog catalog = new();
		catalog.TryGetCommand("add-item", out HelpCommandMetadata command).Should().BeTrue(
			because: "the add-item command should exist in the canonical help catalog");

		string output = _exportRenderer.RenderMarkdownDoc(command);

		output.Should().Contain("## Detail Collections",
			because: "custom manual headings should be emitted as markdown sections");
		output.Should().Contain("Custom section line.",
			because: "custom manual section content should survive markdown generation");
		output.IndexOf("## Detail Collections", StringComparison.Ordinal).Should().BeLessThan(
			output.IndexOf("## Model Validation", StringComparison.Ordinal),
			because: "custom sections should keep the same order as the manual help file");
	}

	[Test]
	[Description("Falls back to reflected arguments and options in markdown docs when manual sections are absent.")]
	public void RenderMarkdownDoc_WhenManualHelpOmitsSyntaxSections_UsesGeneratedFallback() {
		FileSystem.AddDirectory(_helpDirectory);
		FileSystem.AddFile(
			System.IO.Path.Combine(_helpDirectory, "set-pkg-version.txt"),
			new MockFileData("""
COMMAND TYPE
    Service commands

NAME
    set-pkg-version - set package version

DESCRIPTION
    Set specified package version into descriptor.json by specified package path.

EXAMPLE
    clio set-pkg-version <PACKAGE PATH> -v <PACKAGE VERSION>
"""));
		CommandHelpCatalog catalog = new();
		catalog.TryGetCommand("set-pkg-version", out HelpCommandMetadata command).Should().BeTrue(
			because: "the set-pkg-version command should exist in the canonical help catalog");

		string output = _exportRenderer.RenderMarkdownDoc(command);

		output.Should().Contain("## Arguments",
			because: "markdown docs should still describe reflected positional arguments when manual help omits them");
		output.Should().Contain("PackagePath",
			because: "the positional package path argument should remain documented");
		output.Should().Contain("## Options",
			because: "markdown docs should still include reflected options when the manual section is missing");
		output.Should().Contain("-v, --PackageVersion <VALUE>",
			because: "the required package version switch should remain documented in markdown output");
	}

	private CommandHelpRenderer CreateRenderer(Func<bool> supportsAnsi) =>
		new(FileSystem, new CommandHelpCatalog(), supportsAnsi);
}
