namespace Clio.Command;

using System;
using Clio.Common;
using CommandLine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[Verb("update-schema", Aliases = ["schema-update"],
	HelpText = "Update the body of a C# source-code schema on a remote Creatio environment")]
public class SourceCodeSchemaUpdateOptions : EnvironmentOptions {

	[Option("schema-name", Required = true, HelpText = "C# source-code schema name")]
	public string SchemaName { get; set; }

	[Option("body", Required = false, HelpText = "New C# body to save. Use body-file for large bodies.")]
	public string Body { get; set; }

	[Option("body-file", Required = false, HelpText = "Absolute path to a file whose contents are used as the new schema body. Takes precedence over --body when both are provided.")]
	public string BodyFile { get; set; }

	[Option("dry-run", Required = false, HelpText = "Validate and resolve the schema without saving")]
	public bool DryRun { get; set; }
}

public class SourceCodeSchemaUpdateCommand : Command<SourceCodeSchemaUpdateOptions> {

	private static readonly SchemaDesignerKind Kind = SchemaDesignerKind.SourceCode;

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly ILogger _logger;

	public SourceCodeSchemaUpdateCommand(
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder,
		ILogger logger) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_logger = logger;
	}

	public bool TryUpdateSchema(
		SourceCodeSchemaUpdateOptions options,
		out SourceCodeSchemaUpdateResponse response) {
		try {
			if (string.IsNullOrWhiteSpace(options.SchemaName)) {
				response = new SourceCodeSchemaUpdateResponse { Success = false, Error = "schema-name is required" };
				return false;
			}
			(string body, string bodyError) = SchemaDesignerHelper.ResolveBody(options.Body, options.BodyFile);
			if (bodyError != null) {
				response = new SourceCodeSchemaUpdateResponse { Success = false, Error = bodyError };
				return false;
			}
			options.Body = body;
			(string schemaUId, string resolveError) = SchemaDesignerHelper.ResolveSchemaUId(
				_applicationClient, _serviceUrlBuilder, options.SchemaName, Kind);
			if (resolveError != null) {
				response = new SourceCodeSchemaUpdateResponse { Success = false, Error = resolveError };
				return false;
			}
			if (options.DryRun) {
				response = CreateSuccessResponse(options, dryRun: true);
				return true;
			}
			(JObject schemaToSave, string loadError) = SchemaDesignerHelper.LoadSchema(
				_applicationClient, _serviceUrlBuilder, schemaUId, Kind);
			if (loadError != null) {
				response = new SourceCodeSchemaUpdateResponse { Success = false, Error = loadError };
				return false;
			}
			schemaToSave["body"] = options.Body;
			string saveError = SchemaDesignerHelper.SaveSchema(_applicationClient, _serviceUrlBuilder, schemaToSave, Kind);
			if (saveError != null) {
				response = new SourceCodeSchemaUpdateResponse { Success = false, Error = saveError };
				return false;
			}
			response = CreateSuccessResponse(options, dryRun: false);
			return true;
		}
		catch (Exception ex) {
			response = new SourceCodeSchemaUpdateResponse { Success = false, Error = ex.Message };
			return false;
		}
	}

	public override int Execute(SourceCodeSchemaUpdateOptions options) {
		bool success = TryUpdateSchema(options, out SourceCodeSchemaUpdateResponse response);
		_logger.WriteInfo(JsonConvert.SerializeObject(response));
		return success ? 0 : 1;
	}

	private static SourceCodeSchemaUpdateResponse CreateSuccessResponse(
		SourceCodeSchemaUpdateOptions options,
		bool dryRun) {
		return new SourceCodeSchemaUpdateResponse {
			Success = true,
			SchemaName = options.SchemaName,
			BodyLength = options.Body.Length,
			DryRun = dryRun
		};
	}
}

public sealed class SourceCodeSchemaUpdateResponse {

	[JsonProperty("success")]
	[System.Text.Json.Serialization.JsonPropertyName("success")]
	public bool Success { get; set; }

	[JsonProperty("schemaName")]
	[System.Text.Json.Serialization.JsonPropertyName("schemaName")]
	public string SchemaName { get; set; }

	[JsonProperty("bodyLength")]
	[System.Text.Json.Serialization.JsonPropertyName("bodyLength")]
	public int BodyLength { get; set; }

	[JsonProperty("dryRun")]
	[System.Text.Json.Serialization.JsonPropertyName("dryRun")]
	public bool DryRun { get; set; }

	[JsonProperty("error")]
	[System.Text.Json.Serialization.JsonPropertyName("error")]
	public string Error { get; set; }
}
