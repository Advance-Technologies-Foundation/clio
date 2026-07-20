using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clio.Help;
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
	[Description("Every Classic->Freedom schema migration verb is classified in the Development group with an explicit description so the catalog does not fall back to source-index classification.")]
	public void ClassicToFreedomSchemaCommands_ShouldBeClassifiedWithDescription_WhenCatalogBuilt() {
		// Arrange
		string[] verbs = [
			"get-classic-schema-by-uid",
			"get-classic-migration-bundle",
			"list-schema-hierarchy",
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
