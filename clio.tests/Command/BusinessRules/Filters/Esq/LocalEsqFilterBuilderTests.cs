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
public sealed class LocalEsqFilterBuilderTests {

	private const int LookupDataValueType = 10;
	private const int TextDataValueType = 1;
	private const int BooleanDataValueType = 12;
	private const int IntegerDataValueType = 4;
	private const int DateTimeDataValueType = 7;

	[Test]
	[Category("Unit")]
	[Description("Top-level FilterGroup envelope carries rootSchemaName, filterType=6, className 'Terrasoft.FilterGroup' and the requested logicalOperation.")]
	public void Build_Should_Emit_Top_Level_FilterGroup_Envelope() {
		IFilterSchemaProvider provider = StubProvider("Country", new[] {
			Column("Name", TextDataValueType)
		});
		LocalEsqFilterBuilder builder = new(provider);
		StaticFilterGroup filter = new("AND",
			[new StaticFilterLeaf("Name", "EQUAL", JsonElement("\"X\""))],
			[]);

		string envelope = builder.ConvertToEsqFilter("Country", filter);

		using JsonDocument doc = JsonDocument.Parse(envelope);
		JsonElement root = doc.RootElement;
		root.GetProperty("rootSchemaName").GetString().Should().Be("Country");
		root.GetProperty("filterType").GetInt32().Should().Be(6,
			because: "Terrasoft.Nui.ServiceModel.DataContract.FilterType.FilterGroup = 6");
		root.GetProperty("className").GetString().Should().Be("Terrasoft.FilterGroup");
		root.GetProperty("logicalOperation").GetInt32().Should().Be(0,
			because: "Terrasoft.Common.LogicalOperationStrict.And = 0");
		root.GetProperty("items").GetProperty("Filter_0").Should().NotBeNull();
	}

	[Test]
	[Category("Unit")]
	[Description("EQUAL on a Text column emits filterType=1 (CompareFilter) with comparisonType=3 (Equal) and a Text parameter.")]
	public void Build_Should_Emit_CompareFilter_For_Text_Equal() {
		IFilterSchemaProvider provider = StubProvider("Country", new[] {
			Column("Name", TextDataValueType)
		});
		LocalEsqFilterBuilder builder = new(provider);
		StaticFilterGroup filter = new("AND",
			[new StaticFilterLeaf("Name", "EQUAL", JsonElement("\"Ukraine\""))],
			[]);

		string envelope = builder.ConvertToEsqFilter("Country", filter);

		using JsonDocument doc = JsonDocument.Parse(envelope);
		JsonElement leaf = doc.RootElement.GetProperty("items").GetProperty("Filter_0");
		leaf.GetProperty("filterType").GetInt32().Should().Be(1);
		leaf.GetProperty("comparisonType").GetInt32().Should().Be(3);
		leaf.GetProperty("className").GetString().Should().Be("Terrasoft.CompareFilter");
		leaf.GetProperty("leftExpression").GetProperty("columnPath").GetString().Should().Be("Name");
		leaf.GetProperty("leftExpression").GetProperty("expressionType").GetInt32().Should().Be(0,
			because: "EntitySchemaQueryExpressionType.SchemaColumn = 0");
		JsonElement right = leaf.GetProperty("rightExpression");
		right.GetProperty("expressionType").GetInt32().Should().Be(2,
			because: "EntitySchemaQueryExpressionType.Parameter = 2");
		right.GetProperty("parameter").GetProperty("dataValueType").GetInt32().Should().Be(1,
			because: "DataValueType.Text = 1");
		right.GetProperty("parameter").GetProperty("value").GetString().Should().Be("Ukraine");
	}

