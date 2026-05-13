using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
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
	/// Gets the top-level condition group. Optional; omit to make the rule always apply.
	/// </summary>
	[JsonPropertyName("condition")]
	[Description("Optional top-level condition group with logicalOperation AND or OR. Omit to make the rule always apply (useful for unconditional apply-static-filter).")]
	public BusinessRuleConditionGroup? Condition { get; init; }

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
[JsonDerivedType(typeof(EntityApplyStaticFilterBusinessRuleActionMcpContract), "apply-static-filter")]
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
/// MCP action contract that applies a static filter to a lookup attribute.
/// </summary>
public sealed record EntityApplyStaticFilterBusinessRuleActionMcpContract : EntityBusinessRuleActionMcpContract
{
	public EntityApplyStaticFilterBusinessRuleActionMcpContract()
	{
	}

	public EntityApplyStaticFilterBusinessRuleActionMcpContract(string targetAttribute, JsonElement filter)
	{
		TargetAttribute = targetAttribute;
		Filter = filter;
	}

	/// <summary>
	/// Gets the lookup attribute on the entity that the static filter restricts.
	/// </summary>
	[JsonPropertyName("targetAttribute")]
	[Description("Lookup attribute on the entity that the static filter restricts.")]
	[Required]
	public string TargetAttribute { get; init; } = null!;

	/// <summary>
	/// Gets the friendly filter group restricting the lookup's reference schema.
	/// </summary>
	[JsonPropertyName("filter")]
	[Description("Friendly filter group (logicalOperation + filters[] + backwardReferenceFilters[]) restricting the lookup's reference schema.")]
	[Required]
	public JsonElement Filter { get; init; }

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }

	internal override BusinessRuleAction ToBusinessRuleAction() {
		ApplyStaticFilterBusinessRuleAction action = new(TargetAttribute, Filter);
		return ExtensionData is null
			? action
			: action with { ExtensionData = new Dictionary<string, JsonElement>(ExtensionData) };
	}
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
[JsonDerivedType(typeof(PageApplyStaticFilterBusinessRuleActionMcpContract), "apply-static-filter")]
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

/// MCP action contract registered on the page polymorphic surface so apply-static-filter
/// payloads deserialize cleanly and reach the page validator, which rejects them with a
/// clear "use create-entity-business-rule instead" message.
/// </summary>
public sealed record PageApplyStaticFilterBusinessRuleActionMcpContract : PageBusinessRuleActionMcpContract
{
	[JsonPropertyName("targetAttribute")]
	public string? TargetAttribute { get; init; }

	[JsonPropertyName("filter")]
	public JsonElement Filter { get; init; }

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }

	internal override BusinessRuleAction ToBusinessRuleAction() =>
		new ApplyStaticFilterBusinessRuleAction {
			TargetAttribute = TargetAttribute ?? string.Empty,
			Filter = Filter,
			ExtensionData = ExtensionData
		};
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
