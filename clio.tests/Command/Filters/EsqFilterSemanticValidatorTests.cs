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
				"relational": { "filterType": 1, "comparisonType": 7, "leftExpression": { "expressionType": 0, "columnPath": "Name" },
					"rightExpression": { "expressionType": 2, "parameter": { "dataValueType": 1, "value": "A" } } },
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
		findings.Errors.Should().ContainSingle(error => error.Contains("needs a numeric or date/time column"),
			because: "greater-than on a text column is meaningless");
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
		findings.Warnings.Should().ContainSingle(because: "the caller is told the leaf could not be verified")
			.Which.Should().Contain("could not be verified");
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
