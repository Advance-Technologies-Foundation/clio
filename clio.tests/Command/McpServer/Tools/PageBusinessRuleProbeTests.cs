using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Command.AddonSchemaDesigner;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;
using static Clio.Command.BusinessRules.BusinessRuleConstants;

namespace Clio.Tests.Command.McpServer.Tools;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageBusinessRuleProbeTests {

	private static AddonSchemaDto Schema(string metaData, params AddonResourceDto[] resources) =>
		new() { MetaData = metaData, Resources = resources.ToList() };

	[Test]
	[Description("Parses a persisted page rule: element items split from CSV, condition reverse-mapped to input shape.")]
	public void ParseRules_ReverseMapsConditionAndElementActions() {
		string meta = $$"""
		{
		  "typeName": "{{BusinessRulesMetadataTypeName}}",
		  "rules": [
		    {
		      "typeName": "{{BusinessRuleTypeName}}",
		      "uId": "rule-1",
		      "name": "BusinessRule_1",
		      "caption": "Hide warnings when status filled",
		      "cases": [
		        {
		          "typeName": "{{BusinessRuleCaseTypeName}}",
		          "condition": {
		            "typeName": "{{BusinessRuleGroupConditionTypeName}}",
		            "logicalOperation": 1,
		            "conditions": [
		              {
		                "typeName": "{{BusinessRuleConditionTypeName}}",
		                "comparisonType": 1,
		                "leftExpression": {
		                  "typeName": "{{BusinessRuleAttributeExpressionTypeName}}",
		                  "type": "AttributeValue",
		                  "path": "PDS_Status"
		                },
		                "rightExpression": null
		              }
		            ]
		          },
		          "actions": [
		            { "typeName": "{{BusinessRuleHideElementTypeName}}", "items": "Warn1,Warn2" },
		            { "typeName": "{{BusinessRuleShowElementTypeName}}", "items": "Hint" }
		          ]
		        }
		      ]
		    }
		  ]
		}
		""";

		List<SourcePageBusinessRule> rules = PageBusinessRuleProbe.ParseRules(Schema(meta));

		rules.Should().HaveCount(1);
		SourcePageBusinessRule rule = rules[0];
		rule.Caption.Should().Be("Hide warnings when status filled");

		// Condition reverse-mapped into the create-page-business-rule input shape.
		rule.Condition!["logicalOperation"]!.GetValue<string>().Should().Be("AND");
		var condition = rule.Condition!["conditions"]!.AsArray()[0]!;
		condition["comparisonType"]!.GetValue<string>().Should().Be("is-filled-in");
		condition["leftExpression"]!["type"]!.GetValue<string>().Should().Be("AttributeValue");
		condition["leftExpression"]!["path"]!.GetValue<string>().Should().Be("PDS_Status");

		// Actions: hide-element (CSV split into two items) + show-element (single item).
		rule.Actions.Should().HaveCount(2);
		rule.Actions[0].ActionType.Should().Be("hide-element");
		rule.Actions[0].ElementItems.Should().Equal("Warn1", "Warn2");
		rule.Actions[1].ActionType.Should().Be("show-element");
		rule.Actions[1].ElementItems.Should().Equal("Hint");
	}

