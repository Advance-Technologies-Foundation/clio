using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Clio.Help;
using CommandLine;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests;

[TestFixture]
[Category("Unit")]
[Property("Module", "Core")]
internal class HelpArtifactConsistencyTests {
	private static readonly string RepositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
	private static readonly string HelpDirectory = Path.Combine(RepositoryRoot, "clio", "help", "en");
	private static readonly string DocsDirectory = Path.Combine(RepositoryRoot, "clio", "docs", "commands");
	private static readonly string CommandsPath = Path.Combine(RepositoryRoot, "clio", "Commands.md");
	private static readonly string WikiAnchorsPath = Path.Combine(RepositoryRoot, "clio", "Wiki", "WikiAnchors.txt");

	[Test]
	[Description("Every visible command should have canonical markdown, index, and wiki artifacts even when manual txt help is optional.")]
	public void VisibleCommands_ShouldHaveCanonicalArtifacts() {
		CommandHelpCatalog catalog = new();
		string commandsContent = File.ReadAllText(CommandsPath);
		string[] wikiAnchors = File.ReadAllLines(WikiAnchorsPath);

		foreach (HelpCommandMetadata command in catalog.GetVisibleCommands()) {
			File.Exists(Path.Combine(DocsDirectory, $"{command.CanonicalName}.md")).Should().BeTrue(
				because: "every visible command should have a canonical markdown document");
			commandsContent.Should().Contain($"(docs/commands/{command.CanonicalName}.md)",
				because: "every visible command should be listed in Commands.md");
			wikiAnchors.Should().Contain(line => line.StartsWith($"{command.CanonicalName}:", StringComparison.OrdinalIgnoreCase),
				because: "every visible command should have a canonical wiki anchor mapping");
		}
	}

	[Test]
	[Description("Every deploy-identity / OAuth configuration verb is classified in the Deployment & Infrastructure group with an explicit description so the catalog does not fall back to source-index classification.")]
	public void DeploymentIdentityCommands_ShouldBeClassifiedWithDescription_WhenCatalogBuilt() {
		// Arrange
		string[] verbs = [
			"deploy-identity",
			"uninstall-identity",
			"get-identity-service-config",
			"resolve-oauth-system-user",
			"create-oauth-technical-user",
			"create-server-to-server-oauth-app",
			"verify-oauth-app"
		];
		CommandHelpCatalog catalog = new();

		// Act & Assert
		foreach (string verb in verbs) {
			catalog.TryGetCommand(verb, out HelpCommandMetadata command).Should().BeTrue(
				because: $"'{verb}' must be present in the canonical help catalog");
			command.GroupId.Should().Be(HelpGroupId.DeploymentAndInfrastructure,
				because: $"'{verb}' must be grouped with deploy-identity under Deployment & Infrastructure, not fallback-classified");
			command.ShortDescription.Should().NotBe(verb,
				because: $"'{verb}' must have an explicit description override rather than echoing its own verb name");
			command.ShortDescription.Should().NotBeNullOrWhiteSpace(
				because: $"'{verb}' must carry a human-readable description in the catalog");
		}
	}

	[Test]
	[Description("The custom logging command is explicitly classified under Package Management.")]
	public void AddCustomLogging_ShouldUsePackageManagementGroup_WhenCatalogBuilt() {
		// Arrange
		CommandHelpCatalog catalog = new();

		// Act
		bool found = catalog.TryGetCommand("add-custom-logging", out HelpCommandMetadata command);

		// Assert
		found.Should().BeTrue(because: "the new command must be present in the canonical help catalog");
		command.GroupId.Should().Be(HelpGroupId.PackageManagement,
			because: "package-specific logging configuration belongs under Package Management");
	}

	[Test]
	[Description("The Creatio artifact merge command is explicitly classified under Development.")]
	public void MergeCreatioArtifact_ShouldUseDevelopmentGroup_WhenCatalogBuilt() {
		// Arrange
		CommandHelpCatalog catalog = new();

		// Act
		bool found = catalog.TryGetCommand("merge-creatio-artifact", out HelpCommandMetadata command);

		// Assert
		found.Should().BeTrue(because: "the CLI-first merge command must be present in the canonical help catalog");
		command.GroupId.Should().Be(HelpGroupId.Development,
			because: "semantic package-artifact merging is a development workflow and must not use positional fallback grouping");
	}

