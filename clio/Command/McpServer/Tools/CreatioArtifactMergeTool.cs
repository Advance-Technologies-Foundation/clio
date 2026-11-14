using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Exposes preview-only semantic merging for supported Creatio package artifacts.
/// </summary>
[McpServerToolType]
public sealed class CreatioArtifactMergeTool(ICreatioArtifactMergeService mergeService) {

	/// <summary>The stable MCP tool name.</summary>
	public const string ToolName = "merge-creatio-artifact";

	/// <summary>
	/// Merges inline Git base, ours, and theirs content without reading or changing a repository.
	/// </summary>
	/// <param name="args">Inline artifact path, stage contents, and optional descriptor evidence.</param>
	/// <param name="cancellationToken">Cancels waiting for bounded resolver capacity.</param>
	/// <returns>A domain-status result. Only resolved or conflict-marker outcomes contain content.</returns>
	[McpToolExecution(
		Location = McpToolExecutionLocation.InProcess,
		Lifetime = McpToolExecutionLifetime.NotApplicable,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.None,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Preview-only semantic three-way merge for supported Creatio EntitySchema, ClientUnit, " +
	             "ServiceSchema, Addon, descriptor, properties, resource, and data-binding artifacts. " +
	             "ProcessSchema metadata, descriptor, and resources, plus C# and SQL merge, are recognized but not implemented. Uses inline content " +
	             "only; never reads or changes a repository. Read get-guidance name=creatio-three-way-merge before use. " +
	             "When an EntitySchema column type conflicts, ask the user the question returned in diagnostics before choosing a side. " +
	             "A busy status is transient; retry the same request.")]
	public Task<CreatioArtifactMergeResult> Merge(
		[Description("Inline Git stage contents and a repository-relative classification path.")]
		[Required]
		CreatioArtifactMergeArgs args,
		CancellationToken cancellationToken = default) {
		return mergeService.MergeAsync(args, cancellationToken);
	}
}