	[Test]
	[Description("A nested condition group (Creatio persists UI-authored rules this way) is flattened, not emptied: the inner is-not-filled-in leaf survives.")]
	public void ParseRules_NestedConditionGroup_IsFlattenedNotEmptied() {
		string meta = $$"""
		{
		  "typeName": "{{BusinessRulesMetadataTypeName}}",
		  "rules": [
		    {
		      "typeName": "{{BusinessRuleTypeName}}",
		      "uId": "rule-1",
		      "caption": "Hide elements when Account is not filled in",
		      "cases": [
		        {
		          "typeName": "{{BusinessRuleCaseTypeName}}",
		          "condition": {
		            "typeName": "{{BusinessRuleGroupConditionTypeName}}",
		            "logicalOperation": 1,
		            "conditions": [
		              {
		                "typeName": "{{BusinessRuleGroupConditionTypeName}}",
		                "logicalOperation": 1,
		                "conditions": [
		                  {
		                    "typeName": "{{BusinessRuleConditionTypeName}}",
		                    "comparisonType": 0,
		                    "leftExpression": {
		                      "typeName": "{{BusinessRuleAttributeExpressionTypeName}}",
		                      "type": "AttributeValue",
		                      "path": "Parameter_3pxm4wn"
		                    },
		                    "rightExpression": null
		                  }
		                ]
		              }
		            ]
		          },
		          "actions": [
		            { "typeName": "{{BusinessRuleHideElementTypeName}}", "items": "AccountFieldsFlexContainer" }
		          ]
		        }
		      ]
		    }
		  ]
		}
		""";

		List<SourcePageBusinessRule> rules = PageBusinessRuleProbe.ParseRules(Schema(meta));

		rules.Should().HaveCount(1);
		var conditions = rules[0].Condition!["conditions"]!.AsArray();
		conditions.Should().HaveCount(1, because: "the nested group must be flattened, never dropped");
		conditions[0]!["comparisonType"]!.GetValue<string>().Should().Be("is-not-filled-in");
		conditions[0]!["leftExpression"]!["path"]!.GetValue<string>().Should().Be("Parameter_3pxm4wn");
		// A unary operator carries no rightExpression.
		conditions[0]!.AsObject().ContainsKey("rightExpression").Should().BeFalse();
	}

	[Test]
	[Description("A condition mixing AND and OR across nested groups with ≥2 operands (A AND (B OR C)) is flagged not-convertible instead of being flattened into wrong semantics.")]
	public void ParseRules_MixedAndOrCondition_MarksNotConvertible() {
		string meta = $$"""
		{
		  "typeName": "{{BusinessRulesMetadataTypeName}}",
		  "rules": [
		    {
		      "typeName": "{{BusinessRuleTypeName}}",
		      "uId": "rule-1",
		      "caption": "A AND (B OR C)",
		      "cases": [
		        {
		          "typeName": "{{BusinessRuleCaseTypeName}}",
		          "condition": {
		            "typeName": "{{BusinessRuleGroupConditionTypeName}}",
		            "logicalOperation": {{LogicalAnd}},
		            "conditions": [
		              { "typeName": "{{BusinessRuleConditionTypeName}}", "comparisonType": 1,
		                "leftExpression": { "typeName": "{{BusinessRuleAttributeExpressionTypeName}}", "type": "AttributeValue", "path": "A" } },
		              { "typeName": "{{BusinessRuleGroupConditionTypeName}}", "logicalOperation": {{LogicalOr}},
		                "conditions": [
		                  { "typeName": "{{BusinessRuleConditionTypeName}}", "comparisonType": 1,
		                    "leftExpression": { "typeName": "{{BusinessRuleAttributeExpressionTypeName}}", "type": "AttributeValue", "path": "B" } },
		                  { "typeName": "{{BusinessRuleConditionTypeName}}", "comparisonType": 1,
		                    "leftExpression": { "typeName": "{{BusinessRuleAttributeExpressionTypeName}}", "type": "AttributeValue", "path": "C" } }
		                ] }
		            ]
		          },
		          "actions": [ { "typeName": "{{BusinessRuleHideElementTypeName}}", "items": "Field1" } ]
		        }
		      ]
		    }
		  ]
		}
		""";

		List<SourcePageBusinessRule> rules = PageBusinessRuleProbe.ParseRules(Schema(meta));

		rules.Should().HaveCount(1);
		rules[0].ConditionIssue.Should().Be(PageRuleConditionIssue.MixedAndOr, "A AND (B OR C) cannot be flattened into a single operator");
	}

