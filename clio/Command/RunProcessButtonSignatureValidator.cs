namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Pure validation of a single <c>crt.RunBusinessProcessRequest</c> button config against a resolved
/// process signature (ENG-91168). Kept free of MCP/environment concerns so it is unit-testable.
/// </summary>
internal static class RunProcessButtonSignatureValidator {

	internal sealed record Result(string Error, IReadOnlyList<string> Warnings);

	private static readonly HashSet<string> InputlessDirections =
		new(StringComparer.OrdinalIgnoreCase) { "Output", "Internal" };

	/// <summary>
	/// Validates that every parameter code referenced by <paramref name="config"/> exists on the
	/// process signature. An unknown code is an error (the platform silently drops such values); a
	/// code that resolves to an Output/Internal-only parameter is a warning (it will not be applied
	/// as an input).
	/// </summary>
	public static Result Validate(RunProcessButtonConfig config, IReadOnlyList<ProcessSignatureParameter> parameters) {
		IReadOnlyList<ProcessSignatureParameter> signatureParameters = parameters ?? [];
		var byCode = new Dictionary<string, ProcessSignatureParameter>(StringComparer.Ordinal);
		foreach (ProcessSignatureParameter parameter in signatureParameters) {
			if (!string.IsNullOrWhiteSpace(parameter.Name)) {
				byCode[parameter.Name] = parameter;
			}
		}

		string label = string.IsNullOrWhiteSpace(config.ButtonName)
			? "a run-process button"
			: $"run-process button '{config.ButtonName}'";

		List<string> unknown = config.ParameterCodes
			.Where(code => !byCode.ContainsKey(code))
			.Distinct(StringComparer.Ordinal)
			.ToList();
		if (unknown.Count > 0) {
			string validCodes = string.Join(", ", byCode.Keys);
			string error = $"{label} references parameter code(s) [{string.Join(", ", unknown)}] that do not exist "
				+ $"on process '{config.ProcessName}'. Valid parameter codes: [{validCodes}]. "
				+ "The key must be the parameter CODE (the 'name' from get-process-signature), not its caption — "
				+ "the platform silently drops values keyed by an unknown code.";
			return new Result(error, []);
		}

		var warnings = new List<string>();
		foreach (string code in config.ParameterCodes.Distinct(StringComparer.Ordinal)) {
			if (byCode.TryGetValue(code, out ProcessSignatureParameter parameter)
				&& !string.IsNullOrWhiteSpace(parameter.Direction)
				&& InputlessDirections.Contains(parameter.Direction)) {
				warnings.Add($"{label} maps a value to parameter '{code}' whose direction is "
					+ $"'{parameter.Direction}'; the process will not receive it as an input.");
			}
		}

		return new Result(null, warnings);
	}
}
