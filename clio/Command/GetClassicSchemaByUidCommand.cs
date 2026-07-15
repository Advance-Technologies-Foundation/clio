namespace Clio.Command;

using System;
using System.IO;
using Clio.Common;
using CommandLine;
using Newtonsoft.Json.Linq;

[Verb("get-classic-schema-by-uid", Aliases = ["classic-schema-get"],
	HelpText = "Read the JavaScript body and metadata of a Classic client unit schema schema by its UId. " +
		"Unlike get-client-unit-schema (resolves the TOP schema by name), this loads a SPECIFIC schema by SysSchema.UId — " +
		"the foundation for Classic->Freedom migration discovery (read every package schema of a schema).")]
public class GetClassicSchemaByUidOptions : EnvironmentOptions {

	[Option("schema-uid", Required = true, HelpText = "Client unit schema UId (a specific schema's SysSchema.UId)")]
	public string SchemaUId { get; set; }

	[Option("output-file", Required = false,
		HelpText = "Absolute path to write the schema body to. When set, body is omitted from the response.")]
	public string OutputFile { get; set; }
}

public sealed class GetClassicSchemaByUidResponse {

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

	[System.Text.Json.Serialization.JsonPropertyName("error")]
	public string Error { get; set; }
}

public class GetClassicSchemaByUidCommand : Command<GetClassicSchemaByUidOptions> {

	private static readonly SchemaDesignerKind Kind = SchemaDesignerKind.ClientUnit;

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly ILogger _logger;

	public GetClassicSchemaByUidCommand(
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder,
		ILogger logger) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_logger = logger;
	}

	public virtual bool TryGetSchema(GetClassicSchemaByUidOptions options, out GetClassicSchemaByUidResponse response) {
		try {
			if (string.IsNullOrWhiteSpace(options.SchemaUId)) {
				response = new GetClassicSchemaByUidResponse { Success = false, Error = "schema-uid is required" };
				return false;
			}
			(JObject schema, string loadError) = SchemaDesignerHelper.LoadSchema(
				_applicationClient, _serviceUrlBuilder, options.SchemaUId, Kind);
			if (loadError != null) {
				response = new GetClassicSchemaByUidResponse { Success = false, Error = loadError };
				return false;
			}
			string body = schema["body"]?.ToString() ?? string.Empty;
			response = new GetClassicSchemaByUidResponse {
				Success = true,
				SchemaName = schema["name"]?.ToString(),
				SchemaUId = schema["uId"]?.ToString() ?? options.SchemaUId,
				PackageName = schema["package"]?["name"]?.ToString(),
				Caption = SchemaDesignerHelper.ExtractCaption(schema),
				BodyLength = body.Length
			};
			if (!string.IsNullOrWhiteSpace(options.OutputFile)) {
				File.WriteAllText(options.OutputFile, body);
			} else {
				response.Body = body;
			}
			return true;
		}
		catch (Exception ex) {
			response = new GetClassicSchemaByUidResponse { Success = false, Error = ex.Message };
			return false;
		}
	}

	public override int Execute(GetClassicSchemaByUidOptions options) {
		bool success = TryGetSchema(options, out GetClassicSchemaByUidResponse response);
		_logger.WriteInfo(System.Text.Json.JsonSerializer.Serialize(response));
		return success ? 0 : 1;
	}
}
