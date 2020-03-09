using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using Clio.Utilities;
using CommandLine;

namespace Clio
{
	class WikiHelpViewer : CustomHelpViewer
	{
		string baseUrl = "https://github.com/Advance-Technologies-Foundation/clio/wiki/";

		private string GetCommandHelpUrl(string commandName) {
			return $"{baseUrl}{commandName}".TrimEnd('/');
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
