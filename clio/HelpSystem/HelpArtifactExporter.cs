using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;

namespace Clio.Help;

internal sealed class HelpArtifactExporter {
	private readonly IFileSystem _fileSystem;
	private readonly CommandHelpCatalog _catalog;
	private readonly CommandHelpRenderer _renderer;

	public HelpArtifactExporter(IFileSystem fileSystem, CommandHelpCatalog catalog, CommandHelpRenderer renderer) {
		_fileSystem = fileSystem;
		_catalog = catalog;
		_renderer = renderer;
	}

	public int Export(string repositoryRoot) {
		string helpDirectory = _fileSystem.Path.Combine(repositoryRoot, "clio", "help", "en");
		string docsDirectory = _fileSystem.Path.Combine(repositoryRoot, "clio", "docs", "commands");
		string wikiAnchorPath = _fileSystem.Path.Combine(repositoryRoot, "clio", "Wiki", "WikiAnchors.txt");
		string commandsPath = _fileSystem.Path.Combine(repositoryRoot, "clio", "Commands.md");
		WriteFile(_fileSystem.Path.Combine(helpDirectory, "help.txt"), _renderer.RenderRootHelp(RootHelpRenderMode.Export));
		HashSet<string> commandNames = _catalog.Commands.Select(command => command.CanonicalName).ToHashSet(StringComparer.OrdinalIgnoreCase);
		foreach (HelpCommandMetadata command in _catalog.GetVisibleCommands()) {
			WriteFile(_fileSystem.Path.Combine(helpDirectory, $"{command.CanonicalName}.txt"), _renderer.TryRenderCommandHelp(command.CanonicalName));
			EnsureCanonicalMarkdownDoc(docsDirectory, command);
		}
		CleanLegacyHelpFiles(helpDirectory, commandNames);
		CleanLegacyMarkdownDocs(docsDirectory, commandNames);
		WriteFile(commandsPath, _renderer.RenderCommandsMarkdown());
		WriteFile(wikiAnchorPath, _renderer.RenderWikiAnchors());
		return 0;
	}

	private void EnsureCanonicalMarkdownDoc(string docsDirectory, HelpCommandMetadata command) {
		string canonicalPath = _fileSystem.Path.Combine(docsDirectory, $"{command.CanonicalName}.md");
		WriteFile(canonicalPath, _renderer.RenderMarkdownDoc(command));
	}

	private void CleanLegacyHelpFiles(string helpDirectory, HashSet<string> canonicalNames) {
		foreach (string path in _fileSystem.Directory.GetFiles(helpDirectory, "*.txt")) {
			string fileName = _fileSystem.Path.GetFileNameWithoutExtension(path);
			if (canonicalNames.Contains(fileName) || string.Equals(fileName, "help", StringComparison.OrdinalIgnoreCase)) {
				continue;
			}
			_fileSystem.File.Delete(path);
		}
	}

	private void CleanLegacyMarkdownDocs(string docsDirectory, HashSet<string> canonicalNames) {
		HashSet<string> preservedMarkdownNames = [..canonicalNames, "page-sync", "schema-sync"];
		foreach (string path in _fileSystem.Directory.GetFiles(docsDirectory, "*.md")) {
			string fileName = _fileSystem.Path.GetFileNameWithoutExtension(path);
			if (preservedMarkdownNames.Contains(fileName)) {
				continue;
			}
			_fileSystem.File.Delete(path);
		}
	}

	private void WriteFile(string path, string content) {
		_fileSystem.Directory.CreateDirectory(_fileSystem.Path.GetDirectoryName(path));
		_fileSystem.File.WriteAllText(path, content.Replace("\r\n", "\n").Replace("\n", Environment.NewLine));
	}
}
