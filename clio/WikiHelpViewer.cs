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

namespace Clio;

internal class WikiHelpViewer : CustomHelpViewer
{
    public WikiHelpViewer()
    {
        IContainer container = new BindingsModule().Register(null);
        IWorkingDirectoriesProvider directoryProvider = container.Resolve<IWorkingDirectoriesProvider>();
        string anchorFilePath = Path.Combine(directoryProvider.ExecutingDirectory, "Wiki", "WikiAnchors.txt");
        string[] fileLines = File.ReadAllLines(anchorFilePath);
        foreach (string line in fileLines)
        {
            string[] x = line.Split(':');
            if (x.Length == 2)
            {
                wikiAncors[x[0]] = x[1].Split(',').Select(x => x).ToList();
            }
        }
    }

    private readonly Dictionary<string, List<string>> wikiAncors = [];

    private string GetWikiAnchor(string commandName)
    {
        foreach (string anchor in wikiAncors.Keys)
        {
            if (wikiAncors[anchor].Contains(commandName))
            {
                return anchor;
            }
        }

        return commandName;
    }

    private readonly string baseUrl = "https://github.com/Advance-Technologies-Foundation/clio/blob/master/clio/Commands.md";

    private string GetCommandHelpUrl(string commandName)
    {
        string wikiAnchor = GetWikiAnchor(commandName);
        string url = $"{baseUrl}#{wikiAnchor}".TrimEnd('/');
        return url;
    }

    public void ViewHelp(string commandName) => WebBrowser.OpenUrl(GetCommandHelpUrl(commandName));

    public bool CheckHelp(string commandName)
    {
        try
        {
            return WebBrowser.Enabled && WebBrowser.CheckUrl(GetCommandHelpUrl(commandName));
        }
        catch
        {
            return false;
        }
    }
}
