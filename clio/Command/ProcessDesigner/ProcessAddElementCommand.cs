using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Command.ProcessModel;
using Clio.Common;
using Clio.Common.BrowserSession;
using Clio.Common.ProcessDesigner;
using Clio.UserEnvironment;
using CommandLine;

namespace Clio.Command.ProcessDesigner;

/// <summary>
/// Options for adding an element to a Creatio process by driving the visual Process Designer over CDP.
/// </summary>
[Verb("process-add-element", Aliases = ["pae"],
	HelpText = "Drive the Process Designer to append and configure an element (Read data) and save the process.")]
public class ProcessAddElementOptions : EnvironmentOptions {

	/// <summary>Element type to add. This slice supports only <c>read-data</c>.</summary>
	[Option("element-type", Required = true, HelpText = "Element type to add. Supported: read-data.")]
	public string ElementType { get; set; }

	/// <summary>Object the Read data element reads, e.g. <c>Contact</c>.</summary>
	[Option("read-object", Required = true, HelpText = "Object the Read data element reads, e.g. Contact.")]
	public string ReadObject { get; set; }

	/// <summary>Existing process id to open; omit to create a new process.</summary>
	[Option("process-id", Required = false, HelpText = "Existing process id to open; omit to create a new process.")]
	public string ProcessId { get; set; }

	/// <summary>Deterministic caption (readback handle); auto-generated when omitted.</summary>
	[Option("process-caption", Required = false, HelpText = "Process caption (readback handle); auto-generated when omitted.")]
	public string ProcessCaption { get; set; }

	/// <summary>Run the browser headed (default true; headless is currently unverified).</summary>
	[Option("headed", Required = false, Default = true, HelpText = "Run the browser headed (default true; headless unverified).")]
	public bool Headed { get; set; } = true;
}

/// <summary>
/// Validates a planned <c>Start → Read data → End</c> graph, then deterministically drives the live
/// Process Designer over CDP (authenticated session → launched browser → designer driver) to append and
/// configure a Read data element and save the process — returning the saved process identity.
/// </summary>
public class ProcessAddElementCommand(
	IProcessGraphValidator validator,
	IBrowserSessionService browserSessionService,
	IAuthenticatedBrowserLauncher browserLauncher,
	IProcessDesignerDriver designerDriver,
	ISettingsRepository settingsRepository,
	ILogger logger) : Command<ProcessAddElementOptions> {

	private const string SupportedElementType = "read-data";

	private static readonly JsonSerializerOptions OutputOptions = new() {
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	/// <inheritdoc />
	public override int Execute(ProcessAddElementOptions options) {
		if (!string.Equals(options.ElementType, SupportedElementType, StringComparison.OrdinalIgnoreCase)) {
			logger.WriteError($"Error: unsupported --element-type '{options.ElementType}'. This slice supports only '{SupportedElementType}'.");
			return 1;
		}
		if (string.IsNullOrWhiteSpace(options.ReadObject)) {
			logger.WriteError("Error: --read-object is required (the object the Read data element reads, e.g. Contact).");
			return 1;
		}

		try {
			EnvironmentSettings environment = settingsRepository.GetEnvironment(options);

			// Pre-drive validation — abort BEFORE opening a browser on any error finding.
			ProcessGraphValidationResult validation = validator.Validate(BuildPlannedGraph());
			if (validation.HasErrors) {
				foreach (ProcessGraphFinding finding in validation.Findings.Where(f => f.Severity == ProcessGraphSeverity.Error)) {
					logger.WriteError($"Error: planned graph is invalid [{finding.RuleId}]: {finding.Message}");
				}
				return 1;
			}

			string caption = string.IsNullOrWhiteSpace(options.ProcessCaption) ? GenerateCaption() : options.ProcessCaption;

			string sessionPath;
			try {
				sessionPath = browserSessionService.GetSessionPathAsync(environment)
					.ConfigureAwait(false).GetAwaiter().GetResult();
			} catch (CreatioAuthenticationException e) {
				logger.WriteError($"Error: a forms-auth browser session is required to drive the Process Designer " +
					$"for environment '{environment.Uri}'. {e.Message}");
				return 1;
			}

			LaunchResult launch;
			try {
				launch = browserLauncher.LaunchAndKeepOpenAsync(environment, sessionPath)
					.ConfigureAwait(false).GetAwaiter().GetResult();
			} catch (ChromiumNotFoundException e) {
				logger.WriteError($"Error: {e.Message}");
				return 1;
			}

			ProcessAddElementResult result = designerDriver.AddReadDataElementAsync(
					new ProcessAddElementRequest(environment, launch.DevToolsPort, options.ProcessId, options.ReadObject, caption))
				.ConfigureAwait(false).GetAwaiter().GetResult();

			if (!result.Success) {
				logger.WriteError(result.Error ?? "Error: the Process Designer driver did not report a successful save.");
				return 1;
			}

			logger.WriteInfo(JsonSerializer.Serialize(new ProcessAddElementOutput(
				true, result.Code, result.UId, result.Caption, null), OutputOptions));
			return 0;
		} catch (Exception e) {
			logger.WriteError(e.GetReadableMessageException(Program.IsDebugMode));
			return 1;
		}
	}

	private static ProcessGraph BuildPlannedGraph() => new(
		[
			new ProcessGraphNode("start", "startEvent"),
			new ProcessGraphNode("read", "readDataUserTask"),
			new ProcessGraphNode("end", "endEvent")
		],
		[
			new ProcessGraphEdge("start", "read", ProcessFlowKind.Sequence),
			new ProcessGraphEdge("read", "end", ProcessFlowKind.Sequence)
		]);

	private static string GenerateCaption() =>
		$"clio-pae-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}";
}

/// <summary>Structured result of a <c>process-add-element</c> run (the readback handle for the caller).</summary>
public sealed record ProcessAddElementOutput(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("code")] string Code,
	[property: JsonPropertyName("uId")] string UId,
	[property: JsonPropertyName("caption")] string Caption,
	[property: JsonPropertyName("error")] [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string Error);
