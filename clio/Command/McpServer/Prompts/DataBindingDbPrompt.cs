using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Clio.Command.McpServer.Tools;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;

/// <summary>
/// Prompt helpers for DB-first data-binding MCP tools.
/// </summary>
[McpServerPromptType, Description("Prompts for creating and mutating DB-first package data bindings")]
public static class DataBindingDbPrompt {
	/// <summary>
	/// Builds a prompt for creating a DB-first data binding.
	/// </summary>
	[McpServerPrompt(Name = CreateDataBindingDbTool.CreateDataBindingDbToolName),
	 Description("Prompt to create a DB-first package data binding")]
	public static string CreateDataBindingDb(
		[Required] [Description("Creatio environment name")] string environmentName,
		[Required] [Description("Target package name")] string packageName,
		[Required] [Description("Entity schema name")] string schemaName,
		[Description("Optional binding folder name")] string? bindingName = null,
		[Description("Optional JSON array of row objects")] string? rows = null) =>
		$"""
		 Use clio mcp server `{CreateDataBindingDbTool.CreateDataBindingDbToolName}` to create a DB-first data binding
		 for schema `{schemaName}` in package `{packageName}` on environment `{environmentName}`.
		 For canonical workflow selection, call `{GuidanceGetTool.ToolName}` with `name` set to `data-bindings`
		 before choosing this DB-first path.
		 Prefer `sync-schemas` with inline `seed-rows` as the canonical batched path. Use
		 `{CreateDataBindingDbTool.CreateDataBindingDbToolName}` only for explicit fallback or standalone binding work.
		 When the task is standalone lookup seeding in an MCP workflow, prefer this tool over dropping to direct SQL commands.
		 Pass `environment-name` `{environmentName}` exactly as provided.
		 Use `binding-name` `{bindingName ?? "<default: same as schema>"}` and provide `rows`
		 `{rows ?? "<not provided>"}` as a JSON array of objects each with a `values` key.
		 DB-first binding metadata is projected from the primary key plus the columns referenced by currently bound or requested rows.
		 Unrelated runtime-only columns are not blockers, but explicitly requested unsupported runtime columns still fail.
		 After the remote mutation, read back from Creatio instead of treating the request payload or install log as proof.
		 To sync the result to a local workspace after the DB write, use `restore-workspace` separately.
		 """;

	/// <summary>
	/// Builds a prompt for upserting a row in a DB-first data binding.
	/// </summary>
	[McpServerPrompt(Name = UpsertDataBindingRowDbTool.UpsertDataBindingRowDbToolName),
	 Description("Prompt to upsert a row in a DB-first data binding")]
	public static string UpsertDataBindingRowDb(
		[Required] [Description("Creatio environment name")] string environmentName,
		[Required] [Description("Target package name")] string packageName,
		[Required] [Description("Binding folder name")] string bindingName,
		[Required] [Description("Row values JSON")] string values) =>
		$"""
		 Use clio mcp server `{UpsertDataBindingRowDbTool.UpsertDataBindingRowDbToolName}` to upsert a row in binding
		 `{bindingName}` under package `{packageName}` on environment `{environmentName}`.
		 For canonical workflow selection and verification discipline, call `{GuidanceGetTool.ToolName}` with `name`
		 set to `data-bindings` when the overall binding path is not already fixed.
		 The binding must already exist. If it does not, call `{CreateDataBindingDbTool.CreateDataBindingDbToolName}`
		 first and then retry the row upsert.
		 Pass `environment-name` `{environmentName}` and `values` `{values}` exactly as provided.
		 SaveSchema metadata is rebuilt from the primary key plus the columns present in the bound rows and the requested upsert payload.
		 After the remote mutation, read back from Creatio instead of treating the request payload or install log as proof.
		 To sync the result to a local workspace after the DB write, use `restore-workspace` separately.
		 """;

	/// <summary>
	/// Builds a prompt for removing a row from a DB-first data binding.
	/// </summary>
	[McpServerPrompt(Name = RemoveDataBindingRowDbTool.RemoveDataBindingRowDbToolName),
	 Description("Prompt to remove a row from a DB-first data binding")]
	public static string RemoveDataBindingRowDb(
		[Required] [Description("Creatio environment name")] string environmentName,
		[Required] [Description("Target package name")] string packageName,
		[Required] [Description("Binding folder name")] string bindingName,
		[Required] [Description("Primary-key value")] string keyValue) =>
		$"""
		 Use clio mcp server `{RemoveDataBindingRowDbTool.RemoveDataBindingRowDbToolName}` to remove the row with
		 primary key `{keyValue}` from binding `{bindingName}` under package `{packageName}` on environment
		 `{environmentName}`.
		 For canonical workflow selection and verification discipline, call `{GuidanceGetTool.ToolName}` with `name`
		 set to `data-bindings` when the overall binding path is not already fixed.
		 Pass `environment-name` `{environmentName}` exactly as provided.
		 When the last bound row is removed, the tool also deletes the package schema data record from the remote DB.
		 When rows remain, SaveSchema metadata is rebuilt from the primary key plus the columns present in the remaining bound rows.
		 After the remote mutation, read back from Creatio instead of treating the request payload or install log as proof.
		 """;
}
