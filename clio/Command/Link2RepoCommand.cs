using System;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

[Verb("link-to-repository", Aliases = new[]
{
    "l2r", "link2repo"
}, HelpText = "Link environment package(s) to repository.")]
internal class Link2RepoOptions
{

    #region Properties: Public

    [Option('e', "envPkgPath", Required = true,
        HelpText
            = "Path to environment package folder ({LOCAL_CREATIO_PATH}Terrasoft.WebApp\\Terrasoft.Configuration\\Pkg)",
        Default = null)]
    public string envPkgPath { get; set; }

    [Option('r', "repoPath", Required = true,
        HelpText = "Path to package repository folder", Default = null)]
    public string RepoPath { get; set; }

    #endregion

}

internal class Link2RepoCommand : Command<Link2RepoOptions>
{

    #region Methods: Public

    public override int Execute(Link2RepoOptions options)
    {
        try
        {
            if (OperationSystem.Current.IsWindows)
            {
                RfsEnvironment.Link2Repo(options.envPkgPath, options.RepoPath);
                Console.WriteLine("Done.");
                return 0;
            }
            Console.WriteLine("Clio mklink command is only supported on: 'windows'.");
            return 1;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return 1;
        }
    }

    #endregion

}
