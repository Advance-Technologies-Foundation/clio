using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clio.Command.BusinessRules;

public sealed record BusinessRule
{
    public BusinessRule()
    {
    }

    public BusinessRule(string caption, BusinessRuleConditionGroup condition, List<BusinessRuleAction> actions)
    {
        Caption = caption;
        Condition = condition;
        Actions = actions;
    }

    [JsonPropertyName("caption")]
    [Description("Business-rule caption shown to No-code developers.")]
    [Required]
    public string Caption { get; init; } = null!;

    [JsonPropertyName("condition")]
    [Description("Top-level condition group. Supports one group with logicalOperation AND or OR.")]
    [Required]
    public BusinessRuleConditionGroup Condition { get; init; } = null!;

    [JsonPropertyName("actions")]
    [Description("One or more actions to execute when the condition group matches.")]
    [Required]
    public List<BusinessRuleAction> Actions { get; init; } = null!;

    /// <summary>
    /// Internal unique rule name (for example <c>BusinessRule_1c48625</c>). Optional on create
    /// (generated when omitted); the match key for update and delete operations.
    /// </summary>
    [JsonPropertyName("name")]
    [Description("Internal unique rule name. Optional on create (generated when omitted); required match key for update.")]
    public string? Name { get; init; }

    /// <summary>
    /// Whether the rule is active. Defaults to <c>true</c> on create; when omitted on update,
    /// the existing value is preserved.
    /// </summary>
    [JsonPropertyName("enabled")]
    [Description("Whether the rule is active. Defaults to true on create; omitted on update preserves the existing value.")]
    public bool? Enabled { get; init; }
}

public sealed record BusinessRuleConditionGroup
{
    public BusinessRuleConditionGroup()
    {
    }

    public BusinessRuleConditionGroup(string logicalOperation, List<BusinessRuleCondition> conditions)
    {
        LogicalOperation = logicalOperation;
        Conditions = conditions;
    }

    [JsonPropertyName("logicalOperation")]
    [Description("Logical operator. Supported values: AND, OR.")]
    [Required]
    public string LogicalOperation { get; init; } = null!;

    [JsonPropertyName("conditions")]
    [Description("List of conditions evaluated by the condition group.")]
    [Required]
    public List<BusinessRuleCondition> Conditions { get; init; } = null!;
}

public sealed record BusinessRuleCondition
{
    public BusinessRuleCondition()
    {
    }

    public BusinessRuleCondition(
        BusinessRuleExpression leftExpression,
        string comparisonType,
        BusinessRuleExpression? rightExpression = null)
    {
        LeftExpression = leftExpression;
        ComparisonType = comparisonType;
        RightExpression = rightExpression;
    }

    [JsonPropertyName("leftExpression")]
    [Description("Left expression. Must be an attribute reference with type AttributeValue.")]
    [Required]
    public BusinessRuleExpression LeftExpression { get; init; } = null!;

    [JsonPropertyName("comparisonType")]
    [Description(
        "Condition comparison. Supported values: equal, not-equal, is-filled-in, is-not-filled-in, greater-than, greater-than-or-equal, less-than, less-than-or-equal.")]
    [Required]
    public string ComparisonType { get; init; } = null!;

    [JsonPropertyName("rightExpression")]
    [Description(
        "Right expression. Supports AttributeValue, Const, or SysValue for equal, not-equal, and relational comparisons. Omit or null for is-filled-in and is-not-filled-in.")]
    public BusinessRuleExpression? RightExpression { get; init; }

    /// <summary>
    /// Stable condition identity (GUID). Returned by read; pass it back on update to preserve
    /// the block identity so the platform stores a short diff. Ignored on create.
    /// </summary>
    [JsonPropertyName("uId")]
    [Description("Stable condition identity (GUID). Pass the value returned by read back on update to preserve block identity; ignored on create.")]
    public string? UId { get; init; }
}

public sealed record BusinessRuleExpression
{
    public BusinessRuleExpression()
    {
    }

    public BusinessRuleExpression(
        string type,
        string? path = null,
        JsonElement? value = null,
        string? expression = null,
        string? sysValueName = null)
    {
        Type = type;
        Path = path;
        Value = value;
        Expression = expression;
        SysValueName = sysValueName;
    }

