using System.Collections.Generic;
using System.Linq;
using Clio.Command.Filters;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.Filters;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class WidgetFilterLocatorTests {

	[Test]
	[Description("Chart series, indicator and funnel filters are found in inserts and merges, each with the schema it runs against.")]
	public void Locate_Should_Find_Every_Widget_Filter_With_Its_Root_Schema() {
		// Arrange
		const string body = """
			define("UsrPage", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ {
				return {
					viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[
						{ "operation": "insert", "name": "Chart", "values": { "type": "crt.ChartWidget", "config": { "series": [
							{ "data": { "providing": { "schemaName": "Account", "filters": { "filter": { "filterType": 6, "items": {} }, "filterAttributes": [] } } } } ] } } },
						{ "operation": "merge", "name": "Indicator", "values": { "config": { "data": { "providing": {
							"filters": { "filter": { "filterType": 6, "rootSchemaName": "Contact", "items": {} } } } } } } },
						{ "operation": "insert", "name": "Funnel", "values": { "config": { "entities": [
							{ "schemaName": "Opportunity", "filters": { "filterType": 6, "items": {} } } ] } } },
						{ "operation": "merge", "name": "NoRoot", "values": { "config": { "data": { "providing": {
							"filters": { "filter": { "filterType": 6, "items": {} } } } } } } }
					]/**SCHEMA_VIEW_CONFIG_DIFF*/
				};
			});
			""";

		// Act
		IReadOnlyList<PageWidgetFilter> filters = WidgetFilterValidator.WidgetFilterLocator.Locate(body);

		// Assert
		filters.Select(filter => (filter.Path, filter.RootSchemaName)).Should().Equal(
			[
				("Chart.values.config.series[0].data.providing.filters.filter", "Account"),
				("Indicator.values.config.data.providing.filters.filter", "Contact"),
				("Funnel.values.config.entities[0].filters", "Opportunity")
			],
			because: "a merge without schemaName falls back to the group's rootSchemaName and a filter with neither is skipped");
	}

	[Test]
	[Description("A body without a view-config diff, or with one that is not JSON, yields no filters instead of failing.")]
	public void Locate_Should_Return_Nothing_For_A_Body_It_Cannot_Read() {
		// Arrange
		const string body = "define(\"UsrPage\", [], function() { return { viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[ { broken /**SCHEMA_VIEW_CONFIG_DIFF*/ }; });";

		// Act
		IReadOnlyList<PageWidgetFilter> filters = WidgetFilterValidator.WidgetFilterLocator.Locate(body);

		// Assert
		filters.Should().BeEmpty(because: "the syntax gates report a broken body; this check only adds filter findings");
	}
}
