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
// The version literal states what THIS command's code needs — the newest operation it sends that an
// older server does not have. Today that is the element-level performer block and the
// reference-existence guard behind it (bare-Guid Lookup values, performer contact/role), shipped in
// the 1.3.1.1 archive: an older server has no performer member and silently discards the block while
// answering success, and a pre-guard server stores a dead id instead of refusing it. Presence alone
// cannot express either — the email block's 1.2.0.1 floor set this precedent and is subsumed by this
// literal. Raised to 1.4.0.37 by ENG-95891: a build's `mappings[]` may carry an `expression` source, and
// the formula validator behind it is a TIGHTENED VALIDATOR — a server older than 1.4.0.0 stores such a
// mapping with no check at all, so the same descriptor that is refused on a current environment silently
// persists a broken formula on an older one, to fail at run time. The article is explicit that a tightened
// validator takes a literal rather than being left to convergence, because convergence only warns.
//
// Raised again to 1.4.0.42, and this time the reason runs the other way. 1.4.0.41 is the version that STOPPED
// validating formulas in the package, because the platform's own pre-save gate was already doing it — for a
// mapped expression AND for a flow condition, measured with both package guards built out and installed
// (spec/eng-95891-formula-expressions/eng-95891-formula-expressions-save-gate-probe.md). What the floor buys
// is therefore the MESSAGE contract these descriptions promise, not the existence of a refusal: below .41 a
// bad formula is still refused, but by the package's own wording and its own reference pre-check, and the
// serialised-error rewrite (PlatformValidationMessage) is not there — so an unresolvable parameter reference
// comes back as `{ErrorType:2,ErrorData:{ParameterUId:"…"}}`.
//
// The floor is .42 rather than .41 because .42 is the archive this clio bundles, and because the review round
// after .41 corrected the rewrite these descriptions promise: every serialised error in one message rather
// than the first, and an element-scoped reference named as such rather than called a process parameter.
//
// The floor is NOT lowered back to .37 on the grounds that .37 also refuses. It does, with different text, and
// a description that names what a refusal says is only true from .41. Nor is it a tightened validator any
// more: .41 checks strictly LESS than .37 did, so an environment between the two refuses at least as much.
// The superseded .37 rationale, kept because it is the reason not to go below it either way: each archive on
// the ENG-95891 branch was decompressed and grepped for the marker of every refusal the descriptions promised,
// and the activity-result guard (which SURVIVES the collapse) landed in .32, the platform-grammar element
// segment in .35, the element-retarget refusal in .37.
// The guard fixture asserts the shipped archive satisfies the literal, so clio can never
// demand a version it does not itself carry.
[RequiresPackage(BundledPackages.ProcessBuilderPackageName, "1.4.0.42",
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

		using IOwnedApplicationClient client = applicationClientFactory.CreateOwnedEnvironmentClient(environmentSettings);
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

		return new CreateBusinessProcessResult(result.SchemaName, result.SchemaUId, result.Warnings);
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

		[JsonPropertyName("warnings")]
		public List<string>? Warnings { get; set; }
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
			// Written as WARNINGS on a SUCCESSFUL build, the same way the modify path reports its own: these are
			// outcomes that applied and are not what a caller would assume, so dropping them would leave the caller
			// believing the formula was checked when it was not.
			foreach (string warning in result.Warnings ?? []) {
				logger.WriteWarning(warning);
			}
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
/// <param name="Warnings">
/// Caveats about a build that SUCCEEDED — an unrecognised macro family in a formula, or an expression whose
/// macros could not be resolved on this environment so its result type went unchecked. <c>null</c> or empty
/// when there are none, and always <c>null</c> against a server predating the member.
/// </param>
public sealed record CreateBusinessProcessResult(string? SchemaName, string? SchemaUId,
	IReadOnlyList<string>? Warnings = null);
