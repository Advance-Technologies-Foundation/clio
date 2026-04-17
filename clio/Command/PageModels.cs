namespace Clio.Command;

using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Newtonsoft.Json;
using JsonIgnoreAttribute = System.Text.Json.Serialization.JsonIgnoreAttribute;

/// <summary>
/// Represents a page item returned by <c>list-pages</c>.
/// </summary>
[DataContract]
public sealed class PageListItem {
	/// <summary>
	/// Gets or sets the schema name.
	/// </summary>
	[DataMember(Name = "schema-name")]
	[JsonProperty("schema-name")]
	[JsonPropertyName("schema-name")]
	public string SchemaName { get; set; }

	/// <summary>
	/// Gets or sets the schema identifier.
	/// </summary>
	[DataMember(Name = "uId")]
	[JsonProperty("uId")]
	[JsonPropertyName("uId")]
	public string UId { get; set; }

	/// <summary>
	/// Gets or sets the owning package name.
	/// </summary>
	[DataMember(Name = "packageName")]
	[JsonProperty("packageName")]
	[JsonPropertyName("packageName")]
	public string PackageName { get; set; }

	/// <summary>
	/// Gets or sets the direct parent schema name.
	/// </summary>
	[DataMember(Name = "parentSchemaName")]
	[JsonProperty("parentSchemaName")]
	[JsonPropertyName("parentSchemaName")]
	public string ParentSchemaName { get; set; }
}

/// <summary>
/// Represents the <c>list-pages</c> response envelope.
/// </summary>
[DataContract]
public sealed class PageListResponse {
	/// <summary>
	/// Gets or sets a value indicating whether the request succeeded.
	/// </summary>
	[DataMember(Name = "success")]
	[JsonProperty("success")]
	[JsonPropertyName("success")]
	public bool Success { get; set; }

	/// <summary>
	/// Gets or sets the number of returned pages.
	/// </summary>
	[DataMember(Name = "count")]
	[JsonProperty("count")]
	[JsonPropertyName("count")]
	public int Count { get; set; }

	/// <summary>
	/// Gets or sets the returned pages.
	/// </summary>
	[DataMember(Name = "pages")]
	[JsonProperty("pages")]
	[JsonPropertyName("pages")]
	public List<PageListItem> Pages { get; set; }

	/// <summary>
	/// Gets or sets the error message for failed requests.
	/// </summary>
	[DataMember(Name = "error")]
	[JsonProperty("error")]
	[JsonPropertyName("error")]
	public string Error { get; set; }
}

/// <summary>
/// Represents the <c>get-page</c> response envelope.
/// </summary>
public sealed class PageGetResponse {
	/// <summary>
	/// Gets or sets a value indicating whether the request succeeded.
	/// </summary>
	[JsonProperty("success")]
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	/// <summary>
	/// Gets or sets the page metadata.
	/// </summary>
	[JsonProperty("page")]
	[JsonPropertyName("page")]
	public PageMetadataInfo Page { get; init; }

