using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command.BusinessRules;
using Clio.Command.BusinessRules.Filters.Schema;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.BusinessRules;

[TestFixture]
[Property("Module", "Command")]
public sealed class BusinessRuleMetadataConverterApplyStaticFilterTests {

	[Test]
	[Category("Unit")]
	[Description("Produces a single rule with SetFilter action whose value is the ESQ envelope as a JSON string.")]
	public void ToEntityMetadata_Should_Produce_SetFilter_Rule() {
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap = new Dictionary<string, BusinessRuleAttributeDescriptor> {
			["UsrCountry"] = new("UsrCountry", "Lookup", "Country")
		};
		IFilterSchemaProvider schema = Schema(("Country", [("Name", "Text", null)]));
		BusinessRule rule = MakeRule("UsrCountry",
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "Name", "comparisonType": "START_WITH", "value": "U" } ] }""");

		IReadOnlyList<BusinessRuleMetadataDto> result =
			Convert(attributeMap, rule, "UsrStaticFilterApp", schema, lookupValueResolver: null);

		result.Should().HaveCount(1);
		BusinessRuleMetadataDto dto = result[0];
		dto.Cases.Should().HaveCount(1);
		BaseBusinessRuleActionMetadataDto action = dto.Cases[0].Actions.Single();
		action.TypeName.Should().Be("Terrasoft.Core.BusinessRules.Models.Actions.BusinessRuleActionSetFilter");
		BusinessRuleSetFilterActionMetadataDto setFilter = (BusinessRuleSetFilterActionMetadataDto)action;
		setFilter.Expression.Path.Should().Be("UsrCountry");
		setFilter.Value.Type.Should().Be("Const");
		setFilter.Value.Value.Should().BeOfType<string>();

		string envelope = (string)setFilter.Value.Value!;
		JsonElement parsed = JsonDocument.Parse(envelope).RootElement;
		parsed.GetProperty("rootSchemaName").GetString().Should().Be("Country");
	}

	[Test]
	[Category("Unit")]
	[Description("Emits triggers limited to data-loaded when the condition group is empty.")]
	public void ToEntityMetadata_Should_Emit_Only_DataLoaded_Trigger_For_Unconditional_Rule() {
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap = new Dictionary<string, BusinessRuleAttributeDescriptor> {
			["UsrCountry"] = new("UsrCountry", "Lookup", "Country")
		};
		IFilterSchemaProvider schema = Schema(("Country", [("Name", "Text", null)]));
		BusinessRule rule = MakeRule("UsrCountry",
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "Name", "comparisonType": "START_WITH", "value": "U" } ] }""");

		BusinessRuleMetadataDto dto = Convert(attributeMap, rule, "UsrStaticFilterApp", schema, null)[0];

		dto.Triggers.Should().HaveCount(1);
		dto.Triggers[0].Type.Should().Be(2);
		dto.Triggers[0].Name.Should().BeEmpty();
	}

	[Test]
	[Category("Unit")]
	[Description("Uses the target Lookup ReferenceSchemaName as rootSchemaName, never the caller value.")]
	public void ToEntityMetadata_Should_Infer_RootSchemaName_From_Target_Lookup() {
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap = new Dictionary<string, BusinessRuleAttributeDescriptor> {
			["UsrAccount"] = new("UsrAccount", "Lookup", "Account")
		};
		IFilterSchemaProvider schema = Schema(("Account", [("Name", "Text", null)]));
		BusinessRule rule = MakeRule("UsrAccount",
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "Name", "comparisonType": "EQUAL", "value": "Alpha" } ] }""");

		BusinessRuleMetadataDto dto = Convert(attributeMap, rule, "UsrStaticFilterApp", schema, null)[0];

		string envelope = (string)((BusinessRuleSetFilterActionMetadataDto)dto.Cases[0].Actions.Single()).Value.Value!;
		JsonDocument.Parse(envelope).RootElement.GetProperty("rootSchemaName").GetString().Should().Be("Account");
	}

	[Test]
	[Category("Unit")]
	[Description("Throws when apply-static-filter is converted without a filter schema provider.")]
	public void ToEntityMetadata_Should_Throw_When_No_Schema_Provider() {
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap = new Dictionary<string, BusinessRuleAttributeDescriptor> {
			["UsrCountry"] = new("UsrCountry", "Lookup", "Country")
		};
		BusinessRule rule = MakeRule("UsrCountry",
			"""{ "logicalOperation": "AND", "filters": [] }""");

		Action act = () => Convert(attributeMap, rule, "UsrStaticFilterApp", filterSchemaProvider: null, lookupValueResolver: null);

		act.Should().Throw<InvalidOperationException>().WithMessage("*requires an IFilterSchemaProvider*");
	}

	private static IReadOnlyList<BusinessRuleMetadataDto> Convert(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRule rule,
		string entitySchemaName,
		IFilterSchemaProvider? filterSchemaProvider,
		ILookupValueResolver? lookupValueResolver) =>
		BusinessRuleMetadataConverter.ToEntityMetadata(
			attributeMap, rule, entitySchemaName, filterSchemaProvider, lookupValueResolver);

	private static BusinessRule MakeRule(string targetAttribute, string filterJson) =>
		new(
			"Static filter rule",
			new BusinessRuleConditionGroup("AND", []),
			[new ApplyStaticFilterBusinessRuleAction(targetAttribute,
				JsonDocument.Parse(filterJson).RootElement.Clone())]);

	private static IFilterSchemaProvider Schema(
		params (string Schema, IReadOnlyList<(string Name, string Type, string? Ref)> Columns)[] schemas) {
		IFilterSchemaProvider provider = Substitute.For<IFilterSchemaProvider>();
		foreach (var schema in schemas) {
			Dictionary<string, FilterSchemaColumn> columns = new(StringComparer.Ordinal);
			foreach (var col in schema.Columns) {
				columns[col.Name] = new FilterSchemaColumn {
					Name = col.Name,
					DataValueTypeName = col.Type,
					ReferenceSchemaName = col.Ref
				};
			}

			provider.GetColumns(schema.Schema).Returns(columns);
		}

		return provider;
	}
}
