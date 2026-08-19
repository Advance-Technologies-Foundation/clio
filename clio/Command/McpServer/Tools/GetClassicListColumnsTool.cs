using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>Exposes Classic section default-column resolution through MCP.</summary>
[McpServerToolType]
public sealed class GetClassicListColumnsTool(
	GetClassicListColumnsCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<GetClassicListColumnsOptions>(command, logger, commandResolver) {

	internal const string ToolName = "get-classic-list-columns";

	/// <summary>Resolves a Classic section's effective default list columns without modifying Creatio data.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	// InProcess, and NOT because the tool is cheap — it makes a bounded HTTP read like its Worker-classified
	// neighbours. It is InProcess because of WHAT the read goes through: the hierarchy comes from the schema
	// designer (IPageDesignerHierarchyClient), which is the read path McpWorkerCohort's
	// SchemaDesignerReadsWithheldNames withdrew on 2026-08-19. A worker authenticates on its own Creatio
	// session, and a schema-designer read on a second session is not guaranteed to see a write the
	// host-resident writers just made — measured at 14 s stale, which arms a conflict baseline against a
	// superseded generation. Declaring Worker here would leave that constraint encoded only in a comment, and
	// "never relayed to a worker" is exactly what InProcess means, so the next cohort expansion cannot admit
	// this read by reasoning over the classification. This tool arrived from master after the annotation
	// wave, which is why the classification lands here rather than with the others.
	[McpToolExecution(
		Location = McpToolExecutionLocation.InProcess,
		Lifetime = McpToolExecutionLifetime.NotApplicable,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.None,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description(
		"Resolve the effective default columns of a Classic section list through read-only Creatio APIs. " +
		"Returns source=schema-default for static getGridDataColumns/initColumnsConfig paths, " +
		"source=entity-default for the entity primary display column, or a successful source=none with no columns. " +
		"Does not read or write user profile data. Prefer environment-name; direct connection args are fallback only.")]
	public GetClassicListColumnsResponse Resolve(
		[Description("Parameters: schema-name (required Classic section schema); environment-name preferred.")]
		[Required]
		GetClassicListColumnsArgs args) {
		if (args is null) {
			return new GetClassicListColumnsResponse { Success = false, Error = "args is required" };
		}
		GetClassicListColumnsOptions options = new() {
			SchemaName = args.SchemaName,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		return ExecuteResolved<GetClassicListColumnsCommand, GetClassicListColumnsResponse>(
			options,
			resolvedCommand => {
				resolvedCommand.TryResolve(options, out GetClassicListColumnsResponse response);
				if (!string.IsNullOrEmpty(response?.Error)) {
					response.Error = SensitiveErrorTextRedactor.Redact(response.Error);
				}
				if (response?.Notes is { Count: > 0 }) {
					response.Notes = SensitiveErrorTextRedactor.RedactAll(response.Notes);
				}
				return response;
			},
			error => new GetClassicListColumnsResponse { Success = false, Error = error });
	}
}

/// <summary>Arguments accepted by <see cref="GetClassicListColumnsTool"/>.</summary>
/// <param name="SchemaName">Classic section client-unit schema name.</param>
public sealed record GetClassicListColumnsArgs(
	[property: JsonPropertyName("schema-name")]
	[property: Description("Classic section schema name, for example 'ContactSectionV2'")]
	[property: Required]
	string SchemaName
) : ConnectionArgsBase;
