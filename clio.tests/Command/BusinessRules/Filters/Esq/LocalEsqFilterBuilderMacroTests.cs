using System;
using System.Collections.Generic;
using System.Text.Json;
using Clio.Command.BusinessRules.Filters;
using Clio.Command.BusinessRules.Filters.Esq;
using Clio.Command.EntitySchemaDesigner;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.BusinessRules.Filters.Esq;

[TestFixture]
[Property("Module", "Command.BusinessRules.Filters.Esq")]
public sealed class LocalEsqFilterBuilderMacroTests {

	private const int DateTimeDataValueType = 7;
	private const int DateDataValueType = 8;
	private const int TextDataValueType = 1;

	[Test]
	[Category("Unit")]
	[Description("Simple macro PREVIOUS_WEEK emits a Macros function expression on the right with MacrosType=PreviousWeek (6).")]
	public void Build_Should_Emit_Macros_Function_For_PreviousWeek() {
		IFilterSchemaProvider provider = StubProvider("Order", new[] {
			Column("CreatedOn", DateTimeDataValueType)
		});
		LocalEsqFilterBuilder builder = new(provider);
		StaticFilterGroup filter = new("AND",
			[new StaticFilterLeaf("CreatedOn", "EQUAL", JsonElement("\"PREVIOUS_WEEK\""))],
			[]);

		string envelope = builder.ConvertToEsqFilter("Order", filter);

		using JsonDocument doc = JsonDocument.Parse(envelope);
		JsonElement right = doc.RootElement.GetProperty("items").GetProperty("Filter_0")
			.GetProperty("rightExpression");
		right.GetProperty("expressionType").GetInt32().Should().Be(1,
			because: "EntitySchemaQueryExpressionType.Function = 1");
		right.GetProperty("functionType").GetInt32().Should().Be(1,
			because: "FunctionType.Macros = 1");
		right.GetProperty("macrosType").GetInt32().Should().Be(6,
			because: "EntitySchemaQueryMacrosType.PreviousWeek = 6");
		right.GetProperty("className").GetString().Should().Be("Terrasoft.FunctionExpression");
	}

	[Test]
	[Category("Unit")]
	[Description("ANNIVERSARY_TODAY (with or without parens) emits MacrosType=DayOfYearToday (37).")]
	public void Build_Should_Emit_AnniversaryToday() {
		IFilterSchemaProvider provider = StubProvider("Contact", new[] {
			Column("BirthDate", DateDataValueType)
		});
		LocalEsqFilterBuilder builder = new(provider);
		StaticFilterGroup filter = new("AND",
			[new StaticFilterLeaf("BirthDate", "EQUAL", JsonElement("\"ANNIVERSARY_TODAY()\""))],
			[]);

		string envelope = builder.ConvertToEsqFilter("Contact", filter);

		using JsonDocument doc = JsonDocument.Parse(envelope);
		doc.RootElement.GetProperty("items").GetProperty("Filter_0")
			.GetProperty("rightExpression").GetProperty("macrosType").GetInt32().Should().Be(37);
	}

	[Test]
	[Category("Unit")]
	[Description("WITHIN_PREV_DAYS(7) emits a parameterized Macros function with PreviousNDays + integer parameter 7.")]
	public void Build_Should_Emit_Parameterized_Macro() {
		IFilterSchemaProvider provider = StubProvider("Order", new[] {
			Column("CreatedOn", DateTimeDataValueType)
		});
		LocalEsqFilterBuilder builder = new(provider);
		StaticFilterGroup filter = new("AND",
			[new StaticFilterLeaf("CreatedOn", "EQUAL", JsonElement("\"WITHIN_PREV_DAYS(7)\""))],
			[]);

		string envelope = builder.ConvertToEsqFilter("Order", filter);

		using JsonDocument doc = JsonDocument.Parse(envelope);
		JsonElement right = doc.RootElement.GetProperty("items").GetProperty("Filter_0")
			.GetProperty("rightExpression");
		right.GetProperty("macrosType").GetInt32().Should().Be(25,
			because: "EntitySchemaQueryMacrosType.PreviousNDays = 25");
		right.GetProperty("functionArgument").GetProperty("parameter")
			.GetProperty("value").GetInt32().Should().Be(7);
		right.GetProperty("functionArgument").GetProperty("parameter")
			.GetProperty("dataValueType").GetInt32().Should().Be(4,
				because: "Integer DataValueType = 4");
	}

