using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command.BusinessRules;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.BusinessRules;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageBusinessRuleRedundancyTests {

	private const string Commercial = "6f1c3c3e-0000-4000-8000-000000000001";
	private const string Installment = "6f1c3c3e-0000-4000-8000-000000000002";

	private static BusinessRuleCondition Equal(string attribute, string value) =>
		new(new BusinessRuleExpression("AttributeValue", path: attribute), "equal",
			new BusinessRuleExpression("Const", value: JsonSerializer.SerializeToElement(value)));

	private static BusinessRuleCondition NotEqual(string attribute, string value) =>
		new(new BusinessRuleExpression("AttributeValue", path: attribute), "not-equal",
			new BusinessRuleExpression("Const", value: JsonSerializer.SerializeToElement(value)));

	private static BusinessRule Rule(string caption, string logicalOperation, BusinessRuleAction action,
		params BusinessRuleCondition[] conditions) =>
		new(caption, new BusinessRuleConditionGroup(logicalOperation, conditions.ToList()), [action]) {
			Name = "Rule_" + caption.Replace(' ', '_')
		};

	private static BusinessRuleAction Required(params string[] items) => new MakeRequiredBusinessRuleAction(items.ToList());
	private static BusinessRuleAction Optional(params string[] items) => new MakeOptionalBusinessRuleAction(items.ToList());
	private static BusinessRuleAction ReadOnly(params string[] items) => new MakeReadOnlyBusinessRuleAction(items.ToList());

	[Test]
	[Description("Warns that a rule is subsumed when an existing rule makes the same items required on a strictly wider AND condition.")]
	public void Analyze_WhenNewRuleAddsConditionToExistingRule_ReturnsSubsumedWarning() {
		// Arrange — the reviewed run: "required for Commercial with installment" next to "required for Commercial".
		BusinessRule existing = Rule("Order product required", "AND", Required("OrderProduct"),
			Equal("PDS.Type", Commercial));
		BusinessRule incoming = Rule("Order product required with installment", "AND", Required("OrderProduct"),
			Equal("PDS.Type", Commercial), Equal("PDS.PaymentType", Installment));

		// Act
		IReadOnlyList<IReadOnlyList<string>> warnings = PageBusinessRuleRedundancy.Analyze([existing], [incoming]);

		// Assert
		warnings.Should().ContainSingle(because: "one rule was analyzed");
		warnings[0].Should().ContainSingle(because: "the new rule only repeats the wider rule on a narrower condition")
			.Which.Should().Contain("subsumed", because: "the warning names its rule id")
			.And.Contain("Order product required", because: "the warning names the wider rule that covers this one");
	}

	[Test]
	[Description("Does not warn when the conditions are disjoint, even with the same action.")]
	public void Analyze_WhenConditionsDisjoint_ReturnsNoWarning() {
		// Arrange
		BusinessRule existing = Rule("Required for commercial", "AND", Required("OrderProduct"), Equal("PDS.Type", Commercial));
		BusinessRule incoming = Rule("Required for installment", "AND", Required("OrderProduct"), Equal("PDS.PaymentType", Installment));

		// Act
		IReadOnlyList<IReadOnlyList<string>> warnings = PageBusinessRuleRedundancy.Analyze([existing], [incoming]);

		// Assert
		warnings[0].Should().BeEmpty(because: "neither condition implies the other");
	}

	[Test]
	[Description("Does not warn when the narrower rule applies a different action.")]
	public void Analyze_WhenNarrowerRuleHasDifferentAction_ReturnsNoWarning() {
		// Arrange
		BusinessRule existing = Rule("Required for commercial", "AND", Required("OrderProduct"), Equal("PDS.Type", Commercial));
		BusinessRule incoming = Rule("Read-only for installment", "AND", ReadOnly("OrderProduct"),
			Equal("PDS.Type", Commercial), Equal("PDS.PaymentType", Installment));

		// Act
		IReadOnlyList<IReadOnlyList<string>> warnings = PageBusinessRuleRedundancy.Analyze([existing], [incoming]);

		// Assert
		warnings[0].Should().BeEmpty(because: "a different action is not covered by the wider rule");
	}

	[Test]
	[Description("Does not compare an AND group with an OR group.")]
	public void Analyze_WhenGroupsMixAndOr_ReturnsNoWarning() {
		// Arrange
		BusinessRule existing = Rule("Required when either", "OR", Required("OrderProduct"),
			Equal("PDS.Type", Commercial), Equal("PDS.PaymentType", Installment));
		BusinessRule incoming = Rule("Required when both", "AND", Required("OrderProduct"),
			Equal("PDS.Type", Commercial), Equal("PDS.PaymentType", Installment));

		// Act
		IReadOnlyList<IReadOnlyList<string>> warnings = PageBusinessRuleRedundancy.Analyze([existing], [incoming]);

		// Assert
		warnings[0].Should().BeEmpty(because: "mixed AND/OR groups are out of the check's scope");
	}

	[Test]
	[Description("Treats a one-condition rule as narrower than an OR rule that contains its condition.")]
	public void Analyze_WhenSingleConditionRuleIsPartOfWiderOrRule_ReturnsSubsumedWarning() {
		// Arrange
		BusinessRule existing = Rule("Required when either", "OR", Required("OrderProduct"),
			Equal("PDS.Type", Commercial), Equal("PDS.PaymentType", Installment));
		BusinessRule incoming = Rule("Required for commercial", "AND", Required("OrderProduct"), Equal("PDS.Type", Commercial));

		// Act
		IReadOnlyList<IReadOnlyList<string>> warnings = PageBusinessRuleRedundancy.Analyze([existing], [incoming]);

		// Assert
		warnings[0].Should().ContainSingle(because: "a one-condition group reads the same under AND and OR, and 'A' implies 'A OR B'")
			.Which.Should().Contain("subsumed", because: "the warning names its rule id");
	}

	[TestCase(true)]
	[TestCase(false)]
	[Description("A multi-condition OR rule is subsumed only by an OR rule with strictly more conditions, never by an equal one.")]
	public void Analyze_WhenOrRulesCompared_FlagsOnlyStrictSubset(bool storedHasExtraCondition) {
		// Arrange
		BusinessRuleCondition third = Equal("PDS.Region", Installment);
		BusinessRuleCondition[] storedConditions = storedHasExtraCondition
			? [Equal("PDS.Type", Commercial), Equal("PDS.PaymentType", Installment), third]
			: [Equal("PDS.Type", Commercial), Equal("PDS.PaymentType", Installment)];
		BusinessRule existing = Rule("Required when any", "OR", Required("OrderProduct"), storedConditions);
		BusinessRule incoming = Rule("Required when either", "OR", Required("OrderProduct"),
			Equal("PDS.Type", Commercial), Equal("PDS.PaymentType", Installment));

		// Act
		IReadOnlyList<IReadOnlyList<string>> warnings = PageBusinessRuleRedundancy.Analyze([existing], [incoming]);

		// Assert
		warnings[0].Should().HaveCount(storedHasExtraCondition ? 1 : 0,
			because: "OR {A,B} is narrower than OR {A,B,C}, while two equal OR rules are a duplicate, not a strict subset");
	}

	[Test]
	[Description("An incoming rule that omits enabled keeps the stored value, so a rule that stays disabled is not analyzed.")]
	public void Analyze_WhenUpdateOmitsEnabledOfDisabledRule_ReturnsNoWarning() {
		// Arrange
		BusinessRule other = Rule("Amount required", "AND", Required("Amount"), Equal("PDS.Type", Commercial));
		BusinessRule storedDisabled = Rule("Amount optional", "AND", Optional("Amount"), Equal("PDS.Region", Installment)) with {
			Enabled = false
		};
		BusinessRule update = storedDisabled with {
			Enabled = null,
			Condition = new BusinessRuleConditionGroup("AND", [NotEqual("PDS.Type", Commercial)])
		};

		// Act
		IReadOnlyList<IReadOnlyList<string>> warnings = PageBusinessRuleRedundancy.Analyze([other, storedDisabled], [update]);

		// Assert
		warnings[0].Should().BeEmpty(because: "the update keeps the rule disabled, and a disabled rule never runs");
	}

	[Test]
	[Description("Compares rules of the same batch with each other, not only with the stored rules.")]
	public void Analyze_WhenSubsumedRuleIsInSameBatch_ReturnsSubsumedWarning() {
		// Arrange
		BusinessRule wide = Rule("Order product required", "AND", Required("OrderProduct"), Equal("PDS.Type", Commercial));
		BusinessRule narrow = Rule("Order product required with installment", "AND", Required("OrderProduct"),
			Equal("PDS.Type", Commercial), Equal("PDS.PaymentType", Installment));

		// Act
		IReadOnlyList<IReadOnlyList<string>> warnings = PageBusinessRuleRedundancy.Analyze([], [narrow, wide]);

		// Assert
		warnings[0].Should().ContainSingle(because: "the narrow rule is covered by the wide rule of the same batch");
		warnings[1].Should().BeEmpty(because: "the wide rule is not covered by the narrow one");
	}

	[Test]
	[Description("Warns that a rule only undoes another: negated condition and the opposite state action on the same items.")]
	public void Analyze_WhenRuleIsInverseOfExistingRule_ReturnsInverseRedundantWarning() {
		// Arrange — the reviewed run: "Amount required" plus "Amount optional" on the negated condition.
		BusinessRule existing = Rule("Amount required", "AND", Required("Amount"), Equal("PDS.Type", Commercial));
		BusinessRule incoming = Rule("Amount optional", "AND", Optional("Amount"), NotEqual("PDS.Type", Commercial));

		// Act
		IReadOnlyList<IReadOnlyList<string>> warnings = PageBusinessRuleRedundancy.Analyze([existing], [incoming]);

		// Assert
		warnings[0].Should().ContainSingle(because: "a page state rule already undoes itself when its condition turns false")
			.Which.Should().Contain("inverse-redundant", because: "the warning names its rule id")
			.And.Contain("Amount required", because: "the warning names the rule this one undoes")
			.And.Contain("by default", because: "the warning states the case that was not verified");
	}

	[Test]
	[Description("Treats the De Morgan negation of an AND group as an inverse.")]
	public void Analyze_WhenInverseUsesDeMorganNegation_ReturnsInverseRedundantWarning() {
		// Arrange
		BusinessRule existing = Rule("Read-only for commercial installment", "AND", ReadOnly("Amount"),
			Equal("PDS.Type", Commercial), Equal("PDS.PaymentType", Installment));
		BusinessRule incoming = Rule("Editable otherwise", "OR", new MakeEditableBusinessRuleAction(["Amount"]),
			NotEqual("PDS.Type", Commercial), NotEqual("PDS.PaymentType", Installment));

		// Act
		IReadOnlyList<IReadOnlyList<string>> warnings = PageBusinessRuleRedundancy.Analyze([existing], [incoming]);

		// Assert
		warnings[0].Should().ContainSingle(because: "NOT (a AND b) is (NOT a) OR (NOT b)");
	}

	[Test]
	[Description("Never flags a set-values rule on the negated condition.")]
	public void Analyze_WhenSetValuesOnNegatedCondition_ReturnsNoWarning() {
		// Arrange
		BusinessRule existing = Rule("Amount required", "AND", Required("Amount"), Equal("PDS.Type", Commercial));
		BusinessRule incoming = new("Clear amount",
			new BusinessRuleConditionGroup("AND", [NotEqual("PDS.Type", Commercial)]),
			[new SetValuesBusinessRuleAction([])]);

		// Act
		IReadOnlyList<IReadOnlyList<string>> warnings = PageBusinessRuleRedundancy.Analyze([existing], [incoming]);

		// Assert
		warnings[0].Should().BeEmpty(because: "set-values is a one-shot action and does not undo itself");
	}

	[Test]
	[Description("Flags only the second rule of an inverse pair sent in one batch.")]
	public void Analyze_WhenInversePairInSameBatch_FlagsOnlySecondRule() {
		// Arrange
		BusinessRule required = Rule("Amount required", "AND", Required("Amount"), Equal("PDS.Type", Commercial));
		BusinessRule optional = Rule("Amount optional", "AND", Optional("Amount"), NotEqual("PDS.Type", Commercial));

		// Act
		IReadOnlyList<IReadOnlyList<string>> warnings = PageBusinessRuleRedundancy.Analyze([], [required, optional]);

		// Assert
		warnings[0].Should().BeEmpty(because: "one rule of the pair is needed");
		warnings[1].Should().ContainSingle(because: "the second rule only undoes the first");
	}

	[Test]
	[Description("Ignores the stored version of a rule the batch updates, and disabled rules.")]
	public void Analyze_WhenComparingWithUpdatedOrDisabledRule_ReturnsNoWarning() {
		// Arrange
		BusinessRule stored = Rule("Amount required", "AND", Required("Amount"), Equal("PDS.Type", Commercial));
		BusinessRule disabled = Rule("Amount optional", "AND", Optional("Amount"), NotEqual("PDS.Type", Commercial)) with {
			Enabled = false
		};
		BusinessRule update = stored with { Condition = new BusinessRuleConditionGroup("AND", [NotEqual("PDS.Type", Commercial)]) };

		// Act
		IReadOnlyList<IReadOnlyList<string>> warnings = PageBusinessRuleRedundancy.Analyze([stored, disabled], [update]);

		// Assert
		warnings[0].Should().BeEmpty(because: "the stored copy of the updated rule is replaced, and a disabled rule never runs");
	}
}
