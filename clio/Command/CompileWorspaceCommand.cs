using Clio.Common;
using CommandLine;

namespace Clio.Command;

[Verb("build-workspace", Aliases = new[]
{
    "build", "compile", "compile-all", "rebuild"
}, HelpText = "Build/Rebuild worksapce for selected environment")]
public class CompileOptions : RemoteCommandOptions
{

    #region Properties: Public

    [Option('o', "ModifiedItems", HelpText = "Build modified items")]
    public bool ModifiedItems { get; set; }

    #endregion

}

public class CompileWorkspaceCommand : RemoteCommand<CompileOptions>
{

    #region Constructors: Public

    public CompileWorkspaceCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
        : base(applicationClient, settings)
    { }

    #endregion

    #region Properties: Protected

    protected override string ServicePath => @"/rest/CreatioApiGateway/CompileWorkspace";

    #endregion

    #region Methods: Protected

    protected override string GetRequestData(CompileOptions options)
    {
        return "{" + $"\"compileModified\":{options.ModifiedItems.ToString().ToLower()}" + "}";
    }

    #endregion

}
