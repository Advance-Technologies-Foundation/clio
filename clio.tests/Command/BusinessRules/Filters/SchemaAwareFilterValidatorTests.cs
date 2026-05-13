using System.Collections.Generic;
using System.Text.Json;
using Clio.Command.BusinessRules.Filters;
using Clio.Command.EntitySchemaDesigner;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.BusinessRules.Filters;

[TestFixture]
[Property("Module", "Command.BusinessRules.Filters")]
public sealed class SchemaAwareFilterValidatorTests {

	private const int LookupDataValueType = 10;
	private const int TextDataValueType = 1;
	private const int BooleanDataValueType = 12;
	private const int IntegerDataValueType = 4;
	private const int DateTimeDataValueType = 7;

	[Test]
	[Category("Unit")]
	[Description("Rejects a leaf columnPath that does not exist on the root schema — catches the reported UsrCountry1 case before it reaches the server.")]
	public void Validate_Should_Reject_Unknown_Leaf_Column() {
		IFilterSchemaProvider provider = StubProvider("Country", new[] {
			Column("Name", TextDataValueType),
			Column("Country1", TextDataValueType)
		});
		StaticFilterGroup filter = Group("AND",
			leaves: [Leaf("UsrCountry1", "EQUAL", "\"x\"")]);

		System.Action act = () => new SchemaAwareFilterValidator(provider)
			.Validate(filter, "Country", "rule.actions[*].filter");

		act.Should().Throw<BusinessRuleFilterException>()
			.Where(ex => ex.ErrorCode == BusinessRuleFilterErrorCodes.PathUnknown)
			.WithMessage("*UsrCountry1*not found on schema 'Country'*");
	}

