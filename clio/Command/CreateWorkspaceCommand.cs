using System;
using System.Collections.Generic;
using System.IO;
using Clio.Common;
using Clio.Workspace;
using Clio.Workspaces;
using CommandLine;

namespace Clio.Command;

#region Class: CreateWorkspaceCommandOptions

[Verb("create-workspace", Aliases = ["createw"], HelpText = "Create open project cmd file")]
public class CreateWorkspaceCommandOptions : WorkspaceOptions{
	[Value(0, MetaName = "WorkspaceName", Required = false, HelpText = "Workspace folder name (used with --empty)")]
	public string WorkspaceName { get; set; }

	[Option("empty", Required = false, Default = false,
		HelpText = "Create workspace in a new subfolder without connecting to any environment")]
	public bool Empty { get; set; }

	#region Properties: Protected

	internal override bool RequiredEnvironment => false;

	#endregion
}

#endregion

#region Class: CreateWorkspaceCommand

public class CreateWorkspaceCommand : Command<CreateWorkspaceCommandOptions>{
	#region Fields: Private

	private readonly ILogger _logger;
	private readonly IInstalledApplication _installedApplication;
	private readonly IFileSystem _fileSystem;
	private readonly IWorkspacePathBuilder _workspacePathBuilder;
	private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;

	private readonly IWorkspace _workspace;

	#endregion

	#region Constructors: Public

	public CreateWorkspaceCommand(IWorkspace workspace, ILogger logger, IInstalledApplication installedApplication,
		IFileSystem fileSystem, IWorkspacePathBuilder workspacePathBuilder,
		IWorkingDirectoriesProvider workingDirectoriesProvider) {
		workspace.CheckArgumentNull(nameof(workspace));
		_workspace = workspace;
		_logger = logger;
		_installedApplication = installedApplication;
		_fileSystem = fileSystem;
		_workspacePathBuilder = workspacePathBuilder;
		_workingDirectoriesProvider = workingDirectoriesProvider;
	}

	#endregion

	#region Methods: Public

	public override int Execute(CreateWorkspaceCommandOptions options) {
		try {
			return InternalExecute(options);
		}
		catch (Exception e) {
			_logger.WriteError(e.Message);
			return 1;
		}
	}

	private int InternalExecute(CreateWorkspaceCommandOptions options) {
		// Always operate relative to the current directory. Do not try to detect a parent workspace root.
		// This prevents false positives when the current folder is inside another folder that contains a .clio directory.
		string currentDirectory = _workingDirectoriesProvider.CurrentDirectory;

		if (!string.IsNullOrWhiteSpace(options.WorkspaceName) && !options.Empty) {
			throw new InvalidOperationException(
				"WorkspaceName argument is only supported with --empty. Use 'clio createw <name> --empty'.");
		}

		if (options.Empty) {
			if (string.IsNullOrWhiteSpace(options.WorkspaceName)) {
				throw new ArgumentException("WorkspaceName is required when using --empty.", nameof(options.WorkspaceName));
			}
			if (Path.IsPathRooted(options.WorkspaceName)) {
				throw new InvalidOperationException("WorkspaceName must be a relative folder name.");
			}

			string destinationPath = Path.GetFullPath(Path.Combine(currentDirectory, options.WorkspaceName));
			string currentFullPath = Path.GetFullPath(currentDirectory);
			if (!destinationPath.StartsWith(currentFullPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
					StringComparison.Ordinal)) {
				throw new InvalidOperationException("WorkspaceName must be located under the current directory.");
			}

			if (!options.Force && _fileSystem.ExistsDirectory(destinationPath) && !_fileSystem.IsEmptyDirectory(destinationPath)) {
				throw new InvalidOperationException($"Destination folder already exists and is not empty: {destinationPath}");
			}

			_fileSystem.CreateDirectoryIfNotExists(destinationPath);
			_workspacePathBuilder.RootPath = destinationPath;
			_workspace.Create(null, false, options.Force);
			_logger.WriteInfo("Done");
			return 0;
		}

		_workspacePathBuilder.RootPath = currentDirectory;

		if (options.Environment == null && string.IsNullOrEmpty(options.Uri)) {
			_workspace.Create(options.Environment, false, options.Force);
			_logger.WriteInfo("Done");
			return 0;
		}

		bool appCodeNotExists = options.AppCode == null;
		_workspace.Create(options.Environment, appCodeNotExists, options.Force);
		if (!appCodeNotExists) {
			InstalledAppInfo app = _installedApplication.GetInstalledAppInfo(options.AppCode);
			if (app != null) {
				IEnumerable<string> packages = app.GetPackages();
				foreach (string package in packages) {
					_workspace.AddPackageIfNeeded(package);
				}
			}
		}
		_workspace.Restore(options);
		_logger.WriteInfo("Done");
		return 0;
	}

	#endregion
}

#endregion
