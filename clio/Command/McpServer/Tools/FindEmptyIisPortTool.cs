using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Clio.Common.IIS;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for finding an empty IIS deployment port.
/// </summary>
[McpServerToolType]
public sealed class FindEmptyIisPortTool
{
	/// <summary>
	/// Stable MCP tool name for empty IIS port discovery.
	/// </summary>
	internal const string FindEmptyIisPortToolName = "find-empty-iis-port";

	internal const int RangeStart = 40000;
	internal const int RangeEnd = 42000;

	private readonly IAvailableIisPortService _availableIisPortService;

	/// <summary>
	/// Initializes a new instance of the <see cref="FindEmptyIisPortTool"/> class.
	/// </summary>
	public FindEmptyIisPortTool(IAvailableIisPortService availableIisPortService)
	{
		_availableIisPortService =
			availableIisPortService ?? throw new ArgumentNullException(nameof(availableIisPortService));
	}

	/// <summary>
	/// Finds the first free IIS deployment port between 40000 and 42000.
	/// </summary>
	[McpServerTool(Name = FindEmptyIisPortToolName, ReadOnly = true, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[Description("""
				 Finds the first free IIS deployment port between 40000 and 42000.

				 Use this before `deploy-creatio` when you need a safe local IIS `sitePort`. Run
				 `assert-infrastructure` for full visibility, run `show-passing-infrastructure` to choose
				 database and Redis targets, and then run `find-empty-iis-port` to choose the local IIS port.
				 """)]
	public Task<FindAvailableIisPortResult> FindEmptyIisPort()
	{
		return _availableIisPortService.FindAsync(RangeStart, RangeEnd);
	}
}
