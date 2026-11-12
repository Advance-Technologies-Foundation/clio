using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Clio.Command.BusinessRules;

namespace Clio.Command;

/// <summary>
/// The record-page fragment that adds the stage progress bar: the two inserts the product's application wizard
/// writes for a tabbed Freedom UI record page, wrapped in the marker pairs <c>update-page</c> append needs.
/// </summary>
/// <remarks>
/// A constant rather than agent input on purpose: the bar's runtime wiring (<c>recordId</c>, <c>value</c>,
/// <c>stages</c>, the request handlers) is created and OVERWRITTEN by the platform's page preprocessor on every
/// open, so writing any of it would only add something the next open discards or fights. The bar takes its stage
/// column from the object's first enabled, active-version case, which is why <c>entityName</c> is all it needs.
/// </remarks>
public static class ProgressBarFragment {

	/// <summary>The container's name when the page does not already use it.</summary>
	public const string ContainerName = "ProgressBarContainer";

	/// <summary>The bar's name when the page does not already use it.</summary>
	public const string BarName = "ProgressBar";

	/// <summary>The container the fragment inserts into.</summary>
	public const string ParentName = "MainContainer";

	/// <summary>The component type of the bar.</summary>
	public const string BarType = "crt.EntityStageProgressBar";

	/// <summary>The component type <see cref="ParentName"/> must have.</summary>
	public const string ParentType = "crt.FlexContainer";

	/// <summary>
	/// Keys the fragment must never carry: the runtime preprocessor creates and overwrites each of them.
	/// </summary>
	public static readonly IReadOnlyList<string> ForbiddenKeys = [
		"recordId", "value", "stages", "loadData", "stageChanged", "setAllowedStages",
		"changeToAppropriateEntityStageSchema"
	];

	/// <summary>Builds the view-config diff the fragment carries.</summary>
	/// <param name="entitySchemaName">The case object; the bar's <c>entityName</c>.</param>
	/// <param name="nameSuffix">Appended to both element names when the page already uses them; empty otherwise.</param>
	public static JsonArray BuildViewConfigDiff(string entitySchemaName, string nameSuffix) {
		string containerName = ContainerName + nameSuffix;
		return [
			new JsonObject {
				["operation"] = "insert",
				["name"] = containerName,
				["values"] = new JsonObject {
					["type"] = ParentType,
					["direction"] = "column",
					["fitContent"] = true,
					["color"] = "transparent",
					["borderRadius"] = "none",
					["items"] = new JsonArray(),
					["padding"] = new JsonObject {
						["top"] = "none", ["right"] = "small", ["bottom"] = "none", ["left"] = "small"
					},
					["justifyContent"] = "start",
					["alignItems"] = "stretch",
					["gap"] = "small",
					["wrap"] = "nowrap"
				},
				["parentName"] = ParentName,
				["propertyName"] = "items",
				["index"] = 0
			},
			new JsonObject {
				["operation"] = "insert",
				["name"] = BarName + nameSuffix,
				["values"] = new JsonObject {
					["type"] = BarType,
					["entityName"] = entitySchemaName,
					["saveOnChange"] = true,
					["askUserToChangeSchema"] = false
				},
				["parentName"] = containerName,
				["propertyName"] = "items",
				["index"] = 0
			}
		];
	}

	/// <summary>
	/// Builds the marker-wrapped append body: the one string the dry run, the write, the result and the retry
	/// arguments all carry, so what was checked is exactly what is written.
	/// </summary>
	/// <param name="pageSchemaName">The record page.</param>
	/// <param name="entitySchemaName">The case object.</param>
	/// <param name="nameSuffix">See <see cref="BuildViewConfigDiff"/>.</param>
	public static string BuildBody(string pageSchemaName, string entitySchemaName, string nameSuffix) {
		string diff = BuildViewConfigDiff(entitySchemaName, nameSuffix).ToJsonString();
		return "define(\"" + pageSchemaName + "\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, "
			+ "function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ {\n"
			+ "\treturn {\n"
			+ "\t\tviewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/" + diff + "/**SCHEMA_VIEW_CONFIG_DIFF*/,\n"
			+ "\t\tviewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,\n"
			+ "\t\tmodelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,\n"
			+ "\t\thandlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,\n"
			+ "\t\tconverters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,\n"
			+ "\t\tvalidators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/\n"
			+ "\t};\n"
			+ "});";
	}
}

