using Clio.Common;
using CommandLine;

namespace Clio.Command;

[Verb("build-workspace", Aliases = new[] { "build", "compile", "compile-all", "rebuild" },
    HelpText = "Build/Rebuild worksapce for selected environment")]
public class CompileOptions : RemoteCommandOptions
{
    [Option('o', "ModifiedItems", HelpText = "Build modified items")]
    public bool ModifiedItems { get; set; }
}

public class CompileWorkspaceCommand(IApplicationClient applicationClient, EnvironmentSettings settings): RemoteCommand<CompileOptions>(applicationClient, settings)
{
    protected override string ServicePath => @"/rest/CreatioApiGateway/CompileWorkspace";

    protected override string GetRequestData(CompileOptions options) =>
        "{" + $"\"compileModified\":{options.ModifiedItems.ToString().ToLower()}" + "}";
}
