using System;
using System.Threading;

using Clio.Common;
using CommandLine;

namespace Clio.Command;

[Verb("compile-configuration", Aliases = new[] { "compile-remote" },
    HelpText = "Compile configuration for selected environment")]
public class CompileConfigurationOptions : RemoteCommandOptions
{
    [Option("all", Required = false, HelpText = "Compile configuration all", Default = false)]
    public bool All { get; set; }

    protected override int DefaultTimeout => Timeout.Infinite;
}

public interface ICompileConfigurationCommand
{
    int Execute(CompileConfigurationOptions options);
}

public class CompileConfigurationCommand(IApplicationClient applicationClient, EnvironmentSettings settings): RemoteCommand<CompileConfigurationOptions>(applicationClient, settings), ICompileConfigurationCommand
{
    private static readonly string CompileUrl = @"/ServiceModel/WorkspaceExplorerService.svc/Build";
    private static readonly string CompileAllUrl = @"/ServiceModel/WorkspaceExplorerService.svc/Rebuild";
    private bool _compileAll;

    protected override string ServicePath => _compileAll ? CompileAllUrl : CompileUrl;

    public override int Execute(CompileConfigurationOptions options)
    {
        _compileAll = options.All;
        return base.Execute(options);
    }
}