	[Test]
	[Category("Unit")]
	[Description("IS_NULL emits filterType=2 (IsNullFilter), comparisonType=1 (IsNull) and no right expression.")]
	public void Build_Should_Emit_IsNullFilter_For_Unary_IsNull() {
		IFilterSchemaProvider provider = StubProvider("Country", new[] {
			Column("Name", TextDataValueType)
		});
		LocalEsqFilterBuilder builder = new(provider);
		StaticFilterGroup filter = new("AND",
			[new StaticFilterLeaf("Name", "IS_NULL", null)],
			[]);

		string envelope = builder.ConvertToEsqFilter("Country", filter);

		using JsonDocument doc = JsonDocument.Parse(envelope);
		JsonElement leaf = doc.RootElement.GetProperty("items").GetProperty("Filter_0");
		leaf.GetProperty("filterType").GetInt32().Should().Be(2);
		leaf.GetProperty("comparisonType").GetInt32().Should().Be(1);
		leaf.GetProperty("isNull").GetBoolean().Should().BeTrue();
		leaf.GetProperty("className").GetString().Should().Be("Terrasoft.IsNullFilter");
		leaf.TryGetProperty("rightExpression", out _).Should().BeFalse();
	}

	[Test]
	[Category("Unit")]
	[Description("EQUAL on a Lookup column emits filterType=4 (InFilter) with a Lookup parameter carrying the GUID value and reference schema name.")]
	public void Build_Should_Emit_InFilter_For_Lookup_Equal() {
		Guid lookupId = Guid.NewGuid();
		IFilterSchemaProvider provider = StubProvider("Order", new[] {
			LookupColumn("Customer", "Contact")
		});
		LocalEsqFilterBuilder builder = new(provider);
		StaticFilterGroup filter = new("AND",
			[new StaticFilterLeaf("Customer", "EQUAL", JsonElement($"\"{lookupId}\""))],
			[]);

		string envelope = builder.ConvertToEsqFilter("Order", filter);

		using JsonDocument doc = JsonDocument.Parse(envelope);
		JsonElement leaf = doc.RootElement.GetProperty("items").GetProperty("Filter_0");
		leaf.GetProperty("filterType").GetInt32().Should().Be(4);
		leaf.GetProperty("comparisonType").GetInt32().Should().Be(3);
		leaf.GetProperty("className").GetString().Should().Be("Terrasoft.InFilter");
		leaf.GetProperty("referenceSchemaName").GetString().Should().Be("Contact");
		leaf.GetProperty("dataValueType").GetInt32().Should().Be(10);
		JsonElement parameter = leaf.GetProperty("rightExpressions")[0].GetProperty("parameter");
		parameter.GetProperty("dataValueType").GetInt32().Should().Be(10);
		parameter.GetProperty("value").GetProperty("value").GetGuid().Should().Be(lookupId);
	}

	[Test]
	[Category("Unit")]
	[Description("Non-GUID value on a Lookup column raises filter.lookup-value-not-guid (clio rejects display-name values).")]
	public void Build_Should_Reject_Non_Guid_On_Lookup() {
		IFilterSchemaProvider provider = StubProvider("Order", new[] {
			LookupColumn("Customer", "Contact")
		});
		LocalEsqFilterBuilder builder = new(provider);
		StaticFilterGroup filter = new("AND",
			[new StaticFilterLeaf("Customer", "EQUAL", JsonElement("\"Doe\""))],
			[]);

		Action act = () => builder.ConvertToEsqFilter("Order", filter);

		act.Should().Throw<BusinessRuleFilterException>()
			.Where(ex => ex.ErrorCode == BusinessRuleFilterErrorCodes.LookupValueNotGuid);
	}

	[Test]
	[Category("Unit")]
	[Description("Boolean true emits a Boolean parameter (dataValueType=12).")]
	public void Build_Should_Emit_Boolean_Parameter() {
		IFilterSchemaProvider provider = StubProvider("Contact", new[] {
			Column("Active", BooleanDataValueType)
		});
		LocalEsqFilterBuilder builder = new(provider);
		StaticFilterGroup filter = new("AND",
			[new StaticFilterLeaf("Active", "EQUAL", JsonElement("true"))],
			[]);

		string envelope = builder.ConvertToEsqFilter("Contact", filter);

		using JsonDocument doc = JsonDocument.Parse(envelope);
		JsonElement parameter = doc.RootElement.GetProperty("items")
			.GetProperty("Filter_0").GetProperty("rightExpression").GetProperty("parameter");
		parameter.GetProperty("dataValueType").GetInt32().Should().Be(12);
		parameter.GetProperty("value").GetBoolean().Should().BeTrue();
	}

