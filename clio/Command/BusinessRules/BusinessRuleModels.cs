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
        "Right expression. Supports AttributeValue or Const for equal, not-equal, and relational comparisons. Omit or null for is-filled-in and is-not-filled-in.")]
    public BusinessRuleExpression? RightExpression { get; init; }
}

public sealed record BusinessRuleExpression
{
    public BusinessRuleExpression()
    {
    }

    public BusinessRuleExpression(string type, string? path = null, JsonElement? value = null, string? expression = null)
    {
        Type = type;
        Path = path;
        Value = value;
        Expression = expression;
    }

    [JsonPropertyName("type")]
    [Description("Expression type. Supported values: AttributeValue, Const, Formula.")]
    [Required]
    public string Type { get; init; } = null!;

    [JsonPropertyName("path")]
    [Description("Attribute path when type is AttributeValue.")]
    public string? Path { get; init; }

    [JsonPropertyName("value")]
    [Description(
        "Constant value when type is Const. Boolean constants must use JSON booleans true/false without quotes. Numeric constants must use JSON numbers like 123 or 12.5 without quotes. For lookup constants pass a GUID string. Date constants use yyyy-MM-dd. DateTime constants use ISO 8601 with a timezone suffix ('Z' or '+/-HH:mm'). Time constants use ISO 8601 time with a timezone suffix ('Z' or '+/-HH:mm').")]
    public JsonElement? Value { get; init; }

    /// <summary>
    /// Formula text when <see cref="Type"/> is <c>Formula</c>.
    /// </summary>
    [JsonPropertyName("expression")]
    [Description("Simple direct-field expression when type is Formula, for example 'Field1 + Field2'. Formula functions are not supported in this scope.")]
    public string? Expression { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(MakeEditableBusinessRuleAction), "make-editable")]
[JsonDerivedType(typeof(MakeReadOnlyBusinessRuleAction), "make-read-only")]
[JsonDerivedType(typeof(MakeRequiredBusinessRuleAction), "make-required")]
[JsonDerivedType(typeof(MakeOptionalBusinessRuleAction), "make-optional")]
[JsonDerivedType(typeof(SetValuesBusinessRuleAction), "set-values")]
[JsonDerivedType(typeof(HideElementBusinessRuleAction), "hide-element")]
[JsonDerivedType(typeof(ShowElementBusinessRuleAction), "show-element")]
public abstract record BusinessRuleAction
{
    protected BusinessRuleAction()
    {
    }


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
        "Action items. Field-state actions use attribute names. Page show/hide actions use page element names.")]
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
    [Description("Source value expression. Supported values are Const and Formula.")]
    [Required]
    public BusinessRuleExpression Value { get; init; } = null!;
}
