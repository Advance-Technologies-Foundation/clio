using System;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;

namespace Clio.Command;

/// <summary>
/// Options for reading an existing dynamic case (DCM), or every case of an object, through the ProcessDesignService
/// package. Consumed by the MCP <c>describe-dynamic-case</c> tool, which sets these properties directly; there is no
/// CLI verb. One of <see cref="CaseName"/>, <see cref="CaseUId"/> and <see cref="EntitySchemaName"/> names the target.
/// </summary>
[RequiresPackage(BundledPackages.ProcessBuilderPackageName, DynamicCaseChangeRequirement.MinimumProcessBuilderVersion,
	Hint = BundledPackages.ProcessBuilderInstallHint)]
public sealed class DescribeDynamicCaseOptions : EnvironmentOptions {

	/// <summary>The case's schema name.</summary>
	public string CaseName { get; set; }

	/// <summary>The case's schema UId.</summary>
	public string CaseUId { get; set; }

	/// <summary>The object whose every case, enabled or not, is described.</summary>
	public string EntitySchemaName { get; set; }
}

/// <summary>
/// Reads an existing dynamic case and reports it: a one-line verdict, the result document, the notes to relay and
/// the warnings. The exit code is 0 when the case or the object could be read, and 1 otherwise.
/// </summary>
public class DescribeDynamicCaseCommand(IDynamicCaseService dynamicCaseService, ILogger logger)
	: Command<DescribeDynamicCaseOptions> {

	private static readonly JsonSerializerOptions ResultJsonOptions = new() {
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	/// <inheritdoc />
	public override int Execute(DescribeDynamicCaseOptions options) {
		try {
			ArgumentNullException.ThrowIfNull(options);
			if (string.IsNullOrWhiteSpace(options.Environment)) {
				throw new InvalidOperationException("environment-name is required and cannot be empty.");
			}
			DescribeDynamicCaseResult result = dynamicCaseService.Describe(options);
			Report(result);
			return result.Success ? 0 : 1;
		} catch (Exception exception) {
			logger.WriteError(exception.Message);
			return 1;
		}
	}

	/// <summary>The one-line verdict of a describe, the sentence an agent repeats to the user.</summary>
	/// <param name="result">The result document.</param>
	internal static string Verdict(DescribeDynamicCaseResult result) {
		if (!result.Success) {
			return $"Refused [{result.ErrorCode}]: {result.ErrorMessage}";
		}
		int count = result.Cases?.Count ?? 0;
		if (count == 0) {
			return "No dynamic case was found; nothing was changed.";
		}
		if (count > 1) {
			return $"{count} dynamic cases read; nothing was changed.";
		}
		DynamicCaseInfo found = result.Cases[0];
		string state = found.Enabled ? "enabled" : "disabled";
		return $"Dynamic case '{found.SchemaName}' read: {found.Stages?.Count ?? 0} stages, {state}; nothing was "
			+ "changed.";
	}

	private void Report(DescribeDynamicCaseResult result) {
		string verdict = Verdict(result);
		if (result.Success) {
			logger.WriteInfo(verdict);
		} else {
			logger.WriteError(verdict);
		}
		logger.WriteInfo(JsonSerializer.Serialize(result, ResultJsonOptions));
		foreach (string note in (result.Notes ?? []).Where(note => !string.IsNullOrWhiteSpace(note))) {
			logger.WriteInfo(note);
		}
		foreach (string warning in (result.Warnings ?? []).Where(warning => !string.IsNullOrWhiteSpace(warning))) {
			logger.WriteWarning(warning);
		}
	}
}
