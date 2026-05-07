using System;
using System.Collections.Generic;
using System.Text.Json;
using Clio.Command.BusinessRules;
using Clio.Command.EntitySchemaDesigner;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.BusinessRules.Filters;

[TestFixture]
[Property("Module", "Command.BusinessRules.Filters")]
public sealed class BusinessRuleValidatorSetFilterTests {

	private const int LookupDataValueType = 10;
	private const int TextDataValueType = 1;

	[Test]
	[Category("Unit")]
	[Description("Rejects apply-static-filter actions whose targetAttribute is empty.")]
	public void Validate_Should_Reject_Missing_TargetAttribute() {
		BusinessRule rule = BuildRule(targetAttribute: string.Empty, filterJson: "{\"logicalOperation\":\"AND\",\"filters\":[]}");
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = BuildColumnMap();

		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		act.Should().Throw<ArgumentException>()
			.WithMessage("filter.target-attribute-required:*");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects apply-static-filter actions whose targetAttribute is not on the entity schema.")]
	public void Validate_Should_Reject_Unknown_TargetAttribute() {
		BusinessRule rule = BuildRule(targetAttribute: "UsrMissing", filterJson: "{\"logicalOperation\":\"AND\",\"filters\":[]}");
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = BuildColumnMap();

		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		act.Should().Throw<ArgumentException>()
			.WithMessage("filter.target-attribute-unknown:*");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects apply-static-filter actions whose targetAttribute is not a Lookup column.")]
	public void Validate_Should_Reject_Non_Lookup_TargetAttribute() {
		BusinessRule rule = BuildRule(targetAttribute: "UsrName", filterJson: "{\"logicalOperation\":\"AND\",\"filters\":[]}");
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = BuildColumnMap();

		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		act.Should().Throw<ArgumentException>()
			.WithMessage("filter.target-attribute-not-lookup:*");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects apply-static-filter actions that include `items` alongside targetAttribute / filter.")]
	public void Validate_Should_Reject_Items_Not_Allowed() {
		BusinessRule rule = BuildRule(
			targetAttribute: "UsrCity",
			filterJson: "{\"logicalOperation\":\"AND\",\"filters\":[]}",
			extensionData: new Dictionary<string, JsonElement> {
				["items"] = JsonDocument.Parse("[\"UsrName\"]").RootElement
			});
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = BuildColumnMap();

		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		act.Should().Throw<ArgumentException>()
			.WithMessage("filter.items-not-allowed:*");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects apply-static-filter actions whose filter is missing entirely.")]
	public void Validate_Should_Reject_Missing_Filter() {
		BusinessRule rule = BuildRule(targetAttribute: "UsrCity", filterJson: null);
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = BuildColumnMap();

		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		act.Should().Throw<ArgumentException>()
			.WithMessage("filter.required:*");
	}

	[Test]
	[Category("Unit")]
	[Description("Accepts a well-formed apply-static-filter action against a Lookup target with a valid filter group.")]
	public void Validate_Should_Pass_Happy_Path() {
		BusinessRule rule = BuildRule(
			targetAttribute: "UsrCity",
			filterJson: "{\"logicalOperation\":\"AND\",\"filters\":[" +
				"{\"columnPath\":\"Country\",\"comparisonType\":\"EQUAL\"," +
				"\"value\":\"a470b005-e8bb-df11-b00f-001d60e938c6\"}]}");
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = BuildColumnMap();

		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		act.Should().NotThrow();
	}

	private static BusinessRule BuildRule(
		string targetAttribute,
		string? filterJson,
		Dictionary<string, JsonElement>? extensionData = null) {
		ApplyStaticFilterBusinessRuleAction action = new() {
			TargetAttribute = targetAttribute,
			Filter = filterJson is null
				? default
				: JsonDocument.Parse(filterJson).RootElement.Clone(),
			ExtensionData = extensionData
		};
		BusinessRuleCondition condition = new() {
			LeftExpression = new BusinessRuleExpression {
				Type = "AttributeValue",
				Path = "UsrName"
			},
			ComparisonType = "is-filled-in"
		};
		BusinessRuleConditionGroup conditionGroup = new() {
			LogicalOperation = "AND",
			Conditions = [condition]
		};
		return new BusinessRule {
			Caption = "Restrict City to Ukraine",
			Condition = conditionGroup,
			Actions = [action]
		};
	}

	private static IReadOnlyDictionary<string, EntitySchemaColumnDto> BuildColumnMap() {
		Dictionary<string, EntitySchemaColumnDto> map = new(StringComparer.OrdinalIgnoreCase) {
			["UsrCity"] = new EntitySchemaColumnDto {
				Name = "UsrCity",
				DataValueType = LookupDataValueType,
				ReferenceSchema = new EntityDesignSchemaDto { Name = "City" }
			},
			["UsrName"] = new EntitySchemaColumnDto {
				Name = "UsrName",
				DataValueType = TextDataValueType
			}
		};
		return map;
	}
}
