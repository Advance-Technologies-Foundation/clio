using System.Linq;
using Clio.Common;
using Clio.Workspace;
using CommandLine;

namespace Clio.Command;

[Verb("materialize-nuget", Aliases = new[] {"ml"},
	HelpText = "Converts nuget references to dll references in csproj files")]
public class MaterializeNugetOptions : EnvironmentOptions
{

	#region Properties: Public

	[Value(0, MetaName = "PackageName", Required = true, HelpText = "Package name to convert")]
	public string PackageName { get; set; }

	#endregion

}

public class MaterializeNugetCommand : Command<MaterializeNugetOptions>
{

	#region Fields: Private

	private readonly IWorkspace _workspace;
	private readonly IWorkspacePathBuilder _workspacePathBuilder;
	private readonly ILogger _logger;
	private readonly IFileSystem _fileSystem;
	private readonly INugetMaterializer _nugetMaterializer;

	#endregion

	#region Constructors: Public

	public MaterializeNugetCommand(IWorkspace workspace, IWorkspacePathBuilder workspacePathBuilder, 
		ILogger logger, IFileSystem fileSystem, INugetMaterializer nugetMaterializer
		){
		_workspace = workspace;
		_workspacePathBuilder = workspacePathBuilder;
		_logger = logger;
		_fileSystem = fileSystem;
		_nugetMaterializer = nugetMaterializer;
		
	}

	#endregion

	#region Methods: Public

	public override int Execute(MaterializeNugetOptions options){
		bool isWorkspace = _workspace.IsWorkspace;
		if (!isWorkspace) {
			_logger.WriteLine("This command cannot be run outside of a workspace");
			return 1;
		}
		WorkspaceSettings settings = _workspace.WorkspaceSettings;
		string csprojFilePath = _workspacePathBuilder.BuildPackageProjectPath(options.PackageName);
		if (settings.Packages.Any() && _fileSystem.ExistsFile(csprojFilePath)) {
			return _nugetMaterializer.Materialize(options.PackageName);
		}
		_logger.WriteLine($"{options.PackageName} does not contain C# projects... exiting");
		return 1;

	}

	#endregion

}