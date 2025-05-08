using System.Linq;
using Clio.Common;
using Clio.Workspaces;
using CommandLine;

namespace Clio.Command;

[Verb("switch-nuget-to-dll-reference", Aliases = new[] { "nuget2dll" },
    HelpText = "Switches nuget references to dll references in csproj files")]
public class SwitchNugetToDllOptions : EnvironmentOptions
{
    [Value(0, MetaName = "PackageName", Required = true, HelpText = "Package name to convert")]
    public string PackageName { get; set; }
}

public class SwitchNugetToDllCommand(
    IWorkspace workspace,
    IWorkspacePathBuilder workspacePathBuilder,
    ILogger logger,
    IFileSystem fileSystem,
    INugetMaterializer nugetMaterializer) : Command<SwitchNugetToDllOptions>
{
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly ILogger _logger = logger;
    private readonly INugetMaterializer _nugetMaterializer = nugetMaterializer;
    private readonly IWorkspace _workspace = workspace;
    private readonly IWorkspacePathBuilder _workspacePathBuilder = workspacePathBuilder;

    public override int Execute(SwitchNugetToDllOptions toDllOptions)
    {
        bool isWorkspace = _workspace.IsWorkspace;
        if (!isWorkspace)
        {
            _logger.WriteLine("This command cannot be run outside of a workspace");
            return 1;
        }

        WorkspaceSettings settings = _workspace.WorkspaceSettings;
        string csprojFilePath = _workspacePathBuilder.BuildPackageProjectPath(toDllOptions.PackageName);
        if (settings.Packages.Any() && _fileSystem.ExistsFile(csprojFilePath))
        {
            return _nugetMaterializer.Materialize(toDllOptions.PackageName);
        }

        _logger.WriteLine($"{toDllOptions.PackageName} does not contain C# projects... exiting");
        return 1;
    }
}