    [JsonPropertyName("type")]
    [Description("Expression type. Supported values: AttributeValue, Const, Formula, SysValue.")]
    [Required]
    public string Type { get; init; } = null!;

    [JsonPropertyName("path")]
    [Description("Attribute path when type is AttributeValue.")]
    public string? Path { get; init; }

    [JsonPropertyName("value")]
    [Description(
        "Constant value when type is Const. Boolean constants must use JSON booleans true/false without quotes. Numeric constants must use JSON numbers like 123 or 12.5 without quotes. For lookup constants pass the lookup record primary value, usually Id, as a GUID string. Date constants use yyyy-MM-dd. DateTime constants use ISO 8601 with a timezone suffix ('Z' or '+/-HH:mm'). Time constants use ISO 8601 time with a timezone suffix ('Z' or '+/-HH:mm').")]
    public JsonElement? Value { get; init; }

    /// <summary>
    /// Formula text when <see cref="Type"/> is <c>Formula</c>.
    /// </summary>
    [JsonPropertyName("expression")]
    [Description("Simple numeric direct-field expression when type is Formula, for example 'Field1 + Field2'. Formula functions and date/time arithmetic are not supported in this scope.")]
    public string? Expression { get; init; }

    /// <summary>
    /// System-variable name when <see cref="Type"/> is <c>SysValue</c>.
    /// </summary>
    [JsonPropertyName("sysValueName")]
    [Description(
        "System variable name when type is SysValue. A SysValue may be on either side of a condition. Supported values: CurrentDate (Date), CurrentTime (Time), CurrentDateTime (DateTime), CurrentUser (Lookup referencing SysAdminUnit), CurrentUserContact (Lookup referencing Contact), CurrentUserAccount (Lookup referencing Account), CurrentUserRoles (ObjectList of SysAdminUnit roles; use comparisonType contain/not-contain against a role). Both operands must resolve to the same data value type, and lookup operands must reference the same schema.")]
    public string? SysValueName { get; init; }

    /// <summary>
    /// Stable expression identity (GUID). Returned by read; pass it back on update to preserve
    /// the block identity so the platform stores a short diff. Ignored on create.
    /// </summary>
    [JsonPropertyName("uId")]
    [Description("Stable expression identity (GUID). Pass the value returned by read back on update to preserve block identity; ignored on create.")]
    public string? UId { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(MakeEditableBusinessRuleAction), "make-editable")]
[JsonDerivedType(typeof(MakeReadOnlyBusinessRuleAction), "make-read-only")]
[JsonDerivedType(typeof(MakeRequiredBusinessRuleAction), "make-required")]
[JsonDerivedType(typeof(MakeOptionalBusinessRuleAction), "make-optional")]
[JsonDerivedType(typeof(SetValuesBusinessRuleAction), "set-values")]
[JsonDerivedType(typeof(ApplyFilterBusinessRuleAction), "apply-filter")]
[JsonDerivedType(typeof(ApplyStaticFilterBusinessRuleAction), "apply-static-filter")]
[JsonDerivedType(typeof(HideElementBusinessRuleAction), "hide-element")]
[JsonDerivedType(typeof(ShowElementBusinessRuleAction), "show-element")]
public abstract record BusinessRuleAction
{
    protected BusinessRuleAction()
    {
    }

    /// <summary>
    /// Stable action identity (GUID). Returned by read; pass it back on update to preserve
    /// the block identity so the platform stores a short diff. Ignored on create.
    /// </summary>
    [JsonPropertyName("uId")]
    [Description("Stable action identity (GUID). Pass the value returned by read back on update to preserve block identity; ignored on create.")]
    public string? UId { get; init; }


    [JsonIgnore] public abstract string ActionType { get; }


    [JsonIgnore] public abstract List<string> FieldSelectionItems { get; }


    [JsonIgnore] public abstract List<BusinessRuleSetValueItem> SetValueItems { get; }
}

public abstract record FieldSelectionBusinessRuleAction : BusinessRuleAction
{
    protected FieldSelectionBusinessRuleAction()
    {
    }

