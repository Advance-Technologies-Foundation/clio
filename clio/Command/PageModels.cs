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

	/// <summary>
	/// Gets or sets the summary of operations stored in this schema's own body
	/// (exclusive of inherited operations). Useful for AI callers to decide whether
	/// the schema is "lightly customized" (few ops) or "heavily customized" (many ops)
	/// and to avoid re-sending the full body on update-page.
	/// </summary>
	[JsonProperty("ownBodySummary")]
	[JsonPropertyName("ownBodySummary")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
	public PageOwnBodySummary OwnBodySummary { get; init; }

	/// <summary>
	/// Gets or sets the design package identifier that subsequent <c>update-page</c> writes
	/// will target when <c>mode</c> is not explicitly overridden. Equals the value returned by
	/// <c>ApplicationPackagesService.svc/GetDesignPackageUId</c> for this schema.
	/// </summary>
	[JsonProperty("designPackageUId")]
	[JsonPropertyName("designPackageUId")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
	public string DesignPackageUId { get; init; }

	/// <summary>
	/// Gets or sets the design package name for <see cref="DesignPackageUId"/>. May be empty
	/// for virtual packages that have not yet been materialized in the database.
	/// </summary>
	[JsonProperty("designPackageName")]
	[JsonPropertyName("designPackageName")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
	public string DesignPackageName { get; init; }

	/// <summary>
	/// When <c>true</c>, <see cref="DesignPackageUId"/> differs from <see cref="PackageUId"/>
	/// which means a subsequent write will materialize a NEW replacing schema in the design
	/// package. Callers should warn the user because the edit may land in a different app than
	/// the one currently shown at runtime when multiple apps replace the same platform page.
	/// </summary>
	[JsonProperty("willCreateReplacingInDesignPackage")]
	[JsonPropertyName("willCreateReplacingInDesignPackage")]
	public bool WillCreateReplacingInDesignPackage { get; init; }

	/// <summary>
	/// Gets or sets the root schema identifier — the base schema in the hierarchy that all
	/// replacing schemas ultimately extend. Pass this as the parent when creating a new
	/// replacing schema via update-page to match the designer's ApplyParent behaviour.
	/// </summary>
	[JsonProperty("rootSchemaUId")]
	[JsonPropertyName("rootSchemaUId")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
	public string RootSchemaUId { get; init; }
}

/// <summary>
/// Describes how many operations are present in the current schema's own body per section.
/// </summary>
public sealed class PageOwnBodySummary {
	/// <summary>
	/// Number of operations in the schema's own <c>viewConfigDiff</c>.
	/// </summary>
	[JsonProperty("viewConfigDiffOperations")]
	[JsonPropertyName("viewConfigDiffOperations")]
	public int ViewConfigDiffOperations { get; init; }

	/// <summary>
	/// Number of operations in the schema's own <c>viewModelConfigDiff</c>.
	/// </summary>
	[JsonProperty("viewModelConfigDiffOperations")]
	[JsonPropertyName("viewModelConfigDiffOperations")]
	public int ViewModelConfigDiffOperations { get; init; }

	/// <summary>
	/// Number of operations in the schema's own <c>modelConfigDiff</c>.
	/// </summary>
	[JsonProperty("modelConfigDiffOperations")]
	[JsonPropertyName("modelConfigDiffOperations")]
	public int ModelConfigDiffOperations { get; init; }

	/// <summary>
	/// Number of handler entries registered in the schema.
	/// </summary>
	[JsonProperty("handlerEntries")]
	[JsonPropertyName("handlerEntries")]
	public int HandlerEntries { get; init; }

	/// <summary>
	/// Length of the schema's own raw body in characters. A small value (&lt; 1000) indicates
	/// an empty replacing schema where `raw.body` is safe to resend; a large value is a signal
	/// to send only new operations rather than the full body.
	/// </summary>
	[JsonProperty("bodyLength")]
	[JsonPropertyName("bodyLength")]
	public int BodyLength { get; init; }

	/// <summary>
	/// Flat list of <c>viewConfigDiff</c> operations present in the schema's own body.
	/// Each entry exposes <c>name</c>, <c>operation</c>, <c>type</c>, and <c>parentName</c> so AI
	/// callers can see which components already exist before composing a new delta.
	/// </summary>
	[JsonProperty("viewConfigDiffOps")]
	[JsonPropertyName("viewConfigDiffOps")]
	public IReadOnlyList<PageOperationInfo> ViewConfigDiffOps { get; init; } = [];

	/// <summary>
	/// Flat list of handler requests registered in the schema's own body.
	/// </summary>
	[JsonProperty("handlerRequests")]
	[JsonPropertyName("handlerRequests")]
	public IReadOnlyList<string> HandlerRequests { get; init; } = [];
}

/// <summary>
/// Describes a single <c>viewConfigDiff</c> operation entry from the schema body.
/// </summary>
public sealed class PageOperationInfo {
	[JsonProperty("operation")]
	[JsonPropertyName("operation")]
	public string Operation { get; init; }

	[JsonProperty("name")]
	[JsonPropertyName("name")]
	public string Name { get; init; }

	[JsonProperty("type")]
	[JsonPropertyName("type")]
	public string Type { get; init; }

	[JsonProperty("parentName")]
	[JsonPropertyName("parentName")]
	public string ParentName { get; init; }
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

	/// <summary>
	/// Gets or sets the flattened list of containers discovered in <see cref="ViewConfig"/>.
	/// Each entry exposes <c>name</c>, <c>type</c>, <c>childCount</c> and <c>path</c> so AI callers
	/// can pick a valid <c>parentName</c> when composing <c>viewConfigDiff</c> entries without walking
	/// the full tree manually.
	/// </summary>
	[JsonProperty("containers")]
	[JsonPropertyName("containers")]
	public IReadOnlyList<PageContainerInfo> Containers { get; init; } = [];

	/// <summary>
	/// Gets or sets the full inheritance chain ordered from HEAD (most-derived) to ROOT.
	/// Includes ALL schemas even those with no readable body (compiled platform schemas show
	/// <c>hasBody: false</c>). Use this list to understand which packages contribute to the page
	/// and to locate inherited fields that are not visible in <see cref="ViewConfig"/>.
	/// </summary>
	[JsonProperty("schemas")]
	[JsonPropertyName("schemas")]
	public IReadOnlyList<PageSchemaChainEntry> Schemas { get; init; } = [];
}

/// <summary>
/// Describes a single container node discovered in the merged <c>viewConfig</c>.
/// </summary>
public sealed class PageContainerInfo {
	/// <summary>
	/// Gets the container name — value to use as <c>parentName</c> in <c>viewConfigDiff</c>.
	/// </summary>
	[JsonProperty("name")]
	[JsonPropertyName("name")]
	public string Name { get; init; }

	/// <summary>
	/// Gets the component type (e.g. <c>crt.FlexContainer</c>, <c>crt.Grid</c>).
	/// </summary>
	[JsonProperty("type")]
	[JsonPropertyName("type")]
	public string Type { get; init; }

	/// <summary>
	/// Gets the number of existing children in the container.
	/// </summary>
	[JsonProperty("childCount")]
	[JsonPropertyName("childCount")]
	public int ChildCount { get; init; }

	/// <summary>
	/// Gets the ancestor chain path (names joined by <c>/</c>) for disambiguation when the same
	/// <c>name</c> appears in multiple branches.
	/// </summary>
	[JsonProperty("path")]
	[JsonPropertyName("path")]
	public string Path { get; init; }
}

/// <summary>
/// Represents one schema in the page inheritance chain.
/// </summary>
public sealed class PageSchemaChainEntry {
	[JsonProperty("schemaUId")]
	[JsonPropertyName("schemaUId")]
	public string SchemaUId { get; init; }

	[JsonProperty("schemaName")]
	[JsonPropertyName("schemaName")]
	public string SchemaName { get; init; }

	[JsonProperty("packageUId")]
	[JsonPropertyName("packageUId")]
	public string PackageUId { get; init; }

	[JsonProperty("packageName")]
	[JsonPropertyName("packageName")]
	public string PackageName { get; init; }

	/// <summary>
	/// Gets a value indicating whether this schema has a readable body. When <c>false</c> the
	/// schema is compiled or empty — its fields are not reflected in <c>viewConfig</c>. Look up
	/// inherited fields in the package store (~Projects/ps) using the package name.
	/// </summary>
	[JsonProperty("hasBody")]
	[JsonPropertyName("hasBody")]
	public bool HasBody { get; init; }
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

	[JsonProperty("page", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("page")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
	public PageMetadataInfo? Page { get; set; }
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

/// <summary>
/// Represents a single Freedom UI template entry from <c>list-page-templates</c> / <c>create-page</c>.
/// </summary>
[DataContract]
public sealed class PageTemplateInfo {
	[DataMember(Name = "uId")]
	[JsonProperty("uId")]
	[JsonPropertyName("uId")]
	public string UId { get; set; }

	[DataMember(Name = "name")]
	[JsonProperty("name")]
	[JsonPropertyName("name")]
	public string Name { get; set; }

	[DataMember(Name = "title")]
	[JsonProperty("title")]
	[JsonPropertyName("title")]
	public string Title { get; set; }

	[DataMember(Name = "groupName")]
	[JsonProperty("groupName")]
	[JsonPropertyName("groupName")]
	public string GroupName { get; set; }

	[DataMember(Name = "schemaType")]
	[JsonProperty("schemaType")]
	[JsonPropertyName("schemaType")]
	public int SchemaType { get; set; }
}

/// <summary>
/// Represents the <c>list-page-templates</c> response envelope.
/// </summary>
[DataContract]
public sealed class PageTemplateListResponse {
	[DataMember(Name = "success")]
	[JsonProperty("success")]
	[JsonPropertyName("success")]
	public bool Success { get; set; }

	[DataMember(Name = "count")]
	[JsonProperty("count")]
	[JsonPropertyName("count")]
	public int Count { get; set; }

	[DataMember(Name = "items")]
	[JsonProperty("items")]
	[JsonPropertyName("items")]
	public List<PageTemplateInfo> Items { get; set; }

	[DataMember(Name = "error")]
	[JsonProperty("error")]
	[JsonPropertyName("error")]
	public string Error { get; set; }
}

/// <summary>
/// Represents the <c>create-page</c> response envelope.
/// </summary>
[DataContract]
public sealed class PageCreateResponse {
	[DataMember(Name = "success")]
	[JsonProperty("success")]
	[JsonPropertyName("success")]
	public bool Success { get; set; }

	[DataMember(Name = "schemaName")]
	[JsonProperty("schemaName")]
	[JsonPropertyName("schemaName")]
	public string SchemaName { get; set; }

	[DataMember(Name = "schemaUId")]
	[JsonProperty("schemaUId")]
	[JsonPropertyName("schemaUId")]
	public string SchemaUId { get; set; }

	[DataMember(Name = "packageName")]
	[JsonProperty("packageName")]
	[JsonPropertyName("packageName")]
	public string PackageName { get; set; }

	[DataMember(Name = "packageUId")]
	[JsonProperty("packageUId")]
	[JsonPropertyName("packageUId")]
	public string PackageUId { get; set; }

	[DataMember(Name = "templateName")]
	[JsonProperty("templateName")]
	[JsonPropertyName("templateName")]
	public string TemplateName { get; set; }

	[DataMember(Name = "templateUId")]
	[JsonProperty("templateUId")]
	[JsonPropertyName("templateUId")]
	public string TemplateUId { get; set; }

	[DataMember(Name = "caption")]
	[JsonProperty("caption", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("caption")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Caption { get; set; }

	[DataMember(Name = "entitySchemaName")]
	[JsonProperty("entitySchemaName", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("entitySchemaName")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string EntitySchemaName { get; set; }

	[DataMember(Name = "entitySchemaUId")]
	[JsonProperty("entitySchemaUId", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("entitySchemaUId")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string EntitySchemaUId { get; set; }

	[DataMember(Name = "error")]
	[JsonProperty("error")]
	[JsonPropertyName("error")]
	public string Error { get; set; }
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
