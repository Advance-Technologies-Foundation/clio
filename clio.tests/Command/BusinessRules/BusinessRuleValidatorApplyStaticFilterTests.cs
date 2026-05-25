using System;
using System.Collections.Generic;
using System.Text.Json;
using Clio.Command.BusinessRules;
using Clio.Command.BusinessRules.Filters.Schema;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.BusinessRules;

[TestFixture]
[Property("Module", "Command")]
public sealed class BusinessRuleValidatorApplyStaticFilterTests {

	[Test]
	[Category("Unit")]
	[Description("Accepts a minimal apply-static-filter payload with an empty condition group.")]
	public void Validate_Should_Accept_Minimal_ApplyStaticFilter() {
		// Arrange
		BusinessRule rule = CreateApplyStaticFilterRule(
			"UsrCountry",
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "Name", "comparisonType": "START_WITH", "value": "U" } ] }""");
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap = Attributes(
			("UsrCountry", "Lookup", "Country"));
		IFilterSchemaProvider schemaProvider = SchemaProvider(("Country", [
			("Name", "Text", null)
		]));

		// Act
		Action act = () => BusinessRuleValidator.ValidateEntity(rule, attributeMap, schemaProvider);

		// Assert
		act.Should().NotThrow();
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects when targetAttribute does not exist on the entity schema.")]
	public void Validate_Should_Reject_Unknown_TargetAttribute() {
		BusinessRule rule = CreateApplyStaticFilterRule("UsrCity1",
			"""{ "logicalOperation": "AND", "filters": [] }""");
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap = Attributes(
			("UsrCountry", "Lookup", "Country"));

		Action act = () => BusinessRuleValidator.ValidateEntity(rule, attributeMap, Substitute.For<IFilterSchemaProvider>());

		act.Should().Throw<ArgumentException>()
			.WithMessage("filter.target-attribute-unknown: targetAttribute 'UsrCity1' was not found on the entity schema.");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects when the target attribute is not a Lookup.")]
	public void Validate_Should_Reject_NonLookup_Target() {
		BusinessRule rule = CreateApplyStaticFilterRule("Title",
			"""{ "logicalOperation": "AND", "filters": [] }""");
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap = Attributes(
			("Title", "Text", null));

		Action act = () => BusinessRuleValidator.ValidateEntity(rule, attributeMap, Substitute.For<IFilterSchemaProvider>());

		act.Should().Throw<ArgumentException>()
			.WithMessage("Attribute 'Title' in rule.actions[*].targetAttribute must be a Lookup.");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects when columnPath is unknown on the resolved schema, with available names listed.")]
	public void Validate_Should_Reject_Unknown_ColumnPath() {
		BusinessRule rule = CreateApplyStaticFilterRule("UsrCity",
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "Name1", "comparisonType": "EQUAL", "value": "X" } ] }""");
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap = Attributes(
			("UsrCity", "Lookup", "City"));
		IFilterSchemaProvider schemaProvider = SchemaProvider(("City", [
			("Name", "Text", null),
			("Country", "Lookup", "Country")
		]));

		Action act = () => BusinessRuleValidator.ValidateEntity(rule, attributeMap, schemaProvider);

		act.Should().Throw<ArgumentException>()
			.WithMessage("filter.path-unknown: Column 'Name1' not found on schema 'City' (looked up by Name).*");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects unsupported comparison tokens at structural stage.")]
	public void Validate_Should_Reject_Unknown_Comparison() {
		BusinessRule rule = CreateApplyStaticFilterRule("UsrCountry",
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "Name", "comparisonType": "FOO", "value": "U" } ] }""");
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap = Attributes(
			("UsrCountry", "Lookup", "Country"));

		Action act = () => BusinessRuleValidator.ValidateEntity(rule, attributeMap,
			SchemaProvider(("Country", [("Name", "Text", null)])));

		act.Should().Throw<ArgumentException>().WithMessage("*unsupported value 'FOO'*");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects unary IS_NULL when a value is supplied.")]
	public void Validate_Should_Reject_Unary_With_Value() {
		BusinessRule rule = CreateApplyStaticFilterRule("UsrCountry",
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "Name", "comparisonType": "IS_NULL", "value": "X" } ] }""");
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap = Attributes(
			("UsrCountry", "Lookup", "Country"));

		Action act = () => BusinessRuleValidator.ValidateEntity(rule, attributeMap,
			SchemaProvider(("Country", [("Name", "Text", null)])));

		act.Should().Throw<ArgumentException>().WithMessage("*must be omitted when comparisonType is 'IS_NULL'*");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects relational comparison applied to a text column.")]
	public void Validate_Should_Reject_Relational_On_Text() {
		BusinessRule rule = CreateApplyStaticFilterRule("UsrCountry",
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "Name", "comparisonType": "GREATER", "value": "U" } ] }""");
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap = Attributes(
			("UsrCountry", "Lookup", "Country"));

		Action act = () => BusinessRuleValidator.ValidateEntity(rule, attributeMap,
			SchemaProvider(("Country", [("Name", "Text", null)])));

		act.Should().Throw<ArgumentException>().WithMessage("*'GREATER' is supported only on numeric and date/time columns*");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects array value (multi-value IN) on a non-Lookup column.")]
	public void Validate_Should_Reject_Array_Value_On_NonLookup() {
		BusinessRule rule = CreateApplyStaticFilterRule("UsrCountry",
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "Name", "comparisonType": "EQUAL", "value": ["A","B"] } ] }""");
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap = Attributes(
			("UsrCountry", "Lookup", "Country"));

		Action act = () => BusinessRuleValidator.ValidateEntity(rule, attributeMap,
			SchemaProvider(("Country", [("Name", "Text", null)])));

		act.Should().Throw<ArgumentException>().WithMessage("*array (multi-value IN) is supported only on Lookup columns*");
	}

	[Test]
	[Category("Unit")]
	[Description("Accepts a forward-path columnPath through Lookup chain.")]
	public void Validate_Should_Accept_ForwardPath() {
		BusinessRule rule = CreateApplyStaticFilterRule("UsrCity",
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "Country.Name", "comparisonType": "EQUAL", "value": "Ukraine" } ] }""");
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap = Attributes(
			("UsrCity", "Lookup", "City"));
		IFilterSchemaProvider schemaProvider = SchemaProvider(
			("City", [("Country", "Lookup", "Country")]),
			("Country", [("Name", "Text", null)]));

		Action act = () => BusinessRuleValidator.ValidateEntity(rule, attributeMap, schemaProvider);

		act.Should().NotThrow();
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects when apply-static-filter is mixed with another action.")]
	public void Validate_Should_Reject_Mixed_With_Other_Action() {
		BusinessRule rule = new(
			"Mixed",
			new BusinessRuleConditionGroup("AND", []),
			[
				new ApplyStaticFilterBusinessRuleAction("UsrCountry",
					ParseFilter("""{ "logicalOperation": "AND", "filters": [] }""")),
				new MakeReadOnlyBusinessRuleAction(["Title"])
			]);
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap = Attributes(
			("UsrCountry", "Lookup", "Country"),
			("Title", "Text", null));

		Action act = () => BusinessRuleValidator.ValidateEntity(rule, attributeMap,
			Substitute.For<IFilterSchemaProvider>());

		act.Should().Throw<ArgumentException>().WithMessage("apply-static-filter rules support exactly one action*");
	}

	[Test]
	[Category("Unit")]
	[Description("Accepts backward reference filter with EXISTS comparison.")]
	public void Validate_Should_Accept_Backward_Exists() {
		BusinessRule rule = CreateApplyStaticFilterRule("UsrContact",
			"""
			{
			  "logicalOperation": "AND",
			  "backwardReferenceFilters": [
			    { "referenceColumnPath": "[Activity:Owner]", "comparisonType": "EXISTS", "filter": { "logicalOperation": "AND" } }
			  ]
			}
			""");
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap = Attributes(
			("UsrContact", "Lookup", "Contact"));
		IFilterSchemaProvider schemaProvider = SchemaProvider(
			("Contact", []),
			("Activity", [("Owner", "Lookup", "Contact")]));

		Action act = () => BusinessRuleValidator.ValidateEntity(rule, attributeMap, schemaProvider);

		act.Should().NotThrow();
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects backward reference whose link column does not point back to the root schema.")]
	public void Validate_Should_Reject_Bad_Backward_Reference() {
		BusinessRule rule = CreateApplyStaticFilterRule("UsrContact",
			"""
			{
			  "logicalOperation": "AND",
			  "backwardReferenceFilters": [
			    { "referenceColumnPath": "[Activity:Account]", "comparisonType": "EXISTS" }
			  ]
			}
			""");
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap = Attributes(
			("UsrContact", "Lookup", "Contact"));
		IFilterSchemaProvider schemaProvider = SchemaProvider(
			("Contact", []),
			("Activity", [("Account", "Lookup", "Account")]));

		Action act = () => BusinessRuleValidator.ValidateEntity(rule, attributeMap, schemaProvider);

		act.Should().Throw<ArgumentException>().WithMessage("*must be a Lookup pointing back to root schema 'Contact'*");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects empty filter payload (missing logicalOperation).")]
	public void Validate_Should_Reject_Empty_Filter_Payload() {
		BusinessRule rule = CreateApplyStaticFilterRule("UsrCountry", "{}");
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap = Attributes(
			("UsrCountry", "Lookup", "Country"));

		Action act = () => BusinessRuleValidator.ValidateEntity(rule, attributeMap,
			Substitute.For<IFilterSchemaProvider>());

		act.Should().Throw<ArgumentException>().WithMessage("filter.logicalOperation:*");
	}

	// ---- helpers ----

	private static BusinessRule CreateApplyStaticFilterRule(string targetAttribute, string filterJson) =>
		new(
			"Static filter rule",
			new BusinessRuleConditionGroup("AND", []),
			[new ApplyStaticFilterBusinessRuleAction(targetAttribute, ParseFilter(filterJson))]);

	private static JsonElement ParseFilter(string json) =>
		JsonDocument.Parse(json).RootElement.Clone();

	private static IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> Attributes(
		params (string Path, string DataValueTypeName, string? ReferenceSchemaName)[] entries) {
		Dictionary<string, BusinessRuleAttributeDescriptor> map = new(StringComparer.Ordinal);
		foreach (var entry in entries) {
			map[entry.Path] = new BusinessRuleAttributeDescriptor(entry.Path, entry.DataValueTypeName, entry.ReferenceSchemaName);
		}

		return map;
	}

	private static IFilterSchemaProvider SchemaProvider(
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
