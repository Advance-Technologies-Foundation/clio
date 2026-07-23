namespace Clio.Command;

using System;
using System.Collections.Generic;
using Clio.Common;
using CommandLine;
using Newtonsoft.Json.Linq;

/// <summary>Options for the <c>get-client-unit-schema</c> command.</summary>
[Verb("get-client-unit-schema", Aliases = ["client-unit-schema-get"],
	HelpText = "Read the body and metadata of a client unit (JavaScript) schema from a remote Creatio environment")]
public class GetClientUnitSchemaOptions : EnvironmentOptions {

	/// <summary>Schema name to resolve; required unless <see cref="SchemaUId"/> is provided.</summary>
	[Option("schema-name", Required = false, HelpText = "Client unit schema name (required unless --schema-uid is provided)")]
	public string SchemaName { get; set; }

	[Option("output-file", Required = false,
		HelpText = "Absolute path to write the schema body to. When set, body is omitted from the response.")]
	public string OutputFile { get; set; }

	[Option("full-hierarchy", Required = false,
		HelpText = "Also return the localizable strings MERGED across the full inheritance/package hierarchy " +
			"(each with its parentSchemaUId provenance). The body stays this schema's own top layer — the merge " +
			"folds localization and metadata, not the view. Default false.")]
	public bool FullHierarchy { get; set; }

	[Option("schema-uid", Required = false,
		HelpText = "Fetch this exact schema UId directly, bypassing name resolution. Use to target a " +
			"specific layer of a multi-layer classic schema deterministically.")]
	public string SchemaUId { get; set; }
}

/// <summary>Response envelope for the <c>get-client-unit-schema</c> command.</summary>
public sealed class GetClientUnitSchemaResponse {

	[System.Text.Json.Serialization.JsonPropertyName("success")]
	public bool Success { get; set; }

	[System.Text.Json.Serialization.JsonPropertyName("schemaName")]
	public string SchemaName { get; set; }

	[System.Text.Json.Serialization.JsonPropertyName("schemaUId")]
	public string SchemaUId { get; set; }

	[System.Text.Json.Serialization.JsonPropertyName("packageName")]
	public string PackageName { get; set; }

	[System.Text.Json.Serialization.JsonPropertyName("caption")]
	public string Caption { get; set; }

	[System.Text.Json.Serialization.JsonPropertyName("body")]
	public string Body { get; set; }

	[System.Text.Json.Serialization.JsonPropertyName("bodyLength")]
	public int BodyLength { get; set; }

	[System.Text.Json.Serialization.JsonPropertyName("fullHierarchy")]
	public bool FullHierarchy { get; set; }

	[System.Text.Json.Serialization.JsonPropertyName("localizableStringCount")]
	public int LocalizableStringCount { get; set; }

	[System.Text.Json.Serialization.JsonPropertyName("localizableStrings")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<MergedLocalizableString> LocalizableStrings { get; set; }

	[System.Text.Json.Serialization.JsonPropertyName("error")]
	public string Error { get; set; }
}

/// <summary>
/// Reads a client unit schema (body + metadata) from a remote Creatio environment, resolving a
/// multi-layer name deterministically to the top (most-derived) layer, or fetching an exact layer
/// by <c>--schema-uid</c>. With <c>--full-hierarchy</c> it also extracts the localizable strings
/// merged across the whole inheritance/package chain.
/// </summary>
public class GetClientUnitSchemaCommand : Command<GetClientUnitSchemaOptions> {

	private static readonly SchemaDesignerKind Kind = SchemaDesignerKind.ClientUnit;

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly IFileSystem _fileSystem;
	private readonly ILogger _logger;

	public GetClientUnitSchemaCommand(
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder,
		IFileSystem fileSystem,
		ILogger logger) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_fileSystem = fileSystem;
		_logger = logger;
	}

	/// <summary>
	/// Fetches the schema described by <paramref name="options"/>. Returns <c>true</c> with a populated
	/// response, or <c>false</c> with <see cref="GetClientUnitSchemaResponse.Error"/> set when neither
	/// name nor UId is provided, resolution fails, or the designer service reports a failure.
	/// </summary>
	public virtual bool TryGetSchema(GetClientUnitSchemaOptions options, out GetClientUnitSchemaResponse response) {
		try {
			string schemaUId;
			if (!string.IsNullOrWhiteSpace(options.SchemaUId)) {
				schemaUId = options.SchemaUId;
			} else {
				if (string.IsNullOrWhiteSpace(options.SchemaName)) {
					response = new GetClientUnitSchemaResponse { Success = false, Error = "schema-name or schema-uid is required" };
					return false;
				}
				string resolveError;
				(schemaUId, resolveError) = SchemaDesignerHelper.ResolveSchemaUId(
					_applicationClient, _serviceUrlBuilder, options.SchemaName, Kind);
				if (resolveError != null) {
					response = new GetClientUnitSchemaResponse { Success = false, Error = resolveError };
					return false;
				}
			}
			(JObject schema, string loadError) = SchemaDesignerHelper.LoadSchema(
				_applicationClient, _serviceUrlBuilder, schemaUId, Kind, options.SchemaName, options.FullHierarchy);
			if (loadError != null) {
				response = new GetClientUnitSchemaResponse { Success = false, Error = loadError };
				return false;
			}
			string body = schema["body"]?.ToString() ?? string.Empty;
			string caption = SchemaDesignerHelper.ExtractCaption(schema);
			string packageName = schema["package"]?["name"]?.ToString();
			string schemaName = schema["name"]?.ToString() ?? options.SchemaName;
			response = new GetClientUnitSchemaResponse {
				Success = true,
				SchemaName = schemaName,
				SchemaUId = schemaUId,
				PackageName = packageName,
				Caption = caption,
				BodyLength = body.Length,
				FullHierarchy = options.FullHierarchy
			};
			if (options.FullHierarchy) {
				IReadOnlyList<MergedLocalizableString> localizableStrings =
					SchemaDesignerHelper.ExtractMergedLocalizableStrings(schema);
				response.LocalizableStringCount = localizableStrings.Count;
				if (!string.IsNullOrWhiteSpace(options.OutputFile)) {
					// Honest --full-hierarchy contract: write the MERGED localizable strings (the real content the
					// merge adds, WITH parentSchemaUId provenance) as a documented structure — NOT a raw
					// DesignSchemaDto dump. body stays the single top-layer body (the view is not folded).
					var fileContent = new {
						schemaName,
						schemaUId,
						fullHierarchy = true,
						body,
						localizableStrings
					};
					_fileSystem.WriteAllTextToFile(options.OutputFile,
						System.Text.Json.JsonSerializer.Serialize(fileContent,
							new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
				} else {
					response.Body = body;
					response.LocalizableStrings = localizableStrings;
				}
			} else if (!string.IsNullOrWhiteSpace(options.OutputFile)) {
				_fileSystem.WriteAllTextToFile(options.OutputFile, body);
			} else {
				response.Body = body;
			}
			return true;
		}
		catch (Exception ex) {
			response = new GetClientUnitSchemaResponse { Success = false, Error = ex.Message };
			return false;
		}
	}

	public override int Execute(GetClientUnitSchemaOptions options) {
		bool success = TryGetSchema(options, out GetClientUnitSchemaResponse response);
		_logger.WriteInfo(System.Text.Json.JsonSerializer.Serialize(response));
		return success ? 0 : 1;
	}
}
