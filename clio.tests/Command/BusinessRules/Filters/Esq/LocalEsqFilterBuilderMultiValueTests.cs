using System;
using System.Collections.Generic;
using System.Linq;
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
public sealed class LocalEsqFilterBuilderMultiValueTests {

	private const int LookupDataValueType = 10;
	private const int TextDataValueType = 1;

	[Test]
	[Category("Unit")]
	[Description("EQUAL with a JSON array of GUIDs on a Lookup column emits InFilter with multiple parameters (IN semantics). Use case: Source IN [GoogleId, FacebookId].")]
	public void Build_Should_Emit_InFilter_With_Multiple_Parameters() {
		Guid id1 = Guid.NewGuid();
		Guid id2 = Guid.NewGuid();
		IFilterSchemaProvider provider = StubProvider("Lead", new[] {
			LookupColumn("Source", "LeadSource")
		});
		LocalEsqFilterBuilder builder = new(provider);
		string arrayJson = $"[\"{id1}\",\"{id2}\"]";
		StaticFilterGroup filter = new("AND",
			[new StaticFilterLeaf("Source", "EQUAL", JsonElement(arrayJson))],
			[]);

		string envelope = builder.ConvertToEsqFilter("Lead", filter);

		using JsonDocument doc = JsonDocument.Parse(envelope);
		JsonElement leaf = doc.RootElement.GetProperty("items").GetProperty("Filter_0");
		leaf.GetProperty("filterType").GetInt32().Should().Be(4);
		leaf.GetProperty("comparisonType").GetInt32().Should().Be(3,
			because: "single EQUAL token still emitted; platform treats multi-parameter InFilter as IN");
		JsonElement rightExpressions = leaf.GetProperty("rightExpressions");
		rightExpressions.GetArrayLength().Should().Be(2);
		HashSet<Guid> emitted = rightExpressions.EnumerateArray()
			.Select(p => p.GetProperty("parameter").GetProperty("value").GetProperty("value").GetGuid())
			.ToHashSet();
		emitted.Should().BeEquivalentTo(new[] { id1, id2 });
	}

	[Test]
	[Category("Unit")]
	[Description("NOT_EQUAL with a JSON array of GUIDs on a Lookup column is also accepted (NOT IN semantics).")]
	public void Build_Should_Emit_InFilter_With_NotEqual_Comparison() {
		IFilterSchemaProvider provider = StubProvider("Account", new[] {
			LookupColumn("Industry", "Industry")
		});
		LocalEsqFilterBuilder builder = new(provider);
		Guid a = Guid.NewGuid();
		Guid b = Guid.NewGuid();
		StaticFilterGroup filter = new("AND",
			[new StaticFilterLeaf("Industry", "NOT_EQUAL", JsonElement($"[\"{a}\",\"{b}\"]"))],
			[]);

		string envelope = builder.ConvertToEsqFilter("Account", filter);

		using JsonDocument doc = JsonDocument.Parse(envelope);
		JsonElement leaf = doc.RootElement.GetProperty("items").GetProperty("Filter_0");
		leaf.GetProperty("comparisonType").GetInt32().Should().Be(4,
			because: "FilterComparisonType.NotEqual = 4");
		leaf.GetProperty("rightExpressions").GetArrayLength().Should().Be(2);
	}

	[Test]
	[Category("Unit")]
	[Description("Empty array on a Lookup column raises filter.lookup-value-not-guid (no IN-of-nothing).")]
	public void Build_Should_Reject_Empty_Array() {
		IFilterSchemaProvider provider = StubProvider("Lead", new[] {
			LookupColumn("Source", "LeadSource")
		});
		LocalEsqFilterBuilder builder = new(provider);
		StaticFilterGroup filter = new("AND",
			[new StaticFilterLeaf("Source", "EQUAL", JsonElement("[]"))],
			[]);

		Action act = () => builder.ConvertToEsqFilter("Lead", filter);

		act.Should().Throw<BusinessRuleFilterException>()
			.Where(ex => ex.ErrorCode == BusinessRuleFilterErrorCodes.LookupValueNotGuid)
			.WithMessage("*non-empty array*");
	}

	[Test]
	[Category("Unit")]
	[Description("Schema-aware validator rejects an array element that is not a GUID string (catches partial-bad lists before converter runs).")]
	public void SchemaAware_Should_Reject_Array_With_Non_Guid_Element() {
		IFilterSchemaProvider provider = StubProvider("Lead", new[] {
			LookupColumn("Source", "LeadSource")
		});
		Guid validId = Guid.NewGuid();
		StaticFilterGroup filter = new("AND",
			[new StaticFilterLeaf("Source", "EQUAL", JsonElement($"[\"{validId}\",\"NotAGuid\"]"))],
			[]);

		Action act = () => new SchemaAwareFilterValidator(provider)
			.Validate(filter, "Lead", "rule.actions[*].filter");

		act.Should().Throw<BusinessRuleFilterException>()
			.Where(ex => ex.ErrorCode == BusinessRuleFilterErrorCodes.LookupValueNotGuid);
	}

	[Test]
	[Category("Unit")]
	[Description("Array value on a non-Lookup column is rejected with filter.value-shape (arrays are only meaningful for IN-style Lookup comparisons).")]
	public void SchemaAware_Should_Reject_Array_On_Non_Lookup_Column() {
		IFilterSchemaProvider provider = StubProvider("Contact", new[] {
			Column("Name", TextDataValueType)
		});
		StaticFilterGroup filter = new("AND",
			[new StaticFilterLeaf("Name", "EQUAL", JsonElement("[\"A\",\"B\"]"))],
			[]);

		Action act = () => new SchemaAwareFilterValidator(provider)
			.Validate(filter, "Contact", "rule.actions[*].filter");

		act.Should().Throw<BusinessRuleFilterException>()
			.Where(ex => ex.ErrorCode == BusinessRuleFilterErrorCodes.ValueShape)
			.WithMessage("*Array values are only supported for Lookup columns*");
	}

	[Test]
	[Category("Unit")]
	[Description("Array values are rejected with comparisons other than EQUAL/NOT_EQUAL (no IN semantics for GREATER/CONTAIN/etc.).")]
	public void SchemaAware_Should_Reject_Array_With_Non_Equality_Comparison() {
		Guid id = Guid.NewGuid();
		IFilterSchemaProvider provider = StubProvider("Lead", new[] {
			LookupColumn("Source", "LeadSource")
		});
		StaticFilterGroup filter = new("AND",
			[new StaticFilterLeaf("Source", "CONTAIN", JsonElement($"[\"{id}\"]"))],
			[]);

		Action act = () => new SchemaAwareFilterValidator(provider)
			.Validate(filter, "Lead", "rule.actions[*].filter");

		act.Should().Throw<BusinessRuleFilterException>()
			.Where(ex => ex.ErrorCode == BusinessRuleFilterErrorCodes.ComparisonNotSupportedForDatatype)
			.WithMessage("*Array values on Lookup columns are only supported with EQUAL*");
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
