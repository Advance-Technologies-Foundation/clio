using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Clio.Command.BusinessRules.Filters.Schema;
using Clio.Command.Filters;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.Filters;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class EsqFilterSemanticValidatorTests {

	private static readonly IReadOnlyList<(string Schema, (string Name, string Type, string? Ref)[] Columns)> Schemas = [
		("Account", [
			("Id", "Guid", null), ("Name", "Text", null), ("Industry", "Lookup", "AccountIndustry"),
			("CreatedOn", "DateTime", null), ("Owner", "Lookup", "Contact"), ("AnnualRevenue", "Money", null),
			("IsActive", "Boolean", null)
		]),
		("AccountIndustry", [("Id", "Guid", null), ("Name", "Text", null)]),
		("Contact", [("Id", "Guid", null), ("Name", "Text", null), ("Account", "Lookup", "Account"),
			("Age", "Integer", null)]),
		("Lead", [("Id", "Guid", null), ("QualifiedContact", "Lookup", "Contact"), ("Budget", "Money", null)])
	];

	[Test]
	[Description("A filter whose columns, operators, macros and lookup values all fit the schema yields no findings.")]
	public void Validate_Should_Accept_A_Filter_That_Fits_The_Schema() {
		// Arrange
		EsqFilterGroupNode group = Read("""
			{ "filterType": 6, "logicalOperation": 0, "items": {
				"name": { "filterType": 1, "comparisonType": 11, "leftExpression": { "expressionType": 0, "columnPath": "Name" },
					"rightExpression": { "expressionType": 2, "parameter": { "dataValueType": 1, "value": "Acme" } } },
				"created": { "filterType": 1, "comparisonType": 3, "leftExpression": { "expressionType": 0, "columnPath": "CreatedOn" },
					"rightExpression": { "expressionType": 1, "functionType": 1, "macrosType": 19 } },
				"industry": { "filterType": 4, "comparisonType": 3, "leftExpression": { "expressionType": 0, "columnPath": "Industry" },
					"rightExpressions": [ { "expressionType": 2, "parameter": { "dataValueType": 10,
						"value": { "value": "a470b005-e8bb-df11-b00f-001d60e938c6", "displayValue": "Retail" } } } ] },
				"ownerName": { "filterType": 2, "comparisonType": 2, "isNull": false, "leftExpression": { "expressionType": 0, "columnPath": "Owner.Name" } },
				"contacts": { "filterType": 5, "comparisonType": 15, "leftExpression": { "expressionType": 0, "columnPath": "[Contact:Account].Id" },
					"subFilters": { "filterType": 6, "logicalOperation": 0, "items": {
						"age": { "filterType": 1, "comparisonType": 7, "leftExpression": { "expressionType": 0, "columnPath": "Age" },
							"rightExpression": { "expressionType": 2, "parameter": { "dataValueType": 4, "value": 30 } } } } } }
			} }
			""");

		// Act
		EsqFilterFindings findings = CreateValidator().Validate(group, "Account");

		// Assert
		findings.Errors.Should().BeEmpty(because: "every leaf resolves on Account and fits its column type");
		findings.Warnings.Should().BeEmpty(because: "every schema on the way could be read");
	}

	[Test]
	[Description("An unknown column, a forward path through a non-lookup, and an unknown backward link are each reported with the leaf path.")]
	public void Validate_Should_Report_Columns_That_Do_Not_Resolve() {
		// Arrange
		EsqFilterGroupNode group = Read("""
			{ "filterType": 6, "logicalOperation": 0, "items": {
				"typo": { "filterType": 2, "comparisonType": 2, "leftExpression": { "expressionType": 0, "columnPath": "Nmae" } },
				"through": { "filterType": 2, "comparisonType": 2, "leftExpression": { "expressionType": 0, "columnPath": "Name.Id" } },
				"link": { "filterType": 5, "comparisonType": 15, "leftExpression": { "expressionType": 0, "columnPath": "[Contact:Company].Id" } }
			} }
			""");

		// Act
		EsqFilterFindings findings = CreateValidator().Validate(group, "Account");

		// Assert
		findings.Errors.Should().HaveCount(3, because: "each of the three leaves has a path that does not resolve");
		findings.Errors[0].Should().StartWith("filter.items.typo.leftExpression.columnPath",
			because: "the error names the exact JSON location of the bad column");
		findings.Errors[0].Should().Contain("'Nmae' was not found on schema 'Account'",
			because: "the agent needs the missing name and the schema it was looked up on");
		findings.Errors[1].Should().Contain("not a lookup",
			because: "a forward path can only continue through a lookup column");
		findings.Errors[2].Should().Contain("'Company' of backward reference",
			because: "the backward link column must exist on the child schema");
	}

	[Test]
	[Description("Operator/type, macro/type, lookup and boolean value mismatches are reported as errors.")]
	public void Validate_Should_Report_Values_That_Do_Not_Fit_The_Column() {
		// Arrange
		EsqFilterGroupNode group = Read("""
			{ "filterType": 6, "logicalOperation": 0, "items": {
				"relational": { "filterType": 1, "comparisonType": 7, "leftExpression": { "expressionType": 0, "columnPath": "Industry" },
					"rightExpression": { "expressionType": 2, "parameter": { "dataValueType": 10, "value": "a470b005-e8bb-df11-b00f-001d60e938c6" } } },
				"text": { "filterType": 1, "comparisonType": 11, "leftExpression": { "expressionType": 0, "columnPath": "AnnualRevenue" },
					"rightExpression": { "expressionType": 2, "parameter": { "dataValueType": 6, "value": 5 } } },
				"macro": { "filterType": 1, "comparisonType": 3, "leftExpression": { "expressionType": 0, "columnPath": "Name" },
					"rightExpression": { "expressionType": 1, "functionType": 1, "macrosType": 4 } },
				"lookup": { "filterType": 1, "comparisonType": 3, "leftExpression": { "expressionType": 0, "columnPath": "Industry" },
					"rightExpression": { "expressionType": 2, "parameter": { "dataValueType": 10, "value": "Retail" } } },
				"flag": { "filterType": 1, "comparisonType": 3, "leftExpression": { "expressionType": 0, "columnPath": "IsActive" },
					"rightExpression": { "expressionType": 2, "parameter": { "dataValueType": 12, "value": "true" } } }
			} }
			""");

		// Act
		EsqFilterFindings findings = CreateValidator().Validate(group, "Account");

		// Assert
		findings.Errors.Should().HaveCount(5, because: "each leaf compares its column with something its type cannot hold");
		findings.Errors.Should().ContainSingle(error => error.Contains("(less/greater) has no meaning"),
			because: "a lookup has no order, so greater-than cannot run");
		findings.Errors.Should().ContainSingle(error => error.Contains("needs a text column"),
			because: "contains on a money column is meaningless");
		findings.Errors.Should().ContainSingle(error => error.Contains("(Today) applies only to Date/DateTime/Time"),
			because: "a date macro on a text column never matches");
		findings.Errors.Should().ContainSingle(error => error.Contains("must be the record Id"),
			because: "a lookup is compared by record Id, not by display text");
		findings.Errors.Should().ContainSingle(error => error.Contains("must be true or false"),
			because: "a boolean column takes a JSON boolean");
	}

	[Test]
	[Description("A date-part function needs a date column; an exists filter needs a backward reference.")]
	public void Validate_Should_Check_DatePart_And_Exists_Shapes() {
		// Arrange
		EsqFilterGroupNode group = Read("""
			{ "filterType": 6, "logicalOperation": 0, "items": {
				"year": { "filterType": 1, "comparisonType": 3,
					"leftExpression": { "expressionType": 1, "functionType": 3, "datePartType": 4,
						"functionArgument": { "expressionType": 0, "columnPath": "Name" } },
					"rightExpression": { "expressionType": 2, "parameter": { "dataValueType": 4, "value": 2026 } } },
				"exists": { "filterType": 5, "comparisonType": 15, "leftExpression": { "expressionType": 0, "columnPath": "Owner.Id" } }
			} }
			""");

		// Act
		EsqFilterFindings findings = CreateValidator().Validate(group, "Account");

		// Assert
		findings.Errors.Should().HaveCount(2, because: "both leaves misuse their construct");
		findings.Errors.Should().ContainSingle(error => error.Contains("date-part function applies only to Date"),
			because: "YEAR(Name) cannot be computed");
		findings.Errors.Should().ContainSingle(error => error.Contains("needs a backward reference"),
			because: "an exists filter counts child rows, so its path must join a child schema");
	}

	[Test]
	[Description("Sub-filters of an exists filter and of an aggregation sub-query are checked on the joined child schema.")]
	public void Validate_Should_Check_SubFilters_On_The_Joined_Schema() {
		// Arrange
		EsqFilterGroupNode group = Read("""
			{ "filterType": 6, "logicalOperation": 0, "items": {
				"count": { "filterType": 1, "comparisonType": 7,
					"leftExpression": { "expressionType": 3, "functionType": 2, "aggregationType": 1,
						"columnPath": "Owner.[Lead:QualifiedContact].Id",
						"subFilters": { "filterType": 6, "logicalOperation": 0, "items": {
							"bad": { "filterType": 2, "comparisonType": 2, "leftExpression": { "expressionType": 0, "columnPath": "Age" } } } } },
					"rightExpression": { "expressionType": 2, "parameter": { "dataValueType": 4, "value": 0 } } }
			} }
			""");

		// Act
		EsqFilterFindings findings = CreateValidator().Validate(group, "Account");

		// Assert
		findings.Errors.Should().ContainSingle(because: "'Age' belongs to Contact, not to the joined Lead schema");
		findings.Errors[0].Should().Contain("'Age' was not found on schema 'Lead'",
			because: "sub-filter columns are resolved on the schema the backward reference joins");
	}

	[Test]
	[Description("A schema the environment cannot return is a warning, never an error, and disabled leaves are not checked.")]
	public void Validate_Should_Warn_Instead_Of_Fail_When_A_Schema_Cannot_Be_Read() {
		// Arrange
		IFilterSchemaProvider provider = CreateProvider();
		provider.GetColumns("Lead").Returns(_ => throw new InvalidOperationException("forbidden"));
		EsqFilterGroupNode group = Read("""
			{ "filterType": 6, "logicalOperation": 0, "items": {
				"lead": { "filterType": 5, "comparisonType": 15, "leftExpression": { "expressionType": 0, "columnPath": "[Lead:QualifiedContact].Id" } },
				"off": { "filterType": 2, "comparisonType": 2, "isEnabled": false, "leftExpression": { "expressionType": 0, "columnPath": "Nope" } }
			} }
			""");

		// Act
		EsqFilterFindings findings = new EsqFilterSemanticValidator(provider).Validate(group, "Contact");

		// Assert
		findings.Errors.Should().BeEmpty(because: "an unreadable schema proves nothing about the filter, and the disabled leaf is ignored at runtime");
		findings.Warnings.Should().ContainSingle(because: "the caller is told the filters on that schema went unverified")
			.Which.Should().Contain("were not verified");
	}

	[Test]
	[Description("What ESQ runs is not flagged: less/greater on text, an unset lookup value, an id-keyed lookup object, case and Id spellings of a column; contains on a lookup only warns.")]
	public void Validate_Should_Accept_What_The_Runtime_Accepts() {
		// Arrange
		EsqFilterGroupNode group = Read("""
			{ "filterType": 6, "logicalOperation": 0, "items": {
				"textOrder": { "filterType": 1, "comparisonType": 7, "leftExpression": { "expressionType": 0, "columnPath": "Name" },
					"rightExpression": { "expressionType": 2, "parameter": { "dataValueType": 1, "value": "M" } } },
				"unset": { "filterType": 1, "comparisonType": 3, "leftExpression": { "expressionType": 0, "columnPath": "Industry" },
					"rightExpression": { "expressionType": 2, "parameter": { "dataValueType": 10, "value": { "value": "", "displayValue": "" } } } },
				"idKeyed": { "filterType": 4, "comparisonType": 3, "leftExpression": { "expressionType": 0, "columnPath": "IndustryId" },
					"rightExpressions": [ { "expressionType": 2, "parameter": { "dataValueType": 10, "value": { "id": "a470b005-e8bb-df11-b00f-001d60e938c6" } } } ] },
				"lookupText": { "filterType": 1, "comparisonType": 11, "leftExpression": { "expressionType": 0, "columnPath": "industry" },
					"rightExpression": { "expressionType": 2, "parameter": { "dataValueType": 1, "value": "Ret" } } }
			} }
			""");

		// Act
		EsqFilterFindings findings = CreateValidator().Validate(group, "Account");

		// Assert
		findings.Errors.Should().BeEmpty(because: "text orders, an unset lookup value and an id-keyed lookup object all run; names match case-insensitively and by their Id spelling");
		findings.Warnings.Should().ContainSingle(because: "contains on a lookup is only questionable")
			.Which.Should().Contain("compare its display column", because: "the agent is told the safe alternative");
	}

	[Test]
	[Description("Column-path resolution: three-part backward reference, mixed forward/backward chain, a bare trailing backward reference and a path through a non-lookup.")]
	public void Resolver_Should_Resolve_Stored_Path_Forms() {
		// Arrange
		EsqColumnPathResolver resolver = new(CreateProvider());

		// Act
		EsqColumnResolution threePart = resolver.Resolve("[Contact:Account:Id].Age", "Account");
		EsqColumnResolution mixed = resolver.Resolve("Owner.[Lead:QualifiedContact].Budget", "Account");
		EsqColumnResolution bare = resolver.Resolve("[Contact:Account]", "Account");
		EsqColumnResolution throughText = resolver.Resolve("Name.Id", "Account");

		// Assert
		threePart.Column!.Name.Should().Be("Age", because: "the third part names the source column and does not change the join target");
		mixed.Column!.Name.Should().Be("Budget", because: "a forward lookup can lead into a backward reference");
		mixed.LastJoinedSchema.Should().Be("Lead", because: "sub-filters are checked on the schema the last backward reference joins");
		bare.Error.Should().Contain("must be followed by a column", because: "a stored exists filter always names a child column such as Id");
		throughText.Error.Should().Contain("not a lookup", because: "only lookups lead to another schema");
	}

	[Test]
	[Description("A schema the environment refuses is asked for once and reported once, however many leaves use it.")]
	public void Validate_Should_Read_An_Unreadable_Schema_Once() {
		// Arrange
		IFilterSchemaProvider provider = CreateProvider();
		provider.GetColumns("Lead").Returns(_ => throw new InvalidOperationException("forbidden"));
		EsqFilterGroupNode group = Read("""
			{ "filterType": 6, "logicalOperation": 0, "items": {
				"a": { "filterType": 5, "comparisonType": 15, "leftExpression": { "expressionType": 0, "columnPath": "[Lead:QualifiedContact].Id" } },
				"b": { "filterType": 5, "comparisonType": 16, "leftExpression": { "expressionType": 0, "columnPath": "[Lead:QualifiedContact].Id" } }
			} }
			""");

		// Act
		EsqFilterFindings findings = new EsqFilterSemanticValidator(provider).Validate(group, "Contact");

		// Assert
		provider.Received(1).GetColumns("Lead");
		findings.Warnings.Distinct().Should().ContainSingle(because: "both leaves hit the same unreadable schema");
	}

	[Test]
	[Description("The reader understands every stored filter shape harvested from product page schemas.")]
	public void Reader_Should_Read_Every_Stored_Widget_Filter_From_Product_Packages() {
		// Arrange
		using JsonDocument corpus = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(
			TestContext.CurrentContext.TestDirectory, "Command", "Filters", "Fixtures", "stored-esq-filters.json")));

		// Act
		List<(string Source, int Expected, int Read)> results = corpus.RootElement.EnumerateArray()
			.Select(entry => {
				JsonElement filter = entry.GetProperty("filter");
				EsqFilterGroupNode? group = EsqFilterReader.ReadGroup(filter, "filter");
				return (entry.GetProperty("source").GetString()!, CountEnabledLeaves(filter), group is null ? -1 : CountLeaves(group));
			})
			.ToList();

		// Assert
		results.Should().HaveCountGreaterThan(100, because: "the fixture covers every distinct stored shape found in the product packages");
		results.Where(result => result.Expected != result.Read).Should().BeEmpty(
			because: "every enabled stored leaf must reach the semantic checks, in either stored encoding");
	}

	private static int CountEnabledLeaves(JsonElement group) =>
		group.GetProperty("items").EnumerateObject()
			.Where(item => !(item.Value.TryGetProperty("isEnabled", out JsonElement enabled) && enabled.ValueKind == JsonValueKind.False))
			.Sum(item => item.Value.GetProperty("filterType").GetInt32() == 6 ? CountEnabledLeaves(item.Value) : 1);

	private static int CountLeaves(EsqFilterGroupNode group) =>
		group.Items.Sum(node => node is EsqFilterGroupNode nested ? CountLeaves(nested) : 1);

	private static EsqFilterGroupNode Read(string json) =>
		EsqFilterReader.ReadGroup(JsonDocument.Parse(json).RootElement, "filter")!;

	private static EsqFilterSemanticValidator CreateValidator() => new(CreateProvider());

	private static IFilterSchemaProvider CreateProvider() {
		IFilterSchemaProvider provider = Substitute.For<IFilterSchemaProvider>();
		foreach ((string schema, (string Name, string Type, string? Ref)[] columns) in Schemas) {
			Dictionary<string, FilterSchemaColumn> map = new(StringComparer.Ordinal);
			foreach ((string name, string type, string? reference) in columns) {
				map[name] = new FilterSchemaColumn {
					Name = name, DataValueTypeName = type, ReferenceSchemaName = reference,
					DataValueTypeCode = Clio.Common.CreatioDataValueType.GetCode(type) ?? 0
				};
			}
			provider.GetColumns(schema).Returns(map);
		}
		return provider;
	}
}
