using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Clio.Command.BusinessRules;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class BusinessRuleIdentityMergerTests {

	private const string ExistingRuleUId = "11111111-1111-1111-1111-111111111111";
	private const string ExistingCaseUId = "22222222-2222-2222-2222-222222222222";
	private const string ExistingGroupUId = "33333333-3333-3333-3333-333333333333";

	[Test]
	[Category("Unit")]
	[Description("Stamps the existing persisted rule uId onto the freshly generated parent rule so the platform stores a short diff instead of a new rule.")]
	public void Merge_Should_Preserve_Existing_Rule_UId_When_Replacing_Rule() {
		// Arrange
		JsonObject existingRule = CreateExistingRule();
		BusinessRuleMetadataDto generatedParent = CreateGeneratedRule("Rule_A");
		string generatedUId = generatedParent.UId;

		// Act
		IReadOnlyList<BusinessRuleMetadataDto> grafted =
			BusinessRuleIdentityMerger.Merge(existingRule, [generatedParent], requestedEnabled: null);

		// Assert
		grafted[0].UId.Should().Be(ExistingRuleUId,
			because: "the replacement rule must keep the persisted rule identity");
		grafted[0].UId.Should().NotBe(generatedUId,
			because: "the converter's throwaway uId must be discarded in favor of the persisted one");
	}

	[TestCase(false, null, false)]
	[TestCase(true, null, true)]
	[TestCase(false, true, true)]
	[TestCase(true, false, false)]
	[Category("Unit")]
	[Description("Preserves the existing enabled flag when the caller omits it and applies the caller's explicit enabled intent when provided.")]
	public void Merge_Should_Resolve_Enabled_From_Request_Or_Existing_Rule(
		bool existingEnabled,
		bool? requestedEnabled,
		bool expectedEnabled) {
		// Arrange
		JsonObject existingRule = CreateExistingRule(enabled: existingEnabled);
		BusinessRuleMetadataDto generatedParent = CreateGeneratedRule("Rule_A");

		// Act
		IReadOnlyList<BusinessRuleMetadataDto> grafted =
			BusinessRuleIdentityMerger.Merge(existingRule, [generatedParent], requestedEnabled);

		// Assert
		grafted[0].Enabled.Should().Be(expectedEnabled,
			because: "requestedEnabled overrides the persisted flag while an omitted request preserves it");
	}

	[Test]
	[Category("Unit")]
	[Description("Stamps the existing case uId and top-level group-condition uId onto the generated single case so unchanged case structure keeps stable identity.")]
	public void Merge_Should_Merge_Case_And_Group_Condition_UIds_When_Existing_Rule_Has_Single_Case() {
		// Arrange
		JsonObject existingRule = CreateExistingRule();
		BusinessRuleMetadataDto generatedParent = CreateGeneratedRule("Rule_A");
		string generatedCaseUId = generatedParent.Cases[0].UId;
		string generatedGroupUId = generatedParent.Cases[0].Condition!.UId;

		// Act
		IReadOnlyList<BusinessRuleMetadataDto> grafted =
			BusinessRuleIdentityMerger.Merge(existingRule, [generatedParent], requestedEnabled: null);

		// Assert
		grafted[0].Cases[0].UId.Should().Be(ExistingCaseUId,
			because: "the single generated case must inherit the persisted case identity");
		grafted[0].Cases[0].UId.Should().NotBe(generatedCaseUId,
			because: "the converter's throwaway case uId must be discarded");
		grafted[0].Cases[0].Condition!.UId.Should().Be(ExistingGroupUId,
			because: "the top-level group condition must inherit the persisted group identity");
		grafted[0].Cases[0].Condition!.UId.Should().NotBe(generatedGroupUId,
			because: "the converter's throwaway group uId must be discarded");
	}

	[Test]
	[Category("Unit")]
	[Description("Merges trigger uIds matched by case-insensitive name plus type from the existing rule onto the generated triggers.")]
	public void Merge_Should_Merge_Trigger_UIds_When_Name_And_Type_Match_Case_Insensitively() {
		// Arrange
		JsonObject existingRule = CreateExistingRule(triggersJson: """
			[
			  { "typeName": "Trigger", "uId": "44444444-4444-4444-4444-444444444444", "name": "Status", "type": 0 },
			  { "typeName": "Trigger", "uId": "55555555-5555-5555-5555-555555555555", "name": "", "type": 2 }
			]
			""");
		BusinessRuleMetadataDto generatedParent = CreateGeneratedRule("Rule_A");
		generatedParent.Triggers = [
			CreateTrigger("STATUS", 0),
			CreateTrigger(string.Empty, 2)
		];

		// Act
		IReadOnlyList<BusinessRuleMetadataDto> grafted =
			BusinessRuleIdentityMerger.Merge(existingRule, [generatedParent], requestedEnabled: null);

		// Assert
		grafted[0].Triggers[0].UId.Should().Be("44444444-4444-4444-4444-444444444444",
			because: "a trigger matching by case-insensitive name and type inherits the persisted trigger identity");
		grafted[0].Triggers[1].UId.Should().Be("55555555-5555-5555-5555-555555555555",
			because: "the DataLoaded trigger (empty name, type 2) inherits the persisted trigger identity");
	}

	[Test]
	[Category("Unit")]
	[Description("Merges a persisted change trigger whose zero-valued 'type' property was stripped by the platform's addon-metadata normalization, treating an absent type as ChangeAttributeValue (0).")]
	public void Merge_Should_Merge_Trigger_UId_When_Persisted_Change_Trigger_Has_No_Type_Property() {
		// Arrange - Creatio omits zero-valued properties on read-back, so change triggers (type 0)
		// come back without a "type" property at all.
		JsonObject existingRule = CreateExistingRule(triggersJson: """
			[
			  { "typeName": "Trigger", "uId": "44444444-4444-4444-4444-444444444444", "name": "Status" }
			]
			""");
		BusinessRuleMetadataDto generatedParent = CreateGeneratedRule("Rule_A");
		generatedParent.Triggers = [CreateTrigger("Status", 0)];

		// Act
		IReadOnlyList<BusinessRuleMetadataDto> grafted =
			BusinessRuleIdentityMerger.Merge(existingRule, [generatedParent], requestedEnabled: null);

		// Assert
		grafted[0].Triggers[0].UId.Should().Be("44444444-4444-4444-4444-444444444444",
			because: "a persisted change trigger stripped of its zero-valued type must still match a generated type-0 trigger");
	}

	[Test]
	[Category("Unit")]
	[Description("Keeps fresh uIds on generated triggers that have no matching existing trigger and consumes each existing trigger uId at most once.")]
	public void Merge_Should_Keep_Fresh_Trigger_UIds_When_No_Existing_Trigger_Matches() {
		// Arrange
		JsonObject existingRule = CreateExistingRule(triggersJson: """
			[
			  { "typeName": "Trigger", "uId": "44444444-4444-4444-4444-444444444444", "name": "Status", "type": 0 }
			]
			""");
		BusinessRuleMetadataDto generatedParent = CreateGeneratedRule("Rule_A");
		BusinessRuleTriggerMetadataDto firstStatusTrigger = CreateTrigger("Status", 0);
		BusinessRuleTriggerMetadataDto secondStatusTrigger = CreateTrigger("Status", 0);
		BusinessRuleTriggerMetadataDto unmatchedTrigger = CreateTrigger("Owner", 0);
		string secondStatusFreshUId = secondStatusTrigger.UId;
		string unmatchedFreshUId = unmatchedTrigger.UId;
		generatedParent.Triggers = [firstStatusTrigger, secondStatusTrigger, unmatchedTrigger];

		// Act
		BusinessRuleIdentityMerger.Merge(existingRule, [generatedParent], requestedEnabled: null);

		// Assert
		firstStatusTrigger.UId.Should().Be("44444444-4444-4444-4444-444444444444",
			because: "the first matching generated trigger consumes the persisted trigger uId");
		secondStatusTrigger.UId.Should().Be(secondStatusFreshUId,
			because: "each persisted trigger uId is consumed at most once, so a duplicate keeps its fresh uId");
		unmatchedTrigger.UId.Should().Be(unmatchedFreshUId,
			because: "a generated trigger without a persisted (name, type) match keeps its fresh uId");
	}

	[Test]
	[Category("Unit")]
	[Description("Re-anchors regenerated apply-filter child rules from the converter's throwaway parent uId to the preserved existing uId, rewriting the uId embedded in child names and captions, while leaving unrelated children untouched.")]
	public void Merge_Should_Reanchor_Child_Rules_When_Children_Reference_Generated_Parent_UId() {
		// Arrange
		JsonObject existingRule = CreateExistingRule();
		BusinessRuleMetadataDto generatedParent = CreateGeneratedRule("Rule_A");
		string generatedParentUId = generatedParent.UId;
		BusinessRuleMetadataDto child = CreateGeneratedRule($"Autogenerated_{generatedParentUId}_ClearValue");
		child.ParentUId = generatedParentUId;
		child.Caption = $"ChildRule-{generatedParentUId}-ClearValue";
		BusinessRuleMetadataDto unrelatedChild = CreateGeneratedRule("Autogenerated_other_ClearValue");
		unrelatedChild.ParentUId = "99999999-9999-9999-9999-999999999999";
		unrelatedChild.Caption = "ChildRule-other-ClearValue";

		// Act
		BusinessRuleIdentityMerger.Merge(
			existingRule,
			[generatedParent, child, unrelatedChild],
			requestedEnabled: null);

		// Assert
		child.ParentUId.Should().Be(ExistingRuleUId,
			because: "regenerated children must point at the preserved existing parent uId");
		child.Name.Should().Be($"Autogenerated_{ExistingRuleUId}_ClearValue",
			because: "the parent uId embedded in the autogenerated child name must be rewritten");
		child.Caption.Should().Be($"ChildRule-{ExistingRuleUId}-ClearValue",
			because: "the parent uId embedded in the autogenerated child caption must be rewritten");
		unrelatedChild.ParentUId.Should().Be("99999999-9999-9999-9999-999999999999",
			because: "children anchored to a different parent must not be re-anchored");
		unrelatedChild.Name.Should().Be("Autogenerated_other_ClearValue",
			because: "children anchored to a different parent must keep their names untouched");
	}

	[Test]
	[Category("Unit")]
	[Description("Throws when the existing persisted rule carries no uId, because identity cannot be preserved without it.")]
	public void Merge_Should_Throw_When_Existing_Rule_Has_No_UId() {
		// Arrange
		JsonObject existingRule = (JsonObject)JsonNode.Parse("""{ "name": "Rule_A", "enabled": true }""")!;
		BusinessRuleMetadataDto generatedParent = CreateGeneratedRule("Rule_A");

		// Act
		Action act = () => BusinessRuleIdentityMerger.Merge(existingRule, [generatedParent], requestedEnabled: null);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("Existing business rule has no uId.",
				because: "a persisted rule without a uId cannot donate its identity to the replacement");
	}

	private static JsonObject CreateExistingRule(bool enabled = true, string triggersJson = "[]") =>
		(JsonObject)JsonNode.Parse($$"""
			{
			  "typeName": "BusinessRule",
			  "uId": "{{ExistingRuleUId}}",
			  "name": "Rule_A",
			  "enabled": {{(enabled ? "true" : "false")}},
			  "caption": "Rule A",
			  "cases": [
			    {
			      "typeName": "Case",
			      "uId": "{{ExistingCaseUId}}",
			      "condition": {
			        "typeName": "{{BusinessRuleConstants.BusinessRuleGroupConditionTypeName}}",
			        "uId": "{{ExistingGroupUId}}",
			        "logicalOperation": 1,
			        "conditions": []
			      },
			      "actions": []
			    }
			  ],
			  "triggers": {{triggersJson}}
			}
			""")!;

	private static BusinessRuleMetadataDto CreateGeneratedRule(string name) =>
		new() {
			UId = Guid.NewGuid().ToString(),
			Name = name,
			Enabled = true,
			Caption = "Generated caption",
			Cases = [
				new BusinessRuleCaseMetadataDto {
					UId = Guid.NewGuid().ToString(),
					Condition = new BusinessRuleGroupConditionMetadataDto {
						TypeName = BusinessRuleConstants.BusinessRuleGroupConditionTypeName,
						UId = Guid.NewGuid().ToString(),
						LogicalOperation = BusinessRuleConstants.LogicalAnd,
						Conditions = []
					},
					Actions = []
				}
			],
			Triggers = []
		};

	private static BusinessRuleTriggerMetadataDto CreateTrigger(string name, int type) =>
		new() {
			UId = Guid.NewGuid().ToString(),
			Name = name,
			Type = type
		};
}
