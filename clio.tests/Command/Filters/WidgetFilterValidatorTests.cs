using System;
using System.Collections.Generic;
using Clio.Command.BusinessRules;
using Clio.Command.EntitySchemaDesigner;
using Clio.Command.Filters;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.Filters;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class WidgetFilterValidatorTests {

	private static readonly Guid PackageUId = Guid.Parse("11111111-2222-3333-4444-555555555555");

	private static string Body(string filterColumn, string rootSchemaName = "Account") =>
		"""
		define("UsrPage", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ {
			return {
				viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[
					{ "operation": "insert", "name": "Indicator", "values": { "type": "crt.IndicatorWidget", "config": { "data": { "providing": {
						"schemaName": "Account", "filters": { "filter": { "filterType": 6, "logicalOperation": 0, "rootSchemaName": "__ROOT__", "items": {
							"a": { "filterType": 2, "comparisonType": 2, "leftExpression": { "expressionType": 0, "columnPath": "__COLUMN__" } } } } } } } } } }
				]/**SCHEMA_VIEW_CONFIG_DIFF*/
			};
		});
		""".Replace("__COLUMN__", filterColumn).Replace("__ROOT__", rootSchemaName);

	private static WidgetFilterValidator CreateValidator() {
		IEntityBusinessRuleSchemaProvider schemas = Substitute.For<IEntityBusinessRuleSchemaProvider>();
		schemas.GetSchema("Account", PackageUId).Returns(new EntityDesignSchemaDto {
			Name = "Account",
			Columns = [
				new EntitySchemaColumnDto { UId = Guid.NewGuid(), Name = "Id", DataValueType = 0 },
				new EntitySchemaColumnDto { UId = Guid.NewGuid(), Name = "Name", DataValueType = 1 }
			]
		});
		return new WidgetFilterValidator(schemas);
	}

	[Test]
	[Description("A broken filter the save introduces is an error; the same broken filter already stored unchanged on the page is only a warning.")]
	public void Validate_Should_Block_Only_Filters_The_Save_Changes() {
		// Arrange
		WidgetFilterValidator validator = CreateValidator();
		string broken = Body("Nmae");

		// Act
		PageWidgetFilterReport introduced = validator.Validate(broken, PackageUId, persistedBody: Body("Name"));
		PageWidgetFilterReport untouched = validator.Validate(broken, PackageUId, persistedBody: broken);

		// Assert
		introduced.Errors.Should().ContainSingle(because: "this save writes the misspelled column")
			.Which.Should().Contain("'Nmae' was not found on schema 'Account'");
		untouched.Errors.Should().BeEmpty(because: "a stale filter someone else left must not block an unrelated edit");
		untouched.Warnings.Should().ContainSingle(because: "the stale filter is still worth reporting")
			.Which.Should().StartWith("Already on the page", because: "the agent learns it did not cause the problem");
	}

	[Test]
	[Description("A filter whose rootSchemaName contradicts the data source's schemaName is skipped with a warning instead of being judged against either.")]
	public void Validate_Should_Skip_A_Filter_With_A_Conflicting_Root() {
		// Arrange
		WidgetFilterValidator validator = CreateValidator();

		// Act
		PageWidgetFilterReport report = validator.Validate(Body("Nmae", rootSchemaName: "Contact"), PackageUId);

		// Assert
		report.Errors.Should().BeEmpty(because: "with two candidate roots no verdict is certain");
		report.Warnings.Should().ContainSingle(because: "the agent is told the filter went unchecked")
			.Which.Should().Contain("differs from the data source's schemaName");
	}

	[Test]
	[Description("A broken column in a data-source attribute's aggregationConfig.filter blocks the save like a widget filter does.")]
	public void Validate_Should_Report_A_Broken_Aggregation_Filter() {
		// Arrange
		WidgetFilterValidator validator = CreateValidator();
		const string body = """
			define("UsrPage", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ {
				return {
					modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[
						{ "operation": "merge", "path": [], "values": { "dataSources": { "PDS": { "type": "crt.EntityDataSource", "config": {
							"entitySchemaName": "Contact", "attributes": { "AccountCount": { "path": "[Account:PrimaryContact].Id",
								"aggregationConfig": { "aggregationFunction": "count", "filter": { "filterType": 6, "logicalOperation": 0,
									"rootSchemaName": "Account", "items": { "a": { "filterType": 2, "comparisonType": 2,
										"leftExpression": { "expressionType": 0, "columnPath": "Nmae" } } } } } } } } } } } }
					]/**SCHEMA_MODEL_CONFIG_DIFF*/
				};
			});
			""";

		// Act
		PageWidgetFilterReport report = validator.Validate(body, PackageUId);
		PageWidgetFilterReport untouched = validator.Validate(body, PackageUId, persistedBody: body);

		// Assert
		report.Errors.Should().ContainSingle(because: "the aggregation sub-query would fail on the misspelled column")
			.Which.Should().Contain("modelConfigDiff[0].values.dataSources.PDS.config.attributes.AccountCount.aggregationConfig.filter")
			.And.Contain("'Nmae' was not found on schema 'Account'");
		untouched.Errors.Should().BeEmpty(because: "an aggregation filter the page already stores unchanged must not block an unrelated edit either");
		untouched.Warnings.Should().ContainSingle().Which.Should().StartWith("Already on the page");
	}

	[Test]
	[Description("A view-config diff that is not JSON yields a warning that nothing was checked.")]
	public void Validate_Should_Warn_When_The_Diff_Cannot_Be_Read() {
		// Arrange
		WidgetFilterValidator validator = CreateValidator();
		const string body = "define(\"UsrPage\", [], function() { return { viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[ { broken /**SCHEMA_VIEW_CONFIG_DIFF*/ }; });";

		// Act
		PageWidgetFilterReport report = validator.Validate(body, PackageUId);

		// Assert
		report.Errors.Should().BeEmpty(because: "the syntax gates own the verdict on a broken body");
		report.Warnings.Should().ContainSingle(because: "silently checking nothing would read as a pass")
			.Which.Should().Contain("not readable JSON");
	}
}
