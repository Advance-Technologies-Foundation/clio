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
public sealed class LocalEsqFilterBuilderNestedGroupTests {

	private const int TextDataValueType = 1;

	[Test]
	[Category("Unit")]
	[Description("Nested logical groups serialize as Group_N items under the parent envelope; each is a self-contained FilterGroup with its own logicalOperation and items.")]
	public void Build_Should_Emit_Nested_Group_With_Its_Own_LogicalOperation() {
		IFilterSchemaProvider provider = StubProvider("Country", new[] {
			Column("Name", TextDataValueType),
			Column("Region", TextDataValueType)
		});
		LocalEsqFilterBuilder builder = new(provider);
		// (Name = "A" AND Region = "East") OR (Name = "B" AND Region = "West")
		StaticFilterGroup leftGroup = new("AND",
			[
				new StaticFilterLeaf("Name", "EQUAL", JsonElement("\"A\"")),
				new StaticFilterLeaf("Region", "EQUAL", JsonElement("\"East\""))
			], []);
		StaticFilterGroup rightGroup = new("AND",
			[
				new StaticFilterLeaf("Name", "EQUAL", JsonElement("\"B\"")),
				new StaticFilterLeaf("Region", "EQUAL", JsonElement("\"West\""))
			], []);
		StaticFilterGroup root = new("OR", [], [], [leftGroup, rightGroup]);

		string envelope = builder.ConvertToEsqFilter("Country", root);

		using JsonDocument doc = JsonDocument.Parse(envelope);
		JsonElement rootJson = doc.RootElement;
		rootJson.GetProperty("logicalOperation").GetInt32().Should().Be(1,
			because: "outer OR maps to LogicalOperationStrict.Or = 1");
		JsonElement items = rootJson.GetProperty("items");
		items.GetProperty("Group_0").GetProperty("filterType").GetInt32().Should().Be(6);
		items.GetProperty("Group_0").GetProperty("logicalOperation").GetInt32().Should().Be(0,
			because: "left AND group");
		items.GetProperty("Group_1").GetProperty("logicalOperation").GetInt32().Should().Be(0,
			because: "right AND group");
		items.GetProperty("Group_0").GetProperty("items")
			.GetProperty("Filter_1000_0").GetProperty("filterType").GetInt32().Should().Be(1);
	}

	[Test]
	[Category("Unit")]
	[Description("Nested groups inherit the parent root schema for column-path resolution; SchemaAwareFilterValidator rejects an unknown column inside a nested group with filter.path-unknown.")]
	public void SchemaAware_Should_Reject_Unknown_Column_Inside_Nested_Group() {
		IFilterSchemaProvider provider = StubProvider("Country", new[] {
			Column("Name", TextDataValueType)
		});
		StaticFilterGroup inner = new("AND",
			[new StaticFilterLeaf("UsrUnknown", "EQUAL", JsonElement("\"x\""))],
			[]);
		StaticFilterGroup root = new("AND", [], [], [inner]);

		Action act = () => new SchemaAwareFilterValidator(provider)
			.Validate(root, "Country", "rule.actions[*].filter");

		act.Should().Throw<BusinessRuleFilterException>()
			.Where(ex => ex.ErrorCode == BusinessRuleFilterErrorCodes.PathUnknown)
			.WithMessage("*UsrUnknown*not found on schema 'Country'*");
	}

	[Test]
	[Category("Unit")]
	[Description("Structural validator recurses into nested groups and rejects an unknown comparison token wherever it appears.")]
	public void Structural_Should_Reject_Unknown_Token_Inside_Nested_Group() {
		StaticFilterGroup inner = new("AND",
			[new StaticFilterLeaf("Name", "MATCHES", JsonElement("\"x\""))],
			[]);
		StaticFilterGroup root = new("AND", [], [], [inner]);

		Action act = () => StaticFilterStructuralValidator.Validate(root);

		act.Should().Throw<BusinessRuleFilterException>()
			.Where(ex => ex.ErrorCode == BusinessRuleFilterErrorCodes.ComparisonUnknown)
			.WithMessage("*MATCHES*");
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
