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
	[Description("Data actions (set-values) are not page-level and are skipped; a case left with no element action is omitted.")]
	public void ParseRules_NonPageAction_IsSkipped() {
		string meta = $$"""
		{
		  "typeName": "{{BusinessRulesMetadataTypeName}}",
		  "rules": [
		    {
		      "typeName": "{{BusinessRuleTypeName}}",
		      "uId": "r",
		      "caption": "Only set-values",
		      "cases": [
		        { "typeName": "{{BusinessRuleCaseTypeName}}", "condition": null,
		          "actions": [ { "typeName": "{{BusinessRuleSetValuesElementTypeName}}", "items": [] } ] }
		      ]
		    }
		  ]
		}
		""";

		List<SourcePageBusinessRule> rules = PageBusinessRuleProbe.ParseRules(Schema(meta));

		rules.Should().BeEmpty();
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
