using System;
using System.Collections.Generic;
using Clio.Common;
using Clio.UserEnvironment;
using Clio.Workspaces;
using CommandLine;

namespace Clio.Command;

/// <summary>
/// Options for the <c>init-workspace</c> command.
/// </summary>
[Verb("init-workspace", Aliases = ["initw"], HelpText = "Initialize clio workspace in the current directory")]
public class InitWorkspaceCommandOptions : WorkspaceOptions {

	/// <summary>
	/// Indicates that the command does not require an environment by default.
	/// </summary>
	internal override bool RequiredEnvironment => false;
}

/// <summary>
/// Initializes a clio workspace in the current directory without overwriting existing files.
/// </summary>
public class InitWorkspaceCommand : Command<InitWorkspaceCommandOptions> {
	private readonly ILogger _logger;
	private readonly IInstalledApplication _installedApplication;
	private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
	private readonly IWorkspacePathBuilder _workspacePathBuilder;
	private readonly IWorkspace _workspace;

	/// <summary>
	/// Initializes a new instance of the <see cref="InitWorkspaceCommand"/> class.
	/// </summary>
	public InitWorkspaceCommand(
		IWorkspace workspace,
		ILogger logger,
		IInstalledApplication installedApplication,
		IWorkspacePathBuilder workspacePathBuilder,
		IWorkingDirectoriesProvider workingDirectoriesProvider) {
		_workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		_installedApplication = installedApplication ?? throw new ArgumentNullException(nameof(installedApplication));
		_workspacePathBuilder = workspacePathBuilder ?? throw new ArgumentNullException(nameof(workspacePathBuilder));
		_workingDirectoriesProvider = workingDirectoriesProvider ?? throw new ArgumentNullException(nameof(workingDirectoriesProvider));
	}

	/// <inheritdoc />
	public override int Execute(InitWorkspaceCommandOptions options) {
		try {
			return InternalExecute(options);
		}
		catch (Exception e) {
			_logger.WriteError(e.Message);
			return 1;
		}
	}

	private int InternalExecute(InitWorkspaceCommandOptions options) {
		string currentDirectory = _workingDirectoriesProvider.CurrentDirectory;
		_workspacePathBuilder.RootPath = currentDirectory;

		if (options.Environment == null && string.IsNullOrEmpty(options.Uri)) {
			_workspace.Initialize(options.Environment, false);
			_logger.WriteInfo($"Workspace initialized at: {currentDirectory}");
			_logger.WriteInfo("Done");
			return 0;
		}

		bool appCodeNotExists = options.AppCode == null;
		_workspace.Initialize(options.Environment, appCodeNotExists);
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
		_logger.WriteInfo($"Workspace initialized at: {currentDirectory}");
		_logger.WriteInfo("Done");
		return 0;
	}
}
