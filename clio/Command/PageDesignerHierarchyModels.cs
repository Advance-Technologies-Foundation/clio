namespace Clio.Command;

using System.Collections.Generic;
using Newtonsoft.Json.Linq;

/// <summary>
/// Loads the Freedom UI page designer hierarchy from Creatio.
/// </summary>
public interface IPageDesignerHierarchyClient {
	/// <summary>
	/// Gets the full page schema hierarchy for the specified schema and package.
	/// </summary>
	/// <param name="schemaUId">Schema identifier.</param>
	/// <param name="packageUId">Package identifier.</param>
	/// <returns>Hierarchy schemas returned by the designer service.</returns>
	IReadOnlyList<PageDesignerHierarchySchema> GetParentSchemas(string schemaUId, string packageUId);
}

/// <summary>
/// Represents a single page schema returned by the designer hierarchy service.
/// </summary>
public sealed class PageDesignerHierarchySchema {
	/// <summary>
	/// Gets or sets the schema identifier.
	/// </summary>
	public string UId { get; init; }

	/// <summary>
	/// Gets or sets the schema name.
	/// </summary>
	public string Name { get; init; }

	/// <summary>
	/// Gets or sets the package identifier.
	/// </summary>
	public string PackageUId { get; init; }

	/// <summary>
	/// Gets or sets the package name.
	/// </summary>
	public string PackageName { get; init; }

	/// <summary>
	/// Gets or sets the schema version.
	/// </summary>
	public int SchemaVersion { get; init; }

	/// <summary>
	/// Gets or sets the raw schema body.
	/// </summary>
	public string Body { get; init; }

	/// <summary>
	/// Gets or sets the page parameters.
	/// </summary>
	public JArray Parameters { get; init; } = new();

	/// <summary>
	/// Gets or sets the localizable strings.
	/// </summary>
	public JArray LocalizableStrings { get; init; } = new();

	/// <summary>
	/// Gets or sets the optional properties.
	/// </summary>
	public JArray OptionalProperties { get; init; } = new();
}

/// <summary>
/// Represents one parsed hierarchy part used to build a merged page bundle.
/// </summary>
public sealed class PageSchemaBundlePart {
	/// <summary>
	/// Initializes a new instance of the <see cref="PageSchemaBundlePart"/> class.
	/// </summary>
	/// <param name="schema">Hierarchy schema metadata.</param>
	/// <param name="parsedBody">Parsed body sections.</param>
	public PageSchemaBundlePart(PageDesignerHierarchySchema schema, PageParsedSchemaBody parsedBody) {
		Schema = schema;
		ParsedBody = parsedBody;
	}

	/// <summary>
	/// Gets the hierarchy schema metadata.
	/// </summary>
	public PageDesignerHierarchySchema Schema { get; }

	/// <summary>
	/// Gets the parsed schema body sections.
	/// </summary>
	public PageParsedSchemaBody ParsedBody { get; }
}

/// <summary>
/// Represents parsed Freedom UI schema markers extracted from the raw body.
/// </summary>
public sealed class PageParsedSchemaBody {
	/// <summary>
	/// Gets or sets the view config diff.
	/// </summary>
	public JToken ViewConfigDiff { get; init; } = new JArray();

	/// <summary>
	/// Gets or sets the view model config.
	/// </summary>
	public JToken ViewModelConfig { get; init; } = new JObject();

	/// <summary>
	/// Gets or sets the view model config diff.
	/// </summary>
	public JToken ViewModelConfigDiff { get; init; } = new JArray();

	/// <summary>
	/// Gets or sets the model config.
	/// </summary>
	public JToken ModelConfig { get; init; } = new JObject();

	/// <summary>
	/// Gets or sets the model config diff.
	/// </summary>
	public JToken ModelConfigDiff { get; init; } = new JArray();

	/// <summary>
	/// Gets or sets the handlers block.
	/// </summary>
	public string Handlers { get; init; } = "[]";

	/// <summary>
	/// Gets or sets the converters block.
	/// </summary>
	public string Converters { get; init; } = "{}";

	/// <summary>
	/// Gets or sets the validators block.
	/// </summary>
	public string Validators { get; init; } = "{}";

	/// <summary>
	/// Gets or sets the deps block.
	/// </summary>
	public string Deps { get; init; } = "[]";

	/// <summary>
	/// Gets or sets the args block.
	/// </summary>
	public string Args { get; init; } = "()";
}
