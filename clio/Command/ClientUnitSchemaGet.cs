namespace Clio.Command;

using System;
using System.IO;
using Clio.Common;
using CommandLine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[Verb("get-client-unit-schema", Aliases = ["client-unit-schema-get"],
	HelpText = "Read the body and metadata of a client unit (JavaScript) schema from a remote Creatio environment")]
public class GetClientUnitSchemaOptions : EnvironmentOptions {

	[Option("schema-name", Required = true, HelpText = "Client unit schema name")]
	public string SchemaName { get; set; }

	[Option("output-file", Required = false,
		HelpText = "Absolute path to write the schema body to. When set, body is omitted from the response.")]
	public string OutputFile { get; set; }
}

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

	[System.Text.Json.Serialization.JsonPropertyName("error")]
	public string Error { get; set; }
}

public class GetClientUnitSchemaCommand : Command<GetClientUnitSchemaOptions> {

	private static readonly SchemaDesignerKind Kind = SchemaDesignerKind.ClientUnit;

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly ILogger _logger;

	public GetClientUnitSchemaCommand(
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder,
		ILogger logger) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_logger = logger;
	}

	public virtual bool TryGetSchema(GetClientUnitSchemaOptions options, out GetClientUnitSchemaResponse response) {
		try {
			if (string.IsNullOrWhiteSpace(options.SchemaName)) {
				response = new GetClientUnitSchemaResponse { Success = false, Error = "schema-name is required" };
				return false;
			}
			(string schemaUId, string resolveError) = SchemaDesignerHelper.ResolveSchemaUId(
				_applicationClient, _serviceUrlBuilder, options.SchemaName, Kind);
			if (resolveError != null) {
				response = new GetClientUnitSchemaResponse { Success = false, Error = resolveError };
				return false;
			}
			(JObject schema, string loadError) = SchemaDesignerHelper.LoadSchema(
				_applicationClient, _serviceUrlBuilder, schemaUId, Kind);
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
