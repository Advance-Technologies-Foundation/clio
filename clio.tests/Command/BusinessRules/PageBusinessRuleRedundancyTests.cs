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

	private static BusinessRuleCondition IsNotFilledIn(string attribute) =>
		new(new BusinessRuleExpression("AttributeValue", path: attribute), "is-not-filled-in", null);

	private static BusinessRuleCondition IsFilledIn(string attribute) =>
		new(new BusinessRuleExpression("AttributeValue", path: attribute), "is-filled-in", null);

	private static BusinessRuleCondition EqualFlag(string attribute, bool value) =>
		new(new BusinessRuleExpression("AttributeValue", path: attribute), "equal",
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

	[TestCase(true, Description = "The near-opposite rule is the incoming one.")]
	[TestCase(false, Description = "The near-opposite rule is the stored one.")]
	[Description("ENG-107305 reopen: 'X not-equal v OR X is-not-filled-in' undoes 'X equal v', as agents write it.")]
	public void Analyze_WhenInverseAddsIsNotFilledInToNegatedEqual_ReturnsInverseRedundantWarning(bool nearOppositeIncoming) {
		// Arrange — the benchmark run: "read-only when closed" plus "editable when not closed or empty".
		BusinessRule exact = Rule("Read-only when closed", "AND", ReadOnly("Amount"), Equal("Status", Commercial));
		BusinessRule nearOpposite = Rule("Editable when open", "OR", new MakeEditableBusinessRuleAction(["Amount"]),
			NotEqual("Status", Commercial), IsNotFilledIn("Status"));
		(BusinessRule stored, BusinessRule incoming) = nearOppositeIncoming ? (exact, nearOpposite) : (nearOpposite, exact);

		// Act
		IReadOnlyList<IReadOnlyList<string>> warnings = PageBusinessRuleRedundancy.Analyze([stored], [incoming]);

		// Assert
		warnings[0].Should().ContainSingle(because: "an empty value already makes 'equal v' false, so the rule only undoes the other one")
			.Which.Should().Contain("inverse-redundant", because: "the warning names its rule id")
			.And.Contain(stored.Caption, because: "the warning names the rule this one undoes");
	}

	[TestCase(true, Description = "The rule on false is the incoming one.")]
	[TestCase(false, Description = "The rule on false is the stored one.")]
	[Description("ENG-107305 reopen: 'Flag equal false' undoes 'Flag equal true' with the opposite state action.")]
	public void Analyze_WhenInverseUsesOppositeBooleanValue_ReturnsInverseRedundantWarning(bool falseRuleIncoming) {
		// Arrange — the benchmark run: "required when approved" plus "optional when not approved".
		BusinessRule onTrue = Rule("Amount required when approved", "AND", Required("Amount"), EqualFlag("Approved", true));
		BusinessRule onFalse = Rule("Amount optional when not approved", "AND", Optional("Amount"), EqualFlag("Approved", false));
		(BusinessRule stored, BusinessRule incoming) = falseRuleIncoming ? (onTrue, onFalse) : (onFalse, onTrue);

		// Act
		IReadOnlyList<IReadOnlyList<string>> warnings = PageBusinessRuleRedundancy.Analyze([stored], [incoming]);

		// Assert
		warnings[0].Should().ContainSingle(because: "'equal false' is the negation of 'equal true' for a boolean")
			.Which.Should().Contain("inverse-redundant", because: "the warning names its rule id");
	}

	[Test]
	[Description("Does not treat the strings \"true\" and \"false\" as opposite booleans.")]
	public void Analyze_WhenOppositeValuesAreStrings_ReturnsNoWarning() {
		// Arrange
		BusinessRule existing = Rule("Required on true text", "AND", Required("Amount"), Equal("Code", "true"));
		BusinessRule incoming = Rule("Optional on false text", "AND", Optional("Amount"), Equal("Code", "false"));

		// Act
		IReadOnlyList<IReadOnlyList<string>> warnings = PageBusinessRuleRedundancy.Analyze([existing], [incoming]);

		// Assert
		warnings[0].Should().BeEmpty(because: "a text column can hold values other than these two");
	}

	[Test]
	[Description("Keeps an is-not-filled-in check on another attribute: it widens the condition beyond the negation.")]
	public void Analyze_WhenIsNotFilledInTargetsOtherAttribute_ReturnsNoWarning() {
		// Arrange
		BusinessRule existing = Rule("Read-only when closed", "AND", ReadOnly("Amount"), Equal("Status", Commercial));
		BusinessRule incoming = Rule("Editable when open or no owner", "OR", new MakeEditableBusinessRuleAction(["Amount"]),
			NotEqual("Status", Commercial), IsNotFilledIn("Owner"));

		// Act
		IReadOnlyList<IReadOnlyList<string>> warnings = PageBusinessRuleRedundancy.Analyze([existing], [incoming]);

		// Assert
		warnings[0].Should().BeEmpty(because: "an empty owner makes the rule fire even when the status is closed");
	}

	[Test]
	[Description("Keeps an is-not-filled-in check beside 'equal': 'equal v' is false on an empty value, so the check widens the rule.")]
	public void Analyze_WhenIsNotFilledInIsOredWithEqual_ReturnsNoWarning() {
		// Arrange — "Status = v OR Status empty" also fires on an empty status, where "Status not-equal v" fires too.
		BusinessRule existing = Rule("Editable when open", "AND", new MakeEditableBusinessRuleAction(["Amount"]),
			NotEqual("Status", Commercial));
		BusinessRule incoming = Rule("Read-only when closed or empty", "OR", ReadOnly("Amount"),
			Equal("Status", Commercial), IsNotFilledIn("Status"));

		// Act
		IReadOnlyList<IReadOnlyList<string>> warnings = PageBusinessRuleRedundancy.Analyze([existing], [incoming]);

		// Assert
		warnings[0].Should().BeEmpty(because: "the two rules overlap on an empty status instead of negating each other");
	}

	[Test]
	[Description("Keeps an is-filled-in check in an OR group: dropping it would change what the rule matches.")]
	public void Analyze_WhenIsFilledInIsOredWithEqual_ReturnsNoWarning() {
		// Arrange — "Status = v OR Status filled" is just "Status filled", not "Status = v".
		BusinessRule existing = Rule("Editable when open", "AND", new MakeEditableBusinessRuleAction(["Amount"]),
			NotEqual("Status", Commercial));
		BusinessRule incoming = Rule("Read-only when closed or set", "OR", ReadOnly("Amount"),
			Equal("Status", Commercial), IsFilledIn("Status"));

		// Act
		IReadOnlyList<IReadOnlyList<string>> warnings = PageBusinessRuleRedundancy.Analyze([existing], [incoming]);

		// Assert
		warnings[0].Should().BeEmpty(because: "the rule fires for every filled status, not only the negation of the other rule");
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
