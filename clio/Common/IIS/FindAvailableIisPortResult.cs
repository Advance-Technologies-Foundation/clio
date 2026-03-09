using System.Collections.Generic;

namespace Clio.Common.IIS;

/// <summary>
/// Structured result for IIS port discovery used by MCP callers.
/// </summary>
/// <param name="Status">Availability status for the requested range.</param>
/// <param name="Summary">Human-readable scan summary.</param>
/// <param name="RangeStart">Inclusive start of the scanned range.</param>
/// <param name="RangeEnd">Inclusive end of the scanned range.</param>
/// <param name="FirstAvailablePort">First discovered free IIS port, if any.</param>
/// <param name="IisBoundPortCount">Number of ports already claimed by IIS site bindings.</param>
/// <param name="ActiveTcpPortCount">Number of ports already claimed by active TCP listeners or connections.</param>
public sealed record FindAvailableIisPortResult(
	string Status,
	string Summary,
	int RangeStart,
	int RangeEnd,
	int? FirstAvailablePort,
	int IisBoundPortCount,
	int ActiveTcpPortCount);
