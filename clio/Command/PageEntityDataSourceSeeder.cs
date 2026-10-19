namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Newtonsoft.Json.Linq;

/// <summary>
/// The <c>modelConfigDiff</c> operations that bind a new page to its entity, and what they bind.
/// </summary>
/// <param name="ModelConfigDiff">Operations to put into the new page body.</param>
/// <param name="DataSourceName">The data source the operations bind to the entity; <see langword="null"/> when none.</param>
/// <param name="IsPrimary">Whether that data source is the page's primary data source.</param>
/// <param name="Warning">Why the template could not be inspected, when the default shape was used blind.</param>
public sealed record PageEntityDataSourceSeed(
	JArray ModelConfigDiff,
	string? DataSourceName,
	bool IsPrimary,
	string? Warning);

/// <summary>
/// Decides how <c>create-page --entity-schema-name</c> binds the new page to its entity.
/// </summary>
public interface IPageEntityDataSourceSeeder {
	/// <summary>
	/// Builds the <c>modelConfigDiff</c> operations that bind a page created from <paramref name="templateUId"/>
	/// to <paramref name="entitySchemaName"/>, the way the Freedom UI designer does when a data source is set.
	/// </summary>
	/// <remarks>
	/// The template decides the shape. A template that already names a primary data source gets that data
	/// source bound to the entity; one that declares a <c>PDS</c> without making it primary (the list page
	/// template, whose <c>PDS</c> is a <c>viewElement</c> list source) gets <c>PDS</c> bound; any other template
	/// gets a page-scoped <c>PDS</c> that becomes the primary data source, as on stock record pages.
	/// A template that cannot be read gets no operations and a <see cref="PageEntityDataSourceSeed.Warning"/>;
	/// it never fails page creation.
	/// </remarks>
	/// <param name="templateUId">Template (parent) schema identifier.</param>
	/// <param name="packageUId">Package the page is created in.</param>
	/// <param name="entitySchemaName">Entity schema to bind.</param>
	/// <returns>The operations and what they bind.</returns>
	PageEntityDataSourceSeed BuildSeed(string templateUId, string packageUId, string entitySchemaName);
}

/// <inheritdoc />
internal sealed class PageEntityDataSourceSeeder(
	IPageDesignerHierarchyClient hierarchyClient,
	IPageSchemaBodyParser bodyParser,
	IPageBundleBuilder bundleBuilder) : IPageEntityDataSourceSeeder {

	internal const string DefaultDataSourceName = "PDS";
	private const string DataSourcesKey = "dataSources";
	private const string ConfigKey = "config";
	private const string OperationKey = "operation";
	private const string MergeOperation = "merge";
	private const string ValuesKey = "values";

	/// <inheritdoc />
	public PageEntityDataSourceSeed BuildSeed(string templateUId, string packageUId, string entitySchemaName) {
		JsonObject templateModelConfig;
		try {
			IReadOnlyList<PageDesignerHierarchySchema> hierarchy = hierarchyClient.GetParentSchemas(templateUId, packageUId);
			List<PageSchemaBundlePart> parts = hierarchy
				.Select(schema => new PageSchemaBundlePart(
					schema, schema.Body != null ? bodyParser.Parse(schema.Body) : new PageParsedSchemaBody()))
				.ToList();
			templateModelConfig = bundleBuilder.Build(parts).ModelConfig;
		} catch (Exception ex) {
			// Guessing the shape blind could turn the list template's viewElement PDS into a page-scoped primary
			// source, so an unreadable template binds nothing and says so.
			return Unbound(entitySchemaName, $"Could not read the template's model config ({ex.Message})");
		}
		return BuildFromTemplateModelConfig(templateModelConfig, entitySchemaName);
	}

	/// <summary>
	/// A seed that binds nothing, for a template whose shape is unknown; <paramref name="reason"/> starts the warning.
	/// </summary>
	internal static PageEntityDataSourceSeed Unbound(string entitySchemaName, string reason) =>
		new(new JArray(), null, false,
			$"{reason}; no data source was bound to '{entitySchemaName}' - the entity is recorded in the page "
			+ "dependencies only. Bind it with update-page.");

	/// <summary>
	/// Picks the binding from the template's merged <c>modelConfig</c>; <see langword="null"/> means unknown.
	/// </summary>
	internal static PageEntityDataSourceSeed BuildFromTemplateModelConfig(
		JsonObject? templateModelConfig, string entitySchemaName) {
		JsonObject? dataSources = templateModelConfig?[DataSourcesKey] as JsonObject;
		string? primary = (templateModelConfig?["primaryDataSourceName"] as JsonValue)?.TryGetValue(out string? name) == true
			? name
			: null;
		if (!string.IsNullOrWhiteSpace(primary) && dataSources?[primary] is JsonObject primarySource) {
			return new PageEntityDataSourceSeed(
				BindExisting(primary, primarySource, entitySchemaName), primary, true, null);
		}
		if (string.IsNullOrWhiteSpace(primary) && dataSources?[DefaultDataSourceName] is JsonObject listSource) {
			return new PageEntityDataSourceSeed(
				BindExisting(DefaultDataSourceName, listSource, entitySchemaName), DefaultDataSourceName, false, null);
		}
		string? warning = string.IsNullOrWhiteSpace(primary)
			? null
			: $"The template names primary data source '{primary}' but does not declare it; "
				+ $"'{DefaultDataSourceName}' was added and made primary instead.";
		var pageDataSource = new JObject {
			["type"] = "crt.EntityDataSource",
			["scope"] = "page",
			[ConfigKey] = new JObject { ["entitySchemaName"] = entitySchemaName }
		};
		// A merge whose path does not exist is skipped by the runtime applier (and by clio's bundle builder), so a
		// template with no `dataSources` object (the mini page template) gets the data source from the root merge,
		// which merges deep. Stock record pages, whose template does declare `dataSources`, use the two-step form.
		JArray operations = dataSources is null
			? new JArray(new JObject {
				[OperationKey] = MergeOperation,
				["path"] = new JArray(),
				[ValuesKey] = new JObject {
					["primaryDataSourceName"] = DefaultDataSourceName,
					[DataSourcesKey] = new JObject { [DefaultDataSourceName] = pageDataSource }
				}
			})
			: new JArray(
				new JObject {
					[OperationKey] = MergeOperation,
					["path"] = new JArray(),
					[ValuesKey] = new JObject { ["primaryDataSourceName"] = DefaultDataSourceName }
				},
				new JObject {
					[OperationKey] = MergeOperation,
					["path"] = new JArray(DataSourcesKey),
					[ValuesKey] = new JObject { [DefaultDataSourceName] = pageDataSource }
				});
		return new PageEntityDataSourceSeed(operations, DefaultDataSourceName, true, warning);
	}

	// A merge into a missing path is skipped, so the entity goes into `config` only when the source has one.
	private static JArray BindExisting(string dataSourceName, JsonObject dataSource, string entitySchemaName) =>
		dataSource[ConfigKey] is JsonObject
			? new JArray(new JObject {
				[OperationKey] = MergeOperation,
				["path"] = new JArray(DataSourcesKey, dataSourceName, ConfigKey),
				[ValuesKey] = new JObject { ["entitySchemaName"] = entitySchemaName }
			})
			: new JArray(new JObject {
				[OperationKey] = MergeOperation,
				["path"] = new JArray(DataSourcesKey, dataSourceName),
				[ValuesKey] = new JObject { [ConfigKey] = new JObject { ["entitySchemaName"] = entitySchemaName } }
			});
}