	[Test]
	[Description("A PRESENT but unrecognized comparisonType (a numeric outside the supported set) is flagged not-convertible instead of being silently rewritten to the is-not-filled-in default.")]
	public void ParseRules_UnrecognizedComparison_MarksNotConvertible() {
		string meta = $$"""
		{
		  "typeName": "{{BusinessRulesMetadataTypeName}}",
		  "rules": [
		    {
		      "typeName": "{{BusinessRuleTypeName}}",
		      "uId": "rule-1",
		      "caption": "Name begins with A",
		      "cases": [
		        {
		          "typeName": "{{BusinessRuleCaseTypeName}}",
		          "condition": {
		            "typeName": "{{BusinessRuleGroupConditionTypeName}}",
		            "logicalOperation": {{LogicalAnd}},
		            "conditions": [
		              { "typeName": "{{BusinessRuleConditionTypeName}}", "comparisonType": 4,
		                "leftExpression": { "typeName": "{{BusinessRuleAttributeExpressionTypeName}}", "type": "AttributeValue", "path": "Name" },
		                "rightExpression": { "typeName": "{{BusinessRuleAttributeExpressionTypeName}}", "type": "Const", "value": "A" } }
		            ]
		          },
		          "actions": [ { "typeName": "{{BusinessRuleHideElementTypeName}}", "items": "Field1" } ]
		        }
		      ]
		    }
		  ]
		}
		""";

		List<SourcePageBusinessRule> rules = PageBusinessRuleProbe.ParseRules(Schema(meta));

		rules.Should().HaveCount(1);
		rules[0].ConditionIssue.Should().Be(PageRuleConditionIssue.UnrecognizedComparison,
			"comparisonType 4 maps to no supported comparison and must not silently become is-not-filled-in");
	}

	[Test]
	[Description("Same-operator nesting (A AND (B AND C)) is lossless: the inner group is flattened into a single AND group of three leaves, and the rule stays convertible.")]
	public void ParseRules_SameOperatorNesting_IsFlattened() {
		string meta = $$"""
		{
		  "typeName": "{{BusinessRulesMetadataTypeName}}",
		  "rules": [
		    {
		      "typeName": "{{BusinessRuleTypeName}}",
		      "uId": "rule-1",
		      "caption": "A AND (B AND C)",
		      "cases": [
		        {
		          "typeName": "{{BusinessRuleCaseTypeName}}",
		          "condition": {
		            "typeName": "{{BusinessRuleGroupConditionTypeName}}",
		            "logicalOperation": {{LogicalAnd}},
		            "conditions": [
		              { "typeName": "{{BusinessRuleConditionTypeName}}", "comparisonType": 1,
		                "leftExpression": { "typeName": "{{BusinessRuleAttributeExpressionTypeName}}", "type": "AttributeValue", "path": "A" } },
		              { "typeName": "{{BusinessRuleGroupConditionTypeName}}", "logicalOperation": {{LogicalAnd}},
		                "conditions": [
		                  { "typeName": "{{BusinessRuleConditionTypeName}}", "comparisonType": 1,
		                    "leftExpression": { "typeName": "{{BusinessRuleAttributeExpressionTypeName}}", "type": "AttributeValue", "path": "B" } },
		                  { "typeName": "{{BusinessRuleConditionTypeName}}", "comparisonType": 1,
		                    "leftExpression": { "typeName": "{{BusinessRuleAttributeExpressionTypeName}}", "type": "AttributeValue", "path": "C" } }
		                ] }
		            ]
		          },
		          "actions": [ { "typeName": "{{BusinessRuleHideElementTypeName}}", "items": "Field1" } ]
		        }
		      ]
		    }
		  ]
		}
		""";

		List<SourcePageBusinessRule> rules = PageBusinessRuleProbe.ParseRules(Schema(meta));

		rules.Should().HaveCount(1);
		rules[0].ConditionIssue.Should().Be(PageRuleConditionIssue.None);
		rules[0].Condition!["logicalOperation"]!.GetValue<string>().Should().Be("AND");
		var conditions = rules[0].Condition!["conditions"]!.AsArray();
		conditions.Should().HaveCount(3, because: "same-operator nesting flattens losslessly");
		conditions.Select(c => c!["leftExpression"]!["path"]!.GetValue<string>())
			.Should().Equal("A", "B", "C");
	}

