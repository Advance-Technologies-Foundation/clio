using System;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Reflection;
using Clio.Help;
using Clio.Tests.Command;
using Clio.Tests.Infrastructure;
using CommandLine;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests;

[TestFixture]
[Property("Module", "Core")]
// The renderer reads the process-wide Parser.Default.Settings.HelpDirectory on every call, and other fixtures
// (HelpArtifactExporterTests, ReadmeChecker via BaseCommandTests) repoint it while they run. A test that
// renders a manual help file from this fixture's mock file system would then fall back to generated help,
// so the fixture runs exclusively rather than in parallel with them.
[NonParallelizable]
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
		output.Should().NotContain("USAGE",
			because: "manual alias help should no longer be merged with generated syntax sections");
	}

	[Test]
	[Description("Returns only the manual help text when a canonical manual file exists.")]
	public void TryRenderCommandHelp_WhenManualHelpOmitsSyntaxSections_PrefersManualHelpOnly() {
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
		output.Should().NotContain("ARGUMENTS",
			because: "manual help should no longer be merged with generated positional syntax");
		output.Should().NotContain("OPTIONS",
			because: "manual help should no longer be merged with generated options");
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
		output.Should().Contain("List registered Creatio environments\u001b[90m · env, envs, show-web-app, show-web-app-list\u001b[0m",
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
		output.Should().NotContain("## Aliases",
			because: "manual markdown generation should not synthesize sections that are absent from the help file");
	}

	[Test]
	[Description("Does not append generated environment options or requirements to markdown docs when manual help already exists.")]
	public void RenderMarkdownDoc_WhenManualHelpExists_DoesNotAppendGeneratedEnvironmentSections() {
		FileSystem.AddDirectory(_helpDirectory);
		FileSystem.AddFile(
			System.IO.Path.Combine(_helpDirectory, "add-item.txt"),
			new MockFileData("""
COMMAND TYPE
    Development commands

NAME
    add-item - Manual add-item help

DESCRIPTION
    REQUIRES: cliogate must be installed on Creatio environment for model generation.

OPTIONS
    --Environment       -e  Environment name
"""));
		CommandHelpCatalog catalog = new();
		catalog.TryGetCommand("add-item", out HelpCommandMetadata command).Should().BeTrue(
			because: "the add-item command should exist in the canonical help catalog");

		string output = _exportRenderer.RenderMarkdownDoc(command);

		output.Should().Contain("## Options",
			because: "manual options should still be rendered in markdown");
		output.Should().NotContain("## Environment Options",
			because: "manual markdown generation should not append inherited environment option sections");
		output.Should().NotContain("## Requirements",
			because: "manual markdown generation should not duplicate requirement text already present in the manual description");
	}

	[Test]
	[Description("Keeps markdown docs manual-driven when a canonical manual help file exists.")]
	public void RenderMarkdownDoc_WhenManualHelpOmitsSyntaxSections_DoesNotUseGeneratedFallback() {
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

		output.Should().Contain("## Command Type",
			because: "manual markdown generation should still preserve the original manual sections");
		output.Should().Contain("## Example",
			because: "manual markdown generation should preserve the original singular example heading");
		output.Should().NotContain("## Arguments",
			because: "manual markdown generation should not synthesize positional argument sections");
		output.Should().NotContain("## Options",
			because: "manual markdown generation should not synthesize option sections");
	}

	[Test]
	[Description("Renders the effective runtime default for options whose default is computed in the getter rather than declared via [Option(Default = ...)].")]
	public void RenderMarkdownDoc_WhenOptionDefaultIsComputedAtRuntime_UsesEffectiveDefaultInsteadOfClrTypeDefault() {
		CommandHelpCatalog catalog = new();
		catalog.TryGetCommand("idp-upsert", out HelpCommandMetadata command).Should().BeTrue(
			because: "the idp-upsert command should exist in the canonical help catalog");

		string output = _exportRenderer.RenderMarkdownDoc(command);
		string[] lines = output.Split('\n');
		int timeoutIndex = Array.FindIndex(lines, line => line.Contains("--timeout"));

		timeoutIndex.Should().BeGreaterThanOrEqualTo(0, because: "idp-upsert inherits --timeout from RemoteCommandOptions");
		string timeoutBlock = string.Join(" ", lines.Skip(timeoutIndex).Take(3));
		timeoutBlock.Should().Contain("Default: 100000.",
			because: "RemoteCommandOptions.TimeOut computes its real default (100_000ms) lazily in the getter, so the renderer must read it from a constructed options instance instead of the CLR default (0) for int");
	}

	[Test]
	[Description("Generated command help omits options declared with Hidden = true, such as backward-compatibility aliases (ENG-101526).")]
	public void TryRenderCommandHelp_WhenOptionIsHidden_OmitsIt() {
		// Act
		string output = _exportRenderer.TryRenderCommandHelp("create-entity-schema");

		// Assert
		output.Should().Contain("--name <VALUE>", because: "visible options are still listed");
		output.Should().Contain("--package <VALUE>", because: "visible options are still listed");
		output.Should().NotContain("--schema-name", because: "the hidden alias of --name must not be listed");
		output.Should().NotContain("--package-name", because: "the hidden alias of --package must not be listed");
	}

	[Test]
	[Description("Generated command help omits Hidden environment aliases (--url, --clientId) from ENVIRONMENT OPTIONS while keeping the visible --uri (ENG-101526).")]
	public void TryRenderCommandHelp_WhenEnvironmentOptionIsHidden_OmitsItFromEnvironmentOptions() {
		// Act
		string output = _exportRenderer.TryRenderCommandHelp("update-entity-schema");
		string environmentOptions = output[output.IndexOf("ENVIRONMENT OPTIONS", StringComparison.Ordinal)..];

		// Assert
		environmentOptions.Should().Contain("--uri <VALUE>", because: "the visible environment option is still listed");
		environmentOptions.Should().NotContain("--url", because: "the hidden alias of --uri must not be listed");
		environmentOptions.Should().NotContain("--clientId", because: "the hidden alias of --client-id must not be listed");
	}

	[Test]
	[Description("The generated markdown doc omits Hidden environment aliases (--url, --clientId) from Environment Options while keeping the visible --uri (ENG-101526).")]
	public void RenderMarkdownDoc_WhenEnvironmentOptionIsHidden_OmitsItFromEnvironmentOptions() {
		// Arrange
		CommandHelpCatalog catalog = new();
		catalog.TryGetCommand("update-entity-schema", out HelpCommandMetadata command).Should().BeTrue(
			because: "update-entity-schema is a catalogued command");

		// Act
		string output = _exportRenderer.RenderMarkdownDoc(command);
		string environmentOptions = output[output.IndexOf("## Environment Options", StringComparison.Ordinal)..];

		// Assert
		environmentOptions.Should().Contain("--uri <VALUE>", because: "the visible environment option is still listed");
		environmentOptions.Should().NotContain("--url", because: "the hidden alias of --uri must not be listed");
		environmentOptions.Should().NotContain("--clientId", because: "the hidden alias of --client-id must not be listed");
	}

	[Test]
	[Description("The generated update-entity-schema help documents several values after one --operation, not repeating the flag, and states how --operations-file is read (ENG-101526).")]
	public void TryRenderCommandHelp_ForUpdateEntitySchemaWithoutManualHelp_DescribesOperationAndOperationsFileUsage() {
		// Act
		string output = _exportRenderer.TryRenderCommandHelp("update-entity-schema");
		string flattened = System.Text.RegularExpressions.Regex.Replace(output, @"\s+", " ");

		// Assert
		flattened.Should().Contain("after one --operation", because: "the accepted form is several values after a single --operation");
		flattened.Should().NotContain("Repeat the option", because: "a repeated --operation is rejected by the parser");
		flattened.Should().Contain("A relative path resolves from the current directory",
			because: "generated help is built from the option attributes, so the path rule must live there");
		flattened.Should().Contain("The file must be UTF-8", because: "a non-UTF-8 operations file is rejected");
	}

	[Test]
	[Description("The shipped update-entity-schema.txt, which runtime --help renders, documents several values after one --operation, --operations, and the --operations-file UTF-8 and relative-path rules (ENG-102433).")]
	public void TryRenderCommandHelp_ForUpdateEntitySchemaManualHelp_DescribesOperationAndOperationsFileUsage() {
		// Arrange
		AddRepositoryHelpFile("update-entity-schema");

		// Act
		string output = _exportRenderer.TryRenderCommandHelp("update-entity-schema");
		string flattened = System.Text.RegularExpressions.Regex.Replace(output, @"\s+", " ");

		// Assert
		flattened.Should().Contain("clio-native batch mutation contract",
			because: "runtime help must come from the manual file, not from the generated fallback");
		flattened.Should().Contain("after one --operation", because: "the accepted form is several values after a single --operation");
		flattened.Should().NotContain("Repeat the option", because: "a repeated --operation is rejected by the parser");
		ContainsOptionToken(output, "operations").Should().BeTrue(because: "--operations is a visible option of the command");
		flattened.Should().Contain("A relative path resolves from the current directory",
			because: "the manual help must state how a relative --operations-file path is resolved");
		flattened.Should().Contain("The file must be UTF-8", because: "a non-UTF-8 operations file is rejected");
	}

	[Test]
	[Description("A manual help file that mentions 'alias for' in prose, such as a column-type alias note, renders as manual --help instead of being treated as an alias shim (ENG-102433).")]
	public void TryRenderCommandHelp_WhenManualHelpMentionsAliasForInProse_RendersManualHelp() {
		// Arrange
		AddHelpFile("create-entity-schema", AliasForProseHelp);

		// Act
		string output = _exportRenderer.TryRenderCommandHelp("create-entity-schema");

		// Assert
		output.Should().Contain("This line proves the manual file is used.",
			because: "a manual file that only mentions a type alias in prose is ordinary manual help");
		output.Should().Contain("Blob is accepted as an alias for Binary",
			because: "the manual description must be rendered as written");
		output.Should().NotContain("ENVIRONMENT OPTIONS",
			because: "manual help is not merged with the generated environment options");
	}

	[Test]
	[Description("A manual help file that mentions 'alias for' in prose renders as the manual markdown doc instead of the generated one (ENG-102433).")]
	public void RenderMarkdownDoc_WhenManualHelpMentionsAliasForInProse_RendersManualDoc() {
		// Arrange
		AddHelpFile("create-entity-schema", AliasForProseHelp);
		new CommandHelpCatalog().TryGetCommand("create-entity-schema", out HelpCommandMetadata command).Should().BeTrue(
			because: "create-entity-schema is a catalogued command");

		// Act
		string output = _exportRenderer.RenderMarkdownDoc(command);

		// Assert
		output.Should().Contain("This line proves the manual file is used.",
			because: "a manual file that only mentions a type alias in prose is ordinary manual help");
		output.Should().NotContain("## Environment Options",
			because: "the manual markdown doc does not synthesize generated option sections");
	}

	[Test]
	[Description("The set-app-icon help file, a real alias shim marked by its legacy heading, still renders generated help and its stub text is ignored (ENG-102433).")]
	public void TryRenderCommandHelp_WhenHelpFileIsLegacyHeadingShim_RendersGeneratedHelp() {
		// Arrange
		AddRepositoryHelpFile("set-app-icon");

		// Act
		string output = _exportRenderer.TryRenderCommandHelp("set-app-icon");

		// Assert
		output.Should().Contain("clio set-app-icon [options]",
			because: "a shim falls back to the generated usage line");
		output.Should().NotContain("Legacy heading in Commands.md",
			because: "the shim's legacy-heading stub must not reach the rendered help");
		output.Should().NotContain("Supports the canonical set-app-icon command options",
			because: "the shim's placeholder OPTIONS text must be replaced by the generated option list");
	}

	[TestCase("create-entity-schema", "EntitySchemaDesignerService")]
	[TestCase("update-entity-schema", "clio-native batch mutation contract")]
	[TestCase("modify-entity-schema-column", "Supported actions:")]
	[TestCase("assert", "DESIGN PRINCIPLES")]
	[Description("Runtime --help for each command whose manual file mentions 'alias for' in prose renders that shipped manual file, not generated help (ENG-102433).")]
	public void TryRenderCommandHelp_ForCommandWithAliasForProse_RendersShippedManualHelp(string commandName, string manualOnlyText) {
		// Arrange
		AddRepositoryHelpFile(commandName);

		// Act
		string output = _exportRenderer.TryRenderCommandHelp(commandName);

		// Assert
		output.Should().Contain(manualOnlyText,
			because: $"{commandName}.txt is ordinary manual help and must be what --help shows");
		output.Should().NotContain("ENVIRONMENT OPTIONS",
			because: "manual help is not merged with the generated environment options");
	}

	[TestCase("create-entity-schema")]
	[TestCase("update-entity-schema")]
	[TestCase("modify-entity-schema-column")]
	[TestCase("assert")]
	[Description("The committed docs/commands markdown of each command whose manual file mentions 'alias for' in prose equals the doc rendered from its shipped manual file (ENG-102433).")]
	public void RenderMarkdownDoc_ForCommandWithAliasForProse_MatchesCommittedDoc(string commandName) {
		// Arrange
		AddRepositoryHelpFile(commandName);
		new CommandHelpCatalog().TryGetCommand(commandName, out HelpCommandMetadata command).Should().BeTrue(
			because: $"{commandName} is a catalogued command");
		string committedDoc = System.IO.File.ReadAllText(
			System.IO.Path.Combine(RepositoryRoot, "clio", "docs", "commands", $"{commandName}.md"));

		// Act
		string output = _exportRenderer.RenderMarkdownDoc(command);

		// Assert
		NormalizeLineEndings(output).Should().Be(NormalizeLineEndings(committedDoc),
			because: $"docs/commands/{commandName}.md must be regenerated from {commandName}.txt by the help exporter");
	}

	[TestCaseSource(nameof(CatalogCommandNames))]
	[Description("Across the whole command catalog, generated help lists every option not declared Hidden and none of the long names that only a Hidden option declares. This pins the intentional global Hidden filter (ENG-101526, DR1).")]
	public void TryRenderCommandHelp_ForEveryCatalogCommand_ListsVisibleOptionsAndOmitsHiddenOnes(string commandName) {
		// Arrange
		new CommandHelpCatalog().TryGetCommand(commandName, out HelpCommandMetadata command).Should().BeTrue(
			because: "the case source enumerates catalogued commands");
		OptionAttribute[] options = command.OptionsType
			.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Select(property => property.GetCustomAttribute<OptionAttribute>(true))
			.Where(option => option is not null && !string.IsNullOrWhiteSpace(option.LongName))
			.ToArray();
		string[] visibleNames = options.Where(option => !option.Hidden).Select(option => option.LongName).ToArray();
		string[] hiddenOnlyNames = options
			.Where(option => option.Hidden)
			.Select(option => option.LongName)
			.Where(name => !visibleNames.Contains(name, StringComparer.Ordinal))
			.ToArray();

		// Act
		string output = _exportRenderer.TryRenderCommandHelp(commandName);

		// Assert
		output.Should().NotBeNullOrWhiteSpace(because: "every catalogued command renders generated help without a manual file");
		visibleNames.Where(name => !ContainsOptionToken(output, name)).Should().BeEmpty(
			because: $"every option of {commandName} not declared Hidden must still be listed in its help");
		hiddenOnlyNames.Where(name => ContainsOptionToken(output, name)).Should().BeEmpty(
			because: $"options of {commandName} declared Hidden must not be listed in its help");
	}

	private static System.Collections.Generic.IEnumerable<string> CatalogCommandNames() =>
		new CommandHelpCatalog().Commands.Select(command => command.CanonicalName);

	private static bool ContainsOptionToken(string output, string longName) =>
		System.Text.RegularExpressions.Regex.IsMatch(
			output,
			$@"(?<![\w-])--{System.Text.RegularExpressions.Regex.Escape(longName)}(?![\w-])");

	private const string AliasForProseHelp = """
NAME
    create-entity-schema - Manual create help

DESCRIPTION
    Blob is accepted as an alias for Binary.

CUSTOM NOTES
    This line proves the manual file is used.
""";

	private static readonly string RepositoryRoot =
		System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

	private void AddHelpFile(string commandName, string content) {
		FileSystem.AddDirectory(_helpDirectory);
		FileSystem.AddFile(System.IO.Path.Combine(_helpDirectory, $"{commandName}.txt"), new MockFileData(content));
	}

	private void AddRepositoryHelpFile(string commandName) =>
		AddHelpFile(commandName, System.IO.File.ReadAllText(
			System.IO.Path.Combine(RepositoryRoot, "clio", "help", "en", $"{commandName}.txt")));

	private static string NormalizeLineEndings(string value) => value.Replace("\r\n", "\n");

	private CommandHelpRenderer CreateRenderer(Func<bool> supportsAnsi) =>
		new(FileSystem, new CommandHelpCatalog(), featureToggleService: null, supportsAnsi);
}