	/// <summary>
	/// Gets or sets the merged bundle.
	/// </summary>
	[JsonProperty("bundle", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("bundle")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public PageBundleInfo Bundle { get; init; }

	/// <summary>
	/// Gets or sets the raw editable payload.
	/// </summary>
	[JsonProperty("raw", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("raw")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public PageRawInfo Raw { get; init; }

	/// <summary>
	/// Gets or sets the file paths written when the tool saves output to disk.
	/// </summary>
	[JsonProperty("files", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("files")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public PageGetFilesInfo Files { get; init; }

	/// <summary>
	/// Gets or sets the error message for failed requests.
	/// </summary>
	[JsonProperty("error")]
	[JsonPropertyName("error")]
	public string Error { get; init; }
}

/// <summary>
/// Represents page identity and ownership metadata.
/// </summary>
public sealed class PageMetadataInfo {
	/// <summary>
	/// Gets or sets the schema name.
	/// </summary>
	[JsonProperty("schemaName")]
	[JsonPropertyName("schemaName")]
	public string SchemaName { get; init; }

	/// <summary>
	/// Gets or sets the schema identifier.
	/// </summary>
	[JsonProperty("schemaUId")]
	[JsonPropertyName("schemaUId")]
	public string SchemaUId { get; init; }

	/// <summary>
	/// Gets or sets the owning package name.
	/// </summary>
	[JsonProperty("packageName")]
	[JsonPropertyName("packageName")]
	public string PackageName { get; init; }

	/// <summary>
	/// Gets or sets the owning package identifier.
	/// </summary>
	[JsonProperty("packageUId")]
	[JsonPropertyName("packageUId")]
	public string PackageUId { get; init; }

	/// <summary>
	/// Gets or sets the direct parent schema name.
	/// </summary>
	[JsonProperty("parentSchemaName")]
	[JsonPropertyName("parentSchemaName")]
	public string ParentSchemaName { get; init; }
}

/// <summary>
/// Represents the merged built page bundle.
/// </summary>
public sealed class PageBundleInfo {
	/// <summary>
	/// Gets or sets the current page schema name.
	/// </summary>
	[JsonProperty("name")]
	[JsonPropertyName("name")]
	public string Name { get; init; }

	/// <summary>
	/// Gets or sets the merged view configuration.
	/// </summary>
	[JsonProperty("viewConfig")]
	[JsonPropertyName("viewConfig")]
	public JsonArray ViewConfig { get; init; } = [];

	/// <summary>
	/// Gets or sets the merged view-model configuration.
	/// </summary>
	[JsonProperty("viewModelConfig")]
	[JsonPropertyName("viewModelConfig")]
	public JsonObject ViewModelConfig { get; init; } = [];

	/// <summary>
	/// Gets or sets the merged model configuration.
	/// </summary>
	[JsonProperty("modelConfig")]
	[JsonPropertyName("modelConfig")]
	public JsonObject ModelConfig { get; init; } = [];

	/// <summary>
	/// Gets or sets the merged resources object.
	/// </summary>
	[JsonProperty("resources")]
	[JsonPropertyName("resources")]
	public PageResourceInfo Resources { get; init; } = new();

	/// <summary>
	/// Gets or sets the current handlers source.
	/// </summary>
	[JsonProperty("handlers")]
	[JsonPropertyName("handlers")]
	public string Handlers { get; init; } = "[]";

	/// <summary>
	/// Gets or sets the current converters source.
	/// </summary>
	[JsonProperty("converters")]
	[JsonPropertyName("converters")]
	public string Converters { get; init; } = "{}";

	/// <summary>
	/// Gets or sets the current validators source.
	/// </summary>
	[JsonProperty("validators")]
	[JsonPropertyName("validators")]
	public string Validators { get; init; } = "{}";

	/// <summary>
	/// Gets or sets the merged page parameters.
	/// </summary>
	[JsonProperty("parameters")]
	[JsonPropertyName("parameters")]
	public IReadOnlyList<PageParameterInfo> Parameters { get; init; } = [];

	/// <summary>
	/// Gets or sets the current dependency list source.
	/// </summary>
	[JsonProperty("deps")]
	[JsonPropertyName("deps")]
	public string Deps { get; init; } = "[]";

	/// <summary>
	/// Gets or sets the current AMD argument list source.
	/// </summary>
	[JsonProperty("args")]
	[JsonPropertyName("args")]
	public string Args { get; init; } = "()";

	/// <summary>
	/// Gets or sets the merged optional properties.
	/// </summary>
	[JsonProperty("optionalProperties")]
	[JsonPropertyName("optionalProperties")]
	public JsonArray OptionalProperties { get; init; } = [];
}

/// <summary>
/// Represents merged page resources.
/// </summary>
public sealed class PageResourceInfo {
	/// <summary>
	/// Gets or sets the merged string resources keyed by resource and culture.
	/// </summary>
	[JsonProperty("strings")]
	[JsonPropertyName("strings")]
	public JsonObject Strings { get; init; } = [];
}

/// <summary>
/// Represents the raw editable page payload.
/// </summary>
public sealed class PageRawInfo {
	/// <summary>
	/// Gets or sets the original JavaScript body.
	/// </summary>
	[JsonProperty("body")]
	[JsonPropertyName("body")]
	public string Body { get; init; }
}

/// <summary>
/// Represents file paths written by <c>get-page</c> when saving output to disk.
/// </summary>
public sealed class PageGetFilesInfo {
	[JsonProperty("bodyFile")]
	[JsonPropertyName("bodyFile")]
	public string BodyFile { get; init; }

	[JsonProperty("bundleFile")]
	[JsonPropertyName("bundleFile")]
	public string BundleFile { get; init; }

	[JsonProperty("metaFile")]
	[JsonPropertyName("metaFile")]
	public string MetaFile { get; init; }
}

/// <summary>
/// Represents a merged page parameter.
/// </summary>
public sealed class PageParameterInfo {
	/// <summary>
	/// Gets or sets the parameter identifier.
	/// </summary>
	[JsonProperty("uId")]
	[JsonPropertyName("uId")]
	public string UId { get; init; }

	/// <summary>
	/// Gets or sets the parameter name.
	/// </summary>
	[JsonProperty("name")]
	[JsonPropertyName("name")]
	public string Name { get; init; }

	/// <summary>
	/// Gets or sets the parameter caption payload.
	/// </summary>
	[JsonProperty("caption")]
	[JsonPropertyName("caption")]
	public JsonNode Caption { get; init; }

	/// <summary>
	/// Gets or sets the parameter data value type.
	/// </summary>
	[JsonProperty("dataValueType")]
	[JsonPropertyName("dataValueType")]
	public int? DataValueType { get; init; }

	/// <summary>
	/// Gets or sets a value indicating whether the parameter is required.
	/// </summary>
	[JsonProperty("required")]
	[JsonPropertyName("required")]
	public bool Required { get; init; }

	/// <summary>
	/// Gets or sets a value indicating whether the parameter belongs to the current schema.
	/// </summary>
	[JsonProperty("isOwnParameter")]
	[JsonPropertyName("isOwnParameter")]
	public bool IsOwnParameter { get; init; }

	/// <summary>
	/// Gets or sets the lookup schema identifier.
	/// </summary>
	[JsonProperty("referenceSchemaUId")]
	[JsonPropertyName("referenceSchemaUId")]
	public string ReferenceSchemaUId { get; init; }

	/// <summary>
	/// Gets or sets the lookup schema name.
	/// </summary>
	[JsonProperty("referenceSchemaName")]
	[JsonPropertyName("referenceSchemaName")]
	public string ReferenceSchemaName { get; init; }
}

/// <summary>
/// Represents the result of an AI semantic review performed before saving a page body.
/// </summary>
public sealed class PageSamplingReview {

	[JsonPropertyName("ok")]
	public bool Ok { get; init; }

	[JsonPropertyName("issues")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string> Issues { get; init; }

	[JsonPropertyName("warnings")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string> Warnings { get; init; }

	[JsonPropertyName("skipped")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public bool Skipped { get; init; }
}

/// <summary>
/// Represents the <c>update-page</c> response envelope.
/// </summary>
[DataContract]
public sealed class PageUpdateResponse {
	/// <summary>
	/// Gets or sets a value indicating whether the request succeeded.
	/// </summary>
	[DataMember(Name = "success")]
	[JsonProperty("success")]
	[JsonPropertyName("success")]
	public bool Success { get; set; }

	/// <summary>
	/// Gets or sets the page schema name.
	/// </summary>
	[DataMember(Name = "schemaName")]
	[JsonProperty("schemaName")]
	[JsonPropertyName("schemaName")]
	public string SchemaName { get; set; }

	/// <summary>
	/// Gets or sets the length of the submitted body.
	/// </summary>
	[DataMember(Name = "bodyLength")]
	[JsonProperty("bodyLength")]
	[JsonPropertyName("bodyLength")]
	public int BodyLength { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the call was a dry run.
	/// </summary>
	[DataMember(Name = "dryRun")]
	[JsonProperty("dryRun")]
	[JsonPropertyName("dryRun")]
	public bool DryRun { get; set; }

	[DataMember(Name = "error")]
	[JsonProperty("error")]
	[JsonPropertyName("error")]
	public string Error { get; set; }

	[DataMember(Name = "resourcesRegistered")]
	[JsonProperty("resourcesRegistered")]
	[JsonPropertyName("resourcesRegistered")]
	public int ResourcesRegistered { get; set; }

	[DataMember(Name = "registeredResourceKeys")]
	[JsonProperty("registeredResourceKeys", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("registeredResourceKeys")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
	public List<string> RegisteredResourceKeys { get; set; }

	[JsonProperty("samplingReview", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("samplingReview")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
	public PageSamplingReview SamplingReview { get; set; }
}

public sealed record FormFieldSpec(
	[property: JsonPropertyName("path")] string Path,
	[property: JsonPropertyName("type")] string Type,
	[property: JsonPropertyName("name")] string? Name = null,
	[property: JsonPropertyName("attr-key")] string? AttrKey = null,
	[property: JsonPropertyName("label")] string? Label = null,
	[property: JsonPropertyName("parent-name")] string? ParentName = null,
	[property: JsonPropertyName("picker-type")] string? PickerType = null,
	[property: JsonPropertyName("multiline")] bool? Multiline = null,
	[property: JsonPropertyName("decimal-precision")] int? DecimalPrecision = null
);

public sealed record ListColumnSpec(
	[property: JsonPropertyName("code")] string Code,
	[property: JsonPropertyName("data-value-type")] int DataValueType,
	[property: JsonPropertyName("caption")] string? Caption = null,
	[property: JsonPropertyName("width")] int? Width = null,
	[property: JsonPropertyName("id")] string? Id = null
);

public sealed class PageAddFieldsResponse {
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	[JsonPropertyName("schema-name")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string SchemaName { get; init; }

	[JsonPropertyName("body-length")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public int BodyLength { get; init; }

	[JsonPropertyName("fields-added")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public int FieldsAdded { get; init; }

	[JsonPropertyName("resources-registered")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public int ResourcesRegistered { get; init; }

	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Error { get; init; }

	[JsonPropertyName("sampling-review")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public PageSamplingReview SamplingReview { get; init; }
}

public sealed class PageAddColumnsResponse {
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	[JsonPropertyName("schema-name")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string SchemaName { get; init; }

	[JsonPropertyName("body-length")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public int BodyLength { get; init; }

	[JsonPropertyName("columns-added")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public int ColumnsAdded { get; init; }

	[JsonPropertyName("resources-registered")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public int ResourcesRegistered { get; init; }

	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Error { get; init; }

	[JsonPropertyName("sampling-review")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public PageSamplingReview SamplingReview { get; init; }
}
