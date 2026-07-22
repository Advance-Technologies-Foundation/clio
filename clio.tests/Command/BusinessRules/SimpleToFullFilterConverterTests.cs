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
public sealed class SimpleToFullFilterConverterTests {

	[Test]
	[Category("Unit")]
	[Description("Emits a CompareFilter envelope for a constant text comparison.")]
	public void Build_Should_Emit_CompareFilter_For_Text_Start_With() {
		StaticFilterGroup group = Deserialize(
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "Name", "comparisonType": "START_WITH", "value": "U" } ] }""");
		IFilterSchemaProvider schema = SchemaWith(("Country", [("Name", "Text", null)]));
		SimpleToFullFilterConverter builder = new(schema, lookupResolver: null);

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
		SimpleToFullFilterConverter builder = new(schema, lookupResolver: null);

		string json = builder.Build(group, "Contact");
		JsonElement filter0 = JsonDocument.Parse(json).RootElement.GetProperty("items").GetProperty("Filter_0");

		filter0.GetProperty("filterType").GetInt32().Should().Be(2);
		filter0.GetProperty("comparisonType").GetInt32().Should().Be(2,
			because: "Terrasoft FilterComparisonType.IsNotNull is 2; emitting 0 (None) makes the platform reject the IsNullFilter");
		filter0.GetProperty("isNull").GetBoolean().Should().BeFalse();
		filter0.GetProperty("className").GetString().Should().Be("Terrasoft.IsNullFilter");
	}

	[Test]
	[Category("Unit")]
	[Description("Emits an IsNullFilter with comparisonType 1 for IS_NULL.")]
	public void Build_Should_Emit_IsNullFilter_For_IsNull() {
		StaticFilterGroup group = Deserialize(
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "MobilePhone", "comparisonType": "IS_NULL" } ] }""");
		IFilterSchemaProvider schema = SchemaWith(("Contact", [("MobilePhone", "PhoneText", null)]));
		SimpleToFullFilterConverter builder = new(schema, lookupResolver: null);

		string json = builder.Build(group, "Contact");
		JsonElement filter0 = JsonDocument.Parse(json).RootElement.GetProperty("items").GetProperty("Filter_0");

		filter0.GetProperty("filterType").GetInt32().Should().Be(2);
		filter0.GetProperty("comparisonType").GetInt32().Should().Be(1, because: "FilterComparisonType.IsNull is 1");
		filter0.GetProperty("isNull").GetBoolean().Should().BeTrue();
	}

	[Test]
	[Category("Unit")]
	[Description("Emits an InFilter on Lookup with a GUID single value; the GUID is reverse-resolved to Name/displayValue.")]
	public void Build_Should_Emit_InFilter_For_Lookup_Guid() {
		Guid id = Guid.NewGuid();
		string guid = id.ToString("D");
		ILookupValueResolver resolver = Substitute.For<ILookupValueResolver>();
		resolver.TryResolveDisplayNameById("AccountType", id, out Arg.Any<string?>())
			.Returns(call => { call[2] = "Customer"; return true; });
		StaticFilterGroup group = Deserialize(
			$$"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "Type", "comparisonType": "EQUAL", "value": "{{guid}}" } ] }""");
		IFilterSchemaProvider schema = SchemaWith(("Account", [("Type", "Lookup", "AccountType")]));
		SimpleToFullFilterConverter builder = new(schema, resolver);

		string json = builder.Build(group, "Account");
		JsonElement filter0 = JsonDocument.Parse(json).RootElement.GetProperty("items").GetProperty("Filter_0");

		filter0.GetProperty("filterType").GetInt32().Should().Be(4);
		filter0.GetProperty("className").GetString().Should().Be("Terrasoft.InFilter");
		filter0.GetProperty("trimDateTimeParameterToDate").GetBoolean().Should().BeFalse(
			because: "the platform-canonical InFilter carries trimDateTimeParameterToDate=false");
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
		SimpleToFullFilterConverter builder = new(schema, lookupResolver: null);

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
		SimpleToFullFilterConverter builder = new(schema, resolver);

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
		ILookupValueResolver resolver = Substitute.For<ILookupValueResolver>();
		resolver.TryResolveDisplayNameById("Industry", Arg.Any<Guid>(), out Arg.Any<string?>())
			.Returns(call => { call[2] = "X"; return true; });
		StaticFilterGroup group = Deserialize(
			$$"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "Industry", "comparisonType": "EQUAL", "value": ["{{g1}}", "{{g2}}"] } ] }""");
		IFilterSchemaProvider schema = SchemaWith(("Account", [("Industry", "Lookup", "Industry")]));
		SimpleToFullFilterConverter builder = new(schema, resolver);

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
		SimpleToFullFilterConverter builder = new(schema, lookupResolver: null);

		string json = builder.Build(group, "Country");
		JsonElement root = JsonDocument.Parse(json).RootElement;
		root.GetProperty("logicalOperation").GetInt32().Should().Be(1);
		root.GetProperty("items").GetProperty("Group_0").GetProperty("logicalOperation").GetInt32().Should().Be(0);
		root.GetProperty("items").GetProperty("Group_1").GetProperty("logicalOperation").GetInt32().Should().Be(0);
	}

	[Test]
	[Category("Unit")]
	[Description("Emits an ExistsFilter for a backward reference EXISTS clause with nested filter group; columnPath is suffixed with .Id and dataValueType=Integer per platform canonical shape.")]
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
		SimpleToFullFilterConverter builder = new(schema, lookupResolver: null);

		string json = builder.Build(group, "Contact");
		JsonElement backward = JsonDocument.Parse(json).RootElement
			.GetProperty("items").GetProperty("BackwardReferenceFilter_0");

		backward.GetProperty("filterType").GetInt32().Should().Be(5);
		backward.GetProperty("comparisonType").GetInt32().Should().Be(15);
		backward.GetProperty("isAggregative").GetBoolean().Should().BeTrue();
		backward.GetProperty("dataValueType").GetInt32().Should().Be(4,
			because: "platform EXISTS carries dataValueType=Integer (aggregation-count type)");
		backward.GetProperty("trimDateTimeParameterToDate").GetBoolean().Should().BeFalse();
		backward.GetProperty("leftExpression").GetProperty("columnPath").GetString().Should().Be("[Activity:Owner].Id",
			because: "platform EXISTS leftExpression points at the link column Id; the friendly contract supplies `[Schema:Column]` and the builder appends `.Id`");
		backward.GetProperty("subFilters").GetProperty("className").GetString().Should().Be("Terrasoft.FilterGroup");
	}

	[Test]
	[Category("Unit")]
	[Description("Matches the canonical platform wire shape for 'Accounts that have at least one Contact' (backward EXISTS from Account via [Contact:Account]).")]
	public void Build_Should_Match_Canonical_Backward_Exists_Contact_To_Account() {
		StaticFilterGroup group = Deserialize("""
			{
			  "logicalOperation": "AND",
			  "backwardReferenceFilters": [
			    { "referenceColumnPath": "[Contact:Account]", "comparisonType": "EXISTS" }
			  ]
			}
			""");
		IFilterSchemaProvider schema = SchemaWith(
			("Account", []),
			("Contact", [("Account", "Lookup", "Account")]));
		SimpleToFullFilterConverter builder = new(schema, lookupResolver: null);

		string json = builder.Build(group, "Account");
		JsonElement root = JsonDocument.Parse(json).RootElement;

		root.GetProperty("rootSchemaName").GetString().Should().Be("Account");
		root.GetProperty("filterType").GetInt32().Should().Be(6);
		root.GetProperty("logicalOperation").GetInt32().Should().Be(0);

		JsonElement backward = root.GetProperty("items").GetProperty("BackwardReferenceFilter_0");
		backward.GetProperty("filterType").GetInt32().Should().Be(5);
		backward.GetProperty("comparisonType").GetInt32().Should().Be(15);
		backward.GetProperty("isEnabled").GetBoolean().Should().BeTrue();
		backward.GetProperty("isAggregative").GetBoolean().Should().BeTrue();
		backward.GetProperty("dataValueType").GetInt32().Should().Be(4);
		backward.GetProperty("trimDateTimeParameterToDate").GetBoolean().Should().BeFalse();

		JsonElement left = backward.GetProperty("leftExpression");
		left.GetProperty("expressionType").GetInt32().Should().Be(0);
		left.GetProperty("columnPath").GetString().Should().Be("[Contact:Account].Id");

		JsonElement subFilters = backward.GetProperty("subFilters");
		subFilters.GetProperty("rootSchemaName").GetString().Should().Be("Contact");
		subFilters.GetProperty("filterType").GetInt32().Should().Be(6);
		subFilters.GetProperty("logicalOperation").GetInt32().Should().Be(0);
		subFilters.GetProperty("isEnabled").GetBoolean().Should().BeTrue();
		subFilters.GetProperty("items").EnumerateObject().Should().BeEmpty();
	}

	[Test]
	[Category("Unit")]
	[Description("Emits an aggregation CompareFilter (COUNT GREATER 10) for a backward reference with aggregationType=COUNT.")]
	public void Build_Should_Emit_Aggregation_Count_Backward_Reference() {
		StaticFilterGroup group = Deserialize("""
			{
			  "logicalOperation": "AND",
			  "backwardReferenceFilters": [
			    { "referenceColumnPath": "[Activity:Contact]", "aggregationType": "COUNT", "comparisonType": "GREATER", "aggregationValue": 10 }
			  ]
			}
			""");
		IFilterSchemaProvider schema = SchemaWith(
			("Contact", []),
			("Activity", [("Contact", "Lookup", "Contact")]));
		SimpleToFullFilterConverter builder = new(schema, lookupResolver: null);

		string json = builder.Build(group, "Contact");
		JsonElement backward = JsonDocument.Parse(json).RootElement
			.GetProperty("items").GetProperty("BackwardReferenceFilter_0");

		backward.GetProperty("filterType").GetInt32().Should().Be(1, because: "aggregation emits a CompareFilter, not ExistsFilter");
		backward.GetProperty("comparisonType").GetInt32().Should().Be(7, because: "GREATER maps to 7");
		backward.GetProperty("isAggregative").GetBoolean().Should().BeTrue();
		backward.GetProperty("className").GetString().Should().Be("Terrasoft.CompareFilter");

		JsonElement left = backward.GetProperty("leftExpression");
		left.GetProperty("expressionType").GetInt32().Should().Be(3, because: "SubQuery is 3");
		left.GetProperty("functionType").GetInt32().Should().Be(2, because: "Aggregation is 2");
		left.GetProperty("aggregationType").GetInt32().Should().Be(1, because: "Count is 1");
		left.GetProperty("columnPath").GetString().Should().Be("[Activity:Contact].Id");
		left.GetProperty("className").GetString().Should().Be("Terrasoft.AggregationQueryExpression");
		left.GetProperty("subFilters").GetProperty("rootSchemaName").GetString().Should().Be("Activity");

		JsonElement param = backward.GetProperty("rightExpression").GetProperty("parameter");
		param.GetProperty("dataValueType").GetInt32().Should().Be(4, because: "COUNT threshold is Integer (4)");
		param.GetProperty("value").GetInt64().Should().Be(10);

		backward.GetProperty("subFilters").GetProperty("rootSchemaName").GetString().Should().Be("Activity");
	}

	[Test]
	[Category("Unit")]
	[Description("Emits a SUM aggregation over a numeric child column with a Float threshold and aggregationColumnPath appended to the backward path.")]
	public void Build_Should_Emit_Aggregation_Sum_Backward_Reference() {
		StaticFilterGroup group = Deserialize("""
			{
			  "logicalOperation": "AND",
			  "backwardReferenceFilters": [
			    { "referenceColumnPath": "[Order:Customer]", "aggregationType": "SUM", "aggregationColumnPath": "Amount", "comparisonType": "GREATER_OR_EQUAL", "aggregationValue": 1000 }
			  ]
			}
			""");
		IFilterSchemaProvider schema = SchemaWith(
			("Contact", []),
			("Order", [("Customer", "Lookup", "Contact"), ("Amount", "Float", null)]));
		SimpleToFullFilterConverter builder = new(schema, lookupResolver: null);

		string json = builder.Build(group, "Contact");
		JsonElement backward = JsonDocument.Parse(json).RootElement
			.GetProperty("items").GetProperty("BackwardReferenceFilter_0");

		backward.GetProperty("comparisonType").GetInt32().Should().Be(8, because: "GREATER_OR_EQUAL maps to 8");
		JsonElement left = backward.GetProperty("leftExpression");
		left.GetProperty("aggregationType").GetInt32().Should().Be(2, because: "Sum is 2");
		left.GetProperty("columnPath").GetString().Should().Be("[Order:Customer].Amount");
		backward.GetProperty("rightExpression").GetProperty("parameter").GetProperty("dataValueType").GetInt32()
			.Should().Be(5, because: "scalar aggregation threshold is Float (5)");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects COUNT aggregation without aggregationValue during structural validation.")]
	public void Build_Should_Reject_Aggregation_Without_Value() {
		Action act = () => Deserialize("""
			{
			  "logicalOperation": "AND",
			  "backwardReferenceFilters": [
			    { "referenceColumnPath": "[Activity:Contact]", "aggregationType": "COUNT", "comparisonType": "GREATER" }
			  ]
			}
			""");

		act.Should().Throw<ArgumentException>().WithMessage("*aggregationValue: required*");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects COUNT aggregation with an aggregationColumnPath (COUNT must not carry a scalar column).")]
	public void Build_Should_Reject_Count_With_ColumnPath() {
		Action act = () => Deserialize("""
			{
			  "logicalOperation": "AND",
			  "backwardReferenceFilters": [
			    { "referenceColumnPath": "[Activity:Contact]", "aggregationType": "COUNT", "aggregationColumnPath": "Amount", "comparisonType": "GREATER", "aggregationValue": 1 }
			  ]
			}
			""");

		act.Should().Throw<ArgumentException>().WithMessage("*aggregationColumnPath: must be omitted for COUNT*");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects EXISTS comparison with an aggregation token, and aggregation with EXISTS comparison.")]
	public void Build_Should_Reject_Aggregation_With_Exists_Comparison() {
		Action act = () => Deserialize("""
			{
			  "logicalOperation": "AND",
			  "backwardReferenceFilters": [
			    { "referenceColumnPath": "[Activity:Contact]", "aggregationType": "COUNT", "comparisonType": "EXISTS", "aggregationValue": 1 }
			  ]
			}
			""");

		act.Should().Throw<ArgumentException>().WithMessage("*relational/equality token*");
	}

	[Test]
	[Category("Unit")]
	[Description("Schema-aware validation rejects SUM over a non-numeric child column.")]
	public void SchemaValidation_Should_Reject_Sum_Over_NonNumeric_Column() {
		StaticFilterGroup group = Deserialize("""
			{
			  "logicalOperation": "AND",
			  "backwardReferenceFilters": [
			    { "referenceColumnPath": "[Order:Customer]", "aggregationType": "SUM", "aggregationColumnPath": "Note", "comparisonType": "GREATER", "aggregationValue": 1 }
			  ]
			}
			""");
		IFilterSchemaProvider schema = SchemaWith(
			("Contact", []),
			("Order", [("Customer", "Lookup", "Contact"), ("Note", "Text", null)]));
		SchemaAwareFilterValidator validator = new(schema);

		Action act = () => validator.Validate(group, "Contact");

		act.Should().Throw<ArgumentException>().WithMessage("*requires a numeric column*");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a non-GUID Lookup value when no lookup-value resolver is configured.")]
	public void Build_Should_Reject_NonGuid_Lookup_When_No_Resolver() {
		StaticFilterGroup group = Deserialize(
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "Type", "comparisonType": "EQUAL", "value": "Customer" } ] }""");
		IFilterSchemaProvider schema = SchemaWith(("Account", [("Type", "Lookup", "AccountType")]));
		SimpleToFullFilterConverter builder = new(schema, lookupResolver: null);

		Action act = () => builder.Build(group, "Account");

		act.Should().Throw<ArgumentException>().WithMessage("*no lookup-value resolver is configured*");
	}

	[Test]
	[Category("Unit")]
	[Description("Preserves the full DateTime precision in the parameter and omits trimDateTimeParameterToDate.")]
	public void Build_Should_Preserve_DateTime_Precision() {
		StaticFilterGroup group = Deserialize(
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "CreatedOn", "comparisonType": "GREATER", "value": "2026-05-01T12:00:00Z" } ] }""");
		IFilterSchemaProvider schema = SchemaWith(("Account", [("CreatedOn", "DateTime", null)]));
		SimpleToFullFilterConverter builder = new(schema, lookupResolver: null);

		string json = builder.Build(group, "Account");
		JsonElement filter0 = JsonDocument.Parse(json).RootElement.GetProperty("items").GetProperty("Filter_0");

		filter0.TryGetProperty("trimDateTimeParameterToDate", out _).Should().BeFalse(
			because: "forcing date-only truncation would change CreatedOn GREATER 2026-05-01T12:00:00Z into a date-only comparison");
		filter0.GetProperty("rightExpression").GetProperty("parameter").GetProperty("value").GetString()
			.Should().Be("2026-05-01T12:00:00Z");
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
		SimpleToFullFilterConverter builder = new(schema, lookupResolver: null);

		string json = builder.Build(group, "Country");
		JsonDocument.Parse(json).RootElement.GetProperty("logicalOperation").GetInt32().Should().Be(1);
	}

	[Test]
	[Category("Unit")]
	[Description("Emits a macros FunctionExpression rightExpression for a date macros on a DateTime column.")]
	public void Build_Should_Emit_Macros_For_Date() {
		StaticFilterGroup group = Deserialize(
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "CreatedOn", "comparisonType": "GREATER", "valueMacros": "Today" } ] }""");
		IFilterSchemaProvider schema = SchemaWith(("Account", [("CreatedOn", "DateTime", null)]));
		SimpleToFullFilterConverter builder = new(schema, lookupResolver: null);

		string json = builder.Build(group, "Account");
		JsonElement filter0 = JsonDocument.Parse(json).RootElement.GetProperty("items").GetProperty("Filter_0");
		JsonElement right = filter0.GetProperty("rightExpression");

		filter0.GetProperty("filterType").GetInt32().Should().Be(1);
		filter0.GetProperty("comparisonType").GetInt32().Should().Be(7);
		filter0.GetProperty("trimDateTimeParameterToDate").GetBoolean().Should().BeTrue(
			because: "date macros on a DateTime column must trim time so `CreatedOn EQUAL Today` matches the whole day");
		filter0.GetProperty("isAggregative").GetBoolean().Should().BeFalse();
		filter0.GetProperty("dataValueType").GetInt32().Should().Be(7, because: "DateTime numeric code is 7");
		right.GetProperty("expressionType").GetInt32().Should().Be(1, because: "ExpressionType.Function is 1");
		right.GetProperty("functionType").GetInt32().Should().Be(1, because: "FunctionType.Macros is 1");
		right.GetProperty("macrosType").GetInt32().Should().Be(4, because: "QueryMacrosType.Today is 4");
		right.GetProperty("className").GetString().Should().Be("Terrasoft.FunctionExpression");
		right.TryGetProperty("functionArgument", out _).Should().BeFalse();
	}

	[Test]
	[Category("Unit")]
	[Description("Matches the canonical platform shape for 'CreatedOn EQUAL Today' on a DateTime column.")]
	public void Build_Should_Match_Canonical_CreatedOn_Equal_Today() {
		StaticFilterGroup group = Deserialize(
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "CreatedOn", "comparisonType": "EQUAL", "valueMacros": "Today" } ] }""");
		IFilterSchemaProvider schema = SchemaWith(("Contact", [("CreatedOn", "DateTime", null)]));
		SimpleToFullFilterConverter builder = new(schema, lookupResolver: null);

		string json = builder.Build(group, "Contact");
		JsonElement root = JsonDocument.Parse(json).RootElement;
		root.GetProperty("rootSchemaName").GetString().Should().Be("Contact");
		root.GetProperty("filterType").GetInt32().Should().Be(6);
		root.GetProperty("logicalOperation").GetInt32().Should().Be(0);

		JsonElement filter0 = root.GetProperty("items").GetProperty("Filter_0");
		filter0.GetProperty("filterType").GetInt32().Should().Be(1);
		filter0.GetProperty("comparisonType").GetInt32().Should().Be(3, because: "EQUAL maps to FilterComparisonType.Equal=3");
		filter0.GetProperty("trimDateTimeParameterToDate").GetBoolean().Should().BeTrue();
		filter0.GetProperty("isAggregative").GetBoolean().Should().BeFalse();
		filter0.GetProperty("dataValueType").GetInt32().Should().Be(7);
		filter0.GetProperty("leftExpression").GetProperty("columnPath").GetString().Should().Be("CreatedOn");
		JsonElement right = filter0.GetProperty("rightExpression");
		right.GetProperty("expressionType").GetInt32().Should().Be(1);
		right.GetProperty("functionType").GetInt32().Should().Be(1);
		right.GetProperty("macrosType").GetInt32().Should().Be(4);
	}

	[Test]
	[Category("Unit")]
	[Description("Emits a functionArgument ParameterExpression for an N-style date macros.")]
	public void Build_Should_Emit_Macros_Argument_For_NextNDays() {
		StaticFilterGroup group = Deserialize(
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "DueDate", "comparisonType": "LESS_OR_EQUAL", "valueMacros": "NextNDays", "valueMacrosArgument": 5 } ] }""");
		IFilterSchemaProvider schema = SchemaWith(("Activity", [("DueDate", "Date", null)]));
		SimpleToFullFilterConverter builder = new(schema, lookupResolver: null);

		string json = builder.Build(group, "Activity");
		JsonElement right = JsonDocument.Parse(json).RootElement
			.GetProperty("items").GetProperty("Filter_0").GetProperty("rightExpression");

		right.GetProperty("macrosType").GetInt32().Should().Be(24, because: "QueryMacrosType.NextNDays is 24");
		JsonElement argument = right.GetProperty("functionArgument");
		argument.GetProperty("expressionType").GetInt32().Should().Be(2, because: "ExpressionType.Parameter is 2");
		argument.GetProperty("parameter").GetProperty("dataValueType").GetInt32().Should().Be(4, because: "Integer is 4");
		argument.GetProperty("parameter").GetProperty("value").GetInt32().Should().Be(5);
	}

	[Test]
	[Category("Unit")]
	[Description("Emits a CompareFilter (not InFilter) with a macros rightExpression for CurrentUserContact on a Lookup column.")]
	public void Build_Should_Emit_Macros_CompareFilter_For_Lookup() {
		StaticFilterGroup group = Deserialize(
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "Owner", "comparisonType": "EQUAL", "valueMacros": "CurrentUserContact" } ] }""");
		IFilterSchemaProvider schema = SchemaWith(("Activity", [("Owner", "Lookup", "Contact")]));
		SimpleToFullFilterConverter builder = new(schema, lookupResolver: null);

		string json = builder.Build(group, "Activity");
		JsonElement filter0 = JsonDocument.Parse(json).RootElement.GetProperty("items").GetProperty("Filter_0");

		filter0.GetProperty("filterType").GetInt32().Should().Be(1, because: "macros use CompareFilter, not InFilter");
		filter0.GetProperty("className").GetString().Should().Be("Terrasoft.CompareFilter");
		filter0.GetProperty("rightExpression").GetProperty("macrosType").GetInt32().Should().Be(2,
			because: "QueryMacrosType.CurrentUserContact is 2");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects an unknown macros name during structural validation.")]
	public void Build_Should_Reject_Unknown_Macros() {
		Action act = () => Deserialize(
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "CreatedOn", "comparisonType": "GREATER", "valueMacros": "Nonsense" } ] }""");

		act.Should().Throw<ArgumentException>().WithMessage("*unknown macros 'Nonsense'*");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects an N-style macros without the required argument.")]
	public void Build_Should_Reject_NMacros_Without_Argument() {
		Action act = () => Deserialize(
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "DueDate", "comparisonType": "LESS", "valueMacros": "NextNDays" } ] }""");

		act.Should().Throw<ArgumentException>().WithMessage("*valueMacrosArgument: required*");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects providing both value and valueMacros on the same leaf.")]
	public void Build_Should_Reject_Value_And_Macros_Together() {
		Action act = () => Deserialize(
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "CreatedOn", "comparisonType": "GREATER", "value": "2026-01-01T00:00:00Z", "valueMacros": "Today" } ] }""");

		act.Should().Throw<ArgumentException>().WithMessage("*either value or valueMacros, not both*");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a date macros on a non-date column via schema-aware validation.")]
	public void SchemaValidation_Should_Reject_Date_Macros_On_Text_Column() {
		StaticFilterGroup group = Deserialize(
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "Name", "comparisonType": "EQUAL", "valueMacros": "Today" } ] }""");
		IFilterSchemaProvider schema = SchemaWith(("Account", [("Name", "Text", null)]));
		SchemaAwareFilterValidator validator = new(schema);

		Action act = () => validator.Validate(group, "Account");

		act.Should().Throw<ArgumentException>().WithMessage("*applies only to Date/DateTime/Time columns*");
	}

	[Test]
	[Category("Unit")]
	[Description("Lookup InFilter carries canonical platform metadata: isAggregative=false, dataValueType=10, referenceSchemaName; lookup value carries Name/Id alongside value/displayValue.")]
	public void Build_Should_Emit_Canonical_Lookup_InFilter() {
		Guid resolved = Guid.NewGuid();
		ILookupValueResolver resolver = Substitute.For<ILookupValueResolver>();
		resolver.ResolveIdByDisplayName("AccountType", "Customer").Returns(resolved);

		StaticFilterGroup group = Deserialize(
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "Type", "comparisonType": "EQUAL", "value": "Customer" } ] }""");
		IFilterSchemaProvider schema = SchemaWith(("Account", [("Type", "Lookup", "AccountType")]));
		SimpleToFullFilterConverter builder = new(schema, resolver);

		string json = builder.Build(group, "Account");
		JsonElement filter0 = JsonDocument.Parse(json).RootElement
			.GetProperty("items").GetProperty("Filter_0");

		filter0.GetProperty("filterType").GetInt32().Should().Be(4);
		filter0.GetProperty("isAggregative").GetBoolean().Should().BeFalse();
		filter0.GetProperty("trimDateTimeParameterToDate").GetBoolean().Should().BeFalse();
		filter0.GetProperty("dataValueType").GetInt32().Should().Be(10, because: "Lookup data value type is 10");
		filter0.GetProperty("referenceSchemaName").GetString().Should().Be("AccountType");

		JsonElement lookupValue = filter0.GetProperty("rightExpressions")[0]
			.GetProperty("parameter").GetProperty("value");
		string guidString = resolved.ToString("D");
		lookupValue.GetProperty("Name").GetString().Should().Be("Customer");
		lookupValue.GetProperty("Id").GetString().Should().Be(guidString);
		lookupValue.GetProperty("value").GetString().Should().Be(guidString);
		lookupValue.GetProperty("displayValue").GetString().Should().Be("Customer");
	}

	[Test]
	[Category("Unit")]
	[Description("When the caller passes a raw GUID for a Lookup, the builder reverse-resolves the display name to populate Name/displayValue (canonical store form).")]
	public void Build_Should_Reverse_Resolve_DisplayName_For_Guid_Input() {
		Guid id = Guid.NewGuid();
		ILookupValueResolver resolver = Substitute.For<ILookupValueResolver>();
		resolver.TryResolveDisplayNameById("AccountType", id, out Arg.Any<string?>())
			.Returns(call => { call[2] = "Customer"; return true; });

		StaticFilterGroup group = Deserialize(
			$$"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "Type", "comparisonType": "EQUAL", "value": "{{id:D}}" } ] }""");
		IFilterSchemaProvider schema = SchemaWith(("Account", [("Type", "Lookup", "AccountType")]));
		SimpleToFullFilterConverter builder = new(schema, resolver);

		string json = builder.Build(group, "Account");
		JsonElement lookupValue = JsonDocument.Parse(json).RootElement
			.GetProperty("items").GetProperty("Filter_0")
			.GetProperty("rightExpressions")[0]
			.GetProperty("parameter").GetProperty("value");

		lookupValue.GetProperty("Name").GetString().Should().Be("Customer");
		lookupValue.GetProperty("displayValue").GetString().Should().Be("Customer");
		lookupValue.GetProperty("Id").GetString().Should().Be(id.ToString("D"));
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a raw-GUID Lookup value when its display name cannot be reverse-resolved — an Id-only value breaks the Freedom UI lookup control (ENG-88588).")]
	public void Build_Should_Reject_Lookup_Guid_When_DisplayName_Unresolved() {
		Guid id = Guid.NewGuid();
		ILookupValueResolver resolver = Substitute.For<ILookupValueResolver>();
		resolver.TryResolveDisplayNameById("AccountType", id, out Arg.Any<string?>())
			.Returns(call => { call[2] = null; return false; });
		StaticFilterGroup group = Deserialize(
			$$"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "Type", "comparisonType": "EQUAL", "value": "{{id:D}}" } ] }""");
		IFilterSchemaProvider schema = SchemaWith(("Account", [("Type", "Lookup", "AccountType")]));
		SimpleToFullFilterConverter builder = new(schema, resolver);

		Action act = () => builder.Build(group, "Account");

		act.Should().Throw<ArgumentException>().WithMessage("*could not resolve the display name*");
	}

	[Test]
	[Category("Unit")]
	[Description("Treats a Money0 (dataValueType 48) column as numeric and emits a Float parameter.")]
	public void Build_Should_Treat_Money0_As_Numeric() {
		StaticFilterGroup group = Deserialize(
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "Amount", "comparisonType": "GREATER", "value": 100 } ] }""");
		IFilterSchemaProvider schema = SchemaWith(("Account", [("Amount", "Money0", null)]));
		SimpleToFullFilterConverter builder = new(schema, lookupResolver: null);

		string json = builder.Build(group, "Account");
		JsonElement param = JsonDocument.Parse(json).RootElement
			.GetProperty("items").GetProperty("Filter_0")
			.GetProperty("rightExpression").GetProperty("parameter");

		param.GetProperty("dataValueType").GetInt32().Should().Be(5, because: "Money0 is numeric and maps to EsqDataValueType.Float");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a non-filterable column (e.g. Blob) with a clear message.")]
	public void SchemaValidation_Should_Reject_NonFilterable_Column() {
		StaticFilterGroup group = Deserialize(
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "Data", "comparisonType": "EQUAL", "value": "x" } ] }""");
		IFilterSchemaProvider schema = SchemaWith(("Account", [("Data", "Blob", null)]));
		SchemaAwareFilterValidator validator = new(schema);

		Action act = () => validator.Validate(group, "Account");

		act.Should().Throw<ArgumentException>().WithMessage("*cannot be used as a filter value*");
	}

	[Test]
	[Category("Unit")]
	[Description("Emits a DatePart HourMinute CompareFilter for an exact time-of-day comparison (created at 11:06 AM) on a DateTime column.")]
	public void Build_Should_Emit_DatePart_HourMinute_For_TimeOfDay() {
		StaticFilterGroup group = Deserialize(
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "CreatedOn", "comparisonType": "EQUAL", "datePart": "HourMinute", "value": "11:06:00" } ] }""");
		IFilterSchemaProvider schema = SchemaWith(("Contact", [("CreatedOn", "DateTime", null)]));
		// Fixed clock at UTC+3 so the emitted local/UTC datetime carriers are deterministic.
		SimpleToFullFilterConverter builder = new(schema, lookupResolver: null,
			nowProvider: () => new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.FromHours(3)));

		string json = builder.Build(group, "Contact");
		JsonElement filter0 = JsonDocument.Parse(json).RootElement.GetProperty("items").GetProperty("Filter_0");

		filter0.GetProperty("filterType").GetInt32().Should().Be(1, because: "a DatePart leaf is a CompareFilter");
		filter0.GetProperty("comparisonType").GetInt32().Should().Be(3, because: "EQUAL maps to 3");
		filter0.GetProperty("trimDateTimeParameterToDate").GetBoolean().Should().BeTrue(
			because: "the verified platform HourMinute sample carries trimDateTimeParameterToDate=true");
		filter0.GetProperty("dataValueType").GetInt32().Should().Be(7, because: "the source DateTime column code is 7");

		JsonElement left = filter0.GetProperty("leftExpression");
		left.GetProperty("expressionType").GetInt32().Should().Be(1, because: "ExpressionType.Function is 1");
		left.GetProperty("functionType").GetInt32().Should().Be(3, because: "FunctionType.DatePart is 3");
		left.GetProperty("datePartType").GetInt32().Should().Be(7, because: "DatePartType.HourMinute is 7");
		left.GetProperty("functionArgument").GetProperty("columnPath").GetString().Should().Be("CreatedOn");
		left.GetProperty("className").GetString().Should().Be("Terrasoft.FunctionExpression");

		JsonElement param = filter0.GetProperty("rightExpression").GetProperty("parameter");
		param.GetProperty("dataValueType").GetInt32().Should().Be(9, because: "a time-of-day value is Time (9)");
		param.GetProperty("value").GetString().Should().Be("\"2026-06-10T11:06:00.000\"",
			because: "the Freedom UI control needs the local datetime carrier as a quote-wrapped ISO string, not a bare HH:mm that renders as a placeholder");
		param.GetProperty("dateValue").GetString().Should().Be("2026-06-10T08:06:00.000Z",
			because: "dateValue carries the same instant in UTC (11:06 at +3 = 08:06Z)");
	}

	[Test]
	[Category("Unit")]
	[Description("Emits a DatePart Year CompareFilter comparing the extracted year to an Integer (calendar year 2021).")]
	public void Build_Should_Emit_DatePart_Year_For_CalendarYear() {
		StaticFilterGroup group = Deserialize(
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "CreatedOn", "comparisonType": "EQUAL", "datePart": "Year", "value": 2021 } ] }""");
		IFilterSchemaProvider schema = SchemaWith(("Contact", [("CreatedOn", "DateTime", null)]));
		SimpleToFullFilterConverter builder = new(schema, lookupResolver: null);

		string json = builder.Build(group, "Contact");
		JsonElement filter0 = JsonDocument.Parse(json).RootElement.GetProperty("items").GetProperty("Filter_0");

		filter0.GetProperty("leftExpression").GetProperty("datePartType").GetInt32().Should().Be(4,
			because: "DatePartType.Year is 4");
		filter0.TryGetProperty("trimDateTimeParameterToDate", out _).Should().BeFalse(
			because: "integer date-part comparisons mirror the documented year example, which omits trim");
		filter0.TryGetProperty("dataValueType", out _).Should().BeFalse(
			because: "an integer date-part leaf carries no dataValueType of its own; the integer lives in the right parameter");
		JsonElement param = filter0.GetProperty("rightExpression").GetProperty("parameter");
		param.GetProperty("dataValueType").GetInt32().Should().Be(4, because: "Integer is 4");
		param.GetProperty("value").GetInt64().Should().Be(2021);
	}

	[Test]
	[Category("Unit")]
	[Description("Combines a relative-date macros leaf and a HourMinute datePart leaf under AND for 'previous quarter at 11:06 AM'.")]
	public void Build_Should_Combine_PreviousQuarter_Macros_And_HourMinute_DatePart() {
		StaticFilterGroup group = Deserialize("""
			{ "logicalOperation": "AND", "filters": [
			  { "columnPath": "CreatedOn", "comparisonType": "EQUAL", "valueMacros": "PreviousQuarter" },
			  { "columnPath": "CreatedOn", "comparisonType": "EQUAL", "datePart": "HourMinute", "value": "11:06:00" }
			] }
			""");
		IFilterSchemaProvider schema = SchemaWith(("Contact", [("CreatedOn", "DateTime", null)]));
		SimpleToFullFilterConverter builder = new(schema, lookupResolver: null);

		string json = builder.Build(group, "Contact");
		JsonElement items = JsonDocument.Parse(json).RootElement.GetProperty("items");

		items.GetProperty("Filter_0").GetProperty("rightExpression").GetProperty("macrosType").GetInt32()
			.Should().Be(12, because: "QueryMacrosType.PreviousQuarter is 12");
		items.GetProperty("Filter_1").GetProperty("leftExpression").GetProperty("datePartType").GetInt32()
			.Should().Be(7, because: "the second leaf extracts the HourMinute part");
	}

	[Test]
	[Category("Unit")]
	[Description("Schema-aware validation rejects a datePart on a non-date column.")]
	public void SchemaValidation_Should_Reject_DatePart_On_Text_Column() {
		StaticFilterGroup group = Deserialize(
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "Name", "comparisonType": "EQUAL", "datePart": "Year", "value": 2021 } ] }""");
		IFilterSchemaProvider schema = SchemaWith(("Contact", [("Name", "Text", null)]));
		SchemaAwareFilterValidator validator = new(schema);

		Action act = () => validator.Validate(group, "Contact");

		act.Should().Throw<ArgumentException>().WithMessage("*applies only to Date/DateTime/Time columns*");
	}

	[Test]
	[Category("Unit")]
	[Description("Structural validation rejects an unknown datePart name.")]
	public void Build_Should_Reject_Unknown_DatePart() {
		Action act = () => Deserialize(
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "CreatedOn", "comparisonType": "EQUAL", "datePart": "Nonsense", "value": 1 } ] }""");

		act.Should().Throw<ArgumentException>().WithMessage("*unknown date part 'Nonsense'*");
	}

	[Test]
	[Category("Unit")]
	[Description("Structural validation rejects a HourMinute datePart with a non-string (numeric) value.")]
	public void Build_Should_Reject_HourMinute_DatePart_With_Numeric_Value() {
		Action act = () => Deserialize(
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "CreatedOn", "comparisonType": "EQUAL", "datePart": "HourMinute", "value": 1106 } ] }""");

		act.Should().Throw<ArgumentException>().WithMessage("*time-of-day string*");
	}

	[Test]
	[Category("Unit")]
	[Description("Structural validation rejects an integer datePart (Year) with a non-numeric value.")]
	public void Build_Should_Reject_Year_DatePart_With_String_Value() {
		Action act = () => Deserialize(
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "CreatedOn", "comparisonType": "EQUAL", "datePart": "Year", "value": "2021" } ] }""");

		act.Should().Throw<ArgumentException>().WithMessage("*expects a JSON integer*");
	}

	[Test]
	[Category("Unit")]
	[Description("Structural validation rejects providing both datePart and valueMacros on the same leaf.")]
	public void Build_Should_Reject_DatePart_With_Macros() {
		Action act = () => Deserialize(
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "CreatedOn", "comparisonType": "EQUAL", "datePart": "Year", "valueMacros": "CurrentYear" } ] }""");

		act.Should().Throw<ArgumentException>().WithMessage("*datePart and valueMacros are mutually exclusive*");
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
					DataValueTypeCode = Clio.Common.CreatioDataValueType.GetCode(col.Type) ?? 0,
					ReferenceSchemaName = col.Ref
				};
			}

			provider.GetColumns(schema.Schema).Returns(columns);
		}

		return provider;
	}
}