	[Test]
	[Description("A condition with only a left attribute expression and no comparisonType (Creatio's default 'is not filled in') converts to is-not-filled-in instead of being dropped.")]
	public void ParseRules_ConditionWithoutComparisonType_DefaultsToIsNotFilledIn() {
		string meta = $$"""
		{
		  "typeName": "{{BusinessRulesMetadataTypeName}}",
		  "rules": [
		    {
		      "typeName": "{{BusinessRuleTypeName}}",
		      "uId": "rule-1",
		      "caption": "Hide elements when Account is not filled in",
		      "cases": [
		        {
		          "typeName": "{{BusinessRuleCaseTypeName}}",
		          "condition": {
		            "typeName": "{{BusinessRuleGroupConditionTypeName}}",
		            "logicalOperation": 1,
		            "conditions": [
		              {
		                "typeName": "{{BusinessRuleConditionTypeName}}",
		                "leftExpression": {
		                  "typeName": "{{BusinessRuleAttributeExpressionTypeName}}",
		                  "type": "AttributeValue",
		                  "path": "QualifiedAccount"
		                }
		              }
		            ]
		          },
		          "actions": [
		            { "typeName": "{{BusinessRuleHideElementTypeName}}", "items": "AccountFieldsFlexContainer" }
		          ]
		        }
		      ]
		    }
		  ]
		}
		""";

		List<SourcePageBusinessRule> rules = PageBusinessRuleProbe.ParseRules(Schema(meta));

		rules.Should().HaveCount(1);
		var conditions = rules[0].Condition!["conditions"]!.AsArray();
		conditions.Should().HaveCount(1, because: "a bare condition must convert, never be dropped");
		conditions[0]!["comparisonType"]!.GetValue<string>().Should().Be("is-not-filled-in");
		conditions[0]!["leftExpression"]!["path"]!.GetValue<string>().Should().Be("QualifiedAccount");
		conditions[0]!.AsObject().ContainsKey("rightExpression").Should().BeFalse();
	}

	[Test]
	[Description("When the rule caption is empty, it is resolved from the add-on resources keyed by the rule UId.")]
	public void ParseRules_ResolvesCaptionFromResources() {
		string meta = $$"""
		{
		  "typeName": "{{BusinessRulesMetadataTypeName}}",
		  "rules": [
		    {
		      "typeName": "{{BusinessRuleTypeName}}",
		      "uId": "abc",
		      "name": "BusinessRule_2",
		      "cases": [
		        { "typeName": "{{BusinessRuleCaseTypeName}}", "condition": null,
		          "actions": [ { "typeName": "{{BusinessRuleShowElementTypeName}}", "items": "Field1" } ] }
		      ]
		    }
		  ]
		}
		""";
		var resource = new AddonResourceDto {
			Key = "AddonConfig.Rules.abc.Caption",
			Value = [new AddonResourceValueDto { Key = "en-US", Value = "From Resource" }]
		};

		List<SourcePageBusinessRule> rules = PageBusinessRuleProbe.ParseRules(Schema(meta, resource));

		rules.Should().HaveCount(1);
		rules[0].Caption.Should().Be("From Resource");
		rules[0].Actions.Single().ActionType.Should().Be("show-element");
	}

