namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.ProcessModel;
using Clio.Common;
using CommandLine;
using ErrorOr;
using Newtonsoft.Json;

/// <summary>
/// Options for reading the input/output parameter signature of a Creatio business process.
/// </summary>
[Verb("get-process-signature", Aliases = ["gps"],
	HelpText = "Read the parameter signature (codes, types, direction) of a Creatio business process")]
// NOTE: deliberately NOT [RequiresPackage("clioprocessbuilder")] — get-process-signature reads the built-in
// DataService (ProcessSchemaRequest / VwProcessLib), present on every Creatio; it never calls ProcessDesignService.
// Its MCP tool is grouped with the experimental suite via [FeatureToggle("process-designer")] instead.
public class GetProcessSignatureOptions : EnvironmentOptions {

	/// <summary>
	/// Process code (schema name) as it appears in the Creatio process designer, e.g. <c>UsrProcess_e629820</c>.
	/// </summary>
	[Value(0, MetaName = "ProcessName", Required = true,
		HelpText = "Process code (schema Name) or display caption as it appears in the process designer")]
	public string ProcessName { get; set; } = string.Empty;

	/// <summary>
	/// Culture name used to select localized parameter captions.
	/// </summary>
	[Option('x', "culture", Required = false, HelpText = "Caption culture", Default = "en-US")]
	public string Culture { get; set; } = "en-US";
}

/// <summary>
/// One process parameter projected for the run-process button config builder.
/// The <see cref="Name"/> is the parameter CODE — the value that must be used as the key in
/// <c>processParameters</c> / <c>parameterMappings</c> / <c>recordIdProcessParameterName</c>.
/// The display <see cref="Caption"/> must NOT be used as the key (the core silently drops values
/// keyed by an unknown code).
/// </summary>
public sealed class ProcessSignatureParameter {

	[JsonProperty("name")]
	[System.Text.Json.Serialization.JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonProperty("caption")]
	[System.Text.Json.Serialization.JsonPropertyName("caption")]
	public string Caption { get; set; }

	[JsonProperty("clrType")]
	[System.Text.Json.Serialization.JsonPropertyName("clrType")]
	public string ClrType { get; set; }

	[JsonProperty("dataValueTypeId")]
	[System.Text.Json.Serialization.JsonPropertyName("dataValueTypeId")]
	public string DataValueTypeId { get; set; }

	[JsonProperty("direction")]
	[System.Text.Json.Serialization.JsonPropertyName("direction")]
	public string Direction { get; set; }

	[JsonProperty("isLookup")]
	[System.Text.Json.Serialization.JsonPropertyName("isLookup")]
	public bool IsLookup { get; set; }

	[JsonProperty("referenceSchemaUId")]
	[System.Text.Json.Serialization.JsonPropertyName("referenceSchemaUId")]
	public string ReferenceSchemaUId { get; set; }
}

/// <summary>
/// Structured response for the <c>get-process-signature</c> tool/command.
/// </summary>
public sealed class GetProcessSignatureResponse {

	[JsonProperty("success")]
	[System.Text.Json.Serialization.JsonPropertyName("success")]
	public bool Success { get; set; }

	/// <summary>
	/// <c>true</c> when the process could not be uniquely resolved — it does not exist, OR the caption
	/// matched more than one process (ambiguous) — as opposed to a transport/transient failure. Lets
	/// callers treat a definitive resolution failure as a hard error while downgrading transient
	/// backend failures to a non-blocking warning.
	/// </summary>
	[JsonProperty("processResolutionFailed")]
	[System.Text.Json.Serialization.JsonPropertyName("processResolutionFailed")]
	public bool ProcessResolutionFailed { get; set; }

	[JsonProperty("processCode")]
	[System.Text.Json.Serialization.JsonPropertyName("processCode")]
	public string ProcessCode { get; set; }

	[JsonProperty("processCaption")]
	[System.Text.Json.Serialization.JsonPropertyName("processCaption")]
	public string ProcessCaption { get; set; }

