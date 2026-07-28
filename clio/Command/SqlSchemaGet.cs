namespace Clio.Command;

using System;
using Clio.Common;
using CommandLine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using IoFileSystem = System.IO.Abstractions.IFileSystem;

[Verb("get-sql-schema", Aliases = ["sql-schema-get"],
	HelpText = "Read the body and metadata of a SQL script schema from a remote Creatio environment")]
public class SqlSchemaGetOptions : EnvironmentOptions {

	[Option("schema-name", Required = true, HelpText = "SQL script schema name")]
	public string SchemaName { get; set; }

	[Option("output-file", Required = false,
		HelpText = "Absolute path to write the schema body to. When set, body is omitted from the response.")]
	public string OutputFile { get; set; }
}

public sealed class SqlSchemaGetResponse {

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

public class SqlSchemaGetCommand : Command<SqlSchemaGetOptions> {

	private static readonly SchemaDesignerKind Kind = SchemaDesignerKind.SqlScript;

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly IoFileSystem _ioFileSystem;
	private readonly ILogger _logger;

	public SqlSchemaGetCommand(
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder,
		IoFileSystem ioFileSystem,
		ILogger logger) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_ioFileSystem = ioFileSystem;
		_logger = logger;
	}

	public virtual bool TryGetSchema(SqlSchemaGetOptions options, out SqlSchemaGetResponse response) {
		try {
			if (string.IsNullOrWhiteSpace(options.SchemaName)) {
				response = new SqlSchemaGetResponse { Success = false, Error = "schema-name is required" };
				return false;
			}
			// Confine an agent-suppliable output-file BEFORE any network call (this command is MCP-callable via
			// get-sql-schema): resolve symlinks, drop an untrusted anchor, keep the write inside the workspace or
			// OS temp dir, and refuse an existing target so the Destructive=false classification stays honest.
			string resolvedOutputPath = null;
			if (!string.IsNullOrWhiteSpace(options.OutputFile)) {
				string pathError;
				(resolvedOutputPath, pathError) = OutputPathConfinement.Resolve(_ioFileSystem, options.OutputFile);
				if (pathError != null) {
					response = new SqlSchemaGetResponse { Success = false, Error = pathError };
					return false;
				}
			}
			(string schemaUId, string resolveError) = SchemaDesignerHelper.ResolveSchemaUId(
				_applicationClient, _serviceUrlBuilder, options.SchemaName, Kind);
			if (resolveError != null) {
				response = new SqlSchemaGetResponse { Success = false, Error = resolveError };
				return false;
			}
			(JObject schema, string loadError) = SchemaDesignerHelper.LoadSchema(
				_applicationClient, _serviceUrlBuilder, schemaUId, Kind, options.SchemaName);
			if (loadError != null) {
				response = new SqlSchemaGetResponse { Success = false, Error = loadError };
				return false;
			}
			string body = schema["body"]?.ToString() ?? string.Empty;
			string caption = SchemaDesignerHelper.ExtractCaption(schema);
			string packageName = schema["package"]?["name"]?.ToString();
			string schemaName = schema["name"]?.ToString() ?? options.SchemaName;
			response = new SqlSchemaGetResponse {
				Success = true,
				SchemaName = schemaName,
				SchemaUId = schemaUId,
				PackageName = packageName,
				Caption = caption,
				BodyLength = body.Length
			};
			if (resolvedOutputPath != null) {
				_ioFileSystem.File.WriteAllText(resolvedOutputPath, body);
			} else {
				response.Body = body;
			}
			return true;
		}
		catch (Exception ex) {
			response = new SqlSchemaGetResponse { Success = false, Error = ex.Message };
			return false;
		}
	}

	public override int Execute(SqlSchemaGetOptions options) {
		bool success = TryGetSchema(options, out SqlSchemaGetResponse response);
		_logger.WriteInfo(JsonConvert.SerializeObject(response));
		return success ? 0 : 1;
	}
}
