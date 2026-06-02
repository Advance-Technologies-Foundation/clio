using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Clio.Common.Telemetry;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool for product telemetry measurement events.
/// </summary>
[McpServerToolType]
public sealed class SendMeasurementsTool
{
	/// <summary>
	/// Stable MCP tool name for product measurement events.
	/// </summary>
	internal const string ToolName = "send-measurements";

	private readonly IMeasurementService _measurementService;

	/// <summary>
	/// Initializes a new instance of the <see cref="SendMeasurementsTool"/> class.
	/// </summary>
	public SendMeasurementsTool(IMeasurementService measurementService)
	{
		_measurementService = measurementService ?? throw new ArgumentNullException(nameof(measurementService));
	}

	/// <summary>
	/// Stores a product telemetry measurement as a local event file.
	/// </summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true)]
	[Description("""
				 Stores a product telemetry measurement event as a local OpenTelemetry-shaped JSON file.

				 Call get-measurements-consent before using this tool. Use telemetry_consent only on first run
				 after asking the developer, so Clio can store the local consent decision.
				 """)]
	public MeasurementResult SendMeasurements(
		[Description("Product workflow measurement metadata. Include telemetry_consent only when storing the first-run consent decision.")]
		[Required]
		MeasurementRequest args)
	{
		return _measurementService.Send(args);
	}
}
