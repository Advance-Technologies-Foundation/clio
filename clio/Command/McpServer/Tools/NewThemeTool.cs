using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for the <c>new-theme</c> command.
/// </summary>
public class NewThemeTool(
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<NewThemeOptions>(null, logger, commandResolver) {

	internal const string NewThemeToolName = "new-theme";
	private const string WorkspaceMarkerRelativePath = ".clio/workspaceSettings.json";
	private static readonly Regex PackageNamePattern = new("^[A-Za-z0-9_]+$", RegexOptions.Compiled);

	/// <summary>
	/// Creates a new Freedom UI theme inside the supplied clio workspace package.
	/// </summary>
	[McpServerTool(Name = NewThemeToolName, ReadOnly = false, Destructive = false, Idempotent = false,
		OpenWorld = false)]
	[Description("""
				 Creates a new Freedom UI theme inside an existing clio workspace package.

				 The caller MUST supply an absolute path to the workspace directory (a directory previously
				 created by the create-workspace tool, containing .clio/workspaceSettings.json). The theme is
				 written to <workspaceDirectory>/packages/<packageName>/Files/themes/<cssClassName>/ as
				 theme.json + theme.css from the canonical baseline. The hosting package is created if it does
				 not exist. caption defaults to Title Case of cssClassName; id
				 defaults to a generated UUID.

				 Example: workspaceDirectory: C:\Projects\Workspaces\son, cssClassName: acme-dark-theme,
				 packageName: UsrThemes
				 """)]
	public CommandExecutionResult NewTheme(
		[Description("Theme creation parameters")] [Required] NewThemeArgs args
	) {
		if (args is null) {
			return new CommandExecutionResult(1, [new ErrorMessage("Theme creation parameters are required.")], null);
		}
		if (string.IsNullOrWhiteSpace(args.WorkspaceDirectory)) {
			return new CommandExecutionResult(1,
				[new ErrorMessage("workspaceDirectory is required and must be an absolute path to an existing clio workspace.")], null);
		}
		if (!Path.IsPathFullyQualified(args.WorkspaceDirectory)) {
			return new CommandExecutionResult(1,
				[new ErrorMessage(
					$"workspaceDirectory must be a fully-qualified absolute path. Drive-relative ('C:ws') and root-relative ('\\ws') paths are rejected. Received: '{args.WorkspaceDirectory}'.")], null);
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
		if (string.IsNullOrWhiteSpace(args.PackageName) || !PackageNamePattern.IsMatch(args.PackageName)) {
			return new CommandExecutionResult(1,
				[new ErrorMessage(
					$"packageName must be a simple identifier matching '^[A-Za-z0-9_]+$'. Path separators, '..', and absolute paths are rejected to keep scaffolding inside the workspace. Received: '{args.PackageName}'.")], null);
		}

		NewThemeOptions options = new() {
			CssClassName = args.CssClassName,
			PackageName = args.PackageName,
			Caption = args.Caption,
			Id = args.Id,
			IsSilent = true
		};

		// Pin the process working directory to the supplied workspace for the duration of the command, the
		// same way new-ui-project does: IWorkspacePathBuilder reads Environment.CurrentDirectory at
		// resolution time, so without this an agent invoking the tool from an arbitrary cwd would scaffold
		// the theme under the wrong (or non-workspace) folder. The execution lock is reentrant on the same
		// thread, so InternalExecute re-acquiring it is safe.
		lock (CommandExecutionSyncRoot) {
			string previousDirectory = Directory.GetCurrentDirectory();
			try {
				Directory.SetCurrentDirectory(args.WorkspaceDirectory);
				return InternalExecute<NewThemeCommand>(options);
			} finally {
				Directory.SetCurrentDirectory(previousDirectory);
			}
		}
	}
}

/// <summary>
/// MCP arguments for the <c>new-theme</c> tool.
/// </summary>
public sealed record NewThemeArgs(
	[property: JsonPropertyName("workspaceDirectory")]
	[property: Description("Absolute path to the existing clio workspace directory (must contain .clio/workspaceSettings.json)")]
	[property: Required]
	string WorkspaceDirectory,

	[property: JsonPropertyName("cssClassName")]
	[property: Description("Theme CSS class name (e.g. acme-dark-theme)")]
	[property: Required]
	string CssClassName,

	[property: JsonPropertyName("packageName")]
	[property: Description("Clio package name that will host the theme")]
	[property: Required]
	string PackageName,

	[property: JsonPropertyName("caption")]
	[property: Description("Optional theme caption; defaults to Title Case of the CSS class name")]
	string Caption = null,

	[property: JsonPropertyName("id")]
	[property: Description("Optional theme id; defaults to a generated UUID")]
	string Id = null
);
