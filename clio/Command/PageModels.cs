namespace Clio.Command;

using System;
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
	/// Gets or sets the number of returned pages (after the result cap is applied).
	/// </summary>
	[DataMember(Name = "count")]
	[JsonProperty("count")]
	[JsonPropertyName("count")]
	public int Count { get; set; }

	/// <summary>
	/// Gets or sets the known number of pages matching the query before the requested result limit
	/// is applied. When a bounded fallback reaches its safety cap, this is the number observed and
	/// <see cref="Truncated"/> is <c>true</c> because the complete total cannot be proved.
	/// </summary>
	[DataMember(Name = "total")]
	[JsonProperty("total")]
	[JsonPropertyName("total")]
	public int Total { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the result was truncated by the requested limit or
	/// a bounded fallback reached its safety cap. When <c>true</c>, raise the limit or add a filter
	/// to retrieve the remaining pages.
	/// </summary>
	[DataMember(Name = "truncated")]
	[JsonProperty("truncated")]
	[JsonPropertyName("truncated")]
	public bool Truncated { get; set; }

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
	/// Gets or sets the merged bundle. CLI-only — the MCP tool writes it to <c>bundle.json</c> instead.
	/// </summary>
	[JsonProperty("bundle", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("bundle")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[CliOnlyEnvelopeProperty]
	public PageBundleInfo Bundle { get; init; }

	/// <summary>
	/// Gets or sets the raw editable payload. CLI-only — the MCP tool writes it to <c>body.js</c> instead.
	/// </summary>
	[JsonProperty("raw", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("raw")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[CliOnlyEnvelopeProperty]
	public PageRawInfo Raw { get; init; }

	/// <summary>
	/// Gets or sets the file paths written when the tool saves output to disk.
	/// </summary>
	[JsonProperty("files", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("files")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public PageGetFilesInfo Files { get; init; }

	/// <summary>
	/// Gets or sets the editable (own) schema state captured at fetch time — the conflict-detection
	/// baseline source for subsequent <c>update-page</c> / <c>sync-pages</c> calls. <c>null</c> when
	/// the checksum query failed (best-effort capture, FR-10).
	/// </summary>
	[JsonProperty("editable", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("editable")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public PageEditableSchemaInfo Editable { get; init; }

	/// <summary>
	/// The merged <c>viewModelConfig</c> EXCLUDING the editable schema's own body — the runtime base a
	/// replace-mode write layers over. Populated only when <see cref="PageGetOptions.ExcludeOwnBody"/> is set
	/// (the mobile replace-mode validation base); <c>null</c> otherwise. An in-process validation channel only —
	/// never serialized into the get-page payload.
	/// </summary>
	[Newtonsoft.Json.JsonIgnore]
	[System.Text.Json.Serialization.JsonIgnore]
	public JsonObject BaseViewModelConfig { get; init; }

	/// <summary>
	/// The merged <c>modelConfig</c> EXCLUDING the editable schema's own body — the replace-mode validation
	/// base counterpart to <see cref="BaseViewModelConfig"/>. Same population and non-serialization rules.
	/// </summary>
	[Newtonsoft.Json.JsonIgnore]
	[System.Text.Json.Serialization.JsonIgnore]
	public JsonObject BaseModelConfig { get; init; }

	/// <summary>
	/// Gets or sets the error message for failed requests.
	/// </summary>
	[JsonProperty("error")]
	[JsonPropertyName("error")]
	public string Error { get; init; }
}

/// <summary>
/// Marks a serialized response property that the CLI envelope carries but the MCP tool NEVER sets, so the
/// published MCP tool contract must not describe it. Issue #1185 was exactly this split going undeclared: the
/// contract promised <c>raw.body</c> that the MCP envelope never returns. Declaring it on the property makes
/// the split machine-checkable - the contract oracle derives the expected field set from the type and skips
/// what is marked here, so a NEW property added to the response is required in the contract by default, and
/// only a deliberate CLI-only addition carries this attribute.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class CliOnlyEnvelopePropertyAttribute : Attribute { }

/// <summary>
/// Describes the editable (own) schema state at fetch time: whether a replacing schema already
/// exists in the design package and, when it does, its identity and change signal.
/// </summary>
public sealed class PageEditableSchemaInfo {
	/// <summary>
	/// Gets or sets a value indicating whether the editable schema exists in the design package.
	/// <c>false</c> means a subsequent write will create a new replacing schema.
	/// </summary>
	[JsonProperty("editableSchemaExists")]
	[JsonPropertyName("editableSchemaExists")]
	public bool EditableSchemaExists { get; init; }

	/// <summary>
	/// Gets or sets the editable schema identifier. <c>null</c> when the schema does not exist yet.
	/// </summary>
	[JsonProperty("editableSchemaUId", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("editableSchemaUId")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string EditableSchemaUId { get; init; }

	/// <summary>
	/// Gets or sets the <c>SysSchema.Checksum</c> value at fetch time. <c>null</c> when the schema
	/// does not exist yet.
	/// </summary>
	[JsonProperty("checksum", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("checksum")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Checksum { get; init; }

	/// <summary>
	/// Gets or sets the raw <c>SysSchema.ModifiedOn</c> value at fetch time. Opaque informational
	/// string — its serialization format varies between Creatio versions, so it never participates
	/// in conflict comparison.
	/// </summary>
	[JsonProperty("modifiedOn", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("modifiedOn")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string ModifiedOn { get; init; }
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
	/// Gets or sets the current hierarchy leaf's package name, which may identify a read-only vendor package.
	/// </summary>
	[JsonProperty("packageName")]
	[JsonPropertyName("packageName")]
	public string PackageName { get; init; }

	/// <summary>
	/// Gets the current hierarchy leaf's package name. Explicit alias of <see cref="PackageName"/>;
	/// it identifies the read source, not the destination of a subsequent write.
	/// </summary>
	[JsonProperty("currentLeafPackageName")]
	[JsonPropertyName("currentLeafPackageName")]
	public string CurrentLeafPackageName => PackageName;

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
	/// resolve when no target-package override is supplied. Reads can fall back to the leaf if
	/// design resolution fails; writes resolve the destination independently and fail closed.
	/// </summary>
	[JsonProperty("designPackageUId")]
	[JsonPropertyName("designPackageUId")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
	public string DesignPackageUId { get; init; }

	/// <summary>
	/// Gets or sets the stored or virtual design package name for <see cref="DesignPackageUId"/>.
	/// May be empty when the optional package metadata lookup is unavailable.
	/// </summary>
	[JsonProperty("designPackageName")]
	[JsonPropertyName("designPackageName")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
	public string DesignPackageName { get; init; }

	/// <summary>
	/// Gets whether a subsequent default write needs to create a replacing schema in the design
	/// package. The package itself may already exist or may be virtual until that first save.
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

	/// <summary>
	/// Gets or sets a human-readable schema type label: <c>"mobile"</c> when the schema type is 10,
	/// <c>"web"</c> when 9, or <c>"unknown"</c> otherwise.
	/// Callers should call <c>get-guidance</c> with name <c>mobile-page-modification</c> when this is <c>"mobile"</c>.
	/// </summary>
	[JsonProperty("schema-type")]
	[JsonPropertyName("schema-type")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
	public string SchemaType { get; init; }

	/// <summary>
	/// Gets or sets the RAW numeric <c>ClientUnitSchemaType</c> the hierarchy service reported, before the
	/// web/mobile/unknown collapse — null when the service omitted it. The label above folds "present but neither
	/// web nor mobile" (a Classic page, a module) and "absent" into one <c>unknown</c>, and a consumer that must
	/// tell those apart (the process-page-facts guard) needs the difference: a PRESENT non-web value is a positive
	/// identification, an absent one is not.
	/// </summary>
	[JsonProperty("schema-type-value", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("schema-type-value")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
	public int? SchemaTypeValue { get; init; }
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

	/// <summary>
	/// Gets or sets the ISO-8601 UTC timestamp when these files were written to disk.
	/// Callers should treat the on-disk files as stale whenever <c>fetchedAt</c> in the
	/// response differs from the value they observed previously; <c>get-page</c> wipes
	/// the schema directory on every invocation so the presence of a stale timestamp
	/// means the caller is inspecting a cache that has since been replaced.
	/// </summary>
	[JsonProperty("fetchedAt")]
	[JsonPropertyName("fetchedAt")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
	public string FetchedAt { get; init; }
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
/// What an append merge would do — or did — to a page's <c>viewConfigDiff</c> array.
/// </summary>
/// <remarks>
/// Exists so <c>--dry-run</c> can answer "what will this write change?" before the write happens
/// (GitHub #1150: the dry run reported <c>success</c> and named nothing). Produced as a by-product of
/// the one real merge in <c>PageBodyMerger</c>, never by a second predictor that could drift from the
/// merge's identity rules.
/// <para>
/// THREE distinct loss channels, because the fix differs for each: <see cref="DroppedOperations"/>
/// (the server body's), <see cref="CollapsedIncomingOperations"/> (the caller's own fragment), and
/// <see cref="ViewConfigDiffApplied"/> (the merged array never reaches the body at all). Do not read
/// any one of them as the whole story — an earlier version of this type claimed the dropped set was
/// the only way an append loses an operation, and that was false.
/// </para>
/// <para>
/// Scoped to <c>viewConfigDiff</c> deliberately: it is the only section merged by operation identity,
/// and the only one this projection speaks for. The sibling <c>*_DIFF</c> arrays append
/// unconditionally, so they cannot lose a current entry. Handlers are a different case and NOT covered
/// — say so rather than implying otherwise: converters key on property name and replace, but
/// <c>MergeHandlersRaw</c> drops EVERY current handler whose <c>request</c> appears in the fragment, so
/// a current body carrying that request twice keeps neither and the fragment contributes one. That is
/// the same shape of quiet loss this projection exists to report, in a section it does not read.
/// Widening it means giving the raw handler-text merge a structured identity first; until then,
/// reporting zeros for handlers would read as coverage.
/// </para>
/// </remarks>
[DataContract]
public sealed class PageAppendProjection {

	/// <summary>
	/// Gets the number of <c>viewConfigDiff</c> operations in the schema's current body.
	/// </summary>
	[DataMember(Name = "currentOperationCount")]
	[JsonProperty("currentOperationCount")]
	[JsonPropertyName("currentOperationCount")]
	public int CurrentOperationCount { get; init; }

	/// <summary>
	/// Gets the number of <c>viewConfigDiff</c> operations in the incoming fragment.
	/// </summary>
	[DataMember(Name = "incomingOperationCount")]
	[JsonProperty("incomingOperationCount")]
	[JsonPropertyName("incomingOperationCount")]
	public int IncomingOperationCount { get; init; }

	/// <summary>
	/// Gets the number of <c>viewConfigDiff</c> operations the merged body carries. NOT necessarily
	/// current + incoming: an incoming entry that replaces a current one adds nothing to the total, a
	/// dropped or collapsed entry subtracts from it, and an entry with no usable identity is carried
	/// without being counted as added. This is the number to compare against the one you expect.
	/// </summary>
	[DataMember(Name = "projectedOperationCount")]
	[JsonProperty("projectedOperationCount")]
	[JsonPropertyName("projectedOperationCount")]
	public int ProjectedOperationCount { get; init; }

	/// <summary>
	/// Gets the number of incoming operations that add a new identity rather than replace a current one.
	/// </summary>
	[DataMember(Name = "addedOperationCount")]
	[JsonProperty("addedOperationCount")]
	[JsonPropertyName("addedOperationCount")]
	public int AddedOperationCount { get; init; }

	/// <summary>
	/// Gets the current operations the incoming fragment replaces in place, as <c>verb name</c> labels.
	/// NOT a loss — the operation survives carrying the caller's values instead of the server's. Capped
	/// in length; <see cref="ReplacedOperationCount"/> is exact.
	/// </summary>
	[DataMember(Name = "replacedOperations")]
	[JsonProperty("replacedOperations", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("replacedOperations")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string> ReplacedOperations { get; init; }

	/// <summary>
	/// Gets the exact number of replaced operations, which may exceed <see cref="ReplacedOperations"/>.
	/// </summary>
	[DataMember(Name = "replacedOperationCount")]
	[JsonProperty("replacedOperationCount")]
	[JsonPropertyName("replacedOperationCount")]
	public int ReplacedOperationCount { get; init; }

	/// <summary>
	/// Gets the CURRENT operations the merge would not carry over: a FURTHER current entry of an identity
	/// the fragment already superseded, dropped rather than re-applied after the replacement. Empty for
	/// the overwhelming majority of appends. Capped in length; <see cref="DroppedOperationCount"/> is
	/// exact, and <see cref="SupersededDropWarnings"/> carries the actionable sentence per identity.
	/// </summary>
	[DataMember(Name = "droppedOperations")]
	[JsonProperty("droppedOperations", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("droppedOperations")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string> DroppedOperations { get; init; }

	/// <summary>
	/// Gets the exact number of dropped operations, which may exceed <see cref="DroppedOperations"/>.
	/// </summary>
	[DataMember(Name = "droppedOperationCount")]
	[JsonProperty("droppedOperationCount")]
	[JsonPropertyName("droppedOperationCount")]
	public int DroppedOperationCount { get; init; }

	/// <summary>
	/// Gets the INCOMING operations the fragment supersedes with a later entry of the same identity, so
	/// the earlier one never reaches the merged body. Capped in length;
	/// <see cref="CollapsedIncomingOperationCount"/> is exact.
	/// </summary>
	/// <remarks>
	/// The caller-side mirror of <see cref="DroppedOperations"/>, and the one people are most likely to
	/// hit: these are the CALLER'S OWN operations, lost to their own fragment carrying one identity
	/// twice. Reported here but deliberately NOT warned about — the fragment is the caller's own and they
	/// can read it, so a warning would be noise. It is counted because without it the totals above cannot
	/// be reconciled and a real loss stays invisible (GitHub #1150).
	/// </remarks>
	[DataMember(Name = "collapsedIncomingOperations")]
	[JsonProperty("collapsedIncomingOperations", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("collapsedIncomingOperations")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string> CollapsedIncomingOperations { get; init; }

	/// <summary>
	/// Gets the exact number of collapsed incoming operations, which may exceed
	/// <see cref="CollapsedIncomingOperations"/>.
	/// </summary>
	[DataMember(Name = "collapsedIncomingOperationCount")]
	[JsonProperty("collapsedIncomingOperationCount")]
	[JsonPropertyName("collapsedIncomingOperationCount")]
	public int CollapsedIncomingOperationCount { get; init; }

	/// <summary>
	/// Gets a value indicating whether the merged <c>viewConfigDiff</c> array actually reaches the body
	/// that would be written. <c>false</c> means EVERY count above describes an array the write discards.
	/// </summary>
	/// <remarks>
	/// Only a web body can be <c>false</c> here, and only when it carries no
	/// <c>SCHEMA_VIEW_CONFIG_DIFF</c> marker pair for the merge to write back into: the write is a
	/// single-match regex replace over a marker PAIR, and with no pair it returns the body untouched.
	/// Nothing upstream rejects such a body — marker-integrity validation is skipped in append mode and
	/// only ever inspected the incoming fragment.
	/// </remarks>
	[DataMember(Name = "viewConfigDiffApplied")]
	[JsonProperty("viewConfigDiffApplied")]
	[JsonPropertyName("viewConfigDiffApplied")]
	public bool ViewConfigDiffApplied { get; init; }

	/// <summary>
	/// Gets one ready-made, actionable sentence per IDENTITY whose further current entries were dropped
	/// (GH-1132 AC4). One per identity, not per entry: three carried occurrences would otherwise emit two
	/// byte-identical sentences, and <c>CombineWarnings</c> does not dedupe.
	/// </summary>
	/// <remarks>
	/// Deliberately NOT serialized. These are the sentences the command copies into the response's
	/// top-level <c>warnings</c>; emitting them inside <c>appendProjection</c> as well would report the
	/// same loss twice in one response. The structured, machine-readable view of the same facts is
	/// <see cref="DroppedOperations"/> and <see cref="DroppedOperationCount"/>.
	/// </remarks>
	[Newtonsoft.Json.JsonIgnore]
	[System.Text.Json.Serialization.JsonIgnore]
	public IReadOnlyList<string> SupersededDropWarnings { get; init; }
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

	[JsonProperty("warnings", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("warnings")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string> Warnings { get; set; }

	/// <summary>
	/// Gets or sets what the append merge did to the page's <c>viewConfigDiff</c> array. Populated for
	/// <c>mode: append</c> whenever a merge actually ran — <c>null</c> for <c>replace</c>, which writes
	/// the body verbatim, and <c>null</c> when the stored body is empty, because then the fragment is
	/// written as is and there is no merge to project. On a dry run this is the whole point of the call:
	/// it reports the outcome before the write (GitHub #1150).
	/// </summary>
	[JsonProperty("appendProjection", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("appendProjection")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
	public PageAppendProjection AppendProjection { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether this failure came from the SKIPPABLE half of the
	/// validation chain (content rules), as opposed to the structural floor. It is transport-only: the
	/// MCP adapter reads it to decide whether to advertise <c>validate=false</c>, which is an MCP-only
	/// flag the CLI parser does not expose. Never serialized — a CLI user must not be told about a flag
	/// they cannot set.
	/// </summary>
	[Newtonsoft.Json.JsonIgnore]
	[System.Text.Json.Serialization.JsonIgnore]
	public bool ContentValidationFailure { get; set; }

	[JsonProperty("page", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("page")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
	public PageMetadataInfo? Page { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the save was blocked because the schema was
	/// modified outside the current agent session (external-modification conflict).
	/// </summary>
	[DataMember(Name = "conflict")]
	[JsonProperty("conflict", DefaultValueHandling = DefaultValueHandling.Ignore)]
	[JsonPropertyName("conflict")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
	public bool Conflict { get; set; }

	/// <summary>
	/// Gets or sets the conflict details when <see cref="Conflict"/> is <c>true</c>.
	/// </summary>
	[JsonProperty("conflictDetails", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("conflictDetails")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
	public PageConflictDetails ConflictDetails { get; set; }

	/// <summary>
	/// Gets or sets the fresh <c>SysSchema.Checksum</c> queried after a successful save.
	/// Best-effort: <c>null</c> when the post-save query failed — callers holding a baseline
	/// must then discard it rather than keep a stale value.
	/// </summary>
	[JsonProperty("newChecksum", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("newChecksum")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
	public string NewChecksum { get; set; }

	/// <summary>
	/// Gets or sets the raw <c>SysSchema.ModifiedOn</c> queried after a successful save.
	/// Opaque informational string; never used for comparison.
	/// </summary>
	[JsonProperty("newModifiedOn", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("newModifiedOn")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
	public string NewModifiedOn { get; set; }

	/// <summary>
	/// Gets or sets the identifier of the schema the save actually wrote to (the existing editable
	/// schema, or the newly materialized replacing schema).
	/// </summary>
	[JsonProperty("savedSchemaUId", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("savedSchemaUId")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
	public string SavedSchemaUId { get; set; }
}

/// <summary>
/// Machine-readable reason codes for external-modification conflicts. Serialized verbatim in
/// <see cref="PageConflictDetails.Reason"/>.
/// </summary>
public static class PageConflictReasons {
	/// <summary>The server-side checksum differs from the baseline captured at fetch time.</summary>
	public const string ChecksumMismatch = "checksum-mismatch";

	/// <summary>The baseline recorded no editable schema, but one now exists in the design package.</summary>
	public const string SchemaCreatedExternally = "schema-created-externally";

	/// <summary>The baseline recorded an editable schema, but it no longer exists.</summary>
	public const string SchemaDeletedExternally = "schema-deleted-externally";

	/// <summary>The editable schema resolved to a different UId than the baseline recorded.</summary>
	public const string SchemaUIdMismatch = "schema-uid-mismatch";
}

/// <summary>
/// Describes an external-modification conflict detected before saving a page schema.
/// </summary>
public sealed class PageConflictDetails {
	/// <summary>Gets or sets the conflict reason — one of <see cref="PageConflictReasons"/>.</summary>
	[JsonProperty("reason")]
	[JsonPropertyName("reason")]
	public string Reason { get; init; }

	/// <summary>Gets or sets the checksum the caller expected (from the baseline).</summary>
	[JsonProperty("expectedChecksum", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("expectedChecksum")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string ExpectedChecksum { get; init; }

	/// <summary>Gets or sets the checksum currently stored on the server.</summary>
	[JsonProperty("actualChecksum", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("actualChecksum")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string ActualChecksum { get; init; }

	/// <summary>Gets or sets the editable schema UId the caller expected (from the baseline).</summary>
	[JsonProperty("expectedSchemaUId", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("expectedSchemaUId")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string ExpectedSchemaUId { get; init; }

	/// <summary>Gets or sets the editable schema UId resolved on the server.</summary>
	[JsonProperty("actualSchemaUId", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("actualSchemaUId")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string ActualSchemaUId { get; init; }

	/// <summary>Gets or sets the raw server-side <c>ModifiedOn</c> value. Informational only.</summary>
	[JsonProperty("modifiedOn", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("modifiedOn")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string ModifiedOn { get; init; }
}

/// <summary>
/// Typed model of the <c>meta.json</c> file written by <c>get-page</c> into
/// <c>.clio-pages/{schema-name}/</c>. Property names <c>fetchedAt</c> and <c>page</c> are part of
/// the legacy on-disk contract and must not change.
/// </summary>
public sealed class PageMetaFileModel {
	/// <summary>Gets or sets the ISO-8601 UTC timestamp when the page files were written.</summary>
	[JsonProperty("fetchedAt")]
	[JsonPropertyName("fetchedAt")]
	public string FetchedAt { get; init; }

	/// <summary>Gets or sets the page metadata captured at fetch time.</summary>
	[JsonProperty("page")]
	[JsonPropertyName("page")]
	public PageMetadataInfo Page { get; init; }

	/// <summary>
	/// Gets or sets the conflict-detection baseline. Absent on legacy files and when the checksum
	/// capture failed at fetch time — consumers must skip the conflict check in that case.
	/// </summary>
	[JsonProperty("baseline", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("baseline")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public PageBaselineInfo Baseline { get; init; }
}

/// <summary>
/// Conflict-detection baseline persisted in <c>meta.json</c>: the editable schema state observed
/// by the last <c>get-page</c> (or refreshed after a successful write) plus the environment
/// identity it was captured against.
/// </summary>
public sealed class PageBaselineInfo {
	/// <summary>Gets or sets the page schema name the baseline belongs to.</summary>
	[JsonProperty("schemaName")]
	[JsonPropertyName("schemaName")]
	public string SchemaName { get; init; }

	/// <summary>
	/// Gets or sets the registered environment name the baseline was captured against.
	/// <c>null</c> when the call used a direct URI.
	/// </summary>
	[JsonProperty("environmentName", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("environmentName")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string EnvironmentName { get; init; }

	/// <summary>
	/// Gets or sets the direct Creatio URI the baseline was captured against.
	/// <c>null</c> when the call used a registered environment name.
	/// </summary>
	[JsonProperty("environmentUri", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("environmentUri")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string EnvironmentUri { get; init; }

	/// <summary>Gets or sets a value indicating whether the editable schema existed at capture time.</summary>
	[JsonProperty("editableSchemaExists")]
	[JsonPropertyName("editableSchemaExists")]
	public bool EditableSchemaExists { get; init; }

	/// <summary>Gets or sets the editable schema UId. <c>null</c> when it did not exist.</summary>
	[JsonProperty("editableSchemaUId", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("editableSchemaUId")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string EditableSchemaUId { get; init; }

	/// <summary>Gets or sets the <c>SysSchema.Checksum</c> at capture time. <c>null</c> when the schema did not exist.</summary>
	[JsonProperty("checksum", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("checksum")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Checksum { get; init; }

	/// <summary>Gets or sets the raw <c>SysSchema.ModifiedOn</c> at capture time. Opaque informational string.</summary>
	[JsonProperty("modifiedOn", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("modifiedOn")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string ModifiedOn { get; init; }

	/// <summary>Gets or sets the ISO-8601 UTC timestamp when the baseline was captured or refreshed.</summary>
	[JsonProperty("capturedAt")]
	[JsonPropertyName("capturedAt")]
	public string CapturedAt { get; init; }
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

	/// <summary>
	/// Schema type of the template used to create this page.
	/// 9 = Freedom UI web page (use the Freedom UI page designer).
	/// 10 = mobile page (use the mobile page designer).
	/// Note: <c>BaseHomePage</c>-based pages have schemaType=9 but require the Homepage designer, not the standard page designer.
	/// </summary>
	[DataMember(Name = "schemaType")]
	[JsonProperty("schemaType")]
	[JsonPropertyName("schemaType")]
	public int SchemaType { get; set; }

	[DataMember(Name = "error")]
	[JsonProperty("error")]
	[JsonPropertyName("error")]
	public string Error { get; set; }

	/// <summary>
	/// The app's design (editing) package UId, when it differs from <see cref="PackageUId"/>. Present only
	/// when the chosen package is not the design package — see <see cref="WillCreateReplacingInDesignPackage"/>.
	/// </summary>
	[DataMember(Name = "designPackageUId")]
	[JsonProperty("designPackageUId", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("designPackageUId")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string DesignPackageUId { get; set; }

	/// <summary>
	/// True when the chosen <see cref="PackageUId"/> is not the app's design package. A subsequent
	/// <c>update-page</c> WITHOUT <c>target-schema-uid</c> would then materialize a replacing schema in the
	/// design package and leave THIS freshly-created schema empty (for a mobile page that empty base crashes
	/// the Creatio Mobile app). Callers should pass <c>target-schema-uid=&lt;schemaUId&gt;</c> on update-page.
	/// </summary>
	[DataMember(Name = "willCreateReplacingInDesignPackage")]
	[JsonProperty("willCreateReplacingInDesignPackage", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("willCreateReplacingInDesignPackage")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? WillCreateReplacingInDesignPackage { get; set; }

	[DataMember(Name = "note")]
	[JsonProperty("note", NullValueHandling = NullValueHandling.Ignore)]
	[JsonPropertyName("note")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Note { get; set; }
}