	[Test]
	[Category("Unit")]
	[Description("DAY_OF_WEEK(2) builds a DatePart filter: left expression is DatePart function on the column, right is the integer parameter, comparisonType is forced to Equal.")]
	public void Build_Should_Emit_DatePart_For_DayOfWeek() {
		IFilterSchemaProvider provider = StubProvider("Order", new[] {
			Column("CreatedOn", DateTimeDataValueType)
		});
		LocalEsqFilterBuilder builder = new(provider);
		StaticFilterGroup filter = new("AND",
			[new StaticFilterLeaf("CreatedOn", "EQUAL", JsonElement("\"DAY_OF_WEEK(2)\""))],
			[]);

		string envelope = builder.ConvertToEsqFilter("Order", filter);

		using JsonDocument doc = JsonDocument.Parse(envelope);
		JsonElement leaf = doc.RootElement.GetProperty("items").GetProperty("Filter_0");
		leaf.GetProperty("comparisonType").GetInt32().Should().Be(3,
			because: "FilterComparisonType.Equal = 3 for DatePart filters");
		JsonElement left = leaf.GetProperty("leftExpression");
		left.GetProperty("expressionType").GetInt32().Should().Be(1,
			because: "EntitySchemaQueryExpressionType.Function = 1");
		left.GetProperty("functionType").GetInt32().Should().Be(3,
			because: "FunctionType.DatePart = 3");
		left.GetProperty("datePartType").GetInt32().Should().Be(5,
			because: "DatePart.Weekday = 5");
		left.GetProperty("functionArgument").GetProperty("columnPath").GetString().Should().Be("CreatedOn");
		leaf.GetProperty("rightExpression").GetProperty("parameter")
			.GetProperty("value").GetInt32().Should().Be(2);
	}

	[Test]
	[Category("Unit")]
	[Description("EXACT_YEAR(2025) uses DatePart.Year (4).")]
	public void Build_Should_Emit_DatePart_For_ExactYear() {
		IFilterSchemaProvider provider = StubProvider("Order", new[] {
			Column("CreatedOn", DateTimeDataValueType)
		});
		LocalEsqFilterBuilder builder = new(provider);
		StaticFilterGroup filter = new("AND",
			[new StaticFilterLeaf("CreatedOn", "EQUAL", JsonElement("\"EXACT_YEAR(2025)\""))],
			[]);

		string envelope = builder.ConvertToEsqFilter("Order", filter);

		using JsonDocument doc = JsonDocument.Parse(envelope);
		doc.RootElement.GetProperty("items").GetProperty("Filter_0")
			.GetProperty("leftExpression").GetProperty("datePartType").GetInt32().Should().Be(4);
	}

	[Test]
	[Category("Unit")]
	[Description("ANNIVERSARY_WITHIN_NEXTDAYS(30) maps to NextNDaysOfYear (39) with the integer parameter.")]
	public void Build_Should_Emit_Anniversary_Within_NextDays() {
		IFilterSchemaProvider provider = StubProvider("Contact", new[] {
			Column("BirthDate", DateDataValueType)
		});
		LocalEsqFilterBuilder builder = new(provider);
		StaticFilterGroup filter = new("AND",
			[new StaticFilterLeaf("BirthDate", "EQUAL", JsonElement("\"ANNIVERSARY_WITHIN_NEXTDAYS(30)\""))],
			[]);

		string envelope = builder.ConvertToEsqFilter("Contact", filter);

		using JsonDocument doc = JsonDocument.Parse(envelope);
		JsonElement right = doc.RootElement.GetProperty("items").GetProperty("Filter_0")
			.GetProperty("rightExpression");
		right.GetProperty("macrosType").GetInt32().Should().Be(39);
		right.GetProperty("functionArgument").GetProperty("parameter")
			.GetProperty("value").GetInt32().Should().Be(30);
	}

