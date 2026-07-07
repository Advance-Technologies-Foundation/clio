using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using Clio.Command.BusinessRules;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Type-parameter carrier for <see cref="BaseTool{T}"/> shared by the business-rule
/// read/update/delete tools. These tools resolve environment-scoped services per call through
/// <see cref="IToolCommandResolver"/> and never execute a command instance directly.
/// </summary>
public sealed class BusinessRuleMaintenanceOptions : EnvironmentNameOptions {
}

/// <summary>
/// Shared execution shell for the business-rule read/update/delete tools: resolves the
/// environment-scoped service first (so an unknown/unreachable environment surfaces as the
/// standard command-execution envelope, mirroring the create tools — ENG-91830 / ENG-91825),
/// then runs the operation and folds an operation failure into the tool's typed error response.
/// </summary>
internal static class BusinessRuleMaintenanceToolExecutor {

	internal static object Execute<TService>(
		IToolCommandResolver commandResolver,
		string environmentName,
		Func<TService, object> execute,
		Func<string, object> requestError) where TService : class {
		EnvironmentOptions options = new() { Environment = environmentName };
		TService service;
		try {
			service = commandResolver.Resolve<TService>(options);
		} catch (EnvironmentResolutionException exception) {
			return CommandExecutionResult.FromResolverError(exception);
		} catch (Exception exception) {
			// Unexpected resolve/bootstrap failure → exit code -1 envelope (mirrors
			// BaseTool.InternalExecute) so a real bug is not swallowed by the MCP SDK generic error.
			return CommandExecutionResult.FromException(exception);
		}

		try {
			return execute(service);
		} catch (Exception exception) {
			return requestError(exception.Message);
		}
	}
}

[McpServerToolType]
public sealed class ReadEntityBusinessRuleTool(
	IToolCommandResolver commandResolver,
	ILogger logger)
	: BaseTool<BusinessRuleMaintenanceOptions>(null, logger) {

	internal const string ToolName = "read-entity-business-rules";

	/// <summary>
	/// Reads every business rule persisted for an entity schema.
	/// </summary>
	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[Description("Reads ALL entity-level Freedom UI business rules persisted for an entity schema (full package hierarchy, so inherited rules are included). " +
		"Each convertible rule is returned in the create/update contract shape with 'name', 'enabled', and block 'uId's — pass those uIds back to update-entity-business-rules so the platform stores a short diff. " +
		"Rules the friendly contract cannot represent are returned with convertible=false and the raw add-on metadata; apply-static-filter rules carry the persisted ESQ envelope in 'esqFilter'. " +
		"Call this BEFORE updating or deleting rules to obtain exact rule names and uIds.")]
	public object BusinessRulesRead(
		[Description("environment-name, package-name, entity-schema-name (all required).")]
		[Required]
		ReadEntityBusinessRulesArgs args) =>
		ExecuteWithCleanLog(() => BusinessRuleMaintenanceToolExecutor.Execute<IEntityBusinessRuleService>(
			commandResolver,
			args.EnvironmentName,
			service => BusinessRulesReadResponse.From(service.Read(
				new EntityBusinessRulesReadRequest(args.PackageName, args.EntitySchemaName))),
			BusinessRulesReadResponse.RequestError));
}

/// <summary>
/// MCP argument wrapper for reading entity-level business rules.
/// </summary>
public sealed record ReadEntityBusinessRulesArgs {

	/// <summary>Gets the registered Creatio environment name.</summary>
	[JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
	[Required]
	public string EnvironmentName { get; init; } = null!;

	/// <summary>Gets the target package name on the Creatio environment.</summary>
	[JsonPropertyName("package-name")]
	[Description("Target package name on the Creatio environment.")]
	[Required]
	public string PackageName { get; init; } = null!;

	/// <summary>Gets the target entity schema name.</summary>
	[JsonPropertyName("entity-schema-name")]
	[Description("Target entity schema name.")]
	[Required]
	public string EntitySchemaName { get; init; } = null!;
}

[McpServerToolType]
public sealed class ReadPageBusinessRuleTool(
	IToolCommandResolver commandResolver,
	ILogger logger)
	: BaseTool<BusinessRuleMaintenanceOptions>(null, logger) {

	internal const string ToolName = "read-page-business-rules";

	/// <summary>
	/// Reads every business rule persisted for a Freedom UI page schema.
	/// </summary>
	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[Description("Reads ALL page-level Freedom UI business rules persisted for a page schema (full package hierarchy, so inherited rules are included). " +
		"Each convertible rule is returned in the create/update contract shape with 'name', 'enabled', and block 'uId's — pass those uIds back to update-page-business-rules so the platform stores a short diff. " +
		"Rules the friendly contract cannot represent are returned with convertible=false and the raw add-on metadata. " +
		"Call this BEFORE updating or deleting rules to obtain exact rule names and uIds.")]
	public object BusinessRulesRead(
		[Description("environment-name, package-name, page-schema-name (all required).")]
		[Required]
		ReadPageBusinessRulesArgs args) =>
		ExecuteWithCleanLog(() => BusinessRuleMaintenanceToolExecutor.Execute<IPageBusinessRuleService>(
			commandResolver,
			args.EnvironmentName,
			service => BusinessRulesReadResponse.From(service.Read(
				new PageBusinessRulesReadRequest(args.PackageName, args.PageSchemaName))),
			BusinessRulesReadResponse.RequestError));
}

/// <summary>
/// MCP argument wrapper for reading page-level business rules.
/// </summary>
public sealed record ReadPageBusinessRulesArgs {

