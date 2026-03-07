using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for user task creation.
/// </summary>
public class CreateUserTaskTool(
	CreateUserTaskCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<CreateUserTaskOptions>(command, logger, commandResolver) {

	/// <summary>
	/// Creates a workspace-owned user task and optionally adds initial parameters.
	/// </summary>
	[McpServerTool(Name = "create-user-task", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
	[Description("""
				 Creates a new user task in a package from the specified local workspace and builds that package.
				 
				 The tool can create an empty task or create it with initial parameters in one operation.
				 The workspace path is required because the package must exist in the local workspace.
				 """)]
	public CommandExecutionResult CreateUserTask(
		[Description("create-user-task parameters")] [Required] CreateUserTaskArgs args
	) {
		CreateUserTaskOptions options = new() {
			Code = args.Code,
			Package = args.PackageName,
			Title = args.Title,
			Description = args.Description,
			Culture = string.IsNullOrWhiteSpace(args.Culture) ? "en-US" : args.Culture,
			Parameters = UserTaskToolSupport.SerializeParameterDefinitions(args.Parameters),
			Environment = args.EnvironmentName,
			WorkspacePath = args.WorkspacePath
		};
		return InternalExecute<CreateUserTaskCommand>(options);
	}
}

/// <summary>
/// MCP tool surface for modifying parameters on an existing user task.
/// </summary>
public class ModifyUserTaskParametersTool(
	ModifyUserTaskParametersCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<ModifyUserTaskParametersOptions>(command, logger, commandResolver) {

	/// <summary>
	/// Adds and/or removes parameters on an existing workspace-owned user task.
	/// </summary>
	[McpServerTool(Name = "modify-user-task-parameters", ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("""
				 Modifies parameters on an existing workspace-owned user task in Creatio.
				 
				 This tool can add parameters, remove parameters, or do both in one call.
				 Because it can remove parameters from an existing schema, treat it as destructive.
				 """)]
	public CommandExecutionResult ModifyUserTaskParameters(
		[Description("modify-user-task-parameters parameters")] [Required] ModifyUserTaskParametersArgs args
	) {
		ModifyUserTaskParametersOptions options = new() {
			UserTaskName = args.UserTaskName,
			Culture = string.IsNullOrWhiteSpace(args.Culture) ? "en-US" : args.Culture,
			AddParameters = UserTaskToolSupport.SerializeParameterDefinitions(args.AddParameters),
			RemoveParameters = args.RemoveParameterNames,
			SetDirections = UserTaskToolSupport.SerializeDirectionUpdates(args.SetParameterDirections),
			Environment = args.EnvironmentName,
			WorkspacePath = args.WorkspacePath
		};
		return InternalExecute<ModifyUserTaskParametersCommand>(options);
	}
}

internal static class UserTaskToolSupport {
	public static IEnumerable<string> SerializeParameterDefinitions(IEnumerable<UserTaskParameterArgs> parameters) {
		return parameters?
			.Select(SerializeParameterDefinition)
			.ToList();
	}

	public static IEnumerable<string> SerializeDirectionUpdates(IEnumerable<UserTaskParameterDirectionArgs> updates) {
		return updates?
			.Select(SerializeDirectionUpdate)
			.ToList();
	}

	private static string SerializeParameterDefinition(UserTaskParameterArgs parameter) {
		List<string> segments = [
			$"code={parameter.Code}",
			$"title={parameter.Title}",
			$"type={parameter.Type}"
		];
		AddOptionalString(segments, "direction", parameter.Direction);
		AddOptionalBoolean(segments, "required", parameter.Required);
		AddOptionalBoolean(segments, "resulting", parameter.Resulting);
		AddOptionalBoolean(segments, "serializable", parameter.Serializable);
		AddOptionalBoolean(segments, "copyValue", parameter.CopyValue);
		AddOptionalBoolean(segments, "lazyLoad", parameter.LazyLoad);
		AddOptionalBoolean(segments, "containsPerformerId", parameter.ContainsPerformerId);
		return string.Join(";", segments);
	}

	private static string SerializeDirectionUpdate(UserTaskParameterDirectionArgs update) {
		return $"{update.ParameterName}={update.Direction}";
	}

	private static void AddOptionalBoolean(List<string> segments, string key, bool? value) {
		if (value.HasValue) {
			segments.Add($"{key}={value.Value.ToString().ToLowerInvariant()}");
		}
	}

	private static void AddOptionalString(List<string> segments, string key, string value) {
		if (!string.IsNullOrWhiteSpace(value)) {
			segments.Add($"{key}={value.Trim()}");
		}
	}
}

/// <summary>
/// Structured parameter input for MCP user task tools.
/// </summary>
public record UserTaskParameterArgs(
	[property:JsonPropertyName("code")]
	[Description("Parameter code")]
	[Required]
	string Code,

	[property:JsonPropertyName("title")]
	[Description("Parameter title")]
	[Required]
	string Title,

	[property:JsonPropertyName("type")]
	[Description("Parameter type. Supported values: Boolean, Date, DateTime, Float, Guid, Integer, Money, Text, Time.")]
	[Required]
	string Type,

	[property:JsonPropertyName("required")]
	[Description("Whether the parameter is required.")]
	bool? Required = null,

	[property:JsonPropertyName("direction")]
	[Description("Parameter direction. Supported values: In, Out, Variable, 0, 1, 2. Defaults to Variable when omitted.")]
	string Direction = null,

	[property:JsonPropertyName("resulting")]
	[Description("Whether the parameter is marked as resulting.")]
	bool? Resulting = null,

	[property:JsonPropertyName("serializable")]
	[Description("Whether the parameter is serializable.")]
	bool? Serializable = null,

	[property:JsonPropertyName("copy-value")]
	[Description("Whether the parameter copies its value.")]
	bool? CopyValue = null,

	[property:JsonPropertyName("lazy-load")]
	[Description("Whether the parameter is lazy-loaded.")]
	bool? LazyLoad = null,

	[property:JsonPropertyName("contains-performer-id")]
	[Description("Whether the parameter contains performer id.")]
	bool? ContainsPerformerId = null
);

/// <summary>
/// Structured direction update input for existing user task parameters.
/// </summary>
public record UserTaskParameterDirectionArgs(
	[property:JsonPropertyName("parameter-name")]
	[Description("Existing parameter name")]
	[Required]
	string ParameterName,

	[property:JsonPropertyName("direction")]
	[Description("New parameter direction. Supported values: In, Out, Variable, 0, 1, 2.")]
	[Required]
	string Direction
);

/// <summary>
/// Arguments for the <c>create-user-task</c> MCP tool.
/// </summary>
public record CreateUserTaskArgs(
	[property:JsonPropertyName("code")]
	[Description("User task schema code")]
	[Required]
	string Code,

	[property:JsonPropertyName("package-name")]
	[Description("Workspace package name that will own the user task")]
	[Required]
	string PackageName,

	[property:JsonPropertyName("title")]
	[Description("User task title")]
	[Required]
	string Title,

	[property:JsonPropertyName("environment-name")]
	[Description("Creatio environment name")]
	[Required]
	string EnvironmentName,

	[property:JsonPropertyName("workspace-path")]
	[Description("Absolute path to the local workspace")]
	[Required]
	string WorkspacePath,

	[property:JsonPropertyName("description")]
	[Description("Optional user task description")]
	string Description = null,

	[property:JsonPropertyName("culture")]
	[Description("Culture for title, description, and parameter titles. Defaults to en-US.")]
	string Culture = null,

	[property:JsonPropertyName("parameters")]
	[Description("Optional initial parameters to create with the user task.")]
	IEnumerable<UserTaskParameterArgs> Parameters = null
);

/// <summary>
/// Arguments for the <c>modify-user-task-parameters</c> MCP tool.
/// </summary>
public record ModifyUserTaskParametersArgs(
	[property:JsonPropertyName("user-task-name")]
	[Description("Existing user task schema name")]
	[Required]
	string UserTaskName,

	[property:JsonPropertyName("environment-name")]
	[Description("Creatio environment name")]
	[Required]
	string EnvironmentName,

	[property:JsonPropertyName("workspace-path")]
	[Description("Absolute path to the local workspace")]
	[Required]
	string WorkspacePath,

	[property:JsonPropertyName("culture")]
	[Description("Culture for added parameter titles. Defaults to en-US.")]
	string Culture = null,

	[property:JsonPropertyName("add-parameters")]
	[Description("Parameters to add to the existing user task.")]
	IEnumerable<UserTaskParameterArgs> AddParameters = null,

	[property:JsonPropertyName("remove-parameter-names")]
	[Description("Existing parameter names to remove from the user task.")]
	IEnumerable<string> RemoveParameterNames = null,

	[property:JsonPropertyName("set-parameter-directions")]
	[Description("Direction updates for existing parameters on the user task.")]
	IEnumerable<UserTaskParameterDirectionArgs> SetParameterDirections = null
);