/// <summary>What the merged record page says about adding the bar to it.</summary>
/// <param name="ErrorCode">A page refusal code, or <see langword="null"/> when the bar can be added.</param>
/// <param name="ErrorMessage">The refusal text.</param>
/// <param name="BarAlreadyPresent">The page already carries a stage progress bar.</param>
/// <param name="NamesTaken">The page already uses one of the fragment's element names.</param>
/// <param name="Template">The nearest template in the page's schema chain, or <see langword="null"/>.</param>
public sealed record ProgressBarPlacementCheck(
	string ErrorCode,
	string ErrorMessage,
	bool BarAlreadyPresent,
	bool NamesTaken,
	string Template);

/// <summary>
/// The read-only checks on a page bundle that decide whether and how the bar is added. Pure: the bundle comes from
/// <c>get-page</c>, so nothing here reaches the environment.
/// </summary>
public static class ProgressBarPlacement {

	private const string TabbedTemplatePrefix = "PageWithTabs";

	/// <summary>Checks the merged view tree and model of a record page against the case object.</summary>
	/// <param name="bundle">The merged page bundle.</param>
	/// <param name="entitySchemaName">The case object.</param>
	public static ProgressBarPlacementCheck Check(PageBundleInfo bundle, string entitySchemaName) {
		ArgumentNullException.ThrowIfNull(bundle);
		string template = FindTemplate(bundle.Schemas);
		string primaryEntity = ReadPrimaryEntity(bundle.ModelConfig);
		if (!string.Equals(primaryEntity, entitySchemaName, StringComparison.OrdinalIgnoreCase)) {
			// The bar shows the stages of its entityName's case and writes the stage column of the record the page
			// opens: on a page for another object those are two different records.
			string found = string.IsNullOrWhiteSpace(primaryEntity)
				? "declares no primary data source"
				: $"is for '{primaryEntity}'";
			return Refusal(DynamicCaseErrorCodes.PageEntityMismatch,
				$"The record page {found}, not for '{entitySchemaName}'. Pass page-schema-name with a record page of "
				+ $"'{entitySchemaName}'.", template);
		}
		List<(string Type, string Name)> elements = [.. ViewConfigTraversal.EnumerateNamedElements(bundle.ViewConfig)
			.Select(element => (element.Type, element.Name))];
		if (elements.Any(element => string.Equals(element.Type, ProgressBarFragment.BarType, StringComparison.Ordinal))) {
			return new ProgressBarPlacementCheck(null, null, true, false, template);
		}
		(string Type, string Name) parent = elements.FirstOrDefault(element =>
			string.Equals(element.Name, ProgressBarFragment.ParentName, StringComparison.Ordinal));
		if (!string.Equals(parent.Type, ProgressBarFragment.ParentType, StringComparison.Ordinal)) {
			string found = parent.Name is null ? "has no MainContainer" : $"has a MainContainer of type '{parent.Type}'";
			return Refusal(DynamicCaseErrorCodes.PlacementUnsupported,
				$"The record page {found}; the bar is placed at the top of a crt.FlexContainer named MainContainer, "
				+ "which a tabbed Freedom UI record page has and a mini page does not.", template);
		}
		bool namesTaken = elements.Any(element =>
			string.Equals(element.Name, ProgressBarFragment.ContainerName, StringComparison.Ordinal)
			|| string.Equals(element.Name, ProgressBarFragment.BarName, StringComparison.Ordinal));
		return new ProgressBarPlacementCheck(null, null, false, namesTaken, template);
	}

	/// <summary>Whether a template is one the product adds the bar to.</summary>
	/// <param name="template">The template name, or <see langword="null"/>.</param>
	public static bool IsTabbedTemplate(string template) =>
		template?.StartsWith(TabbedTemplatePrefix, StringComparison.OrdinalIgnoreCase) == true;

	private static ProgressBarPlacementCheck Refusal(string code, string message, string template) =>
		new(code, message, false, false, template);

	private static string FindTemplate(IReadOnlyList<PageSchemaChainEntry> chain) =>
		chain?.Select(entry => entry?.SchemaName)
			.FirstOrDefault(name => name?.EndsWith("Template", StringComparison.OrdinalIgnoreCase) == true);

	// The marker is authoritative; PDS is the conventional name a body that predates the marker uses.
	private static string ReadPrimaryEntity(JsonObject modelConfig) {
		if (modelConfig?["dataSources"] is not JsonObject dataSources) {
			return null;
		}
		string primaryName = ReadString(modelConfig, "primaryDataSourceName");
		JsonObject primary = (!string.IsNullOrWhiteSpace(primaryName) ? dataSources[primaryName] : null) as JsonObject
			?? dataSources["PDS"] as JsonObject;
		return primary?["config"] is JsonObject config ? ReadString(config, "entitySchemaName")?.Trim() : null;
	}

	private static string ReadString(JsonObject node, string key) =>
		node[key] is JsonValue value && value.TryGetValue(out string text) ? text : null;
}
