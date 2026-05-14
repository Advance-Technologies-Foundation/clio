using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using System;
using Clio.Command.BusinessRules;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class CreateEntityBusinessRuleTool(
	CreateEntityBusinessRuleCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<CreateEntityBusinessRuleOptions>(command, logger, commandResolver) {
	
	internal const string BusinessRuleCreateToolName = "create-entity-business-rule";
	
	[McpServerTool(Name = BusinessRuleCreateToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("Creates an entity-level Freedom UI business rule.")]
	public CommandExecutionResult BusinessRuleCreate(
		[Description("Creatio environment name.")]
		[Required]
		string environmentName,
		[Description("Target package name on the Creatio environment.")]
		[Required]
		string packageName,
		[Description("Target entity schema name.")]
		[Required]
		string entitySchemaName,
		[Description("Structured entity business-rule definition.")]
		[Required]
		EntityBusinessRuleMcpContract rule) {
		CreateEntityBusinessRuleOptions options = new () {
			EnvironmentName = environmentName,
			PackageName = packageName,
			EntitySchemaName = entitySchemaName,
			Rule = rule?.ToBusinessRule()!
		};
		return InternalExecute<CreateEntityBusinessRuleCommand>(options);
	}
}

/// <summary>
/// MCP contract for an entity-level Freedom UI business rule.
/// </summary>
public sealed record EntityBusinessRuleMcpContract
{
	public EntityBusinessRuleMcpContract()
	{
	}

	public EntityBusinessRuleMcpContract(
		string caption,
		BusinessRuleConditionGroup condition,
		List<EntityBusinessRuleActionMcpContract> actions)
	{
		Caption = caption;
		Condition = condition;
		Actions = actions;
	}

	/// <summary>
	/// Gets the business-rule caption shown to No-code developers.
	/// </summary>
	[JsonPropertyName("caption")]
	[Description("Business-rule caption shown to No-code developers.")]
	[Required]
	public string Caption { get; init; } = null!;

	/// <summary>
	/// Gets the top-level condition group.
	/// </summary>
	[JsonPropertyName("condition")]
	[Description("Top-level condition group. Supports one group with logicalOperation AND or OR.")]
	[Required]
	public BusinessRuleConditionGroup Condition { get; init; } = null!;

	/// <summary>
	/// Gets the entity-level actions to execute when the condition group matches.
	/// </summary>
	[JsonPropertyName("actions")]
	[Description("One or more entity-level actions to execute when the condition group matches.")]
	[Required]
	public List<EntityBusinessRuleActionMcpContract> Actions { get; init; } = null!;

	/// <summary>
	/// Converts this MCP contract into the shared internal business-rule model.
	/// </summary>
	/// <returns>Shared business-rule model used by command services.</returns>
	public BusinessRule ToBusinessRule() {
		List<BusinessRuleAction> actions = [];
		foreach (EntityBusinessRuleActionMcpContract? action in Actions ?? []) {
			actions.Add(action?.ToBusinessRuleAction()!);
		}

		return new BusinessRule(Caption, Condition, actions);
	}
}

/// <summary>
/// Base MCP action contract for entity-level business rules.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(EntityMakeEditableBusinessRuleActionMcpContract), "make-editable")]
[JsonDerivedType(typeof(EntityMakeReadOnlyBusinessRuleActionMcpContract), "make-read-only")]
[JsonDerivedType(typeof(EntityMakeRequiredBusinessRuleActionMcpContract), "make-required")]
[JsonDerivedType(typeof(EntityMakeOptionalBusinessRuleActionMcpContract), "make-optional")]
[JsonDerivedType(typeof(EntitySetValuesBusinessRuleActionMcpContract), "set-values")]
public abstract record EntityBusinessRuleActionMcpContract
{
	protected EntityBusinessRuleActionMcpContract()
	{
	}

	internal abstract BusinessRuleAction ToBusinessRuleAction();
}

/// <summary>
/// Base MCP action contract for entity field-selection actions.
/// </summary>
public abstract record EntityFieldSelectionBusinessRuleActionMcpContract : EntityBusinessRuleActionMcpContract
{
	protected EntityFieldSelectionBusinessRuleActionMcpContract()
	{
	}

	protected EntityFieldSelectionBusinessRuleActionMcpContract(List<string> items)
	{
		Items = items ?? [];
	}

	/// <summary>
	/// Gets target entity attribute names.
	/// </summary>
	[JsonPropertyName("items")]
	[Description("Target entity attribute names.")]
	[Required]
	public List<string> Items { get; init; } = [];
}

/// <summary>
/// MCP action contract that makes entity fields editable.
/// </summary>
public sealed record EntityMakeEditableBusinessRuleActionMcpContract : EntityFieldSelectionBusinessRuleActionMcpContract
{
	public EntityMakeEditableBusinessRuleActionMcpContract()
	{
	}

	public EntityMakeEditableBusinessRuleActionMcpContract(List<string> items) : base(items)
	{
	}

	internal override BusinessRuleAction ToBusinessRuleAction() => new MakeEditableBusinessRuleAction(Items ?? []);
}

/// <summary>
/// MCP action contract that makes entity fields read-only.
/// </summary>
public sealed record EntityMakeReadOnlyBusinessRuleActionMcpContract : EntityFieldSelectionBusinessRuleActionMcpContract
{
	public EntityMakeReadOnlyBusinessRuleActionMcpContract()
	{
	}

	public EntityMakeReadOnlyBusinessRuleActionMcpContract(List<string> items) : base(items)
	{
	}

	internal override BusinessRuleAction ToBusinessRuleAction() => new MakeReadOnlyBusinessRuleAction(Items ?? []);
}

/// <summary>
/// MCP action contract that makes entity fields required.
/// </summary>
public sealed record EntityMakeRequiredBusinessRuleActionMcpContract : EntityFieldSelectionBusinessRuleActionMcpContract
{
	public EntityMakeRequiredBusinessRuleActionMcpContract()
	{
	}

	public EntityMakeRequiredBusinessRuleActionMcpContract(List<string> items) : base(items)
	{
	}

	internal override BusinessRuleAction ToBusinessRuleAction() => new MakeRequiredBusinessRuleAction(Items ?? []);
}

/// <summary>
/// MCP action contract that makes entity fields optional.
/// </summary>
public sealed record EntityMakeOptionalBusinessRuleActionMcpContract : EntityFieldSelectionBusinessRuleActionMcpContract
{
	public EntityMakeOptionalBusinessRuleActionMcpContract()
	{
	}

	public EntityMakeOptionalBusinessRuleActionMcpContract(List<string> items) : base(items)
	{
	}

	internal override BusinessRuleAction ToBusinessRuleAction() => new MakeOptionalBusinessRuleAction(Items ?? []);
}

/// <summary>
/// MCP action contract that assigns constants, formulas, or attribute values to entity attributes.
/// </summary>
public sealed record EntitySetValuesBusinessRuleActionMcpContract : EntityBusinessRuleActionMcpContract
{
	public EntitySetValuesBusinessRuleActionMcpContract()
	{
	}

	public EntitySetValuesBusinessRuleActionMcpContract(List<BusinessRuleSetValueItem> items)
	{
		Items = items ?? [];
	}

	/// <summary>
	/// Gets target/value assignment items.
	/// </summary>
	[JsonPropertyName("items")]
	[Description("Target/value assignment items for Set values actions.")]
	[Required]
	public List<BusinessRuleSetValueItem> Items { get; init; } = [];

	internal override BusinessRuleAction ToBusinessRuleAction() => new SetValuesBusinessRuleAction(Items ?? []);
}

/// <summary>
/// MCP contract for a page-level Freedom UI business rule.
/// </summary>
public sealed record PageBusinessRuleMcpContract
{
	public PageBusinessRuleMcpContract()
	{
	}

	public PageBusinessRuleMcpContract(
		string caption,
		BusinessRuleConditionGroup condition,
		List<PageBusinessRuleActionMcpContract> actions)
	{
		Caption = caption;
		Condition = condition;
		Actions = actions;
	}

	/// <summary>
	/// Gets the business-rule caption shown to No-code developers.
	/// </summary>
	[JsonPropertyName("caption")]
	[Description("Business-rule caption shown to No-code developers.")]
	[Required]
	public string Caption { get; init; } = null!;

	/// <summary>
	/// Gets the top-level condition group.
	/// </summary>
	[JsonPropertyName("condition")]
	[Description("Top-level condition group. Supports one group with logicalOperation AND or OR.")]
	[Required]
	public BusinessRuleConditionGroup Condition { get; init; } = null!;

	/// <summary>
	/// Gets the page-level actions to execute when the condition group matches.
	/// </summary>
	[JsonPropertyName("actions")]
	[Description("One or more page-level actions to execute when the condition group matches.")]
	[Required]
	public List<PageBusinessRuleActionMcpContract> Actions { get; init; } = null!;

	/// <summary>
	/// Converts this MCP contract into the shared internal business-rule model.
	/// </summary>
	/// <returns>Shared business-rule model used by command services.</returns>
	public BusinessRule ToBusinessRule() {
		List<BusinessRuleAction> actions = [];
		foreach (PageBusinessRuleActionMcpContract? action in Actions ?? []) {
			actions.Add(action?.ToBusinessRuleAction()!);
		}

		return new BusinessRule(Caption, Condition, actions);
	}
}

/// <summary>
/// Base MCP action contract for page-level business rules.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(PageHideElementBusinessRuleActionMcpContract), "hide-element")]
[JsonDerivedType(typeof(PageShowElementBusinessRuleActionMcpContract), "show-element")]
[JsonDerivedType(typeof(PageMakeEditableBusinessRuleActionMcpContract), "make-editable")]
[JsonDerivedType(typeof(PageMakeReadOnlyBusinessRuleActionMcpContract), "make-read-only")]
[JsonDerivedType(typeof(PageMakeRequiredBusinessRuleActionMcpContract), "make-required")]
[JsonDerivedType(typeof(PageMakeOptionalBusinessRuleActionMcpContract), "make-optional")]
public abstract record PageBusinessRuleActionMcpContract
{
	protected PageBusinessRuleActionMcpContract()
	{
	}

	internal abstract BusinessRuleAction ToBusinessRuleAction();
}

/// <summary>
/// Base MCP action contract for page element-selection actions.
/// </summary>
public abstract record PageElementSelectionBusinessRuleActionMcpContract : PageBusinessRuleActionMcpContract
{
	protected PageElementSelectionBusinessRuleActionMcpContract()
	{
	}

	protected PageElementSelectionBusinessRuleActionMcpContract(List<string> items)
	{
		Items = items ?? [];
	}

	/// <summary>
	/// Gets target page element names.
	/// </summary>
	[JsonPropertyName("items")]
	[Description("Target page element names from recursive viewConfig.")]
	[Required]
	public List<string> Items { get; init; } = [];
}

/// <summary>
/// MCP action contract that hides page elements.
/// </summary>
public sealed record PageHideElementBusinessRuleActionMcpContract : PageElementSelectionBusinessRuleActionMcpContract
{
	public PageHideElementBusinessRuleActionMcpContract()
	{
	}

	public PageHideElementBusinessRuleActionMcpContract(List<string> items) : base(items)
	{
	}

	internal override BusinessRuleAction ToBusinessRuleAction() => new HideElementBusinessRuleAction(Items ?? []);
}

/// <summary>
/// MCP action contract that shows page elements.
/// </summary>
public sealed record PageShowElementBusinessRuleActionMcpContract : PageElementSelectionBusinessRuleActionMcpContract
{
	public PageShowElementBusinessRuleActionMcpContract()
	{
	}

	public PageShowElementBusinessRuleActionMcpContract(List<string> items) : base(items)
	{
	}

	internal override BusinessRuleAction ToBusinessRuleAction() => new ShowElementBusinessRuleAction(Items ?? []);
}

/// <summary>
/// MCP action contract that makes page elements editable.
/// </summary>
public sealed record PageMakeEditableBusinessRuleActionMcpContract : PageElementSelectionBusinessRuleActionMcpContract
{
	public PageMakeEditableBusinessRuleActionMcpContract()
	{
	}

	public PageMakeEditableBusinessRuleActionMcpContract(List<string> items) : base(items)
	{
	}

	internal override BusinessRuleAction ToBusinessRuleAction() => new MakeEditableBusinessRuleAction(Items ?? []);
}

/// <summary>
/// MCP action contract that makes page elements read-only.
/// </summary>
public sealed record PageMakeReadOnlyBusinessRuleActionMcpContract : PageElementSelectionBusinessRuleActionMcpContract
{
	public PageMakeReadOnlyBusinessRuleActionMcpContract()
	{
	}

	public PageMakeReadOnlyBusinessRuleActionMcpContract(List<string> items) : base(items)
	{
	}

	internal override BusinessRuleAction ToBusinessRuleAction() => new MakeReadOnlyBusinessRuleAction(Items ?? []);
}

/// <summary>
/// MCP action contract that makes page elements required.
/// </summary>
public sealed record PageMakeRequiredBusinessRuleActionMcpContract : PageElementSelectionBusinessRuleActionMcpContract
{
	public PageMakeRequiredBusinessRuleActionMcpContract()
	{
	}

	public PageMakeRequiredBusinessRuleActionMcpContract(List<string> items) : base(items)
	{
	}

	internal override BusinessRuleAction ToBusinessRuleAction() => new MakeRequiredBusinessRuleAction(Items ?? []);
}

/// <summary>
/// MCP action contract that makes page elements optional.
/// </summary>
public sealed record PageMakeOptionalBusinessRuleActionMcpContract : PageElementSelectionBusinessRuleActionMcpContract
{
	public PageMakeOptionalBusinessRuleActionMcpContract()
	{
	}

	public PageMakeOptionalBusinessRuleActionMcpContract(List<string> items) : base(items)
	{
	}

	internal override BusinessRuleAction ToBusinessRuleAction() => new MakeOptionalBusinessRuleAction(Items ?? []);
}

[McpServerToolType]
public sealed class CreatePageBusinessRuleTool(
	CreatePageBusinessRuleCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<CreatePageBusinessRuleOptions>(command, logger, commandResolver) {

	internal const string BusinessRuleCreateToolName = "create-page-business-rule";

	[McpServerTool(Name = BusinessRuleCreateToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("Creates a page-level Freedom UI business rule that changes page element visibility, editability, or required state.")]
	public CommandExecutionResult BusinessRuleCreate(
		[Description("Creatio environment name.")]
		[Required]
		string environmentName,
		[Description("Target package name on the Creatio environment.")]
		[Required]
		string packageName,
		[Description("Target Freedom UI page schema name.")]
		[Required]
		string pageSchemaName,
		[Description("Structured page business-rule definition. Use declared page attribute names from get-page bundle.viewModelConfig.attributes and page element names from bundle.viewConfig.")]
		[Required]
		PageBusinessRuleMcpContract rule) {
		CreatePageBusinessRuleOptions options = new () {
			EnvironmentName = environmentName,
			PackageName = packageName,
			PageSchemaName = pageSchemaName,
			Rule = rule?.ToBusinessRule()!
		};
		return InternalExecute<CreatePageBusinessRuleCommand>(options);
	}
}

[McpServerToolType]
public sealed class BusinessRuleReadTool(
	BusinessRuleReadCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<BusinessRuleReadOptions>(command, logger, commandResolver) {

	internal const string BusinessRuleListToolName = "list-business-rules";
	internal const string BusinessRuleGetToolName = "get-business-rule";

	[McpServerTool(Name = BusinessRuleListToolName, ReadOnly = true, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[Description("Lists normalized entity or Freedom UI page business rules from a Creatio environment.")]
	public BusinessRuleListResponse BusinessRuleList(
		[Description("Creatio environment name.")]
		[Required]
		string environmentName,
		[Description("Business-rule scope type. Supported values: entity, page.")]
		[Required]
		string scopeType,
		[Description("Entity schema name when scopeType is entity; Freedom UI page schema name when scopeType is page.")]
		[Required]
		string schemaName) {
		BusinessRuleReadOptions options = new() {
			EnvironmentName = environmentName,
			ScopeType = scopeType,
			SchemaName = schemaName
		};
		return ExecuteRead(options, resolvedCommand => resolvedCommand.List(options));
	}

	[McpServerTool(Name = BusinessRuleGetToolName, ReadOnly = true, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[Description("Gets one normalized entity or Freedom UI page business rule from a Creatio environment.")]
	public BusinessRuleGetResponse BusinessRuleGet(
		[Description("Creatio environment name.")]
		[Required]
		string environmentName,
		[Description("Business-rule scope type. Supported values: entity, page.")]
		[Required]
		string scopeType,
		[Description("Entity schema name when scopeType is entity; Freedom UI page schema name when scopeType is page.")]
		[Required]
		string schemaName,
		[Description("Business-rule UID. Preferred selector. Provide exactly one of ruleUId, ruleName, or caption.")]
		string? ruleUId = null,
		[Description("Business-rule platform name, for example BusinessRule_1234567. Provide exactly one of ruleUId, ruleName, or caption.")]
		string? ruleName = null,
		[Description("Business-rule caption. Provide exactly one of ruleUId, ruleName, or caption.")]
		string? caption = null) {
		BusinessRuleReadOptions options = new() {
			EnvironmentName = environmentName,
			ScopeType = scopeType,
			SchemaName = schemaName,
			RuleUId = ruleUId,
			RuleName = ruleName,
			Caption = caption
		};
		return ExecuteRead(options, resolvedCommand => resolvedCommand.Get(options));
	}

	private TResponse ExecuteRead<TResponse>(
		BusinessRuleReadOptions options,
		Func<BusinessRuleReadCommand, TResponse> execute)
		where TResponse : class {
		lock (CommandExecutionSyncRoot) {
			try {
				BusinessRuleReadCommand resolvedCommand = ResolveCommand<BusinessRuleReadCommand>(options);
				return execute(resolvedCommand);
			} catch (Exception ex) {
				return CreateErrorResponse<TResponse>(options, ex.Message);
			}
		}
	}

	private static TResponse CreateErrorResponse<TResponse>(
		BusinessRuleReadOptions options,
		string error)
		where TResponse : class {
		if (typeof(TResponse) == typeof(BusinessRuleListResponse)) {
			return (TResponse)(object)new BusinessRuleListResponse {
				Success = false,
				ScopeType = options.ScopeType,
				SchemaName = options.SchemaName,
				Error = error
			};
		}

		return (TResponse)(object)new BusinessRuleGetResponse {
			Success = false,
			ScopeType = options.ScopeType,
			SchemaName = options.SchemaName,
			Error = error
		};
	}
}