	[JsonProperty("processId")]
	[System.Text.Json.Serialization.JsonPropertyName("processId")]
	public string ProcessId { get; set; }

	[JsonProperty("parameters")]
	[System.Text.Json.Serialization.JsonPropertyName("parameters")]
	public List<ProcessSignatureParameter> Parameters { get; set; } = [];

	[JsonProperty("error")]
	[System.Text.Json.Serialization.JsonPropertyName("error")]
	public string Error { get; set; }
}

/// <summary>
/// Reads the parameter signature of a Creatio business process so an AI agent can map button values
/// to the correct parameter CODES when authoring a <c>crt.RunBusinessProcessRequest</c> button config.
/// Reuses <see cref="IProcessModelGenerator"/> which already resolves the process by name and loads its
/// metadata (parameters).
/// </summary>
public class GetProcessSignatureCommand(IProcessModelGenerator generator, ILogger logger)
	: Command<GetProcessSignatureOptions> {

	/// <summary>
	/// Resolves the process by name and projects its parameters into <paramref name="response"/>.
	/// </summary>
	/// <returns><c>true</c> when the process was resolved; otherwise <c>false</c>.</returns>
	public virtual bool TryGetSignature(GetProcessSignatureOptions options,
		out GetProcessSignatureResponse response) {
		if (string.IsNullOrWhiteSpace(options.ProcessName)) {
			response = new GetProcessSignatureResponse { Success = false, Error = "process-name is required" };
			return false;
		}

		GenerateProcessModelCommandOptions generatorOptions = new() {
			Code = options.ProcessName,
			Culture = string.IsNullOrWhiteSpace(options.Culture) ? "en-US" : options.Culture
		};

		ErrorOr<ProcessModel.ProcessModel> resultOrError = generator.Generate(generatorOptions);
		if (resultOrError.IsError) {
			string error = string.Join("; ",
				resultOrError.Errors.Select(e => $"{e.Code} - {e.Description}"));
			bool resolutionFailed = resultOrError.Errors.Any(e =>
				e.Type is ErrorOr.ErrorType.NotFound or ErrorOr.ErrorType.Conflict);
			response = new GetProcessSignatureResponse {
				Success = false,
				ProcessResolutionFailed = resolutionFailed,
				Error = error
			};
			return false;
		}

		ProcessModel.ProcessModel model = resultOrError.Value;
		response = new GetProcessSignatureResponse {
			Success = true,
			ProcessCode = model.Code,
			ProcessCaption = model.Name,
			ProcessId = model.Id.ToString(),
			Parameters = (model.Parameters ?? [])
				.Select(parameter => ToSignatureParameter(parameter, generatorOptions.Culture))
				.ToList()
		};
		return true;
	}

	private static ProcessSignatureParameter ToSignatureParameter(ProcessParameter parameter, string culture) {
		string caption = null;
		parameter.Captions?.TryGetValue(culture, out caption);
		return new ProcessSignatureParameter {
			Name = parameter.Name,
			Caption = caption,
			ClrType = parameter.DataValueTypeResolved?.FullName,
			DataValueTypeId = parameter.DataValueType == Guid.Empty
				? null
				: parameter.DataValueType.ToString(),
			Direction = parameter.Direction.ToString(),
			IsLookup = parameter.ReferenceSchemaUId.HasValue
				&& parameter.ReferenceSchemaUId.Value != Guid.Empty,
			ReferenceSchemaUId = parameter.ReferenceSchemaUId.HasValue
				&& parameter.ReferenceSchemaUId.Value != Guid.Empty
					? parameter.ReferenceSchemaUId.Value.ToString()
					: null
		};
	}

	public override int Execute(GetProcessSignatureOptions options) {
		bool success = TryGetSignature(options, out GetProcessSignatureResponse response);
		logger.WriteInfo(JsonConvert.SerializeObject(response));
		return success ? 0 : 1;
	}
}
