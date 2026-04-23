namespace Clio.Command;

using System;
using System.IO;
using Clio.Common;
using CommandLine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[Verb("get-schema", Aliases = ["schema-get"],
	HelpText = "Read the body and metadata of a C# source-code schema from a remote Creatio environment")]
public class GetSourceCodeSchemaOptions : EnvironmentOptions {

	[Option("schema-name", Required = true, HelpText = "C# source-code schema name")]
	public string SchemaName { get; set; }

	[Option("output-file", Required = false,
		HelpText = "Absolute path to write the schema body to. When set, body is omitted from the response.")]
	public string OutputFile { get; set; }
}

public sealed class GetSourceCodeSchemaResponse {

	[JsonProperty("success")]
	[System.Text.Json.Serialization.JsonPropertyName("success")]
	public bool Success { get; set; }

	[JsonProperty("schemaName")]
	[System.Text.Json.Serialization.JsonPropertyName("schemaName")]
	public string SchemaName { get; set; }

	[JsonProperty("schemaUId")]
	[System.Text.Json.Serialization.JsonPropertyName("schemaUId")]
	public string SchemaUId { get; set; }

	[JsonProperty("packageName")]
	[System.Text.Json.Serialization.JsonPropertyName("packageName")]
	public string PackageName { get; set; }

	[JsonProperty("caption")]
	[System.Text.Json.Serialization.JsonPropertyName("caption")]
	public string Caption { get; set; }

	[JsonProperty("body")]
	[System.Text.Json.Serialization.JsonPropertyName("body")]
	public string Body { get; set; }

	[JsonProperty("bodyLength")]
	[System.Text.Json.Serialization.JsonPropertyName("bodyLength")]
	public int BodyLength { get; set; }

	[JsonProperty("error")]
	[System.Text.Json.Serialization.JsonPropertyName("error")]
	public string Error { get; set; }
}

public class GetSourceCodeSchemaCommand : Command<GetSourceCodeSchemaOptions> {

	private static readonly SchemaDesignerKind Kind = SchemaDesignerKind.SourceCode;

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly ILogger _logger;

	public GetSourceCodeSchemaCommand(
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder,
		ILogger logger) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_logger = logger;
	}

	public virtual bool TryGetSchema(GetSourceCodeSchemaOptions options, out GetSourceCodeSchemaResponse response) {
		try {
			if (string.IsNullOrWhiteSpace(options.SchemaName)) {
				response = new GetSourceCodeSchemaResponse { Success = false, Error = "schema-name is required" };
				return false;
			}
			(string schemaUId, string resolveError) = SchemaDesignerHelper.ResolveSchemaUId(
				_applicationClient, _serviceUrlBuilder, options.SchemaName, Kind);
			if (resolveError != null) {
				response = new GetSourceCodeSchemaResponse { Success = false, Error = resolveError };
				return false;
			}
			(JObject schema, string loadError) = SchemaDesignerHelper.LoadSchema(
				_applicationClient, _serviceUrlBuilder, schemaUId, Kind);
			if (loadError != null) {
				response = new GetSourceCodeSchemaResponse { Success = false, Error = loadError };
				return false;
			}
			string body = schema["body"]?.ToString() ?? string.Empty;
			string caption = SchemaDesignerHelper.ExtractCaption(schema);
			string packageName = schema["package"]?["name"]?.ToString();
			string schemaName = schema["name"]?.ToString() ?? options.SchemaName;
			response = new GetSourceCodeSchemaResponse {
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
			response = new GetSourceCodeSchemaResponse { Success = false, Error = ex.Message };
			return false;
		}
	}

	public override int Execute(GetSourceCodeSchemaOptions options) {
		bool success = TryGetSchema(options, out GetSourceCodeSchemaResponse response);
		_logger.WriteInfo(JsonConvert.SerializeObject(response));
		return success ? 0 : 1;
	}
}
