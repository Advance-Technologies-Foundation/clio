using System.Threading;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

#region Class: CompileConfigurationOptions

[Verb("compile-configuration", Aliases = new[]
{
    "compile-remote"
}, HelpText = "Compile configuration for selected environment")]
public class CompileConfigurationOptions : RemoteCommandOptions
{

    #region Properties: Protected

    protected override int DefaultTimeout => Timeout.Infinite;

    #endregion

    #region Properties: Public

    [Option("all", Required = false, HelpText = "Compile configuration all", Default = false)]
    public bool All { get; set; }

    #endregion

}

#endregion

#region Interface: CompileConfigurationCommand

public interface ICompileConfigurationCommand
{

    #region Methods: Public

    int Execute(CompileConfigurationOptions options);

    #endregion

}

#endregion

#region Class: CompileConfigurationCommand

public class CompileConfigurationCommand : RemoteCommand<CompileConfigurationOptions>, ICompileConfigurationCommand
{

    #region Fields: Private

    private static readonly string CompileUrl = @"/ServiceModel/WorkspaceExplorerService.svc/Build";
    private static readonly string CompileAllUrl = @"/ServiceModel/WorkspaceExplorerService.svc/Rebuild";
    private bool _compileAll;

    #endregion

    #region Constructors: Public

    public CompileConfigurationCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
        : base(applicationClient, settings)
    { }

    #endregion

    #region Properties: Protected

    protected override string ServicePath => _compileAll ? CompileAllUrl : CompileUrl;

    #endregion

    #region Methods: Public

    public override int Execute(CompileConfigurationOptions options)
    {
        _compileAll = options.All;
        return base.Execute(options);
    }

    #endregion

}

#endregion
