using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Clio.Common.Assertions;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for passing-only infrastructure discovery used by deployment agents.
/// </summary>
[McpServerToolType]
public sealed class ShowPassingInfrastructureTool
{
	/// <summary>
	/// Stable MCP tool name for passing-only infrastructure discovery.
	/// </summary>
	internal const string ShowPassingInfrastructureToolName = "show-passing-infrastructure";

	private readonly IPassingInfrastructureService _passingInfrastructureService;

	/// <summary>
	/// Initializes a new instance of the <see cref="ShowPassingInfrastructureTool"/> class.
	/// </summary>
	public ShowPassingInfrastructureTool(IPassingInfrastructureService passingInfrastructureService)
	{
		_passingInfrastructureService =
			passingInfrastructureService ?? throw new ArgumentNullException(nameof(passingInfrastructureService));
	}

	/// <summary>
	/// Returns only passing infrastructure choices and deployment recommendations.
	/// </summary>
	[McpServerTool(Name = ShowPassingInfrastructureToolName, ReadOnly = true, Destructive = false,
		Idempotent = true, OpenWorld = false)]
	[Description("""
				 Shows only the passing infrastructure choices that are safe to use for deployment selection.

				 Run `assert-infrastructure` first to inspect failing or degraded areas, then run
				 `show-passing-infrastructure` to get deployable database and Redis choices plus the
				 recommended `deploy-creatio` argument bundle for the current infrastructure state.
				 """)]
	public Task<ShowPassingInfrastructureResult> ShowPassingInfrastructure()
	{
		return _passingInfrastructureService.ExecuteAsync();
	}
}
