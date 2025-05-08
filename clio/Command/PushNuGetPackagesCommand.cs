using System;
using Clio.Common;
using Clio.Project.NuGet;
using CommandLine;

namespace Clio.Command;

[Verb("push-nuget-pkg", Aliases = new[]
    {
        "push-n", "push-nuget"
    },
    HelpText = "Push package on NuGet server")]
public class PushNuGetPkgsOptions : EnvironmentOptions
{

    #region Properties: Public

    [Option('k', "ApiKey", Required = true, HelpText = "The API key for the server")]
    public string ApiKey { get; set; }

    [Value(0, MetaName = "NugetPkgPath", Required = true, HelpText = "Nuget package file path")]
    public string NugetPkgPath { get; set; }

    [Option('s', "Source", Required = true, HelpText = "Specifies the server URL")]
    public string SourceUrl { get; set; }

    #endregion

}

public class PushNuGetPackagesCommand : Command<PushNuGetPkgsOptions>
{

    #region Fields: Private

    private readonly INuGetManager _nugetManager;

    #endregion

    #region Constructors: Public

    public PushNuGetPackagesCommand(INuGetManager nugetManager)
    {
        nugetManager.CheckArgumentNull(nameof(nugetManager));
        _nugetManager = nugetManager;
    }

    #endregion

    #region Methods: Public

    public override int Execute(PushNuGetPkgsOptions options)
    {
        try
        {
            _nugetManager.Push(options.NugetPkgPath, options.ApiKey, options.SourceUrl);
            Console.WriteLine("Done");
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            return 1;
        }
    }

    #endregion

}