	[Test]
	[Category("Unit")]
	[Description("Numeric value on an Integer column emits dataValueType=4 with the original number preserved.")]
	public void Build_Should_Emit_Integer_Parameter() {
		IFilterSchemaProvider provider = StubProvider("Order", new[] {
			Column("Total", IntegerDataValueType)
		});
		LocalEsqFilterBuilder builder = new(provider);
		StaticFilterGroup filter = new("AND",
			[new StaticFilterLeaf("Total", "GREATER", JsonElement("100"))],
			[]);

		string envelope = builder.ConvertToEsqFilter("Order", filter);

		using JsonDocument doc = JsonDocument.Parse(envelope);
		JsonElement leaf = doc.RootElement.GetProperty("items").GetProperty("Filter_0");
		leaf.GetProperty("comparisonType").GetInt32().Should().Be(7,
			because: "FilterComparisonType.Greater = 7");
		JsonElement parameter = leaf.GetProperty("rightExpression").GetProperty("parameter");
		parameter.GetProperty("dataValueType").GetInt32().Should().Be(4);
		parameter.GetProperty("value").GetInt64().Should().Be(100);
	}

	[Test]
	[Category("Unit")]
	[Description("DateTime value emits trimDateTimeParameterToDate=true and a SerializableDateParameter with dateValue field.")]
	public void Build_Should_Emit_DateTime_Parameter_With_TrimFlag() {
		IFilterSchemaProvider provider = StubProvider("Order", new[] {
			Column("PaidOn", DateTimeDataValueType)
		});
		LocalEsqFilterBuilder builder = new(provider);
		StaticFilterGroup filter = new("AND",
			[new StaticFilterLeaf("PaidOn", "GREATER", JsonElement("\"2026-01-01T00:00:00Z\""))],
			[]);

		string envelope = builder.ConvertToEsqFilter("Order", filter);

		using JsonDocument doc = JsonDocument.Parse(envelope);
		JsonElement leaf = doc.RootElement.GetProperty("items").GetProperty("Filter_0");
		leaf.GetProperty("trimDateTimeParameterToDate").GetBoolean().Should().BeTrue();
		JsonElement parameter = leaf.GetProperty("rightExpression").GetProperty("parameter");
		parameter.GetProperty("dataValueType").GetInt32().Should().Be(7);
		parameter.GetProperty("dateValue").GetString().Should().Be("2026-01-01T00:00:00Z");
	}

	[Test]
	[Category("Unit")]
	[Description("Forward reference path (Country.Region.Name) resolves through Lookup chain; the leaf column path is preserved in the envelope.")]
	public void Build_Should_Walk_Forward_Reference_Path() {
		IFilterSchemaProvider provider = Substitute.For<IFilterSchemaProvider>();
		provider.GetSchemaColumns("Country").Returns(new Dictionary<string, EntitySchemaColumnDto>(StringComparer.Ordinal) {
			["Region"] = LookupColumn("Region", "Region")
		});
		provider.GetSchemaColumns("Region").Returns(new Dictionary<string, EntitySchemaColumnDto>(StringComparer.Ordinal) {
			["Name"] = Column("Name", TextDataValueType)
		});
		LocalEsqFilterBuilder builder = new(provider);
		StaticFilterGroup filter = new("AND",
			[new StaticFilterLeaf("Region.Name", "EQUAL", JsonElement("\"East\""))],
			[]);

		string envelope = builder.ConvertToEsqFilter("Country", filter);

		using JsonDocument doc = JsonDocument.Parse(envelope);
		JsonElement leaf = doc.RootElement.GetProperty("items").GetProperty("Filter_0");
		leaf.GetProperty("leftExpression").GetProperty("columnPath").GetString().Should().Be("Region.Name");
		leaf.GetProperty("filterType").GetInt32().Should().Be(1);
	}

