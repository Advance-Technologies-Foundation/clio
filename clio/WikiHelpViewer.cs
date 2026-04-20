using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Clio.Common;
using Clio.Help;
using Clio.Utilities;
using CommandLine;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio;

internal class LocalHelpViewer(CommandHelpRenderer commandHelpRenderer, WikiHelpViewer wikiHelpViewer) : CustomHelpViewer{
	private string _helpText = string.Empty;
	private bool _hasLocalHelp;

	public bool CheckHelp(string commandName) {
		_helpText = commandHelpRenderer.TryRenderCommandHelp(commandName) ?? string.Empty;
		_hasLocalHelp = !string.IsNullOrWhiteSpace(_helpText);
		if (_hasLocalHelp) {
			return true;
		}
		return wikiHelpViewer.CheckHelp(commandName);
	}

	public void ViewHelp(string commandName) {
		if (!_hasLocalHelp) {
			wikiHelpViewer.ViewHelp(commandName);
			return;
		}
#pragma warning disable CLIO002
		Console.OutputEncoding = Encoding.UTF8;
		Console.Out.Write(_helpText);
#pragma warning restore CLIO002
	}
}

internal class WikiHelpViewer : CustomHelpViewer{
	#region Constants: Private

	private const string BaseUrl
		= "https://github.com/Advance-Technologies-Foundation/clio/blob/master/clio/Commands.md";

	#endregion

	#region Fields: Private

	private readonly Dictionary<string, List<string>> _wikiAnchors = new();
	private readonly IWebBrowser _webBrowser;

	#endregion

	#region Constructors: Public

	public WikiHelpViewer(IWorkingDirectoriesProvider directoryProvider, IFileSystem fileSystem, IWebBrowser webBrowser) {
		_webBrowser = webBrowser;
		string anchorFilePath = Path.Combine(directoryProvider.ExecutingDirectory, "Wiki", "WikiAnchors.txt");
		string[] fileLines = fileSystem.File.ReadAllLines(anchorFilePath);
		foreach (string line in fileLines) {
			string[] parts = line.Split(':');
			if (parts.Length == 2) {
				_wikiAnchors[parts[0]] = parts[1].Split(',').Select(anchor => anchor).ToList();
			}
		}
	}

	#endregion

	#region Methods: Private

	private string GetCommandHelpUrl(string commandName) {
		string wikiAnchor = GetWikiAnchor(commandName);
		string url = $"{BaseUrl}#{wikiAnchor}".TrimEnd('/');
		return url;
	}

	private string GetWikiAnchor(string commandName) {
		foreach (string anchor in _wikiAnchors.Keys) {
			if (_wikiAnchors[anchor].Contains(commandName)) {
				return anchor;
			}
		}

		return commandName;
	}

	#endregion

	#region Methods: Public

	public bool CheckHelp(string commandName) {
		try {
			return _webBrowser.Enabled && _webBrowser.CheckUrl(GetCommandHelpUrl(commandName));
		}
		catch {
			return false;
		}
	}

	public void ViewHelp(string commandName) {
		_webBrowser.OpenUrl(GetCommandHelpUrl(commandName));
	}

	#endregion
}
