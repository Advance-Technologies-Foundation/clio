using System;
using System.Collections.Generic;
using System.Text.Json;
using Clio.Command.BusinessRules.Filters;
using Clio.Command.BusinessRules.Filters.Esq;
using Clio.Command.BusinessRules.Filters.Schema;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.BusinessRules;

[TestFixture]
[Property("Module", "Command")]
public sealed class LocalEsqFilterBuilderTests {

	[Test]
	[Category("Unit")]
	[Description("Emits a CompareFilter envelope for a constant text comparison.")]
	public void Build_Should_Emit_CompareFilter_For_Text_Start_With() {
		StaticFilterGroup group = Deserialize(
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "Name", "comparisonType": "START_WITH", "value": "U" } ] }""");
		IFilterSchemaProvider schema = SchemaWith(("Country", [("Name", "Text", null)]));
		LocalEsqFilterBuilder builder = new(schema, lookupResolver: null);

		string json = builder.Build(group, "Country");
		JsonElement root = JsonDocument.Parse(json).RootElement;

		root.GetProperty("rootSchemaName").GetString().Should().Be("Country");
		root.GetProperty("filterType").GetInt32().Should().Be(6);
		root.GetProperty("logicalOperation").GetInt32().Should().Be(0);
		root.GetProperty("className").GetString().Should().Be("Terrasoft.FilterGroup");

		JsonElement filter0 = root.GetProperty("items").GetProperty("Filter_0");
		filter0.GetProperty("filterType").GetInt32().Should().Be(1);
		filter0.GetProperty("comparisonType").GetInt32().Should().Be(9);
		filter0.GetProperty("leftExpression").GetProperty("columnPath").GetString().Should().Be("Name");
		filter0.GetProperty("rightExpression").GetProperty("parameter").GetProperty("value").GetString().Should().Be("U");
		filter0.GetProperty("className").GetString().Should().Be("Terrasoft.CompareFilter");
	}

	[Test]
	[Category("Unit")]
	[Description("Emits an IsNullFilter envelope for IS_NOT_NULL on a text column.")]
	public void Build_Should_Emit_IsNullFilter_For_IsNotNull() {
		StaticFilterGroup group = Deserialize(
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "MobilePhone", "comparisonType": "IS_NOT_NULL" } ] }""");
		IFilterSchemaProvider schema = SchemaWith(("Contact", [("MobilePhone", "PhoneText", null)]));
		LocalEsqFilterBuilder builder = new(schema, lookupResolver: null);

		string json = builder.Build(group, "Contact");
		JsonElement filter0 = JsonDocument.Parse(json).RootElement.GetProperty("items").GetProperty("Filter_0");

		filter0.GetProperty("filterType").GetInt32().Should().Be(2);
		filter0.GetProperty("comparisonType").GetInt32().Should().Be(0);
		filter0.GetProperty("isNull").GetBoolean().Should().BeFalse();
		filter0.GetProperty("className").GetString().Should().Be("Terrasoft.IsNullFilter");
	}

	[Test]
	[Category("Unit")]
	[Description("Emits an InFilter on Lookup with a GUID single value.")]
	public void Build_Should_Emit_InFilter_For_Lookup_Guid() {
		string guid = Guid.NewGuid().ToString("D");
		StaticFilterGroup group = Deserialize(
			$$"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "Type", "comparisonType": "EQUAL", "value": "{{guid}}" } ] }""");
		IFilterSchemaProvider schema = SchemaWith(("Account", [("Type", "Lookup", "AccountType")]));
		LocalEsqFilterBuilder builder = new(schema, lookupResolver: null);

		string json = builder.Build(group, "Account");
		JsonElement filter0 = JsonDocument.Parse(json).RootElement.GetProperty("items").GetProperty("Filter_0");

		filter0.GetProperty("filterType").GetInt32().Should().Be(4);
		filter0.GetProperty("className").GetString().Should().Be("Terrasoft.InFilter");
		JsonElement rightExpressions = filter0.GetProperty("rightExpressions");
		rightExpressions.GetArrayLength().Should().Be(1);
		rightExpressions[0].GetProperty("parameter").GetProperty("value").GetProperty("value").GetString().Should().Be(guid);
	}

	[Test]
	[Category("Unit")]
	[Description("Forward path through Lookup chain stays as a dotted columnPath in the emitted envelope.")]
	public void Build_Should_Preserve_ForwardPath_ColumnPath() {
		StaticFilterGroup group = Deserialize(
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "Country.Name", "comparisonType": "EQUAL", "value": "Ukraine" } ] }""");
		IFilterSchemaProvider schema = SchemaWith(
			("City", [("Country", "Lookup", "Country")]),
			("Country", [("Name", "Text", null)]));
		LocalEsqFilterBuilder builder = new(schema, lookupResolver: null);

		string json = builder.Build(group, "City");
		JsonElement filter0 = JsonDocument.Parse(json).RootElement.GetProperty("items").GetProperty("Filter_0");
		filter0.GetProperty("leftExpression").GetProperty("columnPath").GetString().Should().Be("Country.Name");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves a Lookup display name to a GUID via ILookupValueResolver and emits the resolved Id.")]
	public void Build_Should_Resolve_Lookup_DisplayName() {
		Guid resolved = Guid.NewGuid();
		ILookupValueResolver resolver = Substitute.For<ILookupValueResolver>();
		resolver.ResolveIdByDisplayName("AccountType", "Customer").Returns(resolved);

		StaticFilterGroup group = Deserialize(
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "Type", "comparisonType": "EQUAL", "value": "Customer" } ] }""");
		IFilterSchemaProvider schema = SchemaWith(("Account", [("Type", "Lookup", "AccountType")]));
		LocalEsqFilterBuilder builder = new(schema, resolver);

		string json = builder.Build(group, "Account");
		JsonElement param = JsonDocument.Parse(json).RootElement
			.GetProperty("items").GetProperty("Filter_0")
			.GetProperty("rightExpressions")[0].GetProperty("parameter").GetProperty("value");
		param.GetProperty("value").GetString().Should().Be(resolved.ToString("D"));
		param.GetProperty("displayValue").GetString().Should().Be("Customer");
	}

	[Test]
	[Category("Unit")]
	[Description("Emits multi-value IN with multiple right expressions for an array value on a Lookup column.")]
	public void Build_Should_Emit_MultiValue_In() {
		string g1 = Guid.NewGuid().ToString("D");
		string g2 = Guid.NewGuid().ToString("D");
		StaticFilterGroup group = Deserialize(
			$$"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "Industry", "comparisonType": "EQUAL", "value": ["{{g1}}", "{{g2}}"] } ] }""");
		IFilterSchemaProvider schema = SchemaWith(("Account", [("Industry", "Lookup", "Industry")]));
		LocalEsqFilterBuilder builder = new(schema, lookupResolver: null);

		string json = builder.Build(group, "Account");
		JsonElement filter0 = JsonDocument.Parse(json).RootElement.GetProperty("items").GetProperty("Filter_0");

		filter0.GetProperty("filterType").GetInt32().Should().Be(4);
		filter0.GetProperty("rightExpressions").GetArrayLength().Should().Be(2);
	}

	[Test]
	[Category("Unit")]
	[Description("Emits OR-of-ANDs as a nested Group entry.")]
	public void Build_Should_Emit_Nested_Groups() {
		StaticFilterGroup group = Deserialize("""
			{ "logicalOperation": "OR", "groups": [
			  { "logicalOperation": "AND", "filters": [ { "columnPath": "Name", "comparisonType": "EQUAL", "value": "A" } ] },
			  { "logicalOperation": "AND", "filters": [ { "columnPath": "Name", "comparisonType": "EQUAL", "value": "B" } ] }
			] }
			""");
		IFilterSchemaProvider schema = SchemaWith(("Country", [("Name", "Text", null)]));
		LocalEsqFilterBuilder builder = new(schema, lookupResolver: null);

		string json = builder.Build(group, "Country");
		JsonElement root = JsonDocument.Parse(json).RootElement;
		root.GetProperty("logicalOperation").GetInt32().Should().Be(1);
		root.GetProperty("items").GetProperty("Group_0").GetProperty("logicalOperation").GetInt32().Should().Be(0);
		root.GetProperty("items").GetProperty("Group_1").GetProperty("logicalOperation").GetInt32().Should().Be(0);
	}

	[Test]
	[Category("Unit")]
	[Description("Emits an ExistsFilter for a backward reference EXISTS clause with nested filter group.")]
	public void Build_Should_Emit_Backward_Exists() {
		StaticFilterGroup group = Deserialize("""
			{
			  "logicalOperation": "AND",
			  "backwardReferenceFilters": [
			    { "referenceColumnPath": "[Activity:Owner]", "comparisonType": "EXISTS", "filter": { "logicalOperation": "AND" } }
			  ]
			}
			""");
		IFilterSchemaProvider schema = SchemaWith(
			("Contact", []),
			("Activity", [("Owner", "Lookup", "Contact")]));
		LocalEsqFilterBuilder builder = new(schema, lookupResolver: null);

		string json = builder.Build(group, "Contact");
		JsonElement backward = JsonDocument.Parse(json).RootElement
			.GetProperty("items").GetProperty("BackwardReferenceFilter_0");

		backward.GetProperty("filterType").GetInt32().Should().Be(5);
		backward.GetProperty("comparisonType").GetInt32().Should().Be(15);
		backward.GetProperty("isAggregative").GetBoolean().Should().BeTrue();
		backward.GetProperty("leftExpression").GetProperty("columnPath").GetString().Should().Be("[Activity:Owner]");
		backward.GetProperty("subFilters").GetProperty("className").GetString().Should().Be("Terrasoft.FilterGroup");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a non-GUID Lookup value when no lookup-value resolver is configured.")]
	public void Build_Should_Reject_NonGuid_Lookup_When_No_Resolver() {
		StaticFilterGroup group = Deserialize(
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "Type", "comparisonType": "EQUAL", "value": "Customer" } ] }""");
		IFilterSchemaProvider schema = SchemaWith(("Account", [("Type", "Lookup", "AccountType")]));
		LocalEsqFilterBuilder builder = new(schema, lookupResolver: null);

		Action act = () => builder.Build(group, "Account");

		act.Should().Throw<ArgumentException>().WithMessage("*no lookup-value resolver is configured*");
	}

	[Test]
	[Category("Unit")]
	[Description("Emits OR root logicalOperation when filter uses OR.")]
	public void Build_Should_Emit_Or_Root() {
		StaticFilterGroup group = Deserialize("""
			{ "logicalOperation": "OR", "filters": [
			  { "columnPath": "Name", "comparisonType": "EQUAL", "value": "A" },
			  { "columnPath": "Name", "comparisonType": "EQUAL", "value": "B" }
			] }
			""");
		IFilterSchemaProvider schema = SchemaWith(("Country", [("Name", "Text", null)]));
		LocalEsqFilterBuilder builder = new(schema, lookupResolver: null);

		string json = builder.Build(group, "Country");
		JsonDocument.Parse(json).RootElement.GetProperty("logicalOperation").GetInt32().Should().Be(1);
	}

	private static StaticFilterGroup Deserialize(string json) {
		JsonElement element = JsonDocument.Parse(json).RootElement.Clone();
		StaticFilterGroup group = StaticFilterDeserializer.Deserialize(element);
		StaticFilterStructuralValidator.Validate(group);
		return group;
	}

	private static IFilterSchemaProvider SchemaWith(
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