	[Test]
	[Category("Unit")]
	[Description("Backward reference [Order:Customer] emits filterType=5 (Exists) with className Terrasoft.ExistsFilter, isAggregative=true, and nested subFilters group.")]
	public void Build_Should_Emit_ExistsFilter_For_Backward_Reference() {
		IFilterSchemaProvider provider = Substitute.For<IFilterSchemaProvider>();
		provider.GetSchemaColumns("Contact").Returns(new Dictionary<string, EntitySchemaColumnDto>());
		provider.GetSchemaColumns("Order").Returns(new Dictionary<string, EntitySchemaColumnDto>(StringComparer.Ordinal) {
			["Total"] = Column("Total", IntegerDataValueType)
		});
		LocalEsqFilterBuilder builder = new(provider);
		StaticFilterGroup nested = new("AND",
			[new StaticFilterLeaf("Total", "GREATER", JsonElement("100"))],
			[]);
		StaticFilterGroup filter = new("AND", [],
			[new StaticFilterBackwardReference("[Order:Customer]", nested)]);

		string envelope = builder.ConvertToEsqFilter("Contact", filter);

		using JsonDocument doc = JsonDocument.Parse(envelope);
		JsonElement backward = doc.RootElement.GetProperty("items").GetProperty("BackwardReferenceFilter_0");
		backward.GetProperty("filterType").GetInt32().Should().Be(5);
		backward.GetProperty("comparisonType").GetInt32().Should().Be(15,
			because: "FilterComparisonType.Exists = 15");
		backward.GetProperty("isAggregative").GetBoolean().Should().BeTrue();
		backward.GetProperty("className").GetString().Should().Be("Terrasoft.ExistsFilter");
		backward.GetProperty("subFilters").GetProperty("filterType").GetInt32().Should().Be(6);
		backward.GetProperty("subFilters").GetProperty("rootSchemaName").GetString().Should().Be("Order");
	}

	[Test]
	[Category("Unit")]
	[Description("OR group serializes logicalOperation=1 (Terrasoft.Common.LogicalOperationStrict.Or).")]
	public void Build_Should_Emit_Or_LogicalOperation() {
		IFilterSchemaProvider provider = StubProvider("Country", new[] {
			Column("Name", TextDataValueType)
		});
		LocalEsqFilterBuilder builder = new(provider);
		StaticFilterGroup filter = new("OR",
			[new StaticFilterLeaf("Name", "EQUAL", JsonElement("\"A\""))],
			[]);

		string envelope = builder.ConvertToEsqFilter("Country", filter);

		using JsonDocument doc = JsonDocument.Parse(envelope);
		doc.RootElement.GetProperty("logicalOperation").GetInt32().Should().Be(1);
	}

	[Test]
	[Category("Unit")]
	[Description("Unknown column path on the root schema raises filter.path-unknown — catches the UsrCountry1 reproducer locally.")]
	public void Build_Should_Reject_Unknown_Column_Path() {
		IFilterSchemaProvider provider = StubProvider("Country", new[] {
			Column("Name", TextDataValueType),
			Column("Country1", TextDataValueType)
		});
		LocalEsqFilterBuilder builder = new(provider);
		StaticFilterGroup filter = new("AND",
			[new StaticFilterLeaf("UsrCountry1", "EQUAL", JsonElement("\"x\""))],
			[]);

		Action act = () => builder.ConvertToEsqFilter("Country", filter);

		act.Should().Throw<BusinessRuleFilterException>()
			.Where(ex => ex.ErrorCode == BusinessRuleFilterErrorCodes.PathUnknown)
			.WithMessage("*UsrCountry1*not found on schema 'Country'*");
	}

	[Test]
	[Category("Unit")]
	[Description("NormalizeColumnPath strips trailing 'Id' from each path segment to match the platform's reference path storage shape.")]
	public void Normalize_Should_Strip_Trailing_Id_From_Each_Segment() {
		LocalEsqFilterConverter.NormalizeColumnPath("CountryId.RegionId.Name").Should().Be("Country.Region.Name");
		LocalEsqFilterConverter.NormalizeColumnPath("Id").Should().Be("Id",
			because: "single-segment 'Id' is shorter than the 2-char threshold and is preserved as-is");
		LocalEsqFilterConverter.NormalizeColumnPath("[Order:CustomerId]").Should().Be("[Order:Customer]");
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

	private static EntitySchemaColumnDto LookupColumn(string name, string referenceSchemaName) =>
		new() {
			Name = name,
			DataValueType = LookupDataValueType,
			ReferenceSchema = new EntityDesignSchemaDto { Name = referenceSchemaName }
		};

	private static JsonElement JsonElement(string raw) =>
		JsonDocument.Parse(raw).RootElement.Clone();
}
