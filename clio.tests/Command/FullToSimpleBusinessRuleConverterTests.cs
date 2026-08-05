using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Command.AddonSchemaDesigner;
using Clio.Command.BusinessRules;
using Clio.Command.EntitySchemaDesigner;
using FluentAssertions;
using NUnit.Framework;
using Clio.Command.BusinessRules.Converters;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class FullToSimpleBusinessRuleConverterTests {

	[Test]
	[Category("Unit")]
	[Description("Round-trips converter-generated metadata back into a semantically equal friendly rule, propagating every persisted block uId so update can preserve identity.")]
	public void Read_Should_RoundTrip_Converter_Metadata_With_Block_UIds() {
		// Arrange
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("Owner", 10, "Contact"),
			CreateColumn("Amount", 6));
		BusinessRule input = new(
			"Round trip",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "Status", null),
						"equal",
						new BusinessRuleExpression("Const", null, Json("Draft"))),
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "Owner", null),
						"equal",
						new BusinessRuleExpression("SysValue", sysValueName: "CurrentUserContact")),
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "Amount", null),
						"is-filled-in")
				]),
			[
				new MakeReadOnlyBusinessRuleAction(["Status", "Amount"]),
				new SetValuesBusinessRuleAction([
					new BusinessRuleSetValueItem(
						new BusinessRuleExpression("AttributeValue", "Status", null),
						new BusinessRuleExpression("Const", null, Json("Ready")))
				])
			]);
		BusinessRuleMetadataDto metadata = SimpleToFullBusinessRuleConverter.ToMetadata(columnMap, input, "UsrOrder");
		BusinessRuleGroupConditionMetadataDto metadataGroup =
			(BusinessRuleGroupConditionMetadataDto)metadata.Cases[0].Condition!;
		List<BusinessRuleSetValueItemMetadataDto> metadataSetValueItems =
			(List<BusinessRuleSetValueItemMetadataDto>)((FieldSelectionBusinessRuleActionMetadataDto)metadata.Cases[0].Actions[1]).Items!;
		JsonArray rules = [JsonSerializer.SerializeToNode(metadata, BusinessRuleConstants.JsonOptions)];

		// Act
		IReadOnlyList<BusinessRule> models = FullToSimpleBusinessRuleConverter.Convert(rules, []);

		// Assert
		models.Should().ContainSingle(because: "one persisted parent rule yields one friendly rule");
		BusinessRule rule = models[0];
		rule.Caption.Should().Be("Round trip", because: "the persisted caption round-trips onto the friendly rule");
		rule.Name.Should().Be(metadata.Name, because: "the internal rule name is the update/delete match key and must round-trip");
		rule.Enabled.Should().BeTrue(because: "the friendly rule carries the persisted enabled flag");
		rule.Condition.LogicalOperation.Should().Be("AND", because: "the persisted logical AND maps back to the friendly name");
		rule.Condition.Conditions.Should().HaveCount(3, because: "every persisted condition round-trips");
		rule.Condition.Conditions[0].ComparisonType.Should().Be("equal",
			because: "the persisted comparison value maps back to the friendly comparison name");
		rule.Condition.Conditions[0].LeftExpression.Type.Should().Be("AttributeValue",
			because: "attribute operands round-trip with their type");
		rule.Condition.Conditions[0].LeftExpression.Path.Should().Be("Status",
			because: "attribute operands round-trip with their path");
		rule.Condition.Conditions[0].RightExpression!.Type.Should().Be("Const",
			because: "constant operands round-trip with their type");
		rule.Condition.Conditions[0].RightExpression!.Value!.Value.GetString().Should().Be("Draft",
			because: "the persisted constant payload round-trips verbatim");
		rule.Condition.Conditions[1].RightExpression!.Type.Should().Be("SysValue",
			because: "system-variable operands round-trip with their type");
		rule.Condition.Conditions[1].RightExpression!.SysValueName.Should().Be("CurrentUserContact",
			because: "the canonical system-variable name round-trips");
		rule.Condition.Conditions[2].ComparisonType.Should().Be("is-filled-in",
			because: "unary comparisons round-trip by name");
		rule.Condition.Conditions[2].RightExpression.Should().BeNull(
			because: "unary comparisons have no right operand");
		for (int index = 0; index < 3; index++) {
			rule.Condition.Conditions[index].UId.Should().Be(metadataGroup.Conditions[index].UId,
				because: "condition block uIds must be propagated so update can preserve identity");
			rule.Condition.Conditions[index].LeftExpression.UId.Should().Be(metadataGroup.Conditions[index].LeftExpression.UId,
				because: "left expression block uIds must be propagated so update can preserve identity");
		}

		rule.Condition.Conditions[0].RightExpression!.UId.Should().Be(metadataGroup.Conditions[0].RightExpression!.UId,
			because: "right expression block uIds must be propagated so update can preserve identity");
		rule.Actions.Should().HaveCount(2, because: "both persisted actions round-trip");
		MakeReadOnlyBusinessRuleAction readOnlyAction = rule.Actions[0]
			.Should().BeOfType<MakeReadOnlyBusinessRuleAction>(
				because: "the persisted read-only action type maps back to the friendly action").Subject;
		readOnlyAction.Items.Should().Equal(["Status", "Amount"],
			because: "the persisted CSV items string maps back to the friendly item list");
		readOnlyAction.UId.Should().Be(metadata.Cases[0].Actions[0].UId,
			because: "action block uIds must be propagated so update can preserve identity");
		SetValuesBusinessRuleAction setValuesAction = rule.Actions[1]
			.Should().BeOfType<SetValuesBusinessRuleAction>(
				because: "the persisted set-values action type maps back to the friendly action").Subject;
		setValuesAction.UId.Should().Be(metadata.Cases[0].Actions[1].UId,
			because: "set-values action block uIds must be propagated so update can preserve identity");
		setValuesAction.Items.Should().ContainSingle(because: "the single persisted set-value item round-trips");
		setValuesAction.Items[0].UId.Should().Be(metadataSetValueItems[0].UId,
			because: "set-value item block uIds must be propagated so update can preserve identity");
		setValuesAction.Items[0].Expression.Path.Should().Be("Status",
			because: "the set-value target attribute round-trips");
		setValuesAction.Items[0].Value.Type.Should().Be("Const",
			because: "the set-value constant source round-trips with its type");
		setValuesAction.Items[0].Value.Value!.Value.GetString().Should().Be("Ready",
			because: "the set-value constant payload round-trips verbatim");
	}

	[Test]
	[Category("Unit")]
	[Description("Skips autogenerated apply-filter child rules (parentUId set) and folds their behavior into the parent's ApplyFilter action flags and paths, mapping the persisted 'null' filterExpression sentinel back to an absent source filter path.")]
	public void Read_Should_Skip_Child_Rules_And_Fold_ApplyFilter_Flags_Into_Parent_Action() {
		// Arrange
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("City", 10, "City"),
			CreateColumn("Country", 10, "Country"),
			CreateColumn("City.Country", 10, "Country"));
		BusinessRule input = new(
			"Filter city by country",
			new BusinessRuleConditionGroup("AND", []),
			[
				new ApplyFilterBusinessRuleAction(
					"City",
					"Country",
					"Country",
					null,
					clearValue: true,
					populateValue: true)
			]);
		IReadOnlyList<BusinessRuleMetadataDto> generatedRules =
			SimpleToFullBusinessRuleConverter.ToEntityMetadata(columnMap, input, "UsrOrder");
		JsonArray rules = [];
		foreach (BusinessRuleMetadataDto generatedRule in generatedRules) {
			rules.Add(JsonSerializer.SerializeToNode(generatedRule, BusinessRuleConstants.JsonOptions));
		}

		// Act
		IReadOnlyList<BusinessRule> models = FullToSimpleBusinessRuleConverter.Convert(rules, []);

		// Assert
		generatedRules.Should().HaveCount(3,
			because: "clear+populate apply-filter generates the parent plus two child rules, which sets up the skip scenario");
		models.Should().ContainSingle(
			because: "autogenerated child rules with parentUId must never surface as separate rules");
		ApplyFilterBusinessRuleAction action = models[0].Actions.Single()
			.Should().BeOfType<ApplyFilterBusinessRuleAction>(
				because: "the persisted filter-lookup action maps back to the friendly apply-filter action").Subject;
		action.Target.Should().Be("City", because: "the target lookup path round-trips from the left expression");
		action.TargetFilterPath.Should().Be("Country", because: "the target-side filter path round-trips from the left filterExpression");
		action.Source.Should().Be("Country", because: "the source lookup path round-trips from the right expression");
		action.SourceFilterPath.Should().BeNull(
			because: "the persisted literal 'null' filterExpression sentinel maps back to an absent source filter path");
		action.ClearValue.Should().BeTrue(because: "the persisted clearValue flag folds the clear child rule into the parent action");
		action.PopulateValue.Should().BeTrue(because: "the persisted populateValue flag folds the populate child rule into the parent action");
		action.UId.Should().Be(generatedRules[0].Cases[0].Actions[0].UId,
			because: "the parent action block uId must be propagated so update can preserve identity");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps a persisted set-filter action into an ApplyStaticFilterBusinessRuleAction whose persisted ESQ envelope is decompiled back into the friendly filter shape used to create it.")]
	public void Read_Should_Map_SetFilter_Action_To_ApplyStaticFilter_With_Decompiled_Filter() {
		// Arrange
		const string esqEnvelope = """
			{"rootSchemaName":"Account","filterType":6,"logicalOperation":0,"isEnabled":true,"items":{"Filter_0":{"filterType":2,"comparisonType":2,"isNull":false,"isEnabled":true,"leftExpression":{"expressionType":0,"columnPath":"Email","className":"Terrasoft.ColumnExpression"},"key":"","className":"Terrasoft.IsNullFilter"}},"key":"","className":"Terrasoft.FilterGroup"}
			""";
		JsonArray rules = ParseRules($$"""
			[
			  {
			    "typeName": "{{BusinessRuleConstants.BusinessRuleTypeName}}",
			    "uId": "aaaaaaaa-0000-0000-0000-000000000001",
			    "name": "BusinessRule_flt",
			    "enabled": true,
			    "caption": "Static filter",
			    "cases": [
			      {
			        "typeName": "{{BusinessRuleConstants.BusinessRuleCaseTypeName}}",
			        "uId": "aaaaaaaa-0000-0000-0000-000000000002",
			        "condition": {
			          "typeName": "{{BusinessRuleConstants.BusinessRuleGroupConditionTypeName}}",
			          "uId": "aaaaaaaa-0000-0000-0000-000000000003",
			          "logicalOperation": 1,
			          "conditions": []
			        },
			        "actions": [
			          {
			            "typeName": "{{BusinessRuleConstants.BusinessRuleSetFilterElementTypeName}}",
			            "uId": "aaaaaaaa-0000-0000-0000-000000000004",
			            "enabled": true,
			            "expression": {
			              "typeName": "{{BusinessRuleConstants.BusinessRuleAttributeExpressionTypeName}}",
			              "uId": "aaaaaaaa-0000-0000-0000-000000000005",
			              "type": "AttributeValue",
			              "path": "Account"
			            },
			            "value": {
			              "typeName": "{{BusinessRuleConstants.BusinessRuleValueExpressionTypeName}}",
			              "uId": "aaaaaaaa-0000-0000-0000-000000000006",
			              "type": "Const",
			              "value": {{JsonSerializer.Serialize(esqEnvelope)}}
			            }
			          }
			        ]
			      }
			    ],
			    "triggers": []
			  }
			]
			""");

		// Act
		IReadOnlyList<BusinessRule> models = FullToSimpleBusinessRuleConverter.Convert(rules, []);

		// Assert
		models.Should().ContainSingle(because: "the single persisted rule yields one friendly rule");
		ApplyStaticFilterBusinessRuleAction action = models[0].Actions.Single()
			.Should().BeOfType<ApplyStaticFilterBusinessRuleAction>(
				because: "set-filter metadata maps back to the friendly apply-static-filter action").Subject;
		action.TargetAttribute.Should().Be("Account",
			because: "the filtered lookup attribute round-trips from the action expression path");
		action.Filter.ValueKind.Should().Be(JsonValueKind.Object,
			because: "the persisted ESQ envelope is decompiled back into the friendly filter object");
		action.Filter.GetProperty("logicalOperation").GetString().Should().Be("AND",
			because: "the envelope's AND logical operation maps back to the friendly token");
		JsonElement decompiledLeaf = action.Filter.GetProperty("filters").EnumerateArray().Single();
		decompiledLeaf.GetProperty("columnPath").GetString().Should().Be("Email",
			because: "the IsNull filter column round-trips into the friendly leaf");
		decompiledLeaf.GetProperty("comparisonType").GetString().Should().Be("IS_NOT_NULL",
			because: "comparisonType 2 (IsNotNull) maps back to the friendly IS_NOT_NULL token");
		action.UId.Should().Be("aaaaaaaa-0000-0000-0000-000000000004",
			because: "the action block uId must be propagated so update can preserve identity");
	}

	[Test]
	[Category("Unit")]
	[Description("Throws InvalidOperationException naming the rule and the unknown action typeName when a persisted action cannot be represented in the rule contract.")]
	public void Read_Should_Throw_When_Action_TypeName_Is_Unknown() {
		// Arrange
		JsonArray rules = ParseRules($$"""
			[
			  {
			    "typeName": "{{BusinessRuleConstants.BusinessRuleTypeName}}",
			    "uId": "bbbbbbbb-0000-0000-0000-000000000001",
			    "name": "BusinessRule_odd",
			    "enabled": false,
			    "caption": "Designer authored",
			    "cases": [
			      {
			        "typeName": "{{BusinessRuleConstants.BusinessRuleCaseTypeName}}",
			        "uId": "bbbbbbbb-0000-0000-0000-000000000002",
			        "condition": {
			          "typeName": "{{BusinessRuleConstants.BusinessRuleGroupConditionTypeName}}",
			          "uId": "bbbbbbbb-0000-0000-0000-000000000003",
			          "logicalOperation": 1,
			          "conditions": []
			        },
			        "actions": [
			          { "typeName": "Some.Unknown.DesignerAction", "uId": "bbbbbbbb-0000-0000-0000-000000000004", "enabled": true }
			        ]
			      }
			    ],
			    "triggers": []
			  }
			]
			""");

		// Act
		Action act = () => FullToSimpleBusinessRuleConverter.Convert(rules, []);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*Business rule 'BusinessRule_odd' cannot be represented in the rule contract:*",
				because: "the error must name the failing rule so the caller can fix or delete it")
			.WithMessage("*Some.Unknown.DesignerAction*",
				because: "the error must name the unsupported action typeName");
	}

	[Test]
	[Category("Unit")]
	[Description("Throws InvalidOperationException naming the rule for multi-case rules, which the single-case rule contract cannot represent.")]
	public void Read_Should_Throw_When_Rule_Has_Multiple_Cases() {
		// Arrange
		JsonArray rules = ParseRules($$"""
			[
			  {
			    "typeName": "{{BusinessRuleConstants.BusinessRuleTypeName}}",
			    "uId": "cccccccc-0000-0000-0000-000000000001",
			    "name": "BusinessRule_multi",
			    "enabled": true,
			    "caption": "Multi case",
			    "cases": [
			      { "typeName": "{{BusinessRuleConstants.BusinessRuleCaseTypeName}}", "uId": "cccccccc-0000-0000-0000-000000000002", "actions": [] },
			      { "typeName": "{{BusinessRuleConstants.BusinessRuleCaseTypeName}}", "uId": "cccccccc-0000-0000-0000-000000000003", "actions": [] }
			    ],
			    "triggers": []
			  }
			]
			""");

		// Act
		Action act = () => FullToSimpleBusinessRuleConverter.Convert(rules, []);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*Business rule 'BusinessRule_multi' cannot be represented in the rule contract:*",
				because: "the error must name the failing rule so the caller can fix or delete it")
			.WithMessage("*single-case*",
				because: "the error must explain that only single-case rules are supported");
	}

	[Test]
	[Category("Unit")]
	[Description("Throws InvalidOperationException naming the rule when the condition group nests another group, which the single-group rule contract cannot represent.")]
	public void Read_Should_Throw_When_Condition_Group_Contains_Nested_Group() {
		// Arrange
		JsonArray rules = ParseRules($$"""
			[
			  {
			    "typeName": "{{BusinessRuleConstants.BusinessRuleTypeName}}",
			    "uId": "dddddddd-0000-0000-0000-000000000001",
			    "name": "BusinessRule_nested",
			    "enabled": true,
			    "caption": "Nested group",
			    "cases": [
			      {
			        "typeName": "{{BusinessRuleConstants.BusinessRuleCaseTypeName}}",
			        "uId": "dddddddd-0000-0000-0000-000000000002",
			        "condition": {
			          "typeName": "{{BusinessRuleConstants.BusinessRuleGroupConditionTypeName}}",
			          "uId": "dddddddd-0000-0000-0000-000000000003",
			          "logicalOperation": 1,
			          "conditions": [
			            {
			              "typeName": "{{BusinessRuleConstants.BusinessRuleGroupConditionTypeName}}",
			              "uId": "dddddddd-0000-0000-0000-000000000004",
			              "logicalOperation": 2,
			              "conditions": []
			            }
			          ]
			        },
			        "actions": []
			      }
			    ],
			    "triggers": []
			  }
			]
			""");

		// Act
		Action act = () => FullToSimpleBusinessRuleConverter.Convert(rules, []);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*Business rule 'BusinessRule_nested' cannot be represented in the rule contract:*",
				because: "the error must name the failing rule so the caller can fix or delete it")
			.WithMessage("*nested condition typeName*",
				because: "the error must name the unsupported nested-group construct");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves the caption from the {uId}.Caption schema resource and defaults enabled to true when both properties are missing from the rule metadata.")]
	public void Read_Should_Resolve_Caption_From_Resources_And_Default_Enabled_When_Missing() {
		// Arrange
		JsonArray rules = ParseRules($$"""
			[
			  {
			    "typeName": "{{BusinessRuleConstants.BusinessRuleTypeName}}",
			    "uId": "eeeeeeee-0000-0000-0000-000000000001",
			    "name": "BusinessRule_res",
			    "cases": [
			      {
			        "typeName": "{{BusinessRuleConstants.BusinessRuleCaseTypeName}}",
			        "uId": "eeeeeeee-0000-0000-0000-000000000002",
			        "actions": []
			      }
			    ],
			    "triggers": []
			  }
			]
			""");
		List<AddonResourceDto> resources = [
			new AddonResourceDto {
				Key = "eeeeeeee-0000-0000-0000-000000000001.Caption",
				Value = [
					new AddonResourceValueDto { Key = "en-US", Value = "Caption from resource" }
				]
			}
		];

		// Act
		IReadOnlyList<BusinessRule> models = FullToSimpleBusinessRuleConverter.Convert(rules, resources);

		// Assert
		models.Should().ContainSingle(because: "the single persisted rule yields one friendly rule");
		models[0].Caption.Should().Be("Caption from resource",
			because: "a missing metadata caption falls back to the {uId}.Caption schema resource");
		models[0].Enabled.Should().BeTrue(because: "a missing enabled property defaults to true");
		models[0].Condition.LogicalOperation.Should().Be("AND",
			because: "a missing case condition maps to an empty AND group");
		models[0].Condition.Conditions.Should().BeEmpty(
			because: "a missing case condition carries no condition entries");
	}

	[Test]
	[Category("Unit")]
	[Description("Wraps a bare single condition (persisted without a surrounding group) into a one-condition AND group and splits persisted CSV item strings into trimmed lists, honoring enabled=false.")]
	public void Read_Should_Wrap_Bare_Condition_In_And_Group_And_Split_Csv_Items() {
		// Arrange
		JsonArray rules = ParseRules($$"""
			[
			  {
			    "typeName": "{{BusinessRuleConstants.BusinessRuleTypeName}}",
			    "uId": "ffffffff-0000-0000-0000-000000000001",
			    "name": "BusinessRule_csv",
			    "enabled": false,
			    "caption": "CSV items",
			    "cases": [
			      {
			        "typeName": "{{BusinessRuleConstants.BusinessRuleCaseTypeName}}",
			        "uId": "ffffffff-0000-0000-0000-000000000002",
			        "condition": {
			          "typeName": "{{BusinessRuleConstants.BusinessRuleConditionTypeName}}",
			          "uId": "ffffffff-0000-0000-0000-000000000003",
			          "comparisonType": 2,
			          "leftExpression": {
			            "typeName": "{{BusinessRuleConstants.BusinessRuleAttributeExpressionTypeName}}",
			            "uId": "ffffffff-0000-0000-0000-000000000004",
			            "type": "AttributeValue",
			            "path": "Status"
			          },
			          "rightExpression": {
			            "typeName": "{{BusinessRuleConstants.BusinessRuleValueExpressionTypeName}}",
			            "uId": "ffffffff-0000-0000-0000-000000000005",
			            "type": "Const",
			            "value": "Draft"
			          }
			        },
			        "actions": [
			          {
			            "typeName": "{{BusinessRuleConstants.BusinessRuleEditableElementTypeName}}",
			            "uId": "ffffffff-0000-0000-0000-000000000006",
			            "enabled": true,
			            "items": " Owner , Amount ,,Status "
			          }
			        ]
			      }
			    ],
			    "triggers": []
			  }
			]
			""");

		// Act
		IReadOnlyList<BusinessRule> models = FullToSimpleBusinessRuleConverter.Convert(rules, []);

		// Assert
		models.Should().ContainSingle(because: "the single persisted rule yields one friendly rule");
		BusinessRule model = models[0];
		model.Enabled.Should().BeFalse(because: "the persisted enabled=false flag must be honored");
		model.Condition.LogicalOperation.Should().Be("AND",
			because: "a bare condition is wrapped into an AND group, which the platform accepts on update");
		model.Condition.Conditions.Should().ContainSingle(
			because: "the bare condition becomes the single entry of the wrapping group");
		model.Condition.Conditions[0].UId.Should().Be("ffffffff-0000-0000-0000-000000000003",
			because: "the wrapped condition keeps its persisted block uId");
		MakeEditableBusinessRuleAction action = model.Actions.Single()
			.Should().BeOfType<MakeEditableBusinessRuleAction>(
				because: "the persisted editable action type maps back to the friendly action").Subject;
		action.Items.Should().Equal(["Owner", "Amount", "Status"],
			because: "persisted CSV item strings are split, trimmed, and stripped of empty entries");
	}

	[Test]
	[Category("Unit")]
	[Description("Rebuilds the '<dataSource>.<column>' friendly path from a persisted data-source-scoped attribute expression scopeId.")]
	public void Read_Should_Rebuild_Datasource_Scoped_Path_From_ScopeId() {
		// Arrange
		JsonArray rules = ParseRules($$"""
			[
			  {
			    "typeName": "{{BusinessRuleConstants.BusinessRuleTypeName}}",
			    "uId": "cdcdcdcd-0000-0000-0000-000000000001",
			    "name": "BusinessRule_scoped",
			    "enabled": true,
			    "caption": "Scoped condition",
			    "cases": [
			      {
			        "typeName": "{{BusinessRuleConstants.BusinessRuleCaseTypeName}}",
			        "uId": "cdcdcdcd-0000-0000-0000-000000000002",
			        "condition": {
			          "typeName": "{{BusinessRuleConstants.BusinessRuleGroupConditionTypeName}}",
			          "uId": "cdcdcdcd-0000-0000-0000-000000000003",
			          "logicalOperation": 1,
			          "conditions": [
			            {
			              "typeName": "{{BusinessRuleConstants.BusinessRuleConditionTypeName}}",
			              "uId": "cdcdcdcd-0000-0000-0000-000000000004",
			              "comparisonType": 7,
			              "leftExpression": {
			                "typeName": "{{BusinessRuleConstants.BusinessRuleAttributeExpressionTypeName}}",
			                "uId": "cdcdcdcd-0000-0000-0000-000000000005",
			                "type": "AttributeValue",
			                "path": "ModifiedOn",
			                "scopeId": "PDS"
			              },
			              "rightExpression": {
			                "typeName": "{{BusinessRuleConstants.BusinessRuleAttributeExpressionTypeName}}",
			                "uId": "cdcdcdcd-0000-0000-0000-000000000006",
			                "type": "AttributeValue",
			                "path": "CreatedOn",
			                "scopeId": "PDS"
			              }
			            }
			          ]
			        },
			        "actions": [
			          {
			            "typeName": "{{BusinessRuleConstants.BusinessRuleShowElementTypeName}}",
			            "uId": "cdcdcdcd-0000-0000-0000-000000000007",
			            "enabled": true,
			            "items": "Name"
			          }
			        ]
			      }
			    ],
			    "triggers": []
			  }
			]
			""");

		// Act
		IReadOnlyList<BusinessRule> models = FullToSimpleBusinessRuleConverter.Convert(rules, []);

		// Assert
		BusinessRuleCondition condition = models.Single().Condition.Conditions.Single();
		condition.LeftExpression.Path.Should().Be("PDS.ModifiedOn",
			because: "a persisted scopeId is folded back into the '<dataSource>.<column>' friendly path so the rule round-trips");
		condition.RightExpression!.Path.Should().Be("PDS.CreatedOn",
			because: "the right-side scoped attribute path is rebuilt the same way as the left side");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps a persisted formula set-value source to a friendly Formula expression carrying the expressionSchema.expression text as a best effort.")]
	public void Read_Should_Map_Formula_SetValue_Source_To_Formula_Expression_With_Schema_Text() {
		// Arrange
		JsonArray rules = ParseRules($$"""
			[
			  {
			    "typeName": "{{BusinessRuleConstants.BusinessRuleTypeName}}",
			    "uId": "abababab-0000-0000-0000-000000000001",
			    "name": "BusinessRule_formula",
			    "enabled": true,
			    "caption": "Formula",
			    "cases": [
			      {
			        "typeName": "{{BusinessRuleConstants.BusinessRuleCaseTypeName}}",
			        "uId": "abababab-0000-0000-0000-000000000002",
			        "condition": {
			          "typeName": "{{BusinessRuleConstants.BusinessRuleGroupConditionTypeName}}",
			          "uId": "abababab-0000-0000-0000-000000000003",
			          "logicalOperation": 1,
			          "conditions": []
			        },
			        "actions": [
			          {
			            "typeName": "{{BusinessRuleConstants.BusinessRuleSetValuesElementTypeName}}",
			            "uId": "abababab-0000-0000-0000-000000000004",
			            "enabled": true,
			            "items": [
			              {
			                "typeName": "{{BusinessRuleConstants.BusinessRuleSetValueItemTypeName}}",
			                "uId": "abababab-0000-0000-0000-000000000005",
			                "enabled": true,
			                "expression": {
			                  "typeName": "{{BusinessRuleConstants.BusinessRuleAttributeExpressionTypeName}}",
			                  "uId": "abababab-0000-0000-0000-000000000006",
			                  "type": "AttributeValue",
			                  "path": "TotalScore"
			                },
			                "value": {
			                  "typeName": "{{BusinessRuleConstants.BusinessRuleFormulaExpressionTypeName}}",
			                  "uId": "abababab-0000-0000-0000-000000000007",
			                  "type": "Formula",
			                  "expressionSchema": {
			                    "expression": "#UsrOrderRecord.BaseScore# + #UsrOrderRecord.BonusScore#"
			                  }
			                }
			              }
			            ]
			          }
			        ]
			      }
			    ],
			    "triggers": []
			  }
			]
			""");

		// Act
		IReadOnlyList<BusinessRule> models = FullToSimpleBusinessRuleConverter.Convert(rules, []);

		// Assert
		models.Should().ContainSingle(because: "the single persisted rule yields one friendly rule");
		SetValuesBusinessRuleAction action = models[0].Actions.Single()
			.Should().BeOfType<SetValuesBusinessRuleAction>(
				because: "the persisted set-values action type maps back to the friendly action").Subject;
		BusinessRuleSetValueItem item = action.Items.Single();
		item.Value.Type.Should().Be("Formula", because: "formula sources round-trip with the Formula expression type");
		item.Value.Expression.Should().Be("#UsrOrderRecord.BaseScore# + #UsrOrderRecord.BonusScore#",
			because: "the persisted expression-schema text is surfaced as the best-effort formula text");
		item.Value.UId.Should().Be("abababab-0000-0000-0000-000000000007",
			because: "the formula expression block uId must be propagated so update can preserve identity");
	}

	[Test]
	[Category("Unit")]
	[Description("Converts a server-normalized rule whose Const value expression lost the friendly 'type' marker on read-back (the platform persists only typeName/dataValueTypeName/value), dispatching on the persisted typeName instead.")]
	public void Read_Should_Convert_Const_Expression_When_Server_Omits_Type_Marker() {
		// Arrange - the exact shape AddonSchemaDesignerService returns after SaveSchema round-trip:
		// rightExpression has typeName + dataValueTypeName + value but NO "type" property.
		JsonArray rules = ParseRules("""
			[{
				"typeName": "Terrasoft.Core.BusinessRules.BusinessRule",
				"uId": "e0ded58b-d9d4-4a3e-bc79-8a2579ec68f5",
				"name": "UsrServerShapeRule",
				"cases": [{
					"typeName": "Terrasoft.Core.BusinessRules.Models.BusinessRuleCase",
					"uId": "ed18b4de-ac87-40f4-8167-a8772b9c5160",
					"condition": {
						"typeName": "Terrasoft.Core.BusinessRules.Models.Conditions.BusinessRuleGroupCondition",
						"uId": "5778e312-7d26-48c7-b3b6-2c07757b3cd5",
						"logicalOperation": 1,
						"conditions": [{
							"typeName": "Terrasoft.Core.BusinessRules.Models.Conditions.BusinessRuleCondition",
							"uId": "dcac1774-c2ef-4e8c-9543-7ac12495af27",
							"leftExpression": {
								"typeName": "Terrasoft.Core.BusinessRules.Models.Expressions.BusinessRuleAttributeExpression",
								"uId": "685c7f50-a382-437c-a4be-f08bae8e1b55",
								"dataValueTypeName": "MediumText",
								"type": "AttributeValue",
								"path": "Name"
							},
							"rightExpression": {
								"typeName": "Terrasoft.Core.BusinessRules.Models.Expressions.BusinessRuleValueExpression",
								"uId": "94c0cd2b-ffb1-4317-b85d-6ac2819fb574",
								"dataValueTypeName": "MediumText",
								"value": "Alpha"
							},
							"comparisonType": 2
						}]
					},
					"actions": [{
						"typeName": "Terrasoft.Core.BusinessRules.Models.Actions.BusinessRuleActionReadonlyElement",
						"uId": "d7023171-2178-4c35-88a4-b58f89b12b47",
						"items": "Name"
					}]
				}],
				"triggers": []
			}]
			""");

		// Act
		IReadOnlyList<BusinessRule> models = FullToSimpleBusinessRuleConverter.Convert(rules, []);

		// Assert
		BusinessRule model = models.Should().ContainSingle(
				because: "the server-normalized rule is a single parent rule")
			.Which;
		BusinessRuleExpression rightExpression = model.Condition.Conditions[0].RightExpression!;
		rightExpression.Type.Should().Be("Const",
			because: "the persisted BusinessRuleValueExpression typeName maps back to the friendly Const type");
		rightExpression.Value!.Value.GetString().Should().Be("Alpha",
			because: "the persisted constant value round-trips");
		rightExpression.UId.Should().Be("94c0cd2b-ffb1-4317-b85d-6ac2819fb574",
			because: "the persisted expression uId is returned for update round-trips");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps a persisted condition whose zero-valued comparisonType was stripped by the platform's addon-metadata normalization back to is-not-filled-in (ComparisonIsNotFilledIn = 0).")]
	public void Read_Should_Map_Absent_ComparisonType_To_IsNotFilledIn() {
		// Arrange - Creatio omits zero-valued properties on read-back, so a persisted
		// is-not-filled-in condition (comparisonType 0) comes back without the property.
		JsonArray rules = ParseRules("""
			[{
				"typeName": "Terrasoft.Core.BusinessRules.BusinessRule",
				"uId": "1a2b3c4d-0000-4000-8000-000000000001",
				"name": "UsrZeroComparisonRule",
				"cases": [{
					"typeName": "Terrasoft.Core.BusinessRules.Models.BusinessRuleCase",
					"uId": "1a2b3c4d-0000-4000-8000-000000000002",
					"condition": {
						"typeName": "Terrasoft.Core.BusinessRules.Models.Conditions.BusinessRuleGroupCondition",
						"uId": "1a2b3c4d-0000-4000-8000-000000000003",
						"logicalOperation": 1,
						"conditions": [{
							"typeName": "Terrasoft.Core.BusinessRules.Models.Conditions.BusinessRuleCondition",
							"uId": "1a2b3c4d-0000-4000-8000-000000000004",
							"leftExpression": {
								"typeName": "Terrasoft.Core.BusinessRules.Models.Expressions.BusinessRuleAttributeExpression",
								"uId": "1a2b3c4d-0000-4000-8000-000000000005",
								"type": "AttributeValue",
								"path": "Name"
							}
						}]
					},
					"actions": [{
						"typeName": "Terrasoft.Core.BusinessRules.Models.Actions.BusinessRuleActionReadonlyElement",
						"uId": "1a2b3c4d-0000-4000-8000-000000000006",
						"items": "Name"
					}]
				}],
				"triggers": []
			}]
			""");

		// Act
		IReadOnlyList<BusinessRule> models = FullToSimpleBusinessRuleConverter.Convert(rules, []);

		// Assert
		BusinessRule model = models.Should().ContainSingle(
				because: "the zero-comparison rule is a single parent rule")
			.Which;
		model.Condition.Conditions[0].ComparisonType.Should().Be("is-not-filled-in",
			because: "an absent comparisonType means the zero-valued ComparisonIsNotFilledIn was stripped on save");
	}

	[Test]
	[Category("Unit")]
	[Description("Derives clearValue/populateValue from the presence of autogenerated child rules when the platform's addon re-serialization dropped the designer flags from the persisted apply-filter action.")]
	public void Read_Should_Derive_ApplyFilter_Flags_From_Child_Rules_When_Server_Drops_Flags() {
		// Arrange - the persisted parent action carries NO clearValue/populateValue properties
		// (dropped by the server-side model on save); the behavior lives in the child rules.
		JsonArray rules = ParseRules("""
			[{
				"typeName": "Terrasoft.Core.BusinessRules.BusinessRule",
				"uId": "2b3c4d5e-0000-4000-8000-000000000001",
				"name": "UsrFilterParentRule",
				"cases": [{
					"typeName": "Terrasoft.Core.BusinessRules.Models.BusinessRuleCase",
					"uId": "2b3c4d5e-0000-4000-8000-000000000002",
					"condition": {
						"typeName": "Terrasoft.Core.BusinessRules.Models.Conditions.BusinessRuleGroupCondition",
						"uId": "2b3c4d5e-0000-4000-8000-000000000003",
						"logicalOperation": 1,
						"conditions": []
					},
					"actions": [{
						"typeName": "Terrasoft.Core.BusinessRules.Models.Actions.BusinessRuleActionFilterLookup",
						"uId": "2b3c4d5e-0000-4000-8000-000000000004",
						"leftExpression": {
							"typeName": "Terrasoft.Core.BusinessRules.Models.Expressions.BusinessRuleFilterLookupExpression",
							"uId": "2b3c4d5e-0000-4000-8000-000000000005",
							"dataValueTypeName": "Lookup",
							"path": "City",
							"filterExpression": "Country"
						},
						"rightExpression": {
							"typeName": "Terrasoft.Core.BusinessRules.Models.Expressions.BusinessRuleFilterLookupExpression",
							"uId": "2b3c4d5e-0000-4000-8000-000000000006",
							"dataValueTypeName": "Lookup",
							"path": "Country",
							"filterExpression": "null"
						}
					}]
				}],
				"triggers": []
			},
			{
				"typeName": "Terrasoft.Core.BusinessRules.BusinessRule",
				"uId": "2b3c4d5e-0000-4000-8000-000000000011",
				"name": "Autogenerated_2b3c4d5e-0000-4000-8000-000000000001_ClearValue",
				"parentUId": "2b3c4d5e-0000-4000-8000-000000000001",
				"parentActionUId": "2b3c4d5e-0000-4000-8000-000000000004",
				"cases": [],
				"triggers": []
			},
			{
				"typeName": "Terrasoft.Core.BusinessRules.BusinessRule",
				"uId": "2b3c4d5e-0000-4000-8000-000000000012",
				"name": "Autogenerated_2b3c4d5e-0000-4000-8000-000000000001_PopulateValue",
				"parentUId": "2b3c4d5e-0000-4000-8000-000000000001",
				"parentActionUId": "2b3c4d5e-0000-4000-8000-000000000004",
				"cases": [],
				"triggers": []
			}]
			""");

		// Act
		IReadOnlyList<BusinessRule> models = FullToSimpleBusinessRuleConverter.Convert(rules, []);

		// Assert
		BusinessRule model = models.Should().ContainSingle(
				because: "the autogenerated child rules must be folded into the parent, not listed")
			.Which;
		ApplyFilterBusinessRuleAction action = model.Actions.OfType<ApplyFilterBusinessRuleAction>().Should()
			.ContainSingle(because: "the parent rule carries the single apply-filter action")
			.Which;
		action.ClearValue.Should().BeTrue(
			because: "the ClearValue child rule proves the clear-on-change behavior even though the flag was dropped");
		action.PopulateValue.Should().BeTrue(
			because: "the PopulateValue child rule proves the populate behavior even though the flag was dropped");
		action.SourceFilterPath.Should().BeNull(
			because: "the persisted literal 'null' filterExpression maps back to an absent source filter path");
	}

	[Test]
	[Category("Unit")]
	[Description("Throws InvalidOperationException whose message names the rule and the unsupported construct, preserving the underlying conversion failure as the inner exception.")]
	public void Read_Should_Throw_Naming_Unsupported_Construct_When_Rule_Is_Not_Convertible() {
		// Arrange
		JsonArray rules = ParseRules("""
			[{
				"typeName": "Terrasoft.Core.BusinessRules.BusinessRule",
				"uId": "0e6adf3a-9f2f-4d5f-9f5f-0b1f9f34d001",
				"name": "UsrUnsupportedActionRule",
				"cases": [{
					"typeName": "Terrasoft.Core.BusinessRules.Models.BusinessRuleCase",
					"uId": "0e6adf3a-9f2f-4d5f-9f5f-0b1f9f34d002",
					"condition": {
						"typeName": "Terrasoft.Core.BusinessRules.Models.Conditions.BusinessRuleGroupCondition",
						"uId": "0e6adf3a-9f2f-4d5f-9f5f-0b1f9f34d003",
						"logicalOperation": 1,
						"conditions": []
					},
					"actions": [{
						"typeName": "Terrasoft.Core.BusinessRules.Models.Actions.BusinessRuleActionValidateElement",
						"uId": "0e6adf3a-9f2f-4d5f-9f5f-0b1f9f34d004",
						"items": "Name"
					}]
				}],
				"triggers": []
			}]
			""");

		// Act
		Action act = () => FullToSimpleBusinessRuleConverter.Convert(rules, []);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*Business rule 'UsrUnsupportedActionRule' cannot be represented in the rule contract:*",
				because: "the error must name the failing rule so the caller can act on it")
			.WithMessage("*BusinessRuleActionValidateElement*",
				because: "the error must name the unsupported construct so callers can act on it")
			.WithInnerException<InvalidOperationException>(
				because: "the underlying conversion failure must be preserved for diagnostics");
	}

	[Test]
	[Category("Unit")]
	[Description("Round-trips a persisted SysSetting condition operand back into a friendly SysSetting expression carrying the setting code.")]
	public void Read_Should_RoundTrip_SysSetting_Operand() {
		// Arrange
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap =
			new Dictionary<string, BusinessRuleAttributeDescriptor>(StringComparer.Ordinal);
		IReadOnlyDictionary<string, SysSettingOperandDescriptor> sysSettingMap =
			new Dictionary<string, SysSettingOperandDescriptor>(StringComparer.Ordinal) {
				["UseNewShell"] = new("UseNewShell", "Boolean", null)
			};
		BusinessRule input = new(
			"Hide when new shell enabled",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("SysSetting", sysSettingName: "UseNewShell"),
						"equal",
						new BusinessRuleExpression("Const", null, JsonSerializer.Deserialize<JsonElement>("true")))
				]),
			[
				new MakeReadOnlyBusinessRuleAction(["Status"])
			]);
		BusinessRuleMetadataDto metadata = SimpleToFullBusinessRuleConverter.ToMetadata(attributeMap, input, sysSettingMap);
		JsonArray rules = [JsonSerializer.SerializeToNode(metadata, BusinessRuleConstants.JsonOptions)];

		// Act
		IReadOnlyList<BusinessRule> models = FullToSimpleBusinessRuleConverter.Convert(rules, []);

		// Assert
		BusinessRuleExpression left = models[0].Condition.Conditions[0].LeftExpression;
		left.Type.Should().Be(BusinessRuleConstants.SysSettingExpressionType,
			because: "a persisted SysSetting operand must read back as a SysSetting expression");
		left.SysSettingName.Should().Be("UseNewShell",
			because: "the setting code must survive the metadata round-trip so update preserves it");
	}

	private static JsonArray ParseRules(string json) => (JsonArray)JsonNode.Parse(json)!;

	private static IReadOnlyDictionary<string, EntitySchemaColumnDto> CreateColumnMap(params EntitySchemaColumnDto[] columns) {
		Dictionary<string, EntitySchemaColumnDto> result = new(StringComparer.Ordinal);
		foreach (EntitySchemaColumnDto column in columns) {
			result[column.Name] = column;
		}

		return result;
	}

	private static EntitySchemaColumnDto CreateColumn(string name, int dataValueType, string? referenceSchemaName = null) {
		return new EntitySchemaColumnDto {
			Name = name,
			DataValueType = dataValueType,
			ReferenceSchema = string.IsNullOrWhiteSpace(referenceSchemaName)
				? null!
				: new EntityDesignSchemaDto {
					Name = referenceSchemaName
				}
		};
	}

	private static JsonElement Json(string value) =>
		JsonSerializer.Deserialize<JsonElement>($"\"{value}\"");
}
