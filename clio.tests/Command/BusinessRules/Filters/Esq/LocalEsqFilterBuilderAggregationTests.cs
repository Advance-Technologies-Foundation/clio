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
public sealed class LocalEsqFilterBuilderAggregationTests {

	private const int LookupDataValueType = 10;
	private const int TextDataValueType = 1;
	private const int IntegerDataValueType = 4;
	private const int FloatDataValueType = 5;

	[Test]
	[Category("Unit")]
	[Description("Explicit NOT_EXISTS aggregation type emits Terrasoft.ExistsFilter with comparisonType=16 (NotExists).")]
	public void Build_Should_Emit_NotExistsFilter_For_Explicit_NotExists() {
		IFilterSchemaProvider provider = ProviderForChild("Contact", "Order",
			[Column("Total", IntegerDataValueType)]);
		LocalEsqFilterBuilder builder = new(provider);
		StaticFilterGroup root = new("AND", [], [
			new StaticFilterBackwardReference(
				"[Order:Customer]",
				new StaticFilterGroup("AND", [], []),
				AggregationType: "NOT_EXISTS")
		]);

		string envelope = builder.ConvertToEsqFilter("Contact", root);

		using JsonDocument doc = JsonDocument.Parse(envelope);
		JsonElement backward = doc.RootElement.GetProperty("items").GetProperty("BackwardReferenceFilter_0");
		backward.GetProperty("filterType").GetInt32().Should().Be(5);
		backward.GetProperty("comparisonType").GetInt32().Should().Be(16,
			because: "FilterComparisonType.NotExists = 16");
		backward.GetProperty("className").GetString().Should().Be("Terrasoft.ExistsFilter");
	}

	[Test]
	[Category("Unit")]
	[Description("COUNT aggregation emits CompareFilter with left=AggregationQueryExpression (FunctionType=Aggregation, AggregationType=1) and a numeric right parameter.")]
	public void Build_Should_Emit_AggregationQuery_For_Count() {
		IFilterSchemaProvider provider = ProviderForChild("Contact", "Order",
			[Column("Total", IntegerDataValueType)]);
		LocalEsqFilterBuilder builder = new(provider);
		StaticFilterGroup root = new("AND", [], [
			new StaticFilterBackwardReference(
				"[Order:Customer]",
				new StaticFilterGroup("AND", [], []),
				AggregationType: "COUNT",
				ComparisonType: "GREATER_OR_EQUAL",
				AggregationValue: JsonElement("3"))
		]);

		string envelope = builder.ConvertToEsqFilter("Contact", root);

		using JsonDocument doc = JsonDocument.Parse(envelope);
		JsonElement backward = doc.RootElement.GetProperty("items").GetProperty("BackwardReferenceFilter_0");
		backward.GetProperty("filterType").GetInt32().Should().Be(1,
			because: "FilterType.CompareFilter = 1 — aggregation result is compared to value");
		backward.GetProperty("comparisonType").GetInt32().Should().Be(8,
			because: "FilterComparisonType.GreaterOrEqual = 8");
		backward.GetProperty("isAggregative").GetBoolean().Should().BeTrue();
		JsonElement left = backward.GetProperty("leftExpression");
		left.GetProperty("expressionType").GetInt32().Should().Be(3,
			because: "EntitySchemaQueryExpressionType.SubQuery = 3");
		left.GetProperty("functionType").GetInt32().Should().Be(2,
			because: "FunctionType.Aggregation = 2");
		left.GetProperty("aggregationType").GetInt32().Should().Be(1,
			because: "AggregationType.Count = 1");
		left.GetProperty("className").GetString().Should().Be("Terrasoft.AggregationQueryExpression");
		left.GetProperty("subFilters").GetProperty("filterType").GetInt32().Should().Be(6,
			because: "subFilters is a nested FilterGroup against the child schema");
		JsonElement right = backward.GetProperty("rightExpression").GetProperty("parameter");
		right.GetProperty("dataValueType").GetInt32().Should().Be(4,
			because: "COUNT result is Integer");
		right.GetProperty("value").GetInt64().Should().Be(3);
	}

