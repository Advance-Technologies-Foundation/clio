using System;
using System.ComponentModel;
using Clio.Common.Telemetry;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool for reading local ADAC product telemetry consent state.
/// </summary>
[McpServerToolType]
public sealed class GetMeasurementsConsentTool
{
	/// <summary>
	/// Stable MCP tool name for reading product telemetry consent.
	/// </summary>
	internal const string ToolName = "get-measurements-consent";

	private readonly IMeasurementService _measurementService;

	/// <summary>
	/// Initializes a new instance of the <see cref="GetMeasurementsConsentTool"/> class.
	/// </summary>
	public GetMeasurementsConsentTool(IMeasurementService measurementService)
	{
		_measurementService = measurementService ?? throw new ArgumentNullException(nameof(measurementService));
	}

	/// <summary>
	/// Reads locally persisted ADAC product telemetry consent without writing analytics.
	/// </summary>
	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Reads locally persisted ADAC product telemetry consent without storing any measurement event.")]
	public MeasurementConsentResult GetMeasurementsConsent()
	{
		return _measurementService.GetConsentStatus();
	}
}
