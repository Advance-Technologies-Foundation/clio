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

public sealed class BusinessRuleMaintenanceOptions : EnvironmentNameOptions {
}

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

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[Description("Reads ALL entity-level Freedom UI business rules persisted for an entity schema (full package hierarchy, so inherited rules are included). " +
		"Each rule is returned in the create/update contract shape with 'name', 'enabled', and block 'uId's — pass those uIds back to update-entity-business-rules so the platform stores a short diff. " +
		"apply-static-filter rules read back with the same friendly 'filter' shape used to create them. " +
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

public sealed record ReadEntityBusinessRulesArgs {

	[JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
	[Required]
	public string EnvironmentName { get; init; } = null!;

	[JsonPropertyName("package-name")]
	[Description("Target package name on the Creatio environment.")]
	[Required]
	public string PackageName { get; init; } = null!;

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

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[Description("Reads ALL page-level Freedom UI business rules persisted for a page schema (full package hierarchy, so inherited rules are included). " +
		"Each rule is returned in the create/update contract shape with 'name', 'enabled', and block 'uId's — pass those uIds back to update-page-business-rules so the platform stores a short diff. " +
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

public sealed record ReadPageBusinessRulesArgs {

	[JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
	[Required]
	public string EnvironmentName { get; init; } = null!;

	[JsonPropertyName("package-name")]
	[Description("Target package name on the Creatio environment.")]
	[Required]
	public string PackageName { get; init; } = null!;

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
				args.Rules.Select(rule => rule?.ToBusinessRule()!).ToList()))),
			BusinessRuleUpdateBatchResponse.RequestError);
	}
}

public sealed record UpdateEntityBusinessRulesArgs {

	[JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
	[Required]
	public string EnvironmentName { get; init; } = null!;

	[JsonPropertyName("package-name")]
	[Description("Target package name on the Creatio environment where the layered rule diff is stored.")]
	[Required]
	public string PackageName { get; init; } = null!;

	[JsonPropertyName("entity-schema-name")]
	[Description("Target entity schema name.")]
	[Required]
	public string EntitySchemaName { get; init; } = null!;

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
				args.Rules.Select(rule => rule?.ToBusinessRule()!).ToList()))),
			BusinessRuleUpdateBatchResponse.RequestError);
	}
}

public sealed record UpdatePageBusinessRulesArgs {

	[JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
	[Required]
	public string EnvironmentName { get; init; } = null!;

	[JsonPropertyName("package-name")]
	[Description("Target package name on the Creatio environment where the layered rule diff is stored.")]
	[Required]
	public string PackageName { get; init; } = null!;

	[JsonPropertyName("page-schema-name")]
	[Description("Target Freedom UI page schema name.")]
	[Required]
	public string PageSchemaName { get; init; } = null!;

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

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = true,
		OpenWorld = false)]
	[Description("Deletes one or more entity-level Freedom UI business rules by internal rule name in ONE batch call (one configuration rebuild for the whole batch). " +
		"Rule names come from read-entity-business-rules. Autogenerated apply-filter helper rules of a deleted rule are removed automatically. " +
		"Rules inherited from base packages are matched too — deleting one stores a layered removal in the target package. " +
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

public sealed record DeleteEntityBusinessRulesArgs {

	[JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
	[Required]
	public string EnvironmentName { get; init; } = null!;

	[JsonPropertyName("package-name")]
	[Description("Target package name on the Creatio environment.")]
	[Required]
	public string PackageName { get; init; } = null!;

	[JsonPropertyName("entity-schema-name")]
	[Description("Target entity schema name.")]
	[Required]
	public string EntitySchemaName { get; init; } = null!;

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

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = true,
		OpenWorld = false)]
	[Description("Deletes one or more page-level Freedom UI business rules by internal rule name in ONE batch call (one configuration rebuild for the whole batch). " +
		"Rule names come from read-page-business-rules. " +
		"Rules inherited from base packages are matched too — deleting one stores a layered removal in the target package. " +
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

public sealed record DeletePageBusinessRulesArgs {

	[JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
	[Required]
	public string EnvironmentName { get; init; } = null!;

	[JsonPropertyName("package-name")]
	[Description("Target package name on the Creatio environment.")]
	[Required]
	public string PackageName { get; init; } = null!;

	[JsonPropertyName("page-schema-name")]
	[Description("Target Freedom UI page schema name.")]
	[Required]
	public string PageSchemaName { get; init; } = null!;

	[JsonPropertyName("rule-names")]
	[Description("Internal rule names to delete (from read-page-business-rules), not captions.")]
	[Required]
	public List<string> RuleNames { get; init; } = [];
}