	/// <summary>Gets the registered Creatio environment name.</summary>
	[JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
	[Required]
	public string EnvironmentName { get; init; } = null!;

	/// <summary>Gets the target package name on the Creatio environment.</summary>
	[JsonPropertyName("package-name")]
	[Description("Target package name on the Creatio environment.")]
	[Required]
	public string PackageName { get; init; } = null!;

	/// <summary>Gets the target Freedom UI page schema name.</summary>
	[JsonPropertyName("page-schema-name")]
	[Description("Target Freedom UI page schema name.")]
	[Required]
	public string PageSchemaName { get; init; } = null!;
}

[McpServerToolType]
public sealed class UpdateEntityBusinessRuleTool(
	IToolCommandResolver commandResolver,
	ILogger logger)
	: BaseTool<BusinessRuleMaintenanceOptions>(null, logger) {

	internal const string ToolName = "update-entity-business-rules";

	/// <summary>
	/// Updates one or more entity-level business rules matched by name in a single batch call.
	/// </summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = true,
		OpenWorld = false)]
	[Description("Updates one or more entity-level Freedom UI business rules on a single entity schema in ONE batch call (one configuration rebuild for the whole batch). " +
		"Each rule REQUIRES 'name' (the match key — read it with read-entity-business-rules first) and fully replaces the matched rule's definition; there is no partial patch. " +
		"Pass the block 'uId's returned by read to preserve identity of unchanged conditions/expressions/actions so the platform stores a short diff; omit 'enabled' to keep the current value, or set it to enable/disable the rule. " +
		"An unknown name fails only that rule; the rest of the batch still saves. " +
		"Read get-guidance `business-rules` and get-tool-contract `update-entity-business-rules` before calling.")]
	public object BusinessRulesUpdate(
		[Description("environment-name, package-name, entity-schema-name, rules (all required; every rule requires name).")]
		[Required]
		UpdateEntityBusinessRulesArgs args) =>
		ExecuteWithCleanLog(() => UpdateRules(args));

	private object UpdateRules(UpdateEntityBusinessRulesArgs args) {
		if (args.Rules is not { Count: > 0 }) {
			return BusinessRuleUpdateBatchResponse.RequestError("rules is required and must contain at least one rule.");
		}

		return BusinessRuleMaintenanceToolExecutor.Execute<IEntityBusinessRuleService>(
			commandResolver,
			args.EnvironmentName,
			service => BusinessRuleUpdateBatchResponse.From(service.Update(new EntityBusinessRulesBatchRequest(
				args.PackageName,
				args.EntitySchemaName,
				// A null array element must not collapse the whole batch: keep it as a null entry so the
				// service isolates it as a single failed item instead of throwing during the projection.
				args.Rules.Select(rule => rule?.ToBusinessRule()!).ToList()))),
			BusinessRuleUpdateBatchResponse.RequestError);
	}
}

/// <summary>
/// MCP argument wrapper for batch entity-level business-rule updates.
/// </summary>
public sealed record UpdateEntityBusinessRulesArgs {

