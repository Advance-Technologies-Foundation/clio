using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Clio.Common.Assertions;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for the full infrastructure assert sweep.
/// </summary>
[McpServerToolType]
public sealed class AssertInfrastructureTool
{
	/// <summary>
	/// Stable MCP tool name for the full infrastructure assert sweep.
	/// </summary>
	internal const string AssertInfrastructureToolName = "assert-infrastructure";

	private readonly IAssertInfrastructureAggregator _assertInfrastructureAggregator;

	/// <summary>
	/// Initializes a new instance of the <see cref="AssertInfrastructureTool"/> class.
	/// </summary>
	public AssertInfrastructureTool(IAssertInfrastructureAggregator assertInfrastructureAggregator)
	{
		_assertInfrastructureAggregator =
			assertInfrastructureAggregator ?? throw new ArgumentNullException(nameof(assertInfrastructureAggregator));
	}

	/// <summary>
	/// Executes the full infrastructure assertion sweep and returns the machine-readable result.
	/// </summary>
	[McpServerTool(Name = AssertInfrastructureToolName, ReadOnly = true, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[Description("""
				 Runs the full infrastructure assertion sweep in one call and returns a machine-readable aggregate result.
				 
				 The tool evaluates Kubernetes infrastructure, local infrastructure, and filesystem readiness using
				 the same assertion components as the CLI assert command presets. The response includes per-section
				 assertion results plus normalized database candidates that an agent can use to choose a deployment target.
				 """)]
	public Task<AssertInfrastructureResult> AssertInfrastructure()
	{
		return _assertInfrastructureAggregator.ExecuteAsync();
	}
}
