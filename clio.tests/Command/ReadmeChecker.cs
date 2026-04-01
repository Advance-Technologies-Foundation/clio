using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Clio.Help;
using CommandLine;

namespace Clio.Tests.Command;

public class ReadmeChecker
{

	private static readonly string ReadmeFilePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "clio", "Commands.md");
	private static readonly string WikiAnchorsFilePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "clio", "Wiki", "WikiAnchors.txt");
	private static readonly string HelpDirectoryPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "clio", "help", "en");
	private static readonly string DocsDirectoryPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "clio", "docs", "commands");
	private readonly string _readmeContent = File.ReadAllText(ReadmeFilePath);
	private readonly IReadOnlyList<string> _wikiAnchorsContent = File.ReadAllLines(WikiAnchorsFilePath);
	private readonly CommandHelpRenderer _commandHelpRenderer;

	public ReadmeChecker() {
		Parser.Default.Settings.HelpDirectory = HelpDirectoryPath;
		_commandHelpRenderer = new CommandHelpRenderer(new System.IO.Abstractions.FileSystem(), new CommandHelpCatalog());
	}

	
	/// <summary>
	/// Determines whether the specified command option type is represented in the README file.
	/// </summary>
	/// <remarks>
	/// This method first checks if the command opt ion typeis marked as hidden by the VerbAttribute.
	/// If it is not hidden, the method then checks if any of the associated names (derived from the command option type)
	/// are present in the README content, using a case-insensitive comparison.
	/// </remarks>
	/// <param name="commandOptionType">The Type of the command option to check. This should be a class type where VerbAttribute might be applied.</param>
	/// <returns>
	/// True if the command option type is either marked as hidden by VerbAttribute,
	/// or if any associated names are found in the README content; otherwise, false.
	/// </returns>
	/// <example>
	/// <code>
	/// Type commandType = typeof(MyCommandOption);
	/// bool isInReadme = IsInReadme(commandType);
	/// </code>
	/// </example>
	public bool IsInReadme(Type commandOptionType){
		VerbAttribute verbAttribute = commandOptionType.GetCustomAttribute<VerbAttribute>(true);
		if (verbAttribute?.Hidden == true) {
			return true;
		}
		if (verbAttribute == null || string.IsNullOrWhiteSpace(verbAttribute.Name)) {
			return false;
		}
		string canonicalName = verbAttribute.Name;
		return HasCommandIndexEntry(canonicalName)
			&& HasCommandHelp(canonicalName)
			&& HasCanonicalMarkdownDoc(canonicalName)
			&& HasWikiAnchor(canonicalName);
	}

	private bool HasCommandHelp(string canonicalName) =>
		!string.IsNullOrWhiteSpace(_commandHelpRenderer.TryRenderCommandHelp(canonicalName));

	private bool HasCanonicalMarkdownDoc(string canonicalName) =>
		File.Exists(Path.Combine(DocsDirectoryPath, $"{canonicalName}.md"));

	private bool HasCommandIndexEntry(string canonicalName) =>
		_readmeContent.Contains($"(docs/commands/{canonicalName}.md)", StringComparison.OrdinalIgnoreCase);

	private bool HasWikiAnchor(string canonicalName) =>
		_wikiAnchorsContent.Any(line => line.StartsWith($"{canonicalName}:", StringComparison.OrdinalIgnoreCase));
}
