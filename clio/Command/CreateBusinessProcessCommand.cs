using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Clio.Command.ProcessModel;
using Clio.Common;
using Clio.UserEnvironment;
using ErrorOr;

namespace Clio.Command;

/// <summary>
/// Options for building a business process from a declarative descriptor via the ProcessDesignService package.
/// Consumed by the MCP <c>create-business-process</c> tool, which sets these properties directly.
/// </summary>
// The version literal states what THIS command's code needs: the email block ships in the 1.2.0.1
// bundle, and an older server has no email member and silently discards the block while answering
// success. Presence alone cannot express that. The guard fixture asserts the shipped archive
// satisfies this literal, so clio can never demand a version it does not itself carry.
[RequiresPackage(BundledPackages.ProcessBuilderPackageName, "1.2.0.1",
	Hint = BundledPackages.ProcessBuilderInstallHint)]
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
	IProcessDescriber processDescriber,
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
			WarnOnDiscardedEmailBlocks(options, result.SchemaName);
			return 0;
		} catch (Exception exception) {
			logger.WriteError(exception.Message);
			return 1;
		}
	}

	// A server that predates sendEmail DISCARDS an email block and still answers success:true, so a build can
	// report a configured email element that is in fact empty. Read the saved process back and say so when the
	// block did not land. Only runs when the descriptor actually carried a block, so the ordinary path pays
	// nothing; a failure to verify is never escalated, because an unreadable description is not evidence of a
	// dropped block. See EmailBlockExpectation for why this is behavioural rather than version-based.
	private void WarnOnDiscardedEmailBlocks(CreateBusinessProcessOptions options, string? schemaName) {
		IReadOnlyList<string> expected = EmailBlockExpectation.FromDescriptor(options.DescriptorJson);
		if (expected.Count == 0 || string.IsNullOrWhiteSpace(schemaName)) {
			return;
		}

		ErrorOr<DescribeProcessResult> described =
			processDescriber.Describe(new ProcessIdentity(schemaName, null, null), null);
		if (described.IsError) {
			return;
		}

		string? dropped = EmailBlockExpectation.BuildWarning(
			EmailBlockExpectation.Missing(described.Value, expected));
		if (dropped is not null) {
			logger.WriteWarning(dropped);
		}

		// A package that predates the body-macro feature stores the [[…]] placeholders verbatim and still answers
		// success, so the read-back is the only place the un-resolved body surfaces — the element reports a body but
		// describe returns none (a healthy build decodes the tokens back into a non-null [[…]] body, so the presence
		// of [[ in the read-back is NOT the signal). Reuses the description already pulled above.
		string? unresolved = EmailBlockExpectation.BuildMacroWarning(
			EmailBlockExpectation.UnresolvedBodyMacros(
				described.Value, EmailBlockExpectation.MacroBodyElements(options.DescriptorJson)));
		if (unresolved is not null) {
			logger.WriteWarning(unresolved);
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