	[Test]
	[Description("Every Classic->Freedom schema migration verb is classified in the Development group with an explicit description so the catalog does not fall back to source-index classification.")]
	public void ClassicToFreedomSchemaCommands_ShouldBeClassifiedWithDescription_WhenCatalogBuilt() {
		// Arrange
		string[] verbs = [
			"get-classic-list-columns",
			"get-classic-page-sources",
			"list-entity-client-schemas"
		];
		CommandHelpCatalog catalog = new();

		// Act & Assert
		foreach (string verb in verbs) {
			catalog.TryGetCommand(verb, out HelpCommandMetadata command).Should().BeTrue(
				because: $"'{verb}' must be present in the canonical help catalog");
			command.GroupId.Should().Be(HelpGroupId.Development,
				because: $"'{verb}' is a schema-development tool and must be grouped under Development, not source-index fallback-classified as Local Instance Management");
			command.ShortDescription.Should().NotBe(verb,
				because: $"'{verb}' must have an explicit description override rather than echoing its own verb name");
			command.ShortDescription.Should().NotBeNullOrWhiteSpace(
				because: $"'{verb}' must carry a human-readable description in the catalog");
		}
	}

	[TestCase("create-entity-schema")]
	[TestCase("update-entity-schema")]
	[TestCase("modify-entity-schema-column")]
	[TestCase("assert")]
	[TestCase("hosts")]
	[TestCase("mcp-http")]
	[Description("The OPTIONS sections of the manual help file of each command that runtime --help renders from its .txt list every visible option of the command, including inherited ones such as --timeout and the environment credential options, and the file names none of the long names only a Hidden option declares (ENG-102433).")]
	public void ManualHelpFile_ShouldListVisibleOptionsAndOmitHiddenOnes(string commandName) {
		// Arrange
		new CommandHelpCatalog().TryGetCommand(commandName, out HelpCommandMetadata command).Should().BeTrue(
			because: $"{commandName} is a catalogued command");
		(PropertyInfo Property, OptionAttribute Option)[] options = command.OptionsType
			.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Select(property => (Property: property, Option: property.GetCustomAttribute<OptionAttribute>(true)))
			.Where(item => item.Option is not null && !string.IsNullOrWhiteSpace(item.Option.LongName))
			.ToArray();
		// EnvironmentOptions is documented as a short environment block that lists only the credential options
		// (EnvironmentCredentialOptionNames), not every connection setting.
		string[] visibleNames = options
			.Where(item => !item.Option.Hidden
				&& item.Property.DeclaringType != typeof(EnvironmentOptions))
			.Select(item => item.Option.LongName)
			.Concat(typeof(EnvironmentOptions).IsAssignableFrom(command.OptionsType) ? EnvironmentCredentialOptionNames : [])
			.Distinct(StringComparer.Ordinal)
			.ToArray();
		string[] hiddenOnlyNames = options
			.Where(item => item.Option.Hidden)
			.Select(item => item.Option.LongName)
			.Where(name => !options.Any(item => !item.Option.Hidden && item.Option.LongName == name))
			.ToArray();

		// Act
		string helpText = File.ReadAllText(Path.Combine(HelpDirectory, $"{commandName}.txt"));
		string optionsText = GetOptionSectionsText(helpText);

		// Assert
		visibleNames.Should().NotBeEmpty(because: $"{commandName} has command options");
		visibleNames.Where(name => !options.Any(item => !item.Option.Hidden && item.Option.LongName == name)).Should().BeEmpty(
			because: "every name the help file must list has to be a visible option of the command");
		visibleNames.Where(name => !ContainsOptionToken(optionsText, name)).Should().BeEmpty(
			because: $"{commandName}.txt is what --help shows, so its OPTIONS sections must document every visible option, inherited ones included, not only mention it in an example or a note");
		hiddenOnlyNames.Where(name => ContainsOptionToken(helpText, name)).Should().BeEmpty(
			because: $"{commandName}.txt must not advertise backward-compatibility aliases declared Hidden");
	}