	[Test]
	[Category("Unit")]
	[Description("SUM aggregation uses the explicit aggregationColumnPath on the child schema as the SubQuery columnPath; aggregationType maps to 2 (Sum) and value is Float.")]
	public void Build_Should_Emit_AggregationQuery_For_Sum() {
		IFilterSchemaProvider provider = ProviderForChild("Contact", "Order",
			[Column("Total", FloatDataValueType)]);
		LocalEsqFilterBuilder builder = new(provider);
		StaticFilterGroup root = new("AND", [], [
			new StaticFilterBackwardReference(
				"[Order:Customer]",
				new StaticFilterGroup("AND", [], []),
				AggregationType: "SUM",
				AggregationColumnPath: "Total",
				ComparisonType: "GREATER",
				AggregationValue: JsonElement("1000"))
		]);

		string envelope = builder.ConvertToEsqFilter("Contact", root);

		using JsonDocument doc = JsonDocument.Parse(envelope);
		JsonElement backward = doc.RootElement.GetProperty("items").GetProperty("BackwardReferenceFilter_0");
		JsonElement left = backward.GetProperty("leftExpression");
		left.GetProperty("aggregationType").GetInt32().Should().Be(2,
			because: "AggregationType.Sum = 2");
		left.GetProperty("columnPath").GetString().Should().Be("Total",
			because: "SUM aggregates the explicit aggregationColumnPath, not the relationship column");
		backward.GetProperty("rightExpression").GetProperty("parameter")
			.GetProperty("dataValueType").GetInt32().Should().Be(5,
				because: "SUM/AVG/MIN/MAX result materializes as Float");
	}