	[Test]
	[Description("A rule with multiple cases yields one source rule per case, captioned distinctly.")]
	public void ParseRules_MultipleCases_YieldOneRulePerCase() {
		string meta = $$"""
		{
		  "typeName": "{{BusinessRulesMetadataTypeName}}",
		  "rules": [
		    {
		      "typeName": "{{BusinessRuleTypeName}}",
		      "uId": "r",
		      "caption": "Two cases",
		      "cases": [
		        { "typeName": "{{BusinessRuleCaseTypeName}}", "condition": null,
		          "actions": [ { "typeName": "{{BusinessRuleReadonlyElementTypeName}}", "items": "A" } ] },
		        { "typeName": "{{BusinessRuleCaseTypeName}}", "condition": null,
		          "actions": [ { "typeName": "{{BusinessRuleEditableElementTypeName}}", "items": "B" } ] }
		      ]
		    }
		  ]
		}
		""";

		List<SourcePageBusinessRule> rules = PageBusinessRuleProbe.ParseRules(Schema(meta));

		rules.Should().HaveCount(2);
		rules[0].Caption.Should().Be("Two cases (case 1)");
		rules[1].Caption.Should().Be("Two cases (case 2)");
		rules[0].Actions.Single().ActionType.Should().Be("make-read-only");
		rules[1].Actions.Single().ActionType.Should().Be("make-editable");
	}

	[Test]
	[Description("An anomalous action with an unmapped/absent typeName (e.g. set-values, which cannot be authored at page level, or a new/renamed OOTB action) is skipped at action granularity — it never throws and never discards the rest. A rule left with a valid action keeps converting; a rule whose ONLY action is unmapped is dropped; an unrelated fully-valid rule always survives.")]
	public void ParseRules_UnmappedAction_IsSkipped_AndOtherRulesSurvive() {
		string meta = $$"""
		{
		  "typeName": "{{BusinessRulesMetadataTypeName}}",
		  "rules": [
		    {
		      "typeName": "{{BusinessRuleTypeName}}",
		      "uId": "r1",
		      "caption": "Mixed",
		      "cases": [
		        { "typeName": "{{BusinessRuleCaseTypeName}}", "condition": null,
		          "actions": [
		            { "typeName": "{{BusinessRuleSetValuesElementTypeName}}", "items": [] },
		            { "typeName": "{{BusinessRuleHideElementTypeName}}", "items": "Field1" }
		          ] }
		      ]
		    },
		    {
		      "typeName": "{{BusinessRuleTypeName}}",
		      "uId": "r2",
		      "caption": "Only unmapped",
		      "cases": [
		        { "typeName": "{{BusinessRuleCaseTypeName}}", "condition": null,
		          "actions": [ { "typeName": "{{BusinessRuleSetValuesElementTypeName}}", "items": [] } ] }
		      ]
		    },
		    {
		      "typeName": "{{BusinessRuleTypeName}}",
		      "uId": "r3",
		      "caption": "Fully valid",
		      "cases": [
		        { "typeName": "{{BusinessRuleCaseTypeName}}", "condition": null,
		          "actions": [ { "typeName": "{{BusinessRuleShowElementTypeName}}", "items": "Hint" } ] }
		      ]
		    }
		  ]
		}
		""";

		List<SourcePageBusinessRule> rules = PageBusinessRuleProbe.ParseRules(Schema(meta));

		// "Only unmapped" (r2) is dropped (0 recognized actions); the mixed and the valid rule survive —
		// one anomalous action must never take down the whole page.
		rules.Should().HaveCount(2, because: "an unmapped action is skipped, not fatal — only the rule left with no action drops");
		rules.Select(r => r.Caption).Should().Equal("Mixed", "Fully valid");
		rules[0].Actions.Single().ActionType.Should().Be("hide-element",
			because: "the unmapped set-values action is skipped while the valid hide-element keeps converting");
		rules[1].Actions.Single().ActionType.Should().Be("show-element");
	}

	[Test]
	[Description("Empty or missing metadata yields no rules and never throws.")]
	public void ParseRules_EmptyMetadata_YieldsNoRules() {
		PageBusinessRuleProbe.ParseRules(Schema(null)).Should().BeEmpty();
		PageBusinessRuleProbe.ParseRules(Schema("")).Should().BeEmpty();
		PageBusinessRuleProbe.ParseRules(Schema("not json")).Should().BeEmpty();
		PageBusinessRuleProbe.ParseRules(Schema("""{"typeName":"x"}""")).Should().BeEmpty();
	}
}
