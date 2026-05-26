using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
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
	
		[Description("Creates an entity-level Freedom UI business rule. Before calling, read get-guidance business-rules and get-tool-contract for create-entity-business-rule.")]
	public CommandExecutionResult BusinessRuleCreate(
		[Description("Parameters: environment-name, package-name, entity-schema-name, rule (all required).")]
		[Required]
		CreateEntityBusinessRuleRunArgs args) {
		CreateEntityBusinessRuleOptions options = new () {
			EnvironmentName = args.EnvironmentName,
			PackageName = args.PackageName,
			EntitySchemaName = args.EntitySchemaName,
			Rule = args.Rule?.ToBusinessRule()!
		};
		return InternalExecute<CreateEntityBusinessRuleCommand>(options);
	}
}

/// <summary>
/// MCP argument wrapper for entity-level business-rule creation.
/// </summary>
public sealed record CreateEntityBusinessRuleRunArgs : ClioRunArgs
{
	/// <summary>
	/// Gets the registered Creatio environment name.
	/// </summary>
	[JsonPropertyName("environment-name")]
	[Description("Creatio environment name.")]
	[Required]
	public string EnvironmentName { get; init; } = null!;

	/// <summary>
	/// Gets the target package name on the Creatio environment.
	/// </summary>
	[JsonPropertyName("package-name")]
	[Description("Target package name on the Creatio environment.")]
	[Required]
	public string PackageName { get; init; } = null!;

	/// <summary>
	/// Gets the target entity schema name.
	/// </summary>
	[JsonPropertyName("entity-schema-name")]
	[Description("Target entity schema name.")]
	[Required]
	public string EntitySchemaName { get; init; } = null!;

	/// <summary>
	/// Gets the structured entity business-rule definition.
	/// </summary>
	[JsonPropertyName("rule")]
	[Description("Structured entity business-rule definition.")]
	[Required]
	public EntityBusinessRuleMcpContract Rule { get; init; } = null!;
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
[JsonDerivedType(typeof(EntityApplyFilterBusinessRuleActionMcpContract), "apply-filter")]
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
/// MCP action contract that applies dynamic filtering to a lookup field.
/// </summary>
public sealed record EntityApplyFilterBusinessRuleActionMcpContract : EntityBusinessRuleActionMcpContract
{
	/// <summary>
	/// Gets the target lookup attribute on the root entity.
	/// </summary>
	[JsonPropertyName("target")]
	[Description("Target lookup attribute on the root entity.")]
	[Required]
	public string Target { get; init; } = string.Empty;

	/// <summary>
	/// Gets the path inside the target lookup schema used for filtering.
	/// </summary>
	[JsonPropertyName("targetFilterPath")]
	[Description("Lookup-valued path inside the target lookup schema used for filtering. Must resolve to a Lookup attribute, not Guid, for example Country or Country.TimeZone")]
	[Required]
	public string TargetFilterPath { get; init; } = string.Empty;

	/// <summary>
	/// Gets the source lookup attribute on the root entity.
	/// </summary>
	[JsonPropertyName("source")]
	[Description("Source lookup attribute on the root entity.")]
	[Required]
	public string Source { get; init; } = string.Empty;

	/// <summary>
	/// Gets the optional path inside the source lookup schema used on the right side of the filter comparison.
	/// </summary>
	[JsonPropertyName("sourceFilterPath")]
	[Description("Optional lookup-valued path inside the source lookup schema used on the right side of the filter comparison. Must resolve to a Lookup attribute, not Guid.")]
	public string? SourceFilterPath { get; init; }

	/// <summary>
	/// Gets a value indicating whether the target lookup should be cleared when the source changes.
	/// </summary>
	[JsonPropertyName("clearValue")]
	[Description("When true, clears the target lookup when the source lookup value changes.")]
	public bool ClearValue { get; init; }

	/// <summary>
	/// Gets a value indicating whether the source lookup should be populated from the chosen target value.
	/// </summary>
	[JsonPropertyName("populateValue")]
	[Description("When true, populates the source lookup from the chosen target lookup value. Prefer true by default for standard dependent-lookup scenarios unless the user explicitly wants one-way filtering only.")]
	public bool PopulateValue { get; init; }

	internal override BusinessRuleAction ToBusinessRuleAction() => new ApplyFilterBusinessRuleAction(
		Target,
		TargetFilterPath,
		Source,
		SourceFilterPath,
		ClearValue,
		PopulateValue);
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

		[Description("Creates a page-level Freedom UI business rule that changes page element visibility, editability, or required state. Before calling, read get-guidance business-rules and get-tool-contract for create-page-business-rule.")]
	public CommandExecutionResult BusinessRuleCreate(
		[Description("Parameters: environment-name, package-name, page-schema-name, rule (all required).")]
		[Required]
		CreatePageBusinessRuleRunArgs args) {
		CreatePageBusinessRuleOptions options = new () {
			EnvironmentName = args.EnvironmentName,
			PackageName = args.PackageName,
			PageSchemaName = args.PageSchemaName,
			Rule = args.Rule?.ToBusinessRule()!
		};
		return InternalExecute<CreatePageBusinessRuleCommand>(options);
	}
}

/// <summary>
/// MCP argument wrapper for page-level business-rule creation.
/// </summary>
public sealed record CreatePageBusinessRuleRunArgs : ClioRunArgs
{
	/// <summary>
	/// Gets the registered Creatio environment name.
	/// </summary>
	[JsonPropertyName("environment-name")]
	[Description("Creatio environment name.")]
	[Required]
	public string EnvironmentName { get; init; } = null!;

	/// <summary>
	/// Gets the target package name on the Creatio environment.
	/// </summary>
	[JsonPropertyName("package-name")]
	[Description("Target package name on the Creatio environment.")]
	[Required]
	public string PackageName { get; init; } = null!;

	/// <summary>
	/// Gets the target Freedom UI page schema name.
	/// </summary>
	[JsonPropertyName("page-schema-name")]
	[Description("Target Freedom UI page schema name.")]
	[Required]
	public string PageSchemaName { get; init; } = null!;

	/// <summary>
	/// Gets the structured page business-rule definition.
	/// </summary>
	[JsonPropertyName("rule")]
	[Description("Structured page business-rule definition. Use declared page attribute names from get-page bundle.viewModelConfig.attributes and page element names from bundle.viewConfig.")]
	[Required]
	public PageBusinessRuleMcpContract Rule { get; init; } = null!;
}
