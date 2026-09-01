using System;
using System.Linq;
using Clio.Common;
using Clio.Workspaces;
using CommandLine;

namespace Clio.Command;

[Verb("switch-nuget-to-dll-reference", Aliases = new[] {"nuget2dll"},
	HelpText = "Switches nuget references to dll references in csproj files")]
public class SwitchNugetToDllOptions : EnvironmentOptions
{

	#region Properties: Public

	[Value(0, MetaName = "PackageName", Required = true, HelpText = "Package name to convert")]
	public string PackageName { get; set; }

	#endregion

}

public class SwitchNugetToDllCommand : Command<SwitchNugetToDllOptions>
{

	#region Fields: Private

	private readonly IWorkspace _workspace;
	private readonly IWorkspacePathBuilder _workspacePathBuilder;
	private readonly ILogger _logger;
	private readonly IFileSystem _fileSystem;
	private readonly INugetMaterializer _nugetMaterializer;

	#endregion

	#region Constructors: Public

	public SwitchNugetToDllCommand(IWorkspace workspace, IWorkspacePathBuilder workspacePathBuilder, 
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

	public override int Execute(SwitchNugetToDllOptions toDllOptions){
		bool isWorkspace = _workspace.IsWorkspace;
		if (!isWorkspace) {
			_logger.WriteLine("This command cannot be run outside of a workspace");
			return 1;
		}
		WorkspaceSettings settings = _workspace.WorkspaceSettings;
		//The name comes straight from the command line and is used to derive every path the
		//conversion reads, writes and deletes, so it has to name a package this workspace declares
		//and nothing else.
		if (!settings.Packages.Contains(toDllOptions.PackageName, StringComparer.Ordinal)) {
			_logger.WriteLine($"{toDllOptions.PackageName} is not a package of this workspace... exiting");
			return 1;
		}
		//Exact membership alone does not make the name safe: a malformed workspace can declare
		//"..\..\Victim" or a rooted/UNC value, and BuildPackageProjectPath would then derive a path
		//outside the workspace that ExistsFile reads before Materialize ever sees the name. The same
		//plain-name and containment check Materialize runs therefore has to run here first.
		if (!_nugetMaterializer.IsPackageNameWithinPackagesFolder(toDllOptions.PackageName)) {
			return 1;
		}
		string csprojFilePath = _workspacePathBuilder.BuildPackageProjectPath(toDllOptions.PackageName);
		if (_fileSystem.ExistsFile(csprojFilePath)) {
			return _nugetMaterializer.Materialize(toDllOptions.PackageName);
		}
		_logger.WriteLine($"{toDllOptions.PackageName} does not contain C# projects... exiting");
		return 1;

	}

	#endregion

}