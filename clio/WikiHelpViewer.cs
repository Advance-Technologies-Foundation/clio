using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using Autofac;
using Clio.Common;
using Clio.Utilities;
using CommandLine;

namespace Clio
{
	class WikiHelpViewer : CustomHelpViewer
	{
		public WikiHelpViewer() {
			var container = new BindingsModule().Register(null);
			var directoryProvider = container.Resolve<IWorkingDirectoriesProvider>();
			var anchorFilePath = Path.Combine(directoryProvider.ExecutingDirectory, "Wiki", "WikiAnchors.txt");
			var fileLines = File.ReadAllLines(anchorFilePath);
			foreach(var line in fileLines)
            {
				var x = line.Split(':');
				if(x.Length == 2)
                {
					WikiAncors[x[0]] = x[1].Split(',').Select(x => x).ToList();
                }
            }
		}

		private Dictionary<string, List<string>> WikiAncors = new Dictionary<string, List<string>>();

		private string GetWikiAnchor(string commandName) {
			foreach (string anchor in WikiAncors.Keys)
			{
				if (WikiAncors[anchor].Contains(commandName))
                {
					return anchor;
                }
			}
			return commandName;
		}

		string baseUrl = "https://github.com/Advance-Technologies-Foundation/clio/blob/master/README.md";

		private string GetCommandHelpUrl(string commandName) {
			var wikiAnchor = GetWikiAnchor(commandName);
			var url = $"{baseUrl}#{wikiAnchor}".TrimEnd('/');
			return url;
		}

		public void ViewHelp(string commandName) {
			WebBrowser.OpenUrl(GetCommandHelpUrl(commandName));
		}

		public bool CheckHelp(string commandName) {
			try {
				return WebBrowser.Enabled && WebBrowser.CheckUrl(GetCommandHelpUrl(commandName));
			} catch {
				return false;
			}
		}
	}
}
