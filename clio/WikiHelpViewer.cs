using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autofac;
using Clio.Common;
using Clio.Utilities;
using CommandLine;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio;

internal class LocalHelpViewer(IFileSystem fileSystem, WikiHelpViewer wikiHelpViewer) : CustomHelpViewer{
	#region Fields: Private

	private string _helpFile = string.Empty;
	private bool _localHelpFileExists;

	#endregion

	#region Methods: Public

	public bool CheckHelp(string commandName) {
		string helpDir = Parser.Default.Settings.HelpDirectory;

		if (!fileSystem.Directory.Exists(helpDir)) {
			_localHelpFileExists = false;
			return wikiHelpViewer.CheckHelp(commandName);
		}

		List<string> files = fileSystem.Directory
									   .EnumerateFiles(helpDir, $"{commandName}.txt", SearchOption.AllDirectories)
									   .ToList();
		_localHelpFileExists = files.Count != 0;

		if (!_localHelpFileExists) {
			return wikiHelpViewer.CheckHelp(commandName);
		}

		_helpFile = files.First();
		return true;
	}

	public void ViewHelp(string commandName) {
		if (!_localHelpFileExists) {
			wikiHelpViewer.ViewHelp(commandName);
		}
		else {
			Console.OutputEncoding = Encoding.UTF8;
			string content = fileSystem.File.ReadAllText(_helpFile);
			Console.Out.WriteLine(content);
		}
	}

	#endregion
}

internal class WikiHelpViewer : CustomHelpViewer{
	#region Constants: Private

	private const string BaseUrl
		= "https://github.com/Advance-Technologies-Foundation/clio/blob/master/clio/Commands.md";

	#endregion

	#region Fields: Private

	private readonly Dictionary<string, List<string>> _wikiAncors = new();

	#endregion

	#region Constructors: Public

	public WikiHelpViewer() {
		IContainer container = new BindingsModule().Register();
		IWorkingDirectoriesProvider directoryProvider = container.Resolve<IWorkingDirectoriesProvider>();
		string anchorFilePath = Path.Combine(directoryProvider.ExecutingDirectory, "Wiki", "WikiAnchors.txt");
		string[] fileLines = File.ReadAllLines(anchorFilePath);
		foreach (string line in fileLines) {
			string[] x = line.Split(':');
			if (x.Length == 2) {
				_wikiAncors[x[0]] = x[1].Split(',').Select(x => x).ToList();
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
		foreach (string anchor in _wikiAncors.Keys) {
			if (_wikiAncors[anchor].Contains(commandName)) {
				return anchor;
			}
		}

		return commandName;
	}

	#endregion

	#region Methods: Public

	public bool CheckHelp(string commandName) {
		try {
			return WebBrowser.Enabled && WebBrowser.CheckUrl(GetCommandHelpUrl(commandName));
		}
		catch {
			return false;
		}
	}

	public void ViewHelp(string commandName) {
		WebBrowser.OpenUrl(GetCommandHelpUrl(commandName));
	}

	#endregion
}
