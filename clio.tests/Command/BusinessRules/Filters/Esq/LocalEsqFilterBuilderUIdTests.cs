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
public sealed class LocalEsqFilterBuilderUIdTests {

	private const int TextDataValueType = 1;
	private const int LookupDataValueType = 10;

	[Test]
	[Category("Unit")]
	[Description("columnPath segment as GUID UId resolves to the matching column on the root schema; the emitted envelope rewrites the GUID to the canonical Name so the platform's BVE1 runtime can read it.")]
	public void Build_Should_Resolve_Guid_ColumnPath_And_Emit_Name() {
		Guid nameColumnUId = Guid.NewGuid();
		IFilterSchemaProvider provider = StubProvider("Country", new[] {
			ColumnWithUId("Name", nameColumnUId, TextDataValueType)
		});
		LocalEsqFilterBuilder builder = new(provider);
		StaticFilterGroup filter = new("AND",
			[new StaticFilterLeaf(nameColumnUId.ToString(), "EQUAL", JsonElement("\"Ukraine\""))],
			[]);

		string envelope = builder.ConvertToEsqFilter("Country", filter);

		using JsonDocument doc = JsonDocument.Parse(envelope);
		JsonElement leaf = doc.RootElement.GetProperty("items").GetProperty("Filter_0");
		leaf.GetProperty("leftExpression").GetProperty("columnPath").GetString().Should().Be("Name",
			because: "the emitted envelope must use the canonical column Name even when the caller supplied a GUID UId");
	}

	[Test]
	[Category("Unit")]
	[Description("Forward path mixing GUID and Name segments resolves end-to-end; each GUID segment is rewritten to Name in the emitted envelope so platform path traversal works without UId mapping.")]
	public void Build_Should_Resolve_Mixed_Guid_And_Name_Segments() {
		Guid regionLookupUId = Guid.NewGuid();
		IFilterSchemaProvider provider = Substitute.For<IFilterSchemaProvider>();
		provider.GetSchemaColumns("Country").Returns(new Dictionary<string, EntitySchemaColumnDto>(StringComparer.Ordinal) {
			["Region"] = LookupColumnWithUId("Region", regionLookupUId, "Region")
		});
		provider.GetSchemaColumns("Region").Returns(new Dictionary<string, EntitySchemaColumnDto>(StringComparer.Ordinal) {
			["Name"] = ColumnWithUId("Name", Guid.NewGuid(), TextDataValueType)
		});
		LocalEsqFilterBuilder builder = new(provider);
		// columnPath uses GUID for first segment, Name for second
		string columnPath = $"{regionLookupUId}.Name";
		StaticFilterGroup filter = new("AND",
			[new StaticFilterLeaf(columnPath, "EQUAL", JsonElement("\"East\""))],
			[]);

		string envelope = builder.ConvertToEsqFilter("Country", filter);

		using JsonDocument doc = JsonDocument.Parse(envelope);
		doc.RootElement.GetProperty("items").GetProperty("Filter_0")
			.GetProperty("leftExpression").GetProperty("columnPath").GetString()
			.Should().Be("Region.Name",
				because: "GUID segment must be rewritten to its column Name in the emitted envelope");
	}

	[Test]
	[Category("Unit")]
	[Description("A GUID that does not match any column UId on the schema raises filter.path-unknown with both Name and UId mentioned.")]
	public void Build_Should_Reject_Unknown_Guid_Segment() {
		IFilterSchemaProvider provider = StubProvider("Country", new[] {
			ColumnWithUId("Name", Guid.NewGuid(), TextDataValueType)
		});
		LocalEsqFilterBuilder builder = new(provider);
		Guid unknownUId = Guid.NewGuid();
		StaticFilterGroup filter = new("AND",
			[new StaticFilterLeaf(unknownUId.ToString(), "EQUAL", JsonElement("\"x\""))],
			[]);

		Action act = () => builder.ConvertToEsqFilter("Country", filter);

		act.Should().Throw<BusinessRuleFilterException>()
			.Where(ex => ex.ErrorCode == BusinessRuleFilterErrorCodes.PathUnknown)
			.WithMessage("*looked up by Name and UId*");
	}

	[Test]
	[Category("Unit")]
	[Description("Schema-aware validator accepts the GUID segment when the UId matches a column on the schema.")]
	public void SchemaAware_Should_Accept_Guid_Segment() {
		Guid nameUId = Guid.NewGuid();
		IFilterSchemaProvider provider = StubProvider("Country", new[] {
			ColumnWithUId("Name", nameUId, TextDataValueType)
		});
		StaticFilterGroup filter = new("AND",
			[new StaticFilterLeaf(nameUId.ToString(), "EQUAL", JsonElement("\"Ukraine\""))],
			[]);

		Action act = () => new SchemaAwareFilterValidator(provider)
			.Validate(filter, "Country", "rule.actions[*].filter");

		act.Should().NotThrow();
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

	private static EntitySchemaColumnDto ColumnWithUId(string name, Guid uid, int dataValueType) =>
		new() { Name = name, UId = uid, DataValueType = dataValueType };

	private static EntitySchemaColumnDto LookupColumnWithUId(string name, Guid uid, string referenceSchemaName) =>
		new() {
			Name = name,
			UId = uid,
			DataValueType = LookupDataValueType,
			ReferenceSchema = new EntityDesignSchemaDto { Name = referenceSchemaName }
		};

	private static JsonElement JsonElement(string raw) =>
		JsonDocument.Parse(raw).RootElement.Clone();
}
