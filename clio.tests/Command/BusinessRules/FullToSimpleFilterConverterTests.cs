using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Command.BusinessRules.Filters;
using Clio.Command.BusinessRules.Filters.Esq;
using Clio.Command.BusinessRules.Filters.Schema;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.BusinessRules;

[TestFixture]
[Property("Module", "Command")]
public sealed class FullToSimpleFilterConverterTests {

	[Test]
	[Category("Unit")]
	[Description("Round-trips a scalar text START_WITH leaf: the decompiled value stays a JSON string.")]
	public void Decompile_Should_RoundTrip_Text_StartWith_Leaf() {
		// Arrange
		StaticFilterGroup group = Deserialize(
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "Name", "comparisonType": "START_WITH", "value": "U" } ] }""");
		IFilterSchemaProvider schema = SchemaWith(("Country", [("Name", "Text", null)]));

		// Act
		JsonNode decompiled = RoundTrip(group, "Country", schema);

		// Assert
		decompiled["logicalOperation"]!.GetValue<string>().Should().Be("AND",
			because: "the root logical operation round-trips from the envelope");
		JsonNode leaf = decompiled["filters"]!.AsArray()[0]!;
		leaf["columnPath"]!.GetValue<string>().Should().Be("Name",
			because: "the scalar column path round-trips into the friendly leaf");
		leaf["comparisonType"]!.GetValue<string>().Should().Be("START_WITH",
			because: "START_WITH maps back from its wire comparison code");
		leaf["value"]!.GetValueKind().Should().Be(JsonValueKind.String,
			because: "a text value must stay a JSON string on the way back");
		leaf["value"]!.GetValue<string>().Should().Be("U",
			because: "the text value round-trips unchanged");
	}

	[Test]
	[Category("Unit")]
	[Description("Round-trips a scalar text CONTAIN leaf.")]
	public void Decompile_Should_RoundTrip_Text_Contain_Leaf() {
		// Arrange
		StaticFilterGroup group = Deserialize(
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "Name", "comparisonType": "CONTAIN", "value": "abc" } ] }""");
		IFilterSchemaProvider schema = SchemaWith(("Country", [("Name", "Text", null)]));

		// Act
		JsonNode leaf = RoundTrip(group, "Country", schema)["filters"]!.AsArray()[0]!;

		// Assert
		leaf["comparisonType"]!.GetValue<string>().Should().Be("CONTAIN",
			because: "CONTAIN maps back from its wire comparison code");
		leaf["value"]!.GetValue<string>().Should().Be("abc",
			because: "the text value round-trips unchanged");
	}

	[Test]
	[Category("Unit")]
	[Description("Round-trips an Integer GREATER leaf: the decompiled value stays a JSON number.")]
	public void Decompile_Should_RoundTrip_Integer_Greater_Leaf() {
		// Arrange
		StaticFilterGroup group = Deserialize(
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "Age", "comparisonType": "GREATER", "value": 5 } ] }""");
		IFilterSchemaProvider schema = SchemaWith(("Contact", [("Age", "Integer", null)]));

		// Act
		JsonNode leaf = RoundTrip(group, "Contact", schema)["filters"]!.AsArray()[0]!;

		// Assert
		leaf["comparisonType"]!.GetValue<string>().Should().Be("GREATER",
			because: "GREATER maps back from its wire comparison code");
		leaf["value"]!.GetValueKind().Should().Be(JsonValueKind.Number,
			because: "an integer value must stay a JSON number on the way back");
		leaf["value"]!.GetValue<int>().Should().Be(5,
			because: "the integer value round-trips unchanged");
	}

	[Test]
	[Category("Unit")]
	[Description("Round-trips a Float GREATER leaf: the decompiled value stays a JSON number.")]
	public void Decompile_Should_RoundTrip_Float_Greater_Leaf() {
		// Arrange
		StaticFilterGroup group = Deserialize(
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "Amount", "comparisonType": "GREATER", "value": 1.5 } ] }""");
		IFilterSchemaProvider schema = SchemaWith(("Account", [("Amount", "Float", null)]));

		// Act
		JsonNode leaf = RoundTrip(group, "Account", schema)["filters"]!.AsArray()[0]!;

		// Assert
		leaf["value"]!.GetValueKind().Should().Be(JsonValueKind.Number,
			because: "a float value must stay a JSON number on the way back");
		leaf["value"]!.GetValue<double>().Should().Be(1.5d,
			because: "the float value round-trips unchanged");
	}

	[Test]
	[Category("Unit")]
	[Description("Round-trips a Boolean EQUAL leaf: the decompiled value stays a JSON boolean.")]
	public void Decompile_Should_RoundTrip_Boolean_Equal_Leaf() {
		// Arrange
		StaticFilterGroup group = Deserialize(
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "IsUsed", "comparisonType": "EQUAL", "value": true } ] }""");
		IFilterSchemaProvider schema = SchemaWith(("SysLanguage", [("IsUsed", "Boolean", null)]));

		// Act
		JsonNode leaf = RoundTrip(group, "SysLanguage", schema)["filters"]!.AsArray()[0]!;

		// Assert
		leaf["comparisonType"]!.GetValue<string>().Should().Be("EQUAL",
			because: "EQUAL maps back from its wire comparison code");
		leaf["value"]!.GetValueKind().Should().Be(JsonValueKind.True,
			because: "a boolean value must stay a JSON boolean on the way back");
		leaf["value"]!.GetValue<bool>().Should().BeTrue(
			because: "the boolean value round-trips unchanged");
	}

	[Test]
	[Category("Unit")]
	[Description("Round-trips DateTime/Date/Time string comparisons: each decompiled value stays a JSON string.")]
	public void Decompile_Should_RoundTrip_Temporal_String_Compares() {
		// Arrange
		StaticFilterGroup group = Deserialize("""
			{ "logicalOperation": "AND", "filters": [
			  { "columnPath": "CreatedOn", "comparisonType": "GREATER", "value": "2026-05-01T12:00:00Z" },
			  { "columnPath": "DueDate", "comparisonType": "LESS", "value": "2026-05-01" },
			  { "columnPath": "StartTime", "comparisonType": "EQUAL", "value": "11:06:00" }
			] }
			""");
		IFilterSchemaProvider schema = SchemaWith(("Activity", [
			("CreatedOn", "DateTime", null), ("DueDate", "Date", null), ("StartTime", "Time", null)]));

		// Act
		JsonArray leaves = RoundTrip(group, "Activity", schema)["filters"]!.AsArray();

		// Assert
		leaves[0]!["value"]!.GetValueKind().Should().Be(JsonValueKind.String,
			because: "a DateTime value must stay a JSON string on the way back");
		leaves[0]!["value"]!.GetValue<string>().Should().Be("2026-05-01T12:00:00Z",
			because: "the DateTime value round-trips unchanged");
		leaves[1]!["value"]!.GetValue<string>().Should().Be("2026-05-01",
			because: "the Date value round-trips unchanged");
		leaves[2]!["value"]!.GetValue<string>().Should().Be("11:06:00",
			because: "the Time value round-trips unchanged");
	}

	[Test]
	[Category("Unit")]
	[Description("Round-trips IS_NULL and IS_NOT_NULL unary leaves.")]
	public void Decompile_Should_RoundTrip_IsNull_And_IsNotNull_Leaves() {
		// Arrange
		StaticFilterGroup group = Deserialize("""
			{ "logicalOperation": "AND", "filters": [
			  { "columnPath": "MobilePhone", "comparisonType": "IS_NULL" },
			  { "columnPath": "HomePhone", "comparisonType": "IS_NOT_NULL" }
			] }
			""");
		IFilterSchemaProvider schema = SchemaWith(("Contact", [
			("MobilePhone", "PhoneText", null), ("HomePhone", "PhoneText", null)]));

		// Act
		JsonArray leaves = RoundTrip(group, "Contact", schema)["filters"]!.AsArray();

		// Assert
		leaves[0]!["comparisonType"]!.GetValue<string>().Should().Be("IS_NULL",
			because: "IS_NULL maps back from comparison code 1");
		leaves[0]!["columnPath"]!.GetValue<string>().Should().Be("MobilePhone",
			because: "the unary leaf column round-trips");
		leaves[0]!.AsObject().ContainsKey("value").Should().BeFalse(
			because: "an IS_NULL leaf carries no value");
		leaves[1]!["comparisonType"]!.GetValue<string>().Should().Be("IS_NOT_NULL",
			because: "IS_NOT_NULL maps back from comparison code 2");
	}

	[Test]
	[Category("Unit")]
	[Description("Round-trips a single-value Lookup EQUAL leaf: the decompiled value is the stored display Name.")]
	public void Decompile_Should_RoundTrip_Lookup_Single_Value_As_DisplayName() {
		// Arrange
		ILookupValueResolver resolver = Substitute.For<ILookupValueResolver>();
		resolver.ResolveIdByDisplayName("AccountType", "Customer").Returns(Guid.NewGuid());
		StaticFilterGroup group = Deserialize(
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "Type", "comparisonType": "EQUAL", "value": "Customer" } ] }""");
		IFilterSchemaProvider schema = SchemaWith(("Account", [("Type", "Lookup", "AccountType")]));

		// Act
		JsonNode leaf = RoundTrip(group, "Account", schema, resolver)["filters"]!.AsArray()[0]!;

		// Assert
		leaf["comparisonType"]!.GetValue<string>().Should().Be("EQUAL",
			because: "a single-value lookup filter maps back to EQUAL");
		leaf["value"]!.GetValueKind().Should().Be(JsonValueKind.String,
			because: "a single lookup value decompiles to a scalar string, not an array");
		leaf["value"]!.GetValue<string>().Should().Be("Customer",
			because: "the decompiler surfaces the display Name carried in the stored lookup value");
	}

	[Test]
	[Category("Unit")]
	[Description("Round-trips a multi-value Lookup leaf: the decompiled value is an array of display Names.")]
	public void Decompile_Should_RoundTrip_Lookup_Multi_Value_As_DisplayName_Array() {
		// Arrange
		ILookupValueResolver resolver = Substitute.For<ILookupValueResolver>();
		resolver.ResolveIdByDisplayName("AccountType", Arg.Any<string>()).Returns(_ => Guid.NewGuid());
		StaticFilterGroup group = Deserialize(
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "Type", "comparisonType": "EQUAL", "value": ["Customer", "Partner"] } ] }""");
		IFilterSchemaProvider schema = SchemaWith(("Account", [("Type", "Lookup", "AccountType")]));

		// Act
		JsonNode leaf = RoundTrip(group, "Account", schema, resolver)["filters"]!.AsArray()[0]!;

		// Assert
		leaf["value"]!.GetValueKind().Should().Be(JsonValueKind.Array,
			because: "a multi-value lookup filter decompiles to a JSON array");
		JsonArray values = leaf["value"]!.AsArray();
		values.Count.Should().Be(2, because: "both lookup values round-trip");
		values[0]!.GetValue<string>().Should().Be("Customer",
			because: "the first display Name round-trips");
		values[1]!.GetValue<string>().Should().Be("Partner",
			because: "the second display Name round-trips");
	}

	[Test]
	[Category("Unit")]
	[Description("Round-trips a no-argument date macros leaf (CurrentYear).")]
	public void Decompile_Should_RoundTrip_NoArg_Macros() {
		// Arrange
		StaticFilterGroup group = Deserialize(
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "CreatedOn", "comparisonType": "EQUAL", "valueMacros": "CurrentYear" } ] }""");
		IFilterSchemaProvider schema = SchemaWith(("Contact", [("CreatedOn", "DateTime", null)]));

		// Act
		JsonNode leaf = RoundTrip(group, "Contact", schema)["filters"]!.AsArray()[0]!;

		// Assert
		leaf["valueMacros"]!.GetValue<string>().Should().Be("CurrentYear",
			because: "the macros name maps back from its wire macrosType");
		leaf.AsObject().ContainsKey("valueMacrosArgument").Should().BeFalse(
			because: "a no-argument macros carries no valueMacrosArgument");
		leaf.AsObject().ContainsKey("value").Should().BeFalse(
			because: "a macros leaf carries no constant value");
	}

	[Test]
	[Category("Unit")]
	[Description("Round-trips an N-argument date macros leaf (NextNDays) including its integer argument.")]
	public void Decompile_Should_RoundTrip_NArg_Macros_With_Argument() {
		// Arrange
		StaticFilterGroup group = Deserialize(
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "DueDate", "comparisonType": "LESS_OR_EQUAL", "valueMacros": "NextNDays", "valueMacrosArgument": 5 } ] }""");
		IFilterSchemaProvider schema = SchemaWith(("Activity", [("DueDate", "Date", null)]));

		// Act
		JsonNode leaf = RoundTrip(group, "Activity", schema)["filters"]!.AsArray()[0]!;

		// Assert
		leaf["valueMacros"]!.GetValue<string>().Should().Be("NextNDays",
			because: "the N-argument macros name maps back from its wire macrosType");
		leaf["valueMacrosArgument"]!.GetValueKind().Should().Be(JsonValueKind.Number,
			because: "the macros argument stays a JSON integer on the way back");
		leaf["valueMacrosArgument"]!.GetValue<int>().Should().Be(5,
			because: "the macros argument round-trips unchanged");
	}

	[Test]
	[Category("Unit")]
	[Description("Round-trips a lookup macros leaf (CurrentUserContact) on a Lookup column.")]
	public void Decompile_Should_RoundTrip_Lookup_Macros() {
		// Arrange
		StaticFilterGroup group = Deserialize(
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "Owner", "comparisonType": "EQUAL", "valueMacros": "CurrentUserContact" } ] }""");
		IFilterSchemaProvider schema = SchemaWith(("Activity", [("Owner", "Lookup", "Contact")]));

		// Act
		JsonNode leaf = RoundTrip(group, "Activity", schema)["filters"]!.AsArray()[0]!;

		// Assert
		leaf["valueMacros"]!.GetValue<string>().Should().Be("CurrentUserContact",
			because: "the lookup macros name maps back from its wire macrosType");
	}

	[Test]
	[Category("Unit")]
	[Description("Round-trips an integer datePart (Year EQUAL 2021).")]
	public void Decompile_Should_RoundTrip_DatePart_Year() {
		// Arrange
		StaticFilterGroup group = Deserialize(
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "CreatedOn", "comparisonType": "EQUAL", "datePart": "Year", "value": 2021 } ] }""");
		IFilterSchemaProvider schema = SchemaWith(("Contact", [("CreatedOn", "DateTime", null)]));

		// Act
		JsonNode leaf = RoundTrip(group, "Contact", schema)["filters"]!.AsArray()[0]!;

		// Assert
		leaf["datePart"]!.GetValue<string>().Should().Be("Year",
			because: "the date-part name maps back from its wire datePartType");
		leaf["comparisonType"]!.GetValue<string>().Should().Be("EQUAL",
			because: "the date-part comparison round-trips");
		leaf["value"]!.GetValueKind().Should().Be(JsonValueKind.Number,
			because: "an integer date-part value stays a JSON number on the way back");
		leaf["value"]!.GetValue<int>().Should().Be(2021,
			because: "the calendar year round-trips unchanged");
	}

	[Test]
	[Category("Unit")]
	[Description("Round-trips an integer datePart (Hour EQUAL 11).")]
	public void Decompile_Should_RoundTrip_DatePart_Hour() {
		// Arrange
		StaticFilterGroup group = Deserialize(
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "CreatedOn", "comparisonType": "EQUAL", "datePart": "Hour", "value": 11 } ] }""");
		IFilterSchemaProvider schema = SchemaWith(("Contact", [("CreatedOn", "DateTime", null)]));

		// Act
		JsonNode leaf = RoundTrip(group, "Contact", schema)["filters"]!.AsArray()[0]!;

		// Assert
		leaf["datePart"]!.GetValue<string>().Should().Be("Hour",
			because: "the Hour date-part maps back from its wire datePartType");
		leaf["value"]!.GetValue<int>().Should().Be(11,
			because: "the extracted-hour value round-trips unchanged");
	}

	[Test]
	[Category("Unit")]
	[Description("Round-trips a HourMinute datePart back to an HH:mm:ss time-of-day string.")]
	public void Decompile_Should_RoundTrip_DatePart_HourMinute_As_TimeOfDay() {
		// Arrange
		StaticFilterGroup group = Deserialize(
			"""{ "logicalOperation": "AND", "filters": [ { "columnPath": "CreatedOn", "comparisonType": "EQUAL", "datePart": "HourMinute", "value": "11:06:00" } ] }""");
		IFilterSchemaProvider schema = SchemaWith(("Contact", [("CreatedOn", "DateTime", null)]));
		Func<DateTimeOffset> now = () => new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.FromHours(3));

		// Act
		JsonNode leaf = RoundTrip(group, "Contact", schema, resolver: null, now)["filters"]!.AsArray()[0]!;

		// Assert
		leaf["datePart"]!.GetValue<string>().Should().Be("HourMinute",
			because: "the HourMinute date-part maps back from its wire datePartType");
		leaf["value"]!.GetValueKind().Should().Be(JsonValueKind.String,
			because: "a time-of-day value decompiles back to a string");
		leaf["value"]!.GetValue<string>().Should().Be("11:06:00",
			because: "the persisted local datetime carrier is reduced back to its HH:mm:ss time of day");
	}

	[Test]
	[Category("Unit")]
	[Description("Round-trips a backward-reference EXISTS without a nested filter; referenceColumnPath drops the synthetic .Id suffix.")]
	public void Decompile_Should_RoundTrip_Backward_Exists_Without_Filter() {
		// Arrange
		StaticFilterGroup group = Deserialize("""
			{ "logicalOperation": "AND", "backwardReferenceFilters": [
			  { "referenceColumnPath": "[Contact:Account]", "comparisonType": "EXISTS" }
			] }
			""");
		IFilterSchemaProvider schema = SchemaWith(
			("Account", []), ("Contact", [("Account", "Lookup", "Account")]));

		// Act
		JsonNode backward = RoundTrip(group, "Account", schema)["backwardReferenceFilters"]!.AsArray()[0]!;

		// Assert
		backward["referenceColumnPath"]!.GetValue<string>().Should().Be("[Contact:Account]",
			because: "the decompiler strips the platform .Id suffix back to the friendly [Schema:Column] form");
		backward["comparisonType"]!.GetValue<string>().Should().Be("EXISTS",
			because: "EXISTS maps back from comparison code 15");
		backward.AsObject().ContainsKey("filter").Should().BeFalse(
			because: "an EXISTS with an empty sub-filter carries no friendly filter object");
	}

	[Test]
	[Category("Unit")]
	[Description("Round-trips a backward-reference NOT_EXISTS with a nested filter group.")]
	public void Decompile_Should_RoundTrip_Backward_NotExists_With_Nested_Filter() {
		// Arrange
		StaticFilterGroup group = Deserialize("""
			{ "logicalOperation": "AND", "backwardReferenceFilters": [
			  { "referenceColumnPath": "[Contact:Account]", "comparisonType": "NOT_EXISTS",
			    "filter": { "logicalOperation": "AND", "filters": [ { "columnPath": "Name", "comparisonType": "EQUAL", "value": "X" } ] } }
			] }
			""");
		IFilterSchemaProvider schema = SchemaWith(
			("Account", []), ("Contact", [("Account", "Lookup", "Account"), ("Name", "Text", null)]));

		// Act
		JsonNode backward = RoundTrip(group, "Account", schema)["backwardReferenceFilters"]!.AsArray()[0]!;

		// Assert
		backward["comparisonType"]!.GetValue<string>().Should().Be("NOT_EXISTS",
			because: "NOT_EXISTS maps back from comparison code 16");
		JsonNode nestedLeaf = backward["filter"]!["filters"]!.AsArray()[0]!;
		nestedLeaf["columnPath"]!.GetValue<string>().Should().Be("Name",
			because: "the nested sub-filter leaf column round-trips");
		nestedLeaf["value"]!.GetValue<string>().Should().Be("X",
			because: "the nested sub-filter leaf value round-trips");
	}

	[Test]
	[Category("Unit")]
	[Description("Round-trips a COUNT aggregation backward reference (COUNT GREATER 10) with no aggregationColumnPath.")]
	public void Decompile_Should_RoundTrip_Aggregation_Count() {
		// Arrange
		StaticFilterGroup group = Deserialize("""
			{ "logicalOperation": "AND", "backwardReferenceFilters": [
			  { "referenceColumnPath": "[Activity:Contact]", "aggregationType": "COUNT", "comparisonType": "GREATER", "aggregationValue": 10 }
			] }
			""");
		IFilterSchemaProvider schema = SchemaWith(
			("Contact", []), ("Activity", [("Contact", "Lookup", "Contact")]));

		// Act
		JsonNode backward = RoundTrip(group, "Contact", schema)["backwardReferenceFilters"]!.AsArray()[0]!;

		// Assert
		backward["referenceColumnPath"]!.GetValue<string>().Should().Be("[Activity:Contact]",
			because: "the aggregation backward path decompiles to the friendly [Schema:Column] form with no .Id suffix");
		backward["aggregationType"]!.GetValue<string>().Should().Be("COUNT",
			because: "the aggregation type maps back from its wire code");
		backward["comparisonType"]!.GetValue<string>().Should().Be("GREATER",
			because: "the aggregation comparison round-trips");
		backward["aggregationValue"]!.GetValue<double>().Should().Be(10d,
			because: "the aggregation threshold round-trips unchanged");
		backward.AsObject().ContainsKey("aggregationColumnPath").Should().BeFalse(
			because: "COUNT aggregates the child rows and carries no aggregationColumnPath");
	}

	[Test]
	[Category("Unit")]
	[Description("Round-trips a SUM aggregation backward reference (SUM GREATER_OR_EQUAL 1000) with an aggregationColumnPath.")]
	public void Decompile_Should_RoundTrip_Aggregation_Sum_With_ColumnPath() {
		// Arrange
		StaticFilterGroup group = Deserialize("""
			{ "logicalOperation": "AND", "backwardReferenceFilters": [
			  { "referenceColumnPath": "[Order:Customer]", "aggregationType": "SUM", "aggregationColumnPath": "Amount", "comparisonType": "GREATER_OR_EQUAL", "aggregationValue": 1000 }
			] }
			""");
		IFilterSchemaProvider schema = SchemaWith(
			("Contact", []), ("Order", [("Customer", "Lookup", "Contact"), ("Amount", "Float", null)]));

		// Act
		JsonNode backward = RoundTrip(group, "Contact", schema)["backwardReferenceFilters"]!.AsArray()[0]!;

		// Assert
		backward["referenceColumnPath"]!.GetValue<string>().Should().Be("[Order:Customer]",
			because: "the aggregation backward path decompiles to the friendly [Schema:Column] form");
		backward["aggregationType"]!.GetValue<string>().Should().Be("SUM",
			because: "the SUM aggregation type maps back from its wire code");
		backward["aggregationColumnPath"]!.GetValue<string>().Should().Be("Amount",
			because: "the numeric child column is split back out of the aggregated column path");
		backward["comparisonType"]!.GetValue<string>().Should().Be("GREATER_OR_EQUAL",
			because: "GREATER_OR_EQUAL maps back from comparison code 8");
		backward["aggregationValue"]!.GetValue<double>().Should().Be(1000d,
			because: "the aggregation threshold round-trips unchanged");
	}

	[Test]
	[Category("Unit")]
	[Description("Round-trips an AVG aggregation backward reference with an aggregationColumnPath.")]
	public void Decompile_Should_RoundTrip_Aggregation_Avg_With_ColumnPath() {
		// Arrange
		StaticFilterGroup group = Deserialize("""
			{ "logicalOperation": "AND", "backwardReferenceFilters": [
			  { "referenceColumnPath": "[Order:Customer]", "aggregationType": "AVG", "aggregationColumnPath": "Amount", "comparisonType": "GREATER_OR_EQUAL", "aggregationValue": 50 }
			] }
			""");
		IFilterSchemaProvider schema = SchemaWith(
			("Contact", []), ("Order", [("Customer", "Lookup", "Contact"), ("Amount", "Float", null)]));

		// Act
		JsonNode backward = RoundTrip(group, "Contact", schema)["backwardReferenceFilters"]!.AsArray()[0]!;

		// Assert
		backward["aggregationType"]!.GetValue<string>().Should().Be("AVG",
			because: "the AVG aggregation type maps back from its wire code");
		backward["aggregationColumnPath"]!.GetValue<string>().Should().Be("Amount",
			because: "the numeric child column is split back out of the aggregated column path");
	}

	[Test]
	[Category("Unit")]
	[Description("Round-trips a nested OR group (two leaves) inside the root AND group.")]
	public void Decompile_Should_RoundTrip_Nested_Or_Group() {
		// Arrange
		StaticFilterGroup group = Deserialize("""
			{ "logicalOperation": "AND", "groups": [
			  { "logicalOperation": "OR", "filters": [
			    { "columnPath": "Name", "comparisonType": "EQUAL", "value": "A" },
			    { "columnPath": "Name", "comparisonType": "EQUAL", "value": "B" }
			  ] }
			] }
			""");
		IFilterSchemaProvider schema = SchemaWith(("Country", [("Name", "Text", null)]));

		// Act
		JsonNode root = RoundTrip(group, "Country", schema);

		// Assert
		root["logicalOperation"]!.GetValue<string>().Should().Be("AND",
			because: "the outer group operation round-trips");
		JsonNode nested = root["groups"]!.AsArray()[0]!;
		nested["logicalOperation"]!.GetValue<string>().Should().Be("OR",
			because: "the nested group operation round-trips");
		JsonArray nestedLeaves = nested["filters"]!.AsArray();
		nestedLeaves.Count.Should().Be(2, because: "both leaves in the nested group round-trip");
		nestedLeaves[0]!["value"]!.GetValue<string>().Should().Be("A",
			because: "the first nested leaf value round-trips");
		nestedLeaves[1]!["value"]!.GetValue<string>().Should().Be("B",
			because: "the second nested leaf value round-trips");
	}

	[Test]
	[Category("Unit")]
	[Description("Throws InvalidOperationException when the ESQ envelope string is empty.")]
	public void Decompile_Should_Throw_When_Envelope_Is_Empty() {
		// Arrange
		string envelope = string.Empty;

		// Act
		Action act = () => FullToSimpleFilterConverter.Decompile(envelope);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "an empty envelope cannot be decompiled into a friendly filter");
	}

	[Test]
	[Category("Unit")]
	[Description("Throws InvalidOperationException when the ESQ envelope string is not valid JSON.")]
	public void Decompile_Should_Throw_When_Envelope_Is_Malformed_Json() {
		// Arrange
		string envelope = "{ not json";

		// Act
		Action act = () => FullToSimpleFilterConverter.Decompile(envelope);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "a malformed envelope cannot be parsed into a friendly filter");
	}

	[Test]
	[Category("Unit")]
	[Description("Throws InvalidOperationException when an envelope item carries an unknown filterType.")]
	public void Decompile_Should_Throw_When_FilterType_Is_Unknown() {
		// Arrange
		string envelope =
			"""{ "logicalOperation": 0, "items": { "Filter_0": { "filterType": 99 } } }""";

		// Act
		Action act = () => FullToSimpleFilterConverter.Decompile(envelope);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "an unsupported filterType has no friendly-filter representation");
	}

	private static JsonNode RoundTrip(
		StaticFilterGroup group,
		string rootSchemaName,
		IFilterSchemaProvider schema,
		ILookupValueResolver? resolver = null,
		Func<DateTimeOffset>? now = null) {
		SimpleToFullFilterConverter builder = new(schema, resolver, now);
		string envelope = builder.Build(group, rootSchemaName);
		return FullToSimpleFilterConverter.Decompile(envelope);
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
