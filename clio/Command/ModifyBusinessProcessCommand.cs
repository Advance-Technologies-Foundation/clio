using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.UserEnvironment;
using CommandLine;

namespace Clio.Command;

/// <summary>
/// CLI options for editing an existing business process via the ProcessDesignService package.
/// </summary>
[Verb("modify-business-process", Aliases = ["modify-bp"],
	HelpText = "Edit an existing business process on a Creatio environment by applying a list of operations")]
public sealed class ModifyBusinessProcessOptions : EnvironmentOptions {
	[Option("name", Required = false,
		HelpText = "Process code (schema Name) to edit. Provide this or --uid.")]
	public string ProcessName { get; set; } = string.Empty;

	[Option("uid", Required = false,
		HelpText = "Process schema UId to edit. Provide this or --name.")]
	public string ProcessUid { get; set; } = string.Empty;

	[Option("operations", Required = false,
		HelpText = "Path to a JSON file with the operations array "
			+ "([{op:addElement|removeElement|addFlow|removeFlow, …}]). Provide this or --operations-json.")]
	public string OperationsPath { get; set; } = string.Empty;

	[Option("operations-json", Required = false,
		HelpText = "Inline JSON operations array (alternative to --operations).")]
	public string OperationsJson { get; set; } = string.Empty;
}

/// <summary>
/// Edits an existing business process on a Creatio environment via the ProcessDesignService package.
/// </summary>
public interface IModifyBusinessProcessService {
	/// <summary>
	/// Applies the given operations to an existing process and saves it.
	/// </summary>
	/// <param name="environmentName">Registered clio environment name.</param>
	/// <param name="request">Modify request (process identity + operations JSON).</param>
	/// <returns>Structured result with the edited schema identity and applied-operation count.</returns>
	ModifyBusinessProcessResult ModifyProcess(string environmentName, ModifyBusinessProcessRequest request);
}

/// <summary>
/// Default ProcessDesignService-backed implementation of <see cref="IModifyBusinessProcessService"/>.
/// </summary>
public sealed class ModifyBusinessProcessService(
	ISettingsRepository settingsRepository,
	IApplicationClientFactory applicationClientFactory,
	IServiceUrlBuilder serviceUrlBuilder,
	ILogger logger)
	: IModifyBusinessProcessService {
	private static readonly JsonSerializerOptions JsonOptions = new() {
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		PropertyNameCaseInsensitive = true
	};

	/// <inheritdoc />
	public ModifyBusinessProcessResult ModifyProcess(string environmentName, ModifyBusinessProcessRequest request) {
		if (string.IsNullOrWhiteSpace(environmentName)) {
			throw new ArgumentException("Environment name is required.", nameof(environmentName));
		}

		ArgumentNullException.ThrowIfNull(request);
		if (string.IsNullOrWhiteSpace(request.ProcessName) && string.IsNullOrWhiteSpace(request.ProcessUid)) {
			throw new ArgumentException("Either a process name or uid is required.", nameof(request));
		}

		if (string.IsNullOrWhiteSpace(request.OperationsJson)) {
			throw new ArgumentException("Operations content is required.", nameof(request));
		}

		EnvironmentSettings environmentSettings = settingsRepository.FindEnvironment(environmentName)
			?? throw new InvalidOperationException(
				EnvironmentNotFoundError.Build(environmentName, settingsRepository));

		var requestObject = new JsonObject();
		if (!string.IsNullOrWhiteSpace(request.ProcessName)) {
			requestObject["name"] = request.ProcessName;
		}
		if (!string.IsNullOrWhiteSpace(request.ProcessUid)) {
			requestObject["uid"] = request.ProcessUid;
		}
		requestObject["operations"] = ParseOperations(request.OperationsJson);

		IApplicationClient client = applicationClientFactory.CreateEnvironmentClient(environmentSettings);
		string url = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.ModifyProcess, environmentSettings);
		// ProcessDesignService uses BodyStyle=Wrapped: the request is wrapped under a "request" property.
		string requestBody = new JsonObject { ["request"] = requestObject }.ToJsonString();
		logger.WriteInfo($"Editing process '{request.ProcessName ?? request.ProcessUid}' on '{environmentName}'...");

		string responseBody = client.ExecutePostRequest(url, requestBody);
		ModifyProcessResponseEnvelope envelope =
			JsonSerializer.Deserialize<ModifyProcessResponseEnvelope>(responseBody, JsonOptions)
			?? throw new InvalidOperationException("ModifyProcess returned an empty response.");
		ModifyProcessResultDto result = envelope.Result
			?? throw new InvalidOperationException("ModifyProcess returned an unexpected response shape.");
		if (!result.Success) {
			throw new InvalidOperationException(result.ErrorMessage ?? "ModifyProcess failed.");
		}

		return new ModifyBusinessProcessResult(result.SchemaName, result.SchemaUId, result.AppliedOperations);
	}

	private static JsonArray ParseOperations(string operationsJson) {
		JsonNode? node;
		try {
			node = JsonNode.Parse(operationsJson);
		} catch (JsonException exception) {
			throw new InvalidOperationException(
				$"Operations content is not valid JSON: {exception.Message}", exception);
		}

		return node as JsonArray
			?? throw new InvalidOperationException("Operations content must be a JSON array of operations.");
	}

	#region DTOs (wire shape)

	private sealed class ModifyProcessResponseEnvelope {
		[JsonPropertyName("ModifyProcessResult")]
		public ModifyProcessResultDto? Result { get; set; }
	}

	private sealed class ModifyProcessResultDto {
		[JsonPropertyName("success")]
		public bool Success { get; set; }

		[JsonPropertyName("schemaUId")]
		public string? SchemaUId { get; set; }

		[JsonPropertyName("schemaName")]
		public string? SchemaName { get; set; }

		[JsonPropertyName("appliedOperations")]
		public int AppliedOperations { get; set; }

		[JsonPropertyName("errorMessage")]
		public string? ErrorMessage { get; set; }
	}

	#endregion
}