	[Test]
	[Category("Unit")]
	[Description("CURRENT_QUARTER with empty parens normalizes to MacrosType=CurrentQuarter (13).")]
	public void Build_Should_Tolerate_Empty_Parens_On_Simple_Macro() {
		IFilterSchemaProvider provider = StubProvider("Order", new[] {
			Column("CreatedOn", DateTimeDataValueType)
		});
		LocalEsqFilterBuilder builder = new(provider);
		StaticFilterGroup filter = new("AND",
			[new StaticFilterLeaf("CreatedOn", "EQUAL", JsonElement("\"CURRENT_QUARTER()\""))],
			[]);

		string envelope = builder.ConvertToEsqFilter("Order", filter);

		using JsonDocument doc = JsonDocument.Parse(envelope);
		doc.RootElement.GetProperty("items").GetProperty("Filter_0")
			.GetProperty("rightExpression").GetProperty("macrosType").GetInt32().Should().Be(13);
	}

	[Test]
	[Category("Unit")]
	[Description("Macro values on non-temporal columns fall through to the regular text path; schema-aware validator no longer treats them as macros.")]
	public void Build_Should_Not_Treat_Macro_String_On_Text_Column_As_Macro() {
		IFilterSchemaProvider provider = StubProvider("Account", new[] {
			Column("Name", TextDataValueType)
		});
		LocalEsqFilterBuilder builder = new(provider);
		// "PREVIOUS_WEEK" passed on a Text column is interpreted as a literal text comparison,
		// not a macro: macro detection runs only when the column is Date / DateTime / Time.
		StaticFilterGroup filter = new("AND",
			[new StaticFilterLeaf("Name", "EQUAL", JsonElement("\"PREVIOUS_WEEK\""))],
			[]);

		string envelope = builder.ConvertToEsqFilter("Account", filter);

		using JsonDocument doc = JsonDocument.Parse(envelope);
		JsonElement leaf = doc.RootElement.GetProperty("items").GetProperty("Filter_0");
		leaf.GetProperty("filterType").GetInt32().Should().Be(1,
			because: "regular CompareFilter, not a macro");
		leaf.GetProperty("rightExpression").GetProperty("parameter").GetProperty("value").GetString()
			.Should().Be("PREVIOUS_WEEK");
	}

	[Test]
	[Category("Unit")]
	[Description("Macro string on a Date column with a non-supported comparison (CONTAIN) is rejected by the macro builder with a clear message.")]
	public void Build_Should_Reject_Unsupported_Comparison_With_Macro() {
		IFilterSchemaProvider provider = StubProvider("Order", new[] {
			Column("CreatedOn", DateTimeDataValueType)
		});
		LocalEsqFilterBuilder builder = new(provider);
		StaticFilterGroup filter = new("AND",
			[new StaticFilterLeaf("CreatedOn", "CONTAIN", JsonElement("\"PREVIOUS_WEEK\""))],
			[]);

		Action act = () => builder.ConvertToEsqFilter("Order", filter);

		// SchemaAwareFilterValidator rejects the CONTAIN on a DateTime column with
		// comparison-not-supported-for-datatype before macro builder runs.
		act.Should().Throw<BusinessRuleFilterException>()
			.Where(ex => ex.ErrorCode == BusinessRuleFilterErrorCodes.ComparisonNotSupportedForDatatype);
	}

	private static IFilterSchemaProvider StubProvider(string schemaName, IReadOnlyList<EntitySchemaColumnDto> columns) {
		IFilterSchemaProvider provider = Substitute.For<IFilterSchemaProvider>();
		Dictionary<string, EntitySchemaColumnDto> map = new(StringComparer.Ordinal);
		foreach (EntitySchemaColumnDto column in columns) {
			map[column.Name] = column;
		}
		provider.GetSchemaColumns(schemaName).Returns(map);
		return provider;
	}

	private static EntitySchemaColumnDto Column(string name, int dataValueType) =>
		new() { Name = name, DataValueType = dataValueType };

	private static JsonElement JsonElement(string raw) =>
		JsonDocument.Parse(raw).RootElement.Clone();
}
