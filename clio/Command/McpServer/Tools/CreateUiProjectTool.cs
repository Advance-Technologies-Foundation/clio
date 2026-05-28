using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for the <c>new-ui-project</c> command.
/// </summary>
public class CreateUiProjectTool(
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<CreateUiProjectOptions>(null, logger, commandResolver) {

	internal const string CreateUiProjectToolName = "new-ui-project";
	private const string WorkspaceMarkerRelativePath = ".clio/workspaceSettings.json";

	/// <summary>
	/// Creates a new Angular (Freedom UI remote module) project inside the supplied clio workspace.
	/// </summary>
	[McpServerTool(Name = CreateUiProjectToolName, ReadOnly = false, Destructive = false, Idempotent = false,
		OpenWorld = false)]
	[Description("""
				 Creates a new Angular (Freedom UI remote module) project inside an existing clio workspace.

				 The caller MUST supply an absolute path to the workspace directory (a directory previously
				 created by the create-workspace tool, containing .clio/workspaceSettings.json). The project
				 is placed under <workspaceDirectory>/projects/<projectName> and a package is created/linked
				 under <workspaceDirectory>/packages/<packageName>.

				 Example: workspaceDirectory: C:\Projects\Workspaces\son, projectName: my_remote_module,
				 packageName: UsrCustomPkg, vendorPrefix: usr
				 """)]
	public CommandExecutionResult CreateUiProject(
		[Description("UI project creation parameters")] [Required] CreateUiProjectArgs args
	) {
		if (args is null) {
			return new CommandExecutionResult(1, [new ErrorMessage("UI project creation parameters are required.")], null);
		}
		if (string.IsNullOrWhiteSpace(args.WorkspaceDirectory)) {
			return new CommandExecutionResult(1,
				[new ErrorMessage("workspaceDirectory is required and must be an absolute path to an existing clio workspace.")], null);
		}
		if (!Path.IsPathRooted(args.WorkspaceDirectory)) {
			return new CommandExecutionResult(1,
				[new ErrorMessage($"workspaceDirectory must be an absolute path. Received: '{args.WorkspaceDirectory}'.")], null);
		}
		if (!Directory.Exists(args.WorkspaceDirectory)) {
			return new CommandExecutionResult(1,
				[new ErrorMessage($"workspaceDirectory does not exist: '{args.WorkspaceDirectory}'.")], null);
		}
		string workspaceMarker = Path.Combine(args.WorkspaceDirectory, WorkspaceMarkerRelativePath);
		if (!File.Exists(workspaceMarker)) {
			return new CommandExecutionResult(1,
				[new ErrorMessage(
					$"workspaceDirectory is not a clio workspace (missing '{WorkspaceMarkerRelativePath}'): '{args.WorkspaceDirectory}'. "
					+ "Create it first with the create-workspace tool.")], null);
		}

		CreateUiProjectOptions options = new() {
			ProjectName = args.ProjectName,
			PackageName = args.PackageName,
			VendorPrefix = args.VendorPrefix,
			IsEmpty = args.Empty,
			CreatioVersion = args.CreatioVersion ?? string.Empty,
			IsSilent = true
		};

		// Pin the process working directory to the supplied workspace for the duration of the
		// command. IWorkingDirectoriesProvider / IWorkspacePathBuilder both read Environment.CurrentDirectory
		// at resolution time, so without this any agent invoking the tool from an arbitrary cwd would
		// scaffold packages/projects under the wrong (or non-workspace) folder.
		// The execution lock is reentrant on the same thread, so InternalExecute re-acquiring it is safe.
		lock (CommandExecutionSyncRoot) {
			string previousDirectory = Directory.GetCurrentDirectory();
			try {
				Directory.SetCurrentDirectory(args.WorkspaceDirectory);
				return InternalExecute<CreateUiProjectCommand>(options);
			} finally {
				Directory.SetCurrentDirectory(previousDirectory);
			}
		}
	}
}

/// <summary>
/// MCP arguments for the <c>new-ui-project</c> tool.
/// </summary>
public sealed record CreateUiProjectArgs(
	[property: JsonPropertyName("workspaceDirectory")]
	[property: Description("Absolute path to the existing clio workspace directory (must contain .clio/workspaceSettings.json)")]
	[property: Required]
	string WorkspaceDirectory,

	[property: JsonPropertyName("projectName")]
	[property: Description("Angular project name (snake_case: lowercase letters, digits, underscores)")]
	[property: Required]
	string ProjectName,

	[property: JsonPropertyName("packageName")]
	[property: Description("Clio package name to host the project")]
	[property: Required]
	string PackageName,

	[property: JsonPropertyName("vendorPrefix")]
	[property: Description("Vendor prefix: 1-50 lowercase letters only")]
	[property: Required]
	string VendorPrefix,

	[property: JsonPropertyName("empty")]
	[property: Description("When true, scaffold from the empty UI template instead of the default template")]
	bool Empty = false,

	[property: JsonPropertyName("creatioVersion")]
	[property: Description("Optional Creatio version to pick a matching UI project template")]
	string CreatioVersion = null
);
