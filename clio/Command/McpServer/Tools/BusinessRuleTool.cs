using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Command.BusinessRules;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class CreateEntityBusinessRuleTool(
	IToolCommandResolver commandResolver,
	ILogger logger)
	: BaseTool<CreateEntityBusinessRuleOptions>(null, logger) {

	internal const string BusinessRuleCreateToolName = "create-entity-business-rules";

	/// <summary>
	/// Creates one or more Freedom UI business rules in a single batch call.
	/// </summary>
	/// <remarks>
	/// The declared return type is <see cref="object"/> because the method returns one of two shapes:
	/// a <see cref="BusinessRuleBatchResponse"/> with per-rule <c>created</c>/<c>failed</c>/<c>results</c>
	/// on the normal (resolved-environment) path, or a <see cref="CommandExecutionResult"/> envelope when
	/// the environment cannot be resolved. The typed values are always delivered through the MCP text
	/// Content channel; schema-strict clients should not rely on a single SDK-derived output schema.
	/// </remarks>
	[McpServerTool(Name = BusinessRuleCreateToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("Creates one or more entity-level Freedom UI business rules on a single entity schema in ONE batch call (one configuration rebuild for the whole batch): hide/show/enable/disable/require fields, set-values, apply-filter (dynamic dependent lookups), and apply-static-filter (restrict a lookup to records matching a fixed condition by ANY mechanism — attribute value, relative period, time-of-day, child existence/count, or gating by another field). " +
		"PREFER this over editing page DataSource staticFilters in body.js for any 'limit lookup / restrict lookup / show only X where …' request — entity rules apply everywhere the lookup is used. " +
		"Pass every rule for the same entity schema in the 'rules' array; a failed rule does not abort the others (the response reports per-rule status). " +
		"Read get-guidance `business-rules` / `business-rule-filters` and get-tool-contract `create-entity-business-rules` before calling.")]
	public object BusinessRuleCreate(
		[Description("environment-name, package-name, entity-schema-name, rules (all required).")]
		[Required]
		CreateEntityBusinessRulesArgs args) =>
		ExecuteWithCleanLog(() => CreateRules(args));

	private object CreateRules(CreateEntityBusinessRulesArgs args) {
		if (args.Rules is not { Count: > 0 }) {
			return BusinessRuleBatchResponse.RequestError("rules is required and must contain at least one rule.");
		}

		EnvironmentOptions options = new() { Environment = args.EnvironmentName };
		IEntityBusinessRuleService service;
		try {
			// Resolve the environment BEFORE projecting/saving rules so an unknown or unreachable
			// environment surfaces as the standard command-execution envelope (exit code 1) referencing
			// the requested environment, instead of being folded into per-rule batch results that would
			// serialize with an implicit success exit code (ENG-91830 / ENG-91825).
			service = commandResolver.Resolve<IEntityBusinessRuleService>(options);
		} catch (EnvironmentResolutionException exception) {
			return CommandExecutionResult.FromResolverError(exception);
		} catch (Exception exception) {
			// Unexpected resolve/bootstrap failure → exit code -1 envelope (mirrors
			// BaseTool.InternalExecute) so a real bug is not swallowed by the MCP SDK generic error.
			return CommandExecutionResult.FromException(exception);
		}

		try {
			IReadOnlyList<BusinessRuleBatchItemResult> results = service.Create(new EntityBusinessRulesBatchRequest(
				args.PackageName,
				args.EntitySchemaName,
				// A null array element must not collapse the whole batch: keep it as a null entry so the
				// service isolates it as a single failed item instead of throwing during the projection.
				args.Rules.Select(rule => rule?.ToBusinessRule()!).ToList()));
			return BusinessRuleBatchResponse.From(results);
		} catch (Exception exception) {
			return BusinessRuleBatchResponse.From(args.Rules
				.Select(rule => new BusinessRuleBatchItemResult(rule?.Caption ?? string.Empty, false, null, exception.Message))
				.ToList());
		}
	}
}

/// <summary>
/// MCP argument wrapper for batch entity-level business-rule creation.
/// </summary>
public sealed record CreateEntityBusinessRulesArgs
{
	/// <summary>
	/// Gets the registered Creatio environment name.
	/// </summary>
	[JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
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
	/// Gets the structured entity business-rule definitions to create in one batch.
	/// </summary>
	[JsonPropertyName("rules")]
	[Description("One or more structured entity business-rule definitions to create on this entity schema in a single batch (saved with one configuration rebuild).")]
	[Required]
	public List<EntityBusinessRuleMcpContract> Rules { get; init; } = [];
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
	/// Gets the internal unique rule name. Optional on create (generated when omitted);
	/// the required match key for update.
	/// </summary>
	[JsonPropertyName("name")]
	[Description("Internal unique rule name. Optional on create (generated when omitted); required match key for update.")]
	public string? Name { get; init; }

	/// <summary>
	/// Gets whether the rule is active. Defaults to <c>true</c> on create; omitted on update
	/// preserves the existing value.
	/// </summary>
	[JsonPropertyName("enabled")]
	[Description("Whether the rule is active. Defaults to true on create; omitted on update preserves the existing value.")]
	public bool? Enabled { get; init; }

	/// <summary>
	/// Converts this MCP contract into the shared internal business-rule model.
	/// </summary>
	/// <returns>Shared business-rule model used by command services.</returns>
	public BusinessRule ToBusinessRule() {
		List<BusinessRuleAction> actions = [];
		foreach (EntityBusinessRuleActionMcpContract? action in Actions ?? []) {
			actions.Add(action?.ToBusinessRuleAction()!);
		}

		return new BusinessRule(Caption, Condition, actions) { Name = Name, Enabled = Enabled };
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
[JsonDerivedType(typeof(EntityApplyStaticFilterBusinessRuleActionMcpContract), "apply-static-filter")]
public abstract record EntityBusinessRuleActionMcpContract
{
	protected EntityBusinessRuleActionMcpContract()
	{
	}

	/// <summary>
	/// Stable action identity (GUID). Returned by read; pass it back on update to preserve
	/// block identity. Ignored on create.
	/// </summary>
	[JsonPropertyName("uId")]
	[Description("Stable action identity (GUID) returned by read. Pass it back on update to preserve block identity; ignored on create.")]
	public string? UId { get; init; }

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

	internal override BusinessRuleAction ToBusinessRuleAction() => new MakeEditableBusinessRuleAction(Items ?? []) { UId = UId };
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

	internal override BusinessRuleAction ToBusinessRuleAction() => new MakeReadOnlyBusinessRuleAction(Items ?? []) { UId = UId };
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

	internal override BusinessRuleAction ToBusinessRuleAction() => new MakeRequiredBusinessRuleAction(Items ?? []) { UId = UId };
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

	internal override BusinessRuleAction ToBusinessRuleAction() => new MakeOptionalBusinessRuleAction(Items ?? []) { UId = UId };
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

	internal override BusinessRuleAction ToBusinessRuleAction() => new SetValuesBusinessRuleAction(Items ?? []) { UId = UId };
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
		PopulateValue) { UId = UId };
}

/// <summary>
/// MCP action contract that applies a static ESQ filter to a lookup attribute on the entity.
/// </summary>
public sealed record EntityApplyStaticFilterBusinessRuleActionMcpContract : EntityBusinessRuleActionMcpContract
{
	/// <summary>
	/// Gets the target lookup attribute on the root entity whose dropdown is filtered.
	/// </summary>
	[JsonPropertyName("targetAttribute")]
	[Description("Target lookup attribute on the root entity. The lookup's reference schema is used as the filter root; rootSchemaName is never accepted from the caller.")]
	[Required]
	public string TargetAttribute { get; init; } = string.Empty;

	/// <summary>
	/// Gets the friendly filter definition.
	/// </summary>
	[JsonPropertyName("filter")]
	[Description("Friendly filter definition for apply-static-filter. Use get-guidance name=business-rule-filters for the full contract.")]
	[Required]
	public JsonElement Filter { get; init; }

	internal override BusinessRuleAction ToBusinessRuleAction() =>
		new ApplyStaticFilterBusinessRuleAction(TargetAttribute, Filter) { UId = UId };
}

/// <summary>
/// Page-variant of apply-static-filter exists only so payloads deserialize cleanly; page validator rejects it.
/// </summary>
public sealed record PageApplyStaticFilterBusinessRuleActionMcpContract : PageBusinessRuleActionMcpContract
{
	[JsonPropertyName("targetAttribute")]
	public string TargetAttribute { get; init; } = string.Empty;

	[JsonPropertyName("filter")]
	public JsonElement Filter { get; init; }

	internal override BusinessRuleAction ToBusinessRuleAction() =>
		new ApplyStaticFilterBusinessRuleAction(TargetAttribute, Filter) { UId = UId };
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
	/// Gets the internal unique rule name. Optional on create (generated when omitted);
	/// the required match key for update.
	/// </summary>
	[JsonPropertyName("name")]
	[Description("Internal unique rule name. Optional on create (generated when omitted); required match key for update.")]
	public string? Name { get; init; }

	/// <summary>
	/// Gets whether the rule is active. Defaults to <c>true</c> on create; omitted on update
	/// preserves the existing value.
	/// </summary>
	[JsonPropertyName("enabled")]
	[Description("Whether the rule is active. Defaults to true on create; omitted on update preserves the existing value.")]
	public bool? Enabled { get; init; }

	/// <summary>
	/// Converts this MCP contract into the shared internal business-rule model.
	/// </summary>
	/// <returns>Shared business-rule model used by command services.</returns>
	public BusinessRule ToBusinessRule() {
		List<BusinessRuleAction> actions = [];
		foreach (PageBusinessRuleActionMcpContract? action in Actions ?? []) {
			actions.Add(action?.ToBusinessRuleAction()!);
		}

		return new BusinessRule(Caption, Condition, actions) { Name = Name, Enabled = Enabled };
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
[JsonDerivedType(typeof(PageApplyStaticFilterBusinessRuleActionMcpContract), "apply-static-filter")]
public abstract record PageBusinessRuleActionMcpContract
{
	protected PageBusinessRuleActionMcpContract()
	{
	}

	/// <summary>
	/// Stable action identity (GUID). Returned by read; pass it back on update to preserve
	/// block identity. Ignored on create.
	/// </summary>
	[JsonPropertyName("uId")]
	[Description("Stable action identity (GUID) returned by read. Pass it back on update to preserve block identity; ignored on create.")]
	public string? UId { get; init; }

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

	internal override BusinessRuleAction ToBusinessRuleAction() => new HideElementBusinessRuleAction(Items ?? []) { UId = UId };
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

	internal override BusinessRuleAction ToBusinessRuleAction() => new ShowElementBusinessRuleAction(Items ?? []) { UId = UId };
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

	internal override BusinessRuleAction ToBusinessRuleAction() => new MakeEditableBusinessRuleAction(Items ?? []) { UId = UId };
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

	internal override BusinessRuleAction ToBusinessRuleAction() => new MakeReadOnlyBusinessRuleAction(Items ?? []) { UId = UId };
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

	internal override BusinessRuleAction ToBusinessRuleAction() => new MakeRequiredBusinessRuleAction(Items ?? []) { UId = UId };
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

	internal override BusinessRuleAction ToBusinessRuleAction() => new MakeOptionalBusinessRuleAction(Items ?? []) { UId = UId };
}

[McpServerToolType]
public sealed class CreatePageBusinessRuleTool(
	IToolCommandResolver commandResolver,
	ILogger logger)
	: BaseTool<CreatePageBusinessRuleOptions>(null, logger) {

	internal const string BusinessRuleCreateToolName = "create-page-business-rules";

	/// <summary>
	/// Creates one or more Freedom UI business rules in a single batch call.
	/// </summary>
	/// <remarks>
	/// The declared return type is <see cref="object"/> because the method returns one of two shapes:
	/// a <see cref="BusinessRuleBatchResponse"/> with per-rule <c>created</c>/<c>failed</c>/<c>results</c>
	/// on the normal (resolved-environment) path, or a <see cref="CommandExecutionResult"/> envelope when
	/// the environment cannot be resolved. The typed values are always delivered through the MCP text
	/// Content channel; schema-strict clients should not rely on a single SDK-derived output schema.
	/// </remarks>
	[McpServerTool(Name = BusinessRuleCreateToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("Creates one or more page-level Freedom UI business rules on a single page in ONE batch call (one configuration rebuild for the whole batch) that change page element visibility, editability, or required state. Pass every rule for the same page in the 'rules' array and prefer one batch call over many single-rule calls; a failed rule does not abort the others and the response reports per-rule status. Before calling, read get-guidance business-rules and get-tool-contract for create-page-business-rules.")]
	public object BusinessRuleCreate(
		[Description("Parameters: environment-name, package-name, page-schema-name, rules (all required).")]
		[Required]
		CreatePageBusinessRulesArgs args) =>
		ExecuteWithCleanLog(() => CreateRules(args));

	private object CreateRules(CreatePageBusinessRulesArgs args) {
		if (args.Rules is not { Count: > 0 }) {
			return BusinessRuleBatchResponse.RequestError("rules is required and must contain at least one rule.");
		}

		EnvironmentOptions options = new() { Environment = args.EnvironmentName };
		IPageBusinessRuleService service;
		try {
			// Resolve the environment BEFORE projecting/saving rules so an unknown or unreachable
			// environment surfaces as the standard command-execution envelope (exit code 1) referencing
			// the requested environment, instead of being folded into per-rule batch results that would
			// serialize with an implicit success exit code (ENG-91830 / ENG-91825).
			service = commandResolver.Resolve<IPageBusinessRuleService>(options);
		} catch (EnvironmentResolutionException exception) {
			return CommandExecutionResult.FromResolverError(exception);
		} catch (Exception exception) {
			// Unexpected resolve/bootstrap failure → exit code -1 envelope (mirrors
			// BaseTool.InternalExecute) so a real bug is not swallowed by the MCP SDK generic error.
			return CommandExecutionResult.FromException(exception);
		}

		try {
			IReadOnlyList<BusinessRuleBatchItemResult> results = service.Create(new PageBusinessRulesBatchRequest(
				args.PackageName,
				args.PageSchemaName,
				// A null array element must not collapse the whole batch: keep it as a null entry so the
				// service isolates it as a single failed item instead of throwing during the projection.
				args.Rules.Select(rule => rule?.ToBusinessRule()!).ToList()));
			return BusinessRuleBatchResponse.From(results);
		} catch (Exception exception) {
			return BusinessRuleBatchResponse.From(args.Rules
				.Select(rule => new BusinessRuleBatchItemResult(rule?.Caption ?? string.Empty, false, null, exception.Message))
				.ToList());
		}
	}
}

/// <summary>
/// MCP argument wrapper for batch page-level business-rule creation.
/// </summary>
public sealed record CreatePageBusinessRulesArgs
{
	/// <summary>
	/// Gets the registered Creatio environment name.
	/// </summary>
	[JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
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
	/// Gets the structured page business-rule definitions to create in one batch.
	/// </summary>
	[JsonPropertyName("rules")]
	[Description("One or more structured page business-rule definitions to create on this page in a single batch (saved with one configuration rebuild). Use declared page attribute names from get-page bundle.viewModelConfig.attributes and page element names from bundle.viewConfig.")]
	[Required]
	public List<PageBusinessRuleMcpContract> Rules { get; init; } = [];
}
