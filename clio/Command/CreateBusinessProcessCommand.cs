using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.UserEnvironment;

namespace Clio.Command;

/// <summary>
/// Options for building a business process from a declarative descriptor via the ProcessDesignService package.
/// Consumed by the MCP <c>create-business-process</c> tool, which sets these properties directly.
/// </summary>
[RequiresPackage("clioprocessbuilder", Hint = "This experimental feature requires the clioprocessbuilder package on the target environment.")]
public sealed class CreateBusinessProcessOptions : EnvironmentOptions {
	/// <summary>Inline JSON process descriptor (name, caption, packageName, elements[], flows[], parameters[], mappings[]).</summary>
	public string DescriptorJson { get; set; } = string.Empty;

	/// <summary>Overrides the target package from the descriptor (the package the process is created in).</summary>
	public string PackageName { get; set; } = string.Empty;
}

/// <summary>
/// Builds a business process on a Creatio environment via the ProcessDesignService package.
/// </summary>
public interface ICreateBusinessProcessService {
	/// <summary>
	/// Builds and saves a business process from a declarative descriptor.
	/// </summary>
	/// <param name="environmentName">Registered clio environment name.</param>
	/// <param name="request">Build request (descriptor JSON content + optional overrides).</param>
	/// <returns>Structured build result with the created schema identity.</returns>
	CreateBusinessProcessResult BuildProcess(string environmentName, CreateBusinessProcessRequest request);
}

/// <summary>
/// Default ProcessDesignService-backed implementation of <see cref="ICreateBusinessProcessService"/>.
/// </summary>
public sealed class CreateBusinessProcessService(
	ISettingsRepository settingsRepository,
	IApplicationClientFactory applicationClientFactory,
	IServiceUrlBuilder serviceUrlBuilder,
	ILogger logger)
	: ICreateBusinessProcessService {
	private static readonly JsonSerializerOptions JsonOptions = new() {
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		PropertyNameCaseInsensitive = true
	};

	/// <inheritdoc />
	public CreateBusinessProcessResult BuildProcess(string environmentName, CreateBusinessProcessRequest request) {
		if (string.IsNullOrWhiteSpace(environmentName)) {
			throw new ArgumentException("Environment name is required.", nameof(environmentName));
		}

		ArgumentNullException.ThrowIfNull(request);
		if (string.IsNullOrWhiteSpace(request.DescriptorJson)) {
			throw new ArgumentException("Process descriptor content is required.", nameof(request));
		}

		EnvironmentSettings environmentSettings = settingsRepository.FindEnvironment(environmentName)
			?? throw new InvalidOperationException(
				EnvironmentNotFoundError.Build(environmentName, settingsRepository));
		JsonObject descriptor = ParseDescriptor(request.DescriptorJson);
		if (!string.IsNullOrWhiteSpace(request.PackageNameOverride)) {
			descriptor["packageName"] = request.PackageNameOverride;
		}

		IApplicationClient client = applicationClientFactory.CreateEnvironmentClient(environmentSettings);
		string url = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.BuildProcess, environmentSettings);
		// ProcessDesignService uses BodyStyle=Wrapped: the descriptor is wrapped under a "request" property.
		string requestBody = new JsonObject { ["request"] = descriptor }.ToJsonString();
		logger.WriteInfo($"Building process '{descriptor["name"]}' on '{environmentName}'...");

		string responseBody = client.ExecutePostRequest(url, requestBody);
		BuildProcessResponseEnvelope envelope =
			JsonSerializer.Deserialize<BuildProcessResponseEnvelope>(responseBody, JsonOptions)
			?? throw new InvalidOperationException("BuildProcess returned an empty response.");
		BuildProcessResultDto result = envelope.Result
			?? throw new InvalidOperationException("BuildProcess returned an unexpected response shape.");
		if (!result.Success) {
			throw new InvalidOperationException(result.ErrorMessage ?? "BuildProcess failed.");
		}

		return new CreateBusinessProcessResult(result.SchemaName, result.SchemaUId);
	}

	private static JsonObject ParseDescriptor(string descriptorJson) {
		JsonNode? node;
		try {
			node = JsonNode.Parse(descriptorJson);
		} catch (JsonException exception) {
			throw new InvalidOperationException(
				$"Process descriptor is not valid JSON: {exception.Message}", exception);
		}

		return node as JsonObject
			?? throw new InvalidOperationException("Process descriptor must be a JSON object.");
	}

	#region DTOs (wire shape)

	private sealed class BuildProcessResponseEnvelope {
		[JsonPropertyName("BuildProcessResult")]
		public BuildProcessResultDto? Result { get; set; }
	}

	private sealed class BuildProcessResultDto {
		[JsonPropertyName("success")]
		public bool Success { get; set; }

		[JsonPropertyName("schemaUId")]
		public string? SchemaUId { get; set; }

		[JsonPropertyName("schemaName")]
		public string? SchemaName { get; set; }

		[JsonPropertyName("errorMessage")]
		public string? ErrorMessage { get; set; }
	}

	#endregion
}

/// <summary>
/// Builds a business process from an inline JSON descriptor and prints the structured result.
/// </summary>
public class CreateBusinessProcessCommand(
	ICreateBusinessProcessService createBusinessProcessService,
	ILogger logger)
	: Command<CreateBusinessProcessOptions> {
	/// <inheritdoc />
	public override int Execute(CreateBusinessProcessOptions options) {
		try {
			ArgumentNullException.ThrowIfNull(options);
			if (string.IsNullOrWhiteSpace(options.Environment)) {
				throw new InvalidOperationException("Environment name is required.");
			}

			if (string.IsNullOrWhiteSpace(options.DescriptorJson)) {
				throw new InvalidOperationException("A process descriptor is required.");
			}

			CreateBusinessProcessResult result = createBusinessProcessService.BuildProcess(
				options.Environment,
				new CreateBusinessProcessRequest(options.DescriptorJson, options.PackageName));
			logger.WriteInfo($"Process '{result.SchemaName}' created (UId: {result.SchemaUId}).");
			return 0;
		} catch (Exception exception) {
			logger.WriteError(exception.Message);
			return 1;
		}
	}
}

/// <summary>
/// Request payload for building a business process.
/// </summary>
/// <param name="DescriptorJson">The JSON descriptor content (BuildProcessRequest shape).</param>
/// <param name="PackageNameOverride">Optional package name that overrides the descriptor's <c>packageName</c>.</param>
public sealed record CreateBusinessProcessRequest(string DescriptorJson, string? PackageNameOverride = null);

/// <summary>
/// Structured result of a business-process build.
/// </summary>
/// <param name="SchemaName">Final schema name of the created process.</param>
/// <param name="SchemaUId">UId of the created process schema.</param>
public sealed record CreateBusinessProcessResult(string? SchemaName, string? SchemaUId);