	/// <summary>Gets the registered Creatio environment name.</summary>
	[JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
	[Required]
	public string EnvironmentName { get; init; } = null!;

	/// <summary>Gets the target package name on the Creatio environment.</summary>
	[JsonPropertyName("package-name")]
	[Description("Target package name on the Creatio environment where the layered rule diff is stored.")]
	[Required]
	public string PackageName { get; init; } = null!;

	/// <summary>Gets the target entity schema name.</summary>
	[JsonPropertyName("entity-schema-name")]
	[Description("Target entity schema name.")]
	[Required]
	public string EntitySchemaName { get; init; } = null!;

	/// <summary>Gets the rule definitions to apply; each requires <c>name</c> as the match key.</summary>
	[JsonPropertyName("rules")]
	[Description("Full replacement definitions for existing rules, matched by 'name'. Include block uIds from read-entity-business-rules to preserve unchanged-block identity.")]
	[Required]
	public List<EntityBusinessRuleMcpContract> Rules { get; init; } = [];
}

[McpServerToolType]
public sealed class UpdatePageBusinessRuleTool(
	IToolCommandResolver commandResolver,
	ILogger logger)
	: BaseTool<BusinessRuleMaintenanceOptions>(null, logger) {

	internal const string ToolName = "update-page-business-rules";

	/// <summary>
	/// Updates one or more page-level business rules matched by name in a single batch call.
	/// </summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = true,
		OpenWorld = false)]
	[Description("Updates one or more page-level Freedom UI business rules on a single page schema in ONE batch call (one configuration rebuild for the whole batch). " +
		"Each rule REQUIRES 'name' (the match key — read it with read-page-business-rules first) and fully replaces the matched rule's definition; there is no partial patch. " +
		"Pass the block 'uId's returned by read to preserve identity of unchanged conditions/expressions/actions so the platform stores a short diff; omit 'enabled' to keep the current value, or set it to enable/disable the rule. " +
		"An unknown name fails only that rule; the rest of the batch still saves. " +
		"Read get-guidance `business-rules` and get-tool-contract `update-page-business-rules` before calling.")]
	public object BusinessRulesUpdate(
		[Description("environment-name, package-name, page-schema-name, rules (all required; every rule requires name).")]
		[Required]
		UpdatePageBusinessRulesArgs args) =>
		ExecuteWithCleanLog(() => UpdateRules(args));

	private object UpdateRules(UpdatePageBusinessRulesArgs args) {
		if (args.Rules is not { Count: > 0 }) {
			return BusinessRuleUpdateBatchResponse.RequestError("rules is required and must contain at least one rule.");
		}

		return BusinessRuleMaintenanceToolExecutor.Execute<IPageBusinessRuleService>(
			commandResolver,
			args.EnvironmentName,
			service => BusinessRuleUpdateBatchResponse.From(service.Update(new PageBusinessRulesBatchRequest(
				args.PackageName,
				args.PageSchemaName,
				// A null array element must not collapse the whole batch: keep it as a null entry so the
				// service isolates it as a single failed item instead of throwing during the projection.
				args.Rules.Select(rule => rule?.ToBusinessRule()!).ToList()))),
			BusinessRuleUpdateBatchResponse.RequestError);
	}
}

/// <summary>
/// MCP argument wrapper for batch page-level business-rule updates.
/// </summary>
public sealed record UpdatePageBusinessRulesArgs {

	/// <summary>Gets the registered Creatio environment name.</summary>
	[JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
	[Required]
	public string EnvironmentName { get; init; } = null!;

	/// <summary>Gets the target package name on the Creatio environment.</summary>
	[JsonPropertyName("package-name")]
	[Description("Target package name on the Creatio environment where the layered rule diff is stored.")]
	[Required]
	public string PackageName { get; init; } = null!;

	/// <summary>Gets the target Freedom UI page schema name.</summary>
	[JsonPropertyName("page-schema-name")]
	[Description("Target Freedom UI page schema name.")]
	[Required]
	public string PageSchemaName { get; init; } = null!;

	/// <summary>Gets the rule definitions to apply; each requires <c>name</c> as the match key.</summary>
	[JsonPropertyName("rules")]
	[Description("Full replacement definitions for existing rules, matched by 'name'. Include block uIds from read-page-business-rules to preserve unchanged-block identity.")]
	[Required]
	public List<PageBusinessRuleMcpContract> Rules { get; init; } = [];
}

[McpServerToolType]
public sealed class DeleteEntityBusinessRuleTool(
	IToolCommandResolver commandResolver,
	ILogger logger)
	: BaseTool<BusinessRuleMaintenanceOptions>(null, logger) {

	internal const string ToolName = "delete-entity-business-rules";

	/// <summary>
	/// Deletes one or more entity-level business rules by name in a single batch call.
	/// </summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = true,
		OpenWorld = false)]
	[Description("Deletes one or more entity-level Freedom UI business rules by internal rule name in ONE batch call (one configuration rebuild for the whole batch). " +
		"Rule names come from read-entity-business-rules. Autogenerated apply-filter helper rules of a deleted rule are removed automatically. " +
		"An unknown name fails only that entry; the rest of the batch still deletes.")]
	public object BusinessRulesDelete(
		[Description("environment-name, package-name, entity-schema-name, rule-names (all required).")]
		[Required]
		DeleteEntityBusinessRulesArgs args) =>
		ExecuteWithCleanLog(() => DeleteRules(args));

	private object DeleteRules(DeleteEntityBusinessRulesArgs args) {
		if (args.RuleNames is not { Count: > 0 }) {
			return BusinessRuleDeleteBatchResponse.RequestError(
				"rule-names is required and must contain at least one rule name.");
		}

		return BusinessRuleMaintenanceToolExecutor.Execute<IEntityBusinessRuleService>(
			commandResolver,
			args.EnvironmentName,
			service => BusinessRuleDeleteBatchResponse.From(service.Delete(new EntityBusinessRulesDeleteRequest(
				args.PackageName,
				args.EntitySchemaName,
				args.RuleNames))),
			BusinessRuleDeleteBatchResponse.RequestError);
	}
}

