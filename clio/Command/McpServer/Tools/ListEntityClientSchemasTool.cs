using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class ListEntityClientSchemasTool(
	ListEntityClientSchemasCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<ListEntityClientSchemasOptions>(command, logger, commandResolver) {

	internal const string ToolName = "list-entity-client-schemas";

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"Resolve the page-role graph of an entity for a Classic->Freedom migration: its Classic sections, " +
		"edit pages (including per-type/typed pages) and add mini pages, each classified classic, freedom, or unknown. " +
		"One level only — recurse into detail entities by calling this per detail entity; details-on-page and " +
		"Freedom counterparts are read from the page body by the merge module, not here. " +
		"Prefer `environment-name`; keep direct connection args only for bootstrap or emergency fallback flows.")]
	public ListEntityClientSchemasResponse Resolve(
		[Description("Parameters: entity-name (required); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required]
		ListEntityClientSchemasArgs args) {
		if (args is null) {
			return new ListEntityClientSchemasResponse { Success = false, Error = "args is required" };
		}
		ListEntityClientSchemasOptions options = new() {
			EntityName = args.EntityName,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		// This tool is environment-sensitive (environment-name/uri/login/password), so it must run under the
		// PER-TENANT execution lock. ExecuteResolved keys the lock on the resolved tenant and marks the session
		// container in-use for the call (ENG-93208), instead of the environment-less ExecuteWithCleanLog overload
		// which keys on the shared fallback — that would serialize independent tenants and leave the resolved
		// session container evictable mid-call. ExecuteResolved also centralizes the resolution-failure redaction
		// that used to be hand-rolled here.
		return ExecuteResolved<ListEntityClientSchemasCommand, ListEntityClientSchemasResponse>(
			options,
			resolvedCommand => {
				resolvedCommand.TryResolve(options, out ListEntityClientSchemasResponse response);
				if (!string.IsNullOrEmpty(response?.Error)) {
					// The command's inner error can carry an HTTP/DataService message with the environment
					// URI/host; redact before it lands in the MCP transcript.
					response.Error = SensitiveErrorTextRedactor.Redact(response.Error);
				}
				return response;
			},
			error => new ListEntityClientSchemasResponse { Success = false, Error = error });
	}
}

public sealed record ListEntityClientSchemasArgs(
	[property: JsonPropertyName("entity-name")]
	[property: Description("Entity schema name, e.g. 'Contract' or 'SupportUnit'")]
	[property: Required]
	string EntityName
) : ConnectionArgsBase;
