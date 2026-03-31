using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clio.Help;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests;

[TestFixture]
internal class HelpArtifactConsistencyTests {
	private static readonly string RepositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
	private static readonly string HelpDirectory = Path.Combine(RepositoryRoot, "clio", "help", "en");
	private static readonly string DocsDirectory = Path.Combine(RepositoryRoot, "clio", "docs", "commands");
	private static readonly string CommandsPath = Path.Combine(RepositoryRoot, "clio", "Commands.md");
	private static readonly string WikiAnchorsPath = Path.Combine(RepositoryRoot, "clio", "Wiki", "WikiAnchors.txt");

	[Test]
	[Description("Every visible command should have canonical help, markdown, index, and wiki artifacts.")]
	public void VisibleCommands_ShouldHaveCanonicalArtifacts() {
		CommandHelpCatalog catalog = new();
		string commandsContent = File.ReadAllText(CommandsPath);
		string[] wikiAnchors = File.ReadAllLines(WikiAnchorsPath);

		foreach (HelpCommandMetadata command in catalog.GetVisibleCommands()) {
			File.Exists(Path.Combine(HelpDirectory, $"{command.CanonicalName}.txt")).Should().BeTrue(
				because: "every visible command should have a canonical CLI help file");
			File.Exists(Path.Combine(DocsDirectory, $"{command.CanonicalName}.md")).Should().BeTrue(
				because: "every visible command should have a canonical markdown document");
			commandsContent.Should().Contain($"(docs/commands/{command.CanonicalName}.md)",
				because: "every visible command should be listed in Commands.md");
			wikiAnchors.Should().Contain(line => line.StartsWith($"{command.CanonicalName}:", StringComparison.OrdinalIgnoreCase),
				because: "every visible command should have a canonical wiki anchor mapping");
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
		HashSet<string> expectedNames = [..catalog.Commands.Select(command => command.CanonicalName), "page-sync", "schema-sync"];
		string[] actualNames = Directory.GetFiles(DocsDirectory, "*.md")
			.Select(path => Path.GetFileNameWithoutExtension(path))
			.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
			.ToArray();

		actualNames.Should().OnlyContain(name => expectedNames.Contains(name),
			because: "legacy alias, class-name, and duplicate command markdown files should be removed");
	}
}