    protected FieldSelectionBusinessRuleAction(List<string> items)
    {
        Items = items ?? [];
    }


    [JsonPropertyName("items")]
    [Description(
        "Action items. Entity actions use attribute names. Page actions use page element names.")]
    [Required]
    public List<string> Items { get; init; } = [];


    [JsonIgnore] public override List<string> FieldSelectionItems => Items ?? [];


    [JsonIgnore] public override List<BusinessRuleSetValueItem> SetValueItems => [];
}

public sealed record MakeEditableBusinessRuleAction : FieldSelectionBusinessRuleAction
{
    public MakeEditableBusinessRuleAction()
    {
    }


    public MakeEditableBusinessRuleAction(List<string> items) : base(items)
    {
    }


    [JsonIgnore] public override string ActionType => "make-editable";
}

public sealed record MakeReadOnlyBusinessRuleAction : FieldSelectionBusinessRuleAction
{
    public MakeReadOnlyBusinessRuleAction()
    {
    }

    public MakeReadOnlyBusinessRuleAction(List<string> items) : base(items)
    {
    }


    [JsonIgnore] public override string ActionType => "make-read-only";
}

public sealed record MakeRequiredBusinessRuleAction : FieldSelectionBusinessRuleAction
{
    public MakeRequiredBusinessRuleAction()
    {
    }


    public MakeRequiredBusinessRuleAction(List<string> items) : base(items)
    {
    }


    [JsonIgnore] public override string ActionType => "make-required";
}

public sealed record MakeOptionalBusinessRuleAction : FieldSelectionBusinessRuleAction
{
    public MakeOptionalBusinessRuleAction()
    {
    }


    public MakeOptionalBusinessRuleAction(List<string> items) : base(items)
    {
    }


    [JsonIgnore] public override string ActionType => "make-optional";
}

/// <summary>
/// Hides page elements when a page-level business-rule condition matches.
/// </summary>
public sealed record HideElementBusinessRuleAction : FieldSelectionBusinessRuleAction
{
    public HideElementBusinessRuleAction()
    {
    }


    public HideElementBusinessRuleAction(List<string> items) : base(items)
    {
    }


    [JsonIgnore] public override string ActionType => "hide-element";
}

/// <summary>
/// Shows page elements when a page-level business-rule condition matches.
/// </summary>
public sealed record ShowElementBusinessRuleAction : FieldSelectionBusinessRuleAction
{
    public ShowElementBusinessRuleAction()
    {
    }


    public ShowElementBusinessRuleAction(List<string> items) : base(items)
    {
    }


    [JsonIgnore] public override string ActionType => "show-element";
}

public sealed record SetValuesBusinessRuleAction : BusinessRuleAction
{
    public SetValuesBusinessRuleAction()
    {
    }


    public SetValuesBusinessRuleAction(List<BusinessRuleSetValueItem> items)
    {
        Items = items ?? [];
    }

    [JsonIgnore] public override string ActionType => "set-values";


    [JsonPropertyName("items")]
    [Description("Target/value assignment items for Set values actions.")]
    [Required]
    public List<BusinessRuleSetValueItem> Items { get; init; } = [];

    [JsonIgnore] public override List<string> FieldSelectionItems => [];


    [JsonIgnore] public override List<BusinessRuleSetValueItem> SetValueItems => Items ?? [];
}

/// <summary>
/// Applies dynamic lookup filtering to an entity lookup attribute.
/// </summary>
public sealed record ApplyFilterBusinessRuleAction : BusinessRuleAction
{
    public ApplyFilterBusinessRuleAction()
    {
    }

    public ApplyFilterBusinessRuleAction(
        string target,
        string targetFilterPath,
        string source,
        string? sourceFilterPath = null,
        bool clearValue = false,
        bool populateValue = false)
    {
        Target = target;
        TargetFilterPath = targetFilterPath;
        Source = source;
        SourceFilterPath = sourceFilterPath;
        ClearValue = clearValue;
        PopulateValue = populateValue;
    }

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
    [Description("Lookup-valued path inside the target lookup schema used for filtering. Must resolve to a Lookup attribute, not Guid, for example Country or Country.TimeZone.")]
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
    /// Gets a value indicating whether the filtered lookup should be cleared when the source value changes.
    /// </summary>
    [JsonPropertyName("clearValue")]
    [Description("When true, generates an autogenerated child rule that clears the target lookup when the source value changes.")]
    public bool ClearValue { get; init; }

