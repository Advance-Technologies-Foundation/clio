using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Clio.Common;
using Clio.UserEnvironment;
using Clio.WebApplication;
using Clio.Workspaces;
using CommandLine;

namespace Clio.Command;

#region Class: CreateWorkspaceCommandOptions

/// <summary>
/// Options for the <c>create-workspace</c> command.
/// </summary>
[Verb("create-workspace", Aliases = ["createw"], HelpText = "Create open project cmd file")]
public class CreateWorkspaceCommandOptions : WorkspaceOptions{
	[Value(0, MetaName = "WorkspaceName", Required = false, HelpText = "Workspace folder name (used with --empty)")]
	public string WorkspaceName { get; set; }

	[Option("empty", Required = false, Default = false,
		HelpText = "Create workspace in a new subfolder without connecting to any environment")]
	public bool Empty { get; set; }

	[Option("directory", Required = false,
		HelpText = "Absolute base directory for --empty workspace creation. Falls back to appsettings 'workspaces-root' when omitted.")]
	public string Directory { get; set; }

	#region Properties: Protected

	internal override bool RequiredEnvironment => false;

	#endregion
}

#endregion

#region Class: CreateWorkspaceCommand

/// <summary>
/// Creates local clio workspaces either in the current directory or, for empty mode, under a configured root path.
/// </summary>
public class CreateWorkspaceCommand : Command<CreateWorkspaceCommandOptions>{
	#region Fields: Private

	private readonly ILogger _logger;
	private readonly IInstalledApplication _installedApplication;
	private readonly IFileSystem _fileSystem;
	private readonly ISettingsRepository _settingsRepository;
	private readonly IWorkspacePathBuilder _workspacePathBuilder;
	private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
	private readonly IApplicationPing _applicationPing;
	private readonly EnvironmentSettings _environmentSettings;

	private readonly IWorkspace _workspace;

	#endregion

	#region Constructors: Public

	[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters",
		Justification = "The command composes independent DI collaborators (workspace, logger, installed-application "
			+ "reader, file system, settings, path builder, working directories); the ping and its environment "
			+ "settings joined them so an environment that does not answer fails the command before anything is "
			+ "written. Grouping them into a parameter object would hide the injected contract without changing "
			+ "behaviour, which is how the other multi-collaborator commands in this assembly are handled.")]
	public CreateWorkspaceCommand(IWorkspace workspace, ILogger logger, IInstalledApplication installedApplication,
		IFileSystem fileSystem, ISettingsRepository settingsRepository, IWorkspacePathBuilder workspacePathBuilder,
		IWorkingDirectoriesProvider workingDirectoriesProvider, IApplicationPing applicationPing,
		EnvironmentSettings environmentSettings) {
		workspace.CheckArgumentNull(nameof(workspace));
		applicationPing.CheckArgumentNull(nameof(applicationPing));
		_workspace = workspace;
		_logger = logger;
		_installedApplication = installedApplication;
		_fileSystem = fileSystem;
		_settingsRepository = settingsRepository;
		_workspacePathBuilder = workspacePathBuilder;
		_workingDirectoriesProvider = workingDirectoriesProvider;
		_applicationPing = applicationPing;
		_environmentSettings = environmentSettings;
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

		if (!string.IsNullOrWhiteSpace(options.Directory) && !options.Empty) {
			throw new InvalidOperationException(
				"--directory is only supported with --empty. Use 'clio createw <name> --empty --directory <absolute-path>'.");
		}

		if (options.Empty) {
			if (string.IsNullOrWhiteSpace(options.WorkspaceName)) {
				throw new ArgumentException("WorkspaceName is required when using --empty.", nameof(options));
			}
			if (_fileSystem.IsPathRooted(options.WorkspaceName)) {
				throw new InvalidOperationException("WorkspaceName must be a relative folder name.");
			}

			string baseDirectory = ResolveEmptyWorkspaceBaseDirectory(options);
			string workspacePath = _fileSystem.Combine(baseDirectory, options.WorkspaceName); // Validate that the combination is valid (e.g. no invalid chars)
			string destinationPath = _fileSystem.GetFullPath(workspacePath);
			string baseFullPath = _fileSystem.GetFullPath(baseDirectory);
			string baseDirectoryWithSeparator = baseFullPath.TrimEnd(_fileSystem.DirectorySeparatorChar) + _fileSystem.DirectorySeparatorChar;
			if (!destinationPath.StartsWith(baseDirectoryWithSeparator,
					StringComparison.Ordinal)) {
				throw new InvalidOperationException("WorkspaceName must be located under the resolved base directory.");
			}

			if (!options.Force && _fileSystem.ExistsDirectory(destinationPath) && !_fileSystem.IsEmptyDirectory(destinationPath)) {
				throw new InvalidOperationException($"Destination folder already exists and is not empty: {destinationPath}");
			}

			_fileSystem.CreateDirectoryIfNotExists(destinationPath);
			_workspacePathBuilder.RootPath = destinationPath;
			_workspace.Create(null, false, options.Force);
			_logger.WriteInfo($"Workspace created at: {destinationPath}");
			_logger.WriteInfo("Done");
			return 0;
		}

		_workspacePathBuilder.RootPath = currentDirectory;

		if (options.Environment == null && string.IsNullOrEmpty(options.Uri)) {
			_workspace.Create(options.Environment, false, options.Force);
			_logger.WriteInfo($"Workspace created at: {currentDirectory}");
			_logger.WriteInfo("Done");
			return 0;
		}

		// Nothing is written before the checks pass - the rule WorkspaceRestorer keeps (#1119). The local checks
		// come first, so "inside an existing workspace" is the error shown, not a failed login against an
		// environment that would not have been used. The environment comes next: this branch reads the package
		// list or the application from it, and the restore below downloads from it.
		_workspace.ValidateCanCreate(options.Force);
		_applicationPing.EnsureAnswers(_environmentSettings, "Workspace was not created", options.Environment);
		bool appCodeNotExists = options.AppCode == null;
		// Read before Create for the same reason: an application that cannot be read leaves nothing behind.
		InstalledAppInfo app = appCodeNotExists ? null : _installedApplication.GetInstalledAppInfo(options.AppCode);
		_workspace.Create(options.Environment, appCodeNotExists, options.Force);
		foreach (string package in app?.GetPackages() ?? []) {
			_workspace.AddPackageIfNeeded(package);
		}
		_workspace.Restore(options);
		_logger.WriteInfo($"Workspace created at: {currentDirectory}");
		_logger.WriteInfo("Done");
		return 0;
	}

	private string ResolveEmptyWorkspaceBaseDirectory(CreateWorkspaceCommandOptions options) {
		string baseDirectory = options.Directory;
		if (string.IsNullOrWhiteSpace(baseDirectory)) {
			baseDirectory = _settingsRepository.GetWorkspacesRoot();
		}

		if (string.IsNullOrWhiteSpace(baseDirectory)) {
			throw new InvalidOperationException(
				"Provide --directory for create-workspace --empty or configure 'workspaces-root' in appsettings.json.");
		}

		if (!_fileSystem.IsPathRooted(baseDirectory)) {
			throw new InvalidOperationException("--directory and appsettings 'workspaces-root' must be absolute paths.");
		}

		string fullBaseDirectory = _fileSystem.GetFullPath(baseDirectory);
		if (!_fileSystem.ExistsDirectory(fullBaseDirectory)) {
			throw new InvalidOperationException($"Workspace root directory does not exist: {fullBaseDirectory}");
		}

		return fullBaseDirectory;
	}

	#endregion
}

#endregion