/// <summary>
/// MCP argument wrapper for batch entity-level business-rule deletion.
/// </summary>
public sealed record DeleteEntityBusinessRulesArgs {

	/// <summary>Gets the registered Creatio environment name.</summary>
	[JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
	[Required]
	public string EnvironmentName { get; init; } = null!;

	/// <summary>Gets the target package name on the Creatio environment.</summary>
	[JsonPropertyName("package-name")]
	[Description("Target package name on the Creatio environment.")]
	[Required]
	public string PackageName { get; init; } = null!;

	/// <summary>Gets the target entity schema name.</summary>
	[JsonPropertyName("entity-schema-name")]
	[Description("Target entity schema name.")]
	[Required]
	public string EntitySchemaName { get; init; } = null!;

	/// <summary>Gets the internal rule names to delete.</summary>
	[JsonPropertyName("rule-names")]
	[Description("Internal rule names to delete (from read-entity-business-rules), not captions.")]
	[Required]
	public List<string> RuleNames { get; init; } = [];
}

[McpServerToolType]
public sealed class DeletePageBusinessRuleTool(
	IToolCommandResolver commandResolver,
	ILogger logger)
	: BaseTool<BusinessRuleMaintenanceOptions>(null, logger) {

	internal const string ToolName = "delete-page-business-rules";

	/// <summary>
	/// Deletes one or more page-level business rules by name in a single batch call.
	/// </summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = true,
		OpenWorld = false)]
	[Description("Deletes one or more page-level Freedom UI business rules by internal rule name in ONE batch call (one configuration rebuild for the whole batch). " +
		"Rule names come from read-page-business-rules. " +
		"An unknown name fails only that entry; the rest of the batch still deletes.")]
	public object BusinessRulesDelete(
		[Description("environment-name, package-name, page-schema-name, rule-names (all required).")]
		[Required]
		DeletePageBusinessRulesArgs args) =>
		ExecuteWithCleanLog(() => DeleteRules(args));

	private object DeleteRules(DeletePageBusinessRulesArgs args) {
		if (args.RuleNames is not { Count: > 0 }) {
			return BusinessRuleDeleteBatchResponse.RequestError(
				"rule-names is required and must contain at least one rule name.");
		}

		return BusinessRuleMaintenanceToolExecutor.Execute<IPageBusinessRuleService>(
			commandResolver,
			args.EnvironmentName,
			service => BusinessRuleDeleteBatchResponse.From(service.Delete(new PageBusinessRulesDeleteRequest(
				args.PackageName,
				args.PageSchemaName,
				args.RuleNames))),
			BusinessRuleDeleteBatchResponse.RequestError);
	}
}

/// <summary>
/// MCP argument wrapper for batch page-level business-rule deletion.
/// </summary>
public sealed record DeletePageBusinessRulesArgs {

	/// <summary>Gets the registered Creatio environment name.</summary>
	[JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
	[Required]
	public string EnvironmentName { get; init; } = null!;

	/// <summary>Gets the target package name on the Creatio environment.</summary>
	[JsonPropertyName("package-name")]
	[Description("Target package name on the Creatio environment.")]
	[Required]
	public string PackageName { get; init; } = null!;

	/// <summary>Gets the target Freedom UI page schema name.</summary>
	[JsonPropertyName("page-schema-name")]
	[Description("Target Freedom UI page schema name.")]
	[Required]
	public string PageSchemaName { get; init; } = null!;

	/// <summary>Gets the internal rule names to delete.</summary>
	[JsonPropertyName("rule-names")]
	[Description("Internal rule names to delete (from read-page-business-rules), not captions.")]
	[Required]
	public List<string> RuleNames { get; init; } = [];
}