    /// <summary>
    /// Gets a value indicating whether the source lookup should be populated from the selected target lookup value.
    /// </summary>
    [JsonPropertyName("populateValue")]
    [Description("When true, generates an autogenerated child rule that populates the source lookup from the selected target lookup value. Prefer true by default for standard dependent-lookup scenarios unless the user explicitly wants one-way filtering only.")]
    public bool PopulateValue { get; init; }

    [JsonIgnore] public override string ActionType => BusinessRuleConstants.ApplyFilterActionTypeName;

    [JsonIgnore] public override List<string> FieldSelectionItems => [];

    [JsonIgnore] public override List<BusinessRuleSetValueItem> SetValueItems => [];
}

/// <summary>
/// Applies a static ESQ filter to a lookup attribute on the entity. Single-action-per-rule.
/// </summary>
public sealed record ApplyStaticFilterBusinessRuleAction : BusinessRuleAction
{
    public ApplyStaticFilterBusinessRuleAction()
    {
    }

    public ApplyStaticFilterBusinessRuleAction(string targetAttribute, JsonElement filter)
    {
        TargetAttribute = targetAttribute;
        Filter = filter;
    }

    [JsonPropertyName("targetAttribute")]
    [Description("Target lookup attribute on the root entity. The lookup's reference schema is used as the filter root.")]
    [Required]
    public string TargetAttribute { get; init; } = string.Empty;

    [JsonPropertyName("filter")]
    [Description("Friendly filter definition for apply-static-filter. Use get-guidance name=business-rules for the full contract.")]
    [Required]
    public JsonElement Filter { get; init; }

    /// <summary>
    /// The persisted ESQ filter envelope. Populated only on read output — the friendly
    /// <see cref="Filter"/> is not reconstructed from ESQ. To change the filter on update,
    /// supply a friendly <see cref="Filter"/> definition; <c>esqFilter</c> is ignored on input.
    /// </summary>
    [JsonPropertyName("esqFilter")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Description("Persisted ESQ filter envelope, returned by read only. Ignored on input; supply a friendly filter to change it.")]
    public string? EsqFilter { get; init; }

    [JsonIgnore] public override string ActionType => BusinessRuleConstants.ApplyStaticFilterActionTypeName;

    [JsonIgnore] public override List<string> FieldSelectionItems => [];

    [JsonIgnore] public override List<BusinessRuleSetValueItem> SetValueItems => [];
}

public sealed record CustomBusinessRuleAction : FieldSelectionBusinessRuleAction
{
    public CustomBusinessRuleAction(string type, List<string> items) : base(items)
    {
        ActionType = type;
    }


    [JsonIgnore] public override string ActionType { get; }
}

public sealed record BusinessRuleSetValueItem
{
    public BusinessRuleSetValueItem()
    {
    }


    public BusinessRuleSetValueItem(BusinessRuleExpression expression, BusinessRuleExpression value)
    {
        Expression = expression;
        Value = value;
    }


    [JsonPropertyName("expression")]
    [Description("Target attribute expression. Must be AttributeValue with a direct target column path.")]
    [Required]
    public BusinessRuleExpression Expression { get; init; } = null!;


    [JsonPropertyName("value")]
    [Description("Source value expression. Supported values are Const, Formula, and AttributeValue. AttributeValue may use a direct source column path or a forward reference path such as LookupColumn.SourceColumn.")]
    [Required]
    public BusinessRuleExpression Value { get; init; } = null!;

    /// <summary>
    /// Stable set-value item identity (GUID). Returned by read; pass it back on update to
    /// preserve the block identity so the platform stores a short diff. Ignored on create.
    /// </summary>
    [JsonPropertyName("uId")]
    [Description("Stable set-value item identity (GUID). Pass the value returned by read back on update to preserve block identity; ignored on create.")]
    public string? UId { get; init; }
}