	[Test]
	[Category("Unit")]
	[Description("Walks forward references through Lookup columns: Country.Region.Name resolves when each intermediate segment is a Lookup and the final column exists.")]
	public void Validate_Should_Resolve_Forward_Reference_Through_Lookups() {
		EntitySchemaColumnDto regionLookup = LookupColumn("Region", referenceSchemaName: "Region");
		EntitySchemaColumnDto regionNameColumn = Column("Name", TextDataValueType);
		IFilterSchemaProvider provider = Substitute.For<IFilterSchemaProvider>();
		provider.GetSchemaColumns("Country").Returns(new Dictionary<string, EntitySchemaColumnDto> {
			["Region"] = regionLookup
		});
		provider.GetSchemaColumns("Region").Returns(new Dictionary<string, EntitySchemaColumnDto> {
			["Name"] = regionNameColumn
		});
		StaticFilterGroup filter = Group("AND",
			leaves: [Leaf("Region.Name", "EQUAL", "\"East\"")]);

		System.Action act = () => new SchemaAwareFilterValidator(provider)
			.Validate(filter, "Country", "rule.actions[*].filter");

		act.Should().NotThrow();
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects forward path that traverses a non-Lookup column.")]
	public void Validate_Should_Reject_Forward_Path_Through_Non_Lookup() {
		IFilterSchemaProvider provider = StubProvider("Country", new[] {
			Column("Name", TextDataValueType)
		});
		StaticFilterGroup filter = Group("AND",
			leaves: [Leaf("Name.Something", "EQUAL", "\"x\"")]);

		System.Action act = () => new SchemaAwareFilterValidator(provider)
			.Validate(filter, "Country", "rule.actions[*].filter");

		act.Should().Throw<BusinessRuleFilterException>()
			.Where(ex => ex.ErrorCode == BusinessRuleFilterErrorCodes.PathUnknown)
			.WithMessage("*Cannot traverse 'Name'*not a Lookup*");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects backward reference syntax that is not '[ChildSchema:Column]'.")]
	public void Validate_Should_Reject_Malformed_Backward_Reference() {
		IFilterSchemaProvider provider = StubProvider("Country", new[] {
			Column("Name", TextDataValueType)
		});
		StaticFilterGroup filter = Group("AND",
			backwardRefs: [Backward("Order.Country", Group("AND", leaves: []))]);

		System.Action act = () => new SchemaAwareFilterValidator(provider)
			.Validate(filter, "Country", "rule.actions[*].filter");

		act.Should().Throw<BusinessRuleFilterException>()
			.Where(ex => ex.ErrorCode == BusinessRuleFilterErrorCodes.BackwardReferenceNot1N)
			.WithMessage("*Expected '[ChildSchema:ColumnOnChild]' syntax*");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects backward reference when child column does not point to the parent schema (1:N cardinality not satisfied).")]
	public void Validate_Should_Reject_Backward_Reference_Wrong_Cardinality() {
		IFilterSchemaProvider provider = Substitute.For<IFilterSchemaProvider>();
		provider.GetSchemaColumns("Country").Returns(new Dictionary<string, EntitySchemaColumnDto>());
		provider.GetSchemaColumns("Order").Returns(new Dictionary<string, EntitySchemaColumnDto> {
			["Customer"] = LookupColumn("Customer", referenceSchemaName: "Contact")
		});
		StaticFilterGroup filter = Group("AND",
			backwardRefs: [Backward("[Order:Customer]", Group("AND", leaves: []))]);

		System.Action act = () => new SchemaAwareFilterValidator(provider)
			.Validate(filter, "Country", "rule.actions[*].filter");

		act.Should().Throw<BusinessRuleFilterException>()
			.Where(ex => ex.ErrorCode == BusinessRuleFilterErrorCodes.BackwardReferenceNot1N)
			.WithMessage("*does not reference 'Country'*");
	}

	[Test]
	[Category("Unit")]
	[Description("Accepts a well-formed backward reference where the child column points to the parent schema.")]
	public void Validate_Should_Accept_Valid_Backward_Reference() {
		IFilterSchemaProvider provider = Substitute.For<IFilterSchemaProvider>();
		provider.GetSchemaColumns("Country").Returns(new Dictionary<string, EntitySchemaColumnDto>());
		provider.GetSchemaColumns("Order").Returns(new Dictionary<string, EntitySchemaColumnDto> {
			["Country"] = LookupColumn("Country", referenceSchemaName: "Country"),
			["Total"] = Column("Total", IntegerDataValueType)
		});
		StaticFilterGroup filter = Group("AND",
			backwardRefs: [Backward("[Order:Country]",
				Group("AND", leaves: [Leaf("Total", "GREATER", "100")]))]);

		System.Action act = () => new SchemaAwareFilterValidator(provider)
			.Validate(filter, "Country", "rule.actions[*].filter");

		act.Should().NotThrow();
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a JSON boolean value passed for a Text column (datatype mismatch).")]
	public void Validate_Should_Reject_Boolean_Value_On_Text_Column() {
		IFilterSchemaProvider provider = StubProvider("Country", new[] {
			Column("Name", TextDataValueType)
		});
		StaticFilterGroup filter = Group("AND",
			leaves: [Leaf("Name", "EQUAL", "true")]);

		System.Action act = () => new SchemaAwareFilterValidator(provider)
			.Validate(filter, "Country", "rule.actions[*].filter");

		act.Should().Throw<BusinessRuleFilterException>()
			.Where(ex => ex.ErrorCode == BusinessRuleFilterErrorCodes.ValueShape)
			.WithMessage("*expects a JSON string value*");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a non-GUID string value for a Lookup column (must be resolved record Id).")]
	public void Validate_Should_Reject_Non_Guid_Value_On_Lookup_Column() {
		IFilterSchemaProvider provider = StubProvider("Order", new[] {
			LookupColumn("Customer", referenceSchemaName: "Contact")
		});
		StaticFilterGroup filter = Group("AND",
			leaves: [Leaf("Customer", "EQUAL", "\"Doe\"")]);

		System.Action act = () => new SchemaAwareFilterValidator(provider)
			.Validate(filter, "Order", "rule.actions[*].filter");

		act.Should().Throw<BusinessRuleFilterException>()
			.Where(ex => ex.ErrorCode == BusinessRuleFilterErrorCodes.LookupValueNotGuid)
			.WithMessage("*expects a JSON string GUID value*");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a relational comparison (GREATER) on a Text column.")]
	public void Validate_Should_Reject_Relational_Comparison_On_Text_Column() {
		IFilterSchemaProvider provider = StubProvider("Country", new[] {
			Column("Name", TextDataValueType)
		});
		StaticFilterGroup filter = Group("AND",
			leaves: [Leaf("Name", "GREATER", "\"M\"")]);

		System.Action act = () => new SchemaAwareFilterValidator(provider)
			.Validate(filter, "Country", "rule.actions[*].filter");

		act.Should().Throw<BusinessRuleFilterException>()
			.Where(ex => ex.ErrorCode == BusinessRuleFilterErrorCodes.ComparisonNotSupportedForDatatype)
			.WithMessage("*Relational comparison 'GREATER'*");
	}

	[Test]
	[Category("Unit")]
	[Description("Accepts IS_NULL on any column type without requiring a value.")]
	public void Validate_Should_Accept_Unary_Without_Value() {
		IFilterSchemaProvider provider = StubProvider("Country", new[] {
			Column("Name", TextDataValueType)
		});
		StaticFilterGroup filter = Group("AND",
			leaves: [LeafUnary("Name", "IS_NULL")]);

		System.Action act = () => new SchemaAwareFilterValidator(provider)
			.Validate(filter, "Country", "rule.actions[*].filter");

		act.Should().NotThrow();
	}

	[Test]
	[Category("Unit")]
	[Description("Accepts a JSON number value for an Integer column.")]
	public void Validate_Should_Accept_Numeric_Value_On_Integer_Column() {
		IFilterSchemaProvider provider = StubProvider("Order", new[] {
			Column("Total", IntegerDataValueType)
		});
		StaticFilterGroup filter = Group("AND",
			leaves: [Leaf("Total", "GREATER", "100")]);

		System.Action act = () => new SchemaAwareFilterValidator(provider)
			.Validate(filter, "Order", "rule.actions[*].filter");

		act.Should().NotThrow();
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects START_WITH on a non-text column (boolean).")]
	public void Validate_Should_Reject_String_Match_On_Boolean_Column() {
		IFilterSchemaProvider provider = StubProvider("Contact", new[] {
			Column("Active", BooleanDataValueType)
		});
		StaticFilterGroup filter = Group("AND",
			leaves: [Leaf("Active", "START_WITH", "\"true\"")]);

		System.Action act = () => new SchemaAwareFilterValidator(provider)
			.Validate(filter, "Contact", "rule.actions[*].filter");

		act.Should().Throw<BusinessRuleFilterException>()
			.Where(ex => ex.ErrorCode == BusinessRuleFilterErrorCodes.ComparisonNotSupportedForDatatype)
			.WithMessage("*String-match comparison 'START_WITH'*");
	}

	private static IFilterSchemaProvider StubProvider(string schemaName, IReadOnlyList<EntitySchemaColumnDto> columns) {
		IFilterSchemaProvider provider = Substitute.For<IFilterSchemaProvider>();
		Dictionary<string, EntitySchemaColumnDto> map = new(System.StringComparer.Ordinal);
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

	private static StaticFilterGroup Group(
		string logicalOperation,
		IReadOnlyList<StaticFilterLeaf>? leaves = null,
		IReadOnlyList<StaticFilterBackwardReference>? backwardRefs = null) =>
		new(logicalOperation, leaves ?? [], backwardRefs ?? []);

	private static StaticFilterLeaf Leaf(string columnPath, string comparisonType, string rawJsonValue) =>
		new(columnPath, comparisonType, JsonDocument.Parse(rawJsonValue).RootElement.Clone());

	private static StaticFilterLeaf LeafUnary(string columnPath, string comparisonType) =>
		new(columnPath, comparisonType, null);

	private static StaticFilterBackwardReference Backward(string referenceColumnPath, StaticFilterGroup filter) =>
		new(referenceColumnPath, filter);
}