	[Test]
	[Category("Unit")]
	[Description("AVG maps to aggregationType=3, MIN to 4, MAX to 5.")]
	public void Build_Should_Map_All_NonCount_Aggregation_Types() {
		foreach ((string token, int expected) in new[] {
			("AVG", 3), ("MIN", 4), ("MAX", 5)
		}) {
			IFilterSchemaProvider provider = ProviderForChild("Contact", "Order",
				[Column("Total", FloatDataValueType)]);
			LocalEsqFilterBuilder builder = new(provider);
			StaticFilterGroup root = new("AND", [], [
				new StaticFilterBackwardReference(
					"[Order:Customer]",
					new StaticFilterGroup("AND", [], []),
					AggregationType: token,
					AggregationColumnPath: "Total",
					ComparisonType: "GREATER",
					AggregationValue: JsonElement("0"))
			]);

			string envelope = builder.ConvertToEsqFilter("Contact", root);
			using JsonDocument doc = JsonDocument.Parse(envelope);
			doc.RootElement.GetProperty("items")
				.GetProperty("BackwardReferenceFilter_0")
				.GetProperty("leftExpression")
				.GetProperty("aggregationType")
				.GetInt32().Should().Be(expected,
					because: $"AggregationType.{token} numeric id from Terrasoft.Common");
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Structural validator rejects an aggregationType outside the supported set.")]
	public void Validate_Should_Reject_Unknown_AggregationType() {
		StaticFilterGroup root = new("AND", [], [
			new StaticFilterBackwardReference(
				"[Order:Customer]",
				new StaticFilterGroup("AND", [], []),
				AggregationType: "MEDIAN")
		]);

		Action act = () => StaticFilterStructuralValidator.Validate(root);

		act.Should().Throw<BusinessRuleFilterException>()
			.Where(ex => ex.ErrorCode == BusinessRuleFilterErrorCodes.ComparisonUnknown)
			.WithMessage("*MEDIAN*");
	}

	[Test]
	[Category("Unit")]
	[Description("Structural validator rejects COUNT without comparisonType + aggregationValue.")]
	public void Validate_Should_Reject_Count_Without_Comparison() {
		StaticFilterGroup root = new("AND", [], [
			new StaticFilterBackwardReference(
				"[Order:Customer]",
				new StaticFilterGroup("AND", [], []),
				AggregationType: "COUNT")
		]);

		Action act = () => StaticFilterStructuralValidator.Validate(root);

		act.Should().Throw<BusinessRuleFilterException>()
			.Where(ex => ex.ErrorCode == BusinessRuleFilterErrorCodes.ComparisonUnknown);
	}

	[Test]
	[Category("Unit")]
	[Description("Structural validator rejects SUM without aggregationColumnPath.")]
	public void Validate_Should_Reject_Sum_Without_AggregationColumnPath() {
		StaticFilterGroup root = new("AND", [], [
			new StaticFilterBackwardReference(
				"[Order:Customer]",
				new StaticFilterGroup("AND", [], []),
				AggregationType: "SUM",
				ComparisonType: "GREATER",
				AggregationValue: JsonElement("100"))
		]);

		Action act = () => StaticFilterStructuralValidator.Validate(root);

		act.Should().Throw<BusinessRuleFilterException>()
			.Where(ex => ex.ErrorCode == BusinessRuleFilterErrorCodes.PathUnknown)
			.WithMessage("*aggregationColumnPath is required*SUM*");
	}

	[Test]
	[Category("Unit")]
	[Description("Structural validator rejects EXISTS with extra aggregation fields (cross-rule guard).")]
	public void Validate_Should_Reject_Exists_With_Aggregation_Value() {
		StaticFilterGroup root = new("AND", [], [
			new StaticFilterBackwardReference(
				"[Order:Customer]",
				new StaticFilterGroup("AND", [], []),
				AggregationValue: JsonElement("3"))
		]);

		Action act = () => StaticFilterStructuralValidator.Validate(root);

		act.Should().Throw<BusinessRuleFilterException>()
			.Where(ex => ex.ErrorCode == BusinessRuleFilterErrorCodes.ValueShape);
	}

	[Test]
	[Category("Unit")]
	[Description("Schema-aware validator rejects SUM aggregationColumnPath that is not numeric on the child schema.")]
	public void SchemaAware_Should_Reject_NonNumeric_AggregationColumnPath() {
		IFilterSchemaProvider provider = ProviderForChild("Contact", "Order",
			[Column("Subject", TextDataValueType)]);
		StaticFilterGroup root = new("AND", [], [
			new StaticFilterBackwardReference(
				"[Order:Customer]",
				new StaticFilterGroup("AND", [], []),
				AggregationType: "SUM",
				AggregationColumnPath: "Subject",
				ComparisonType: "GREATER",
				AggregationValue: JsonElement("0"))
		]);

		Action act = () => new SchemaAwareFilterValidator(provider)
			.Validate(root, "Contact", "rule.actions[*].filter");

		act.Should().Throw<BusinessRuleFilterException>()
			.Where(ex => ex.ErrorCode == BusinessRuleFilterErrorCodes.ComparisonNotSupportedForDatatype)
			.WithMessage("*SUM*numeric*");
	}

	[Test]
	[Category("Unit")]
	[Description("Schema-aware validator rejects SUM aggregationColumnPath that does not exist on the child schema.")]
	public void SchemaAware_Should_Reject_Unknown_AggregationColumnPath() {
		IFilterSchemaProvider provider = ProviderForChild("Contact", "Order",
			[Column("Total", IntegerDataValueType)]);
		StaticFilterGroup root = new("AND", [], [
			new StaticFilterBackwardReference(
				"[Order:Customer]",
				new StaticFilterGroup("AND", [], []),
				AggregationType: "MAX",
				AggregationColumnPath: "Revenue",
				ComparisonType: "GREATER",
				AggregationValue: JsonElement("0"))
		]);

		Action act = () => new SchemaAwareFilterValidator(provider)
			.Validate(root, "Contact", "rule.actions[*].filter");

		act.Should().Throw<BusinessRuleFilterException>()
			.Where(ex => ex.ErrorCode == BusinessRuleFilterErrorCodes.PathUnknown)
			.WithMessage("*Revenue*not found on child schema 'Order'*");
	}

	private static IFilterSchemaProvider ProviderForChild(string parentSchemaName, string childSchemaName, IReadOnlyList<EntitySchemaColumnDto> childColumns) {
		IFilterSchemaProvider provider = Substitute.For<IFilterSchemaProvider>();
		provider.GetSchemaColumns(parentSchemaName).Returns(new Dictionary<string, EntitySchemaColumnDto>());
		Dictionary<string, EntitySchemaColumnDto> childMap = new(StringComparer.Ordinal) {
			[parentSchemaName == "Contact" ? "Customer" : "Country"] = LookupColumn(
				parentSchemaName == "Contact" ? "Customer" : "Country",
				parentSchemaName)
		};
		foreach (EntitySchemaColumnDto column in childColumns) {
			childMap[column.Name] = column;
		}
		provider.GetSchemaColumns(childSchemaName).Returns(childMap);
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
