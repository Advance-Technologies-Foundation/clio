using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autofac;
using Clio.Common;
using Clio.Utilities;
using CommandLine;

namespace Clio;

internal class WikiHelpViewer : CustomHelpViewer
{

    #region Fields: Private

    private readonly Dictionary<string, List<string>> WikiAncors = new();

    private readonly string baseUrl
        = "https://github.com/Advance-Technologies-Foundation/clio/blob/master/clio/Commands.md";

    #endregion

    #region Constructors: Public

    public WikiHelpViewer()
    {
        IContainer container = new BindingsModule().Register();
        IWorkingDirectoriesProvider directoryProvider = container.Resolve<IWorkingDirectoriesProvider>();
        string anchorFilePath = Path.Combine(directoryProvider.ExecutingDirectory, "Wiki", "WikiAnchors.txt");
        string[] fileLines = File.ReadAllLines(anchorFilePath);
        foreach (string line in fileLines)
        {
            string[] x = line.Split(':');
            if (x.Length == 2)
            {
                WikiAncors[x[0]] = x[1].Split(',').Select(x => x).ToList();
            }
        }
    }

    #endregion

    #region Methods: Private

    private string GetCommandHelpUrl(string commandName)
    {
        string wikiAnchor = GetWikiAnchor(commandName);
        string url = $"{baseUrl}#{wikiAnchor}".TrimEnd('/');
        return url;
    }

    private string GetWikiAnchor(string commandName)
    {
        foreach (string anchor in WikiAncors.Keys)
        {
            if (WikiAncors[anchor].Contains(commandName))
            {
                return anchor;
            }
        }
        return commandName;
    }

    #endregion

    #region Methods: Public

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

    public void ViewHelp(string commandName)
    {
        WebBrowser.OpenUrl(GetCommandHelpUrl(commandName));
    }

    #endregion

}