	[TestCase("create-entity-schema")]
	[TestCase("update-entity-schema")]
	[TestCase("modify-entity-schema-column")]
	[Description("The manual help file of each entity-schema command states the catalog requirement that generated help would print, so switching to manual help does not drop the cliogate prerequisite (ENG-102433).")]
	public void ManualHelpFile_ShouldStateCatalogRequirement(string commandName) {
		// Arrange
		new CommandHelpCatalog().TryGetCommand(commandName, out HelpCommandMetadata command).Should().BeTrue(
			because: $"{commandName} is a catalogued command");
		command.Requirement.Should().NotBeNullOrWhiteSpace(because: $"{commandName} has a catalog requirement");

		// Act
		string helpText = File.ReadAllText(Path.Combine(HelpDirectory, $"{commandName}.txt"));

		// Assert
		Regex.Replace(helpText, @"\s+", " ").Should().Contain(command.Requirement,
			because: $"{commandName}.txt is what --help shows, so it must carry the requirement generated help printed");
	}

	// The credential options a command derived from EnvironmentOptions accepts; its environment block must list them
	// even though the remaining connection settings stay undocumented there.
	private static readonly string[] EnvironmentCredentialOptionNames = [
		"environment", "uri", "login", "password", "client-id", "client-secret", "auth-app-uri", "external-access-token"
	];

	private static readonly Regex SectionHeadingRegex = new(@"^[A-Z][A-Z0-9 /&-]+:?$", RegexOptions.None, TimeSpan.FromSeconds(1));

	// The text of every section whose heading ends in OPTIONS (OPTIONS, COMMON OPTIONS, KUBERNETES OPTIONS, ...).
	private static string GetOptionSectionsText(string helpText) {
		List<string> lines = [];
		bool inOptionSection = false;
		foreach (string line in helpText.Replace("\r\n", "\n").Split('\n')) {
			if (SectionHeadingRegex.IsMatch(line.TrimEnd())) {
				inOptionSection = line.TrimEnd().TrimEnd(':').EndsWith("OPTIONS", StringComparison.Ordinal);
				continue;
			}
			if (inOptionSection) {
				lines.Add(line);
			}
		}
		return string.Join("\n", lines);
	}

	private static bool ContainsOptionToken(string text, string longName) =>
		Regex.IsMatch(text, $@"(?<![\w-])--{Regex.Escape(longName)}(?![\w-])");

	[Test]
	[Description("The CLI help directory should contain only canonical command files plus the root help file.")]
	public void HelpDirectory_ShouldContainOnlyCanonicalFiles() {
		CommandHelpCatalog catalog = new();
		HashSet<string> expectedNames = [..catalog.Commands.Select(command => command.CanonicalName), "help"];
		string[] actualNames = Directory.GetFiles(HelpDirectory, "*.txt")
			.Select(path => Path.GetFileNameWithoutExtension(path))
			.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
			.ToArray();

		actualNames.Should().OnlyContain(name => expectedNames.Contains(name),
			because: "legacy alias-first CLI help files should be removed after normalization");
	}

	[Test]
	[Description("The markdown command docs directory should contain canonical command files plus preserved MCP workflow docs.")]
	public void DocsDirectory_ShouldContainOnlyCanonicalCommandFiles() {
		CommandHelpCatalog catalog = new();
		HashSet<string> expectedNames = [..catalog.Commands.Select(command => command.CanonicalName), "sync-pages", "sync-schemas"];
		string[] actualNames = Directory.GetFiles(DocsDirectory, "*.md")
			.Select(path => Path.GetFileNameWithoutExtension(path))
			.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
			.ToArray();

		actualNames.Should().OnlyContain(name => expectedNames.Contains(name),
			because: "legacy alias, class-name, and duplicate command markdown files should be removed");
	}
}