/// <summary>
/// Edits an existing business process from an operations descriptor (file or inline JSON) and prints the result.
/// </summary>
public class ModifyBusinessProcessCommand(
	IModifyBusinessProcessService modifyBusinessProcessService,
	ILogger logger)
	: Command<ModifyBusinessProcessOptions> {
	/// <inheritdoc />
	public override int Execute(ModifyBusinessProcessOptions options) {
		try {
			ArgumentNullException.ThrowIfNull(options);
			if (string.IsNullOrWhiteSpace(options.Environment)) {
				throw new InvalidOperationException("Environment name is required.");
			}

			if (string.IsNullOrWhiteSpace(options.ProcessName) && string.IsNullOrWhiteSpace(options.ProcessUid)) {
				throw new InvalidOperationException("One of --name or --uid is required.");
			}

			string operationsJson = ResolveOperationsJson(options);
			ModifyBusinessProcessResult result = modifyBusinessProcessService.ModifyProcess(
				options.Environment,
				new ModifyBusinessProcessRequest(options.ProcessName, options.ProcessUid, operationsJson));
			logger.WriteInfo(
				$"Process '{result.SchemaName}' edited ({result.AppliedOperations} operation(s) applied; UId: {result.SchemaUId}).");
			return 0;
		} catch (Exception exception) {
			logger.WriteError(exception.Message);
			return 1;
		}
	}

	/// <summary>Resolves the operations JSON from the inline option or the operations file.</summary>
	private static string ResolveOperationsJson(ModifyBusinessProcessOptions options) {
		if (!string.IsNullOrWhiteSpace(options.OperationsJson)) {
			return options.OperationsJson;
		}

		if (string.IsNullOrWhiteSpace(options.OperationsPath)) {
			throw new InvalidOperationException("One of --operations or --operations-json is required.");
		}

		if (!File.Exists(options.OperationsPath)) {
			throw new FileNotFoundException($"Operations file was not found: '{options.OperationsPath}'.");
		}

		return File.ReadAllText(options.OperationsPath);
	}
}

/// <summary>
/// Request payload for editing a business process.
/// </summary>
/// <param name="ProcessName">Process code (schema Name) to edit.</param>
/// <param name="ProcessUid">Process schema UId to edit.</param>
/// <param name="OperationsJson">The JSON operations array content.</param>
public sealed record ModifyBusinessProcessRequest(string ProcessName, string ProcessUid, string OperationsJson);

/// <summary>
/// Structured result of a business-process edit.
/// </summary>
/// <param name="SchemaName">Name (code) of the edited process schema.</param>
/// <param name="SchemaUId">UId of the edited process schema.</param>
/// <param name="AppliedOperations">Number of operations applied.</param>
public sealed record ModifyBusinessProcessResult(string? SchemaName, string? SchemaUId, int AppliedOperations);
