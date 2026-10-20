using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool that places an application section in a navigation workplace, grants roles access to it, and ships
/// every row as a package data binding — the whole <c>workplaces</c> recipe in one call.
/// </summary>
[McpServerToolType]
public sealed class PlaceSectionInWorkplaceTool(ILogger logger, IToolCommandResolver commandResolver)
	: BaseTool<EnvironmentOptions>(null, logger, commandResolver) {

	/// <summary>Stable MCP tool name.</summary>
	internal const string ToolName = "place-section-in-workplace";

	private const string SectionCodeArgument = "section-code";

	private const string ValidFields =
		"Valid: environment-name, package-name, section-code, workplace-name, client-type, roles, " +
		"remove-from-my-applications, position, mode, workplace-type.";

	private static readonly Dictionary<string, string> LegacyAliases =
		new(McpToolArgumentSupport.EnvironmentNameAliases, StringComparer.Ordinal) {
			["packageName"] = "package-name",
			["package_name"] = "package-name",
			["package"] = "package-name",
			["sectionCode"] = SectionCodeArgument,
			["section_code"] = SectionCodeArgument,
			["code"] = SectionCodeArgument,
			["app-code"] = SectionCodeArgument,
			["workplaceName"] = "workplace-name",
			["workplace_name"] = "workplace-name",
			["workplace"] = "workplace-name",
			["clientType"] = "client-type",
			["client_type"] = "client-type",
			["removeFromMyApplications"] = "remove-from-my-applications",
			["remove_from_my_applications"] = "remove-from-my-applications",
			["workplaceType"] = "workplace-type",
			["workplace_type"] = "workplace-type",
			["type"] = "workplace-type"
		};

	/// <summary>Places the section, grants the roles, binds every row, and reads the result back.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description("""
				 Places an app section in a navigation workplace and grants roles access, in one call. Confirm the
				 workplace and the roles with the user first: a grant decides who sees the data behind the section.
				 Creates the workplace (SysWorkplace, matched by workplace-name and client-type) or reuses it,
				 moves the section's `My applications` placement into it (remove-from-my-applications, default true;
				 otherwise adds a placement; a move no role would see is refused, so pass roles), grants each role
				 (SysAdminUnitInWorkplace), and ships every row as a data binding of package-name so it transfers
				 with the package. Idempotent: a re-run changes nothing. Never deletes a row. Returns ids, what
				 changed (created/moved/granted/already-...), binding names, warnings and section-workplaces (where
				 the section is now). mode: existing (must exist), new (create; refused if the name is taken by a
				 workplace package-name does not ship), none (write nothing, only report), omitted (reuse or
				 create). workplace-type (general|portal|self-service) sets the type of a created workplace;
				 a portal for external users needs portal. An existing workplace of another type is refused,
				 never retyped. Afterwards tell the user to log out and back in.
				 """)]
	public PlaceSectionInWorkplaceResponse PlaceSectionInWorkplace(
		[Description("Parameters: environment-name, section-code (required); workplace-name (required unless mode=none); mode (existing|new|none), workplace-type (general|portal|self-service), package-name, client-type (web|mobile), roles, remove-from-my-applications, position (optional).")]
		[Required] PlaceSectionInWorkplaceArgs args) {
		string aliasError = McpToolArgumentSupport.BuildLegacyAliasError(args.ExtensionData, LegacyAliases, ".",
			ValidFields);
		if (!string.IsNullOrWhiteSpace(aliasError)) {
			return Failure(aliasError);
		}
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			return Failure("environment-name is required and cannot be empty.");
		}
		if (string.IsNullOrWhiteSpace(args.SectionCode)) {
			return Failure("section-code is required and cannot be empty.");
		}
		bool modeNone = string.Equals(args.Mode?.Trim(), WorkplacePlacementService.ModeNone,
			StringComparison.OrdinalIgnoreCase);
		if (!modeNone && string.IsNullOrWhiteSpace(args.WorkplaceName)) {
			return Failure("workplace-name is required unless mode is 'none'.");
		}
		EnvironmentOptions options = new() { Environment = args.EnvironmentName };
		WorkplacePlacementRequest request = new(
			args.EnvironmentName,
			args.PackageName,
			args.SectionCode,
			args.WorkplaceName,
			string.IsNullOrWhiteSpace(args.ClientType) ? "web" : args.ClientType,
			args.Roles ?? [],
			args.RemoveFromMyApplications ?? true,
			args.Position,
			args.Mode,
			args.WorkplaceType);
		return ExecuteWithCleanLog(options, () => {
			try {
				IWorkplacePlacementService service = ResolveFromCallContainer<IWorkplacePlacementService>(options);
				WorkplacePlacementResult result = service.Place(request);
				return result.Error is null ? Success(result) : PartialFailure(result);
			}
			catch (Exception ex) {
				return Failure(SensitiveErrorTextRedactor.Redact(ex.Message));
			}
		});
	}

	private static PlaceSectionInWorkplaceResponse Success(WorkplacePlacementResult result) =>
		new() {
			Success = true,
			PackageName = result.PackageName,
			Workplace = result.Workplace,
			Section = result.Section,
			Roles = result.Roles,
			Warnings = result.Warnings.Count == 0 ? null : result.Warnings,
			SectionWorkplaces = result.SectionWorkplaces,
			NextStep = result.NextStep
		};

	private static PlaceSectionInWorkplaceResponse PartialFailure(WorkplacePlacementResult result) =>
		new() {
			Success = false,
			Error = SensitiveErrorTextRedactor.Redact(result.Error),
			WritesStarted = true,
			Written = result.Written,
			PackageName = result.PackageName,
			Warnings = result.Warnings.Count == 0 ? null : result.Warnings,
			NextStep = result.NextStep
		};

	private static PlaceSectionInWorkplaceResponse Failure(string error) =>
		new() { Success = false, Error = string.IsNullOrWhiteSpace(error) ? "unknown" : error };
}

/// <summary>MCP arguments for the <c>place-section-in-workplace</c> tool.</summary>
public sealed record PlaceSectionInWorkplaceArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	[property: Required]
	string EnvironmentName = null,

	[property: JsonPropertyName("section-code")]
	[property: Description("The section's SysModule.Code (for a create-app app, its code), not its caption")]
	[property: Required]
	string SectionCode = null,

	[property: JsonPropertyName("workplace-name")]
	[property: Description("Workplace to create or reuse, matched by name and client type; required unless mode is none")]
	string WorkplaceName = null,

	[property: JsonPropertyName("package-name")]
	[property: Description("Package that ships every row (use the app's package); omit to use the environment's current package, as get-target-package resolves it")]
	string PackageName = null,

	[property: JsonPropertyName("client-type")]
	[property: Description("web (default) or mobile")]
	string ClientType = null,

	[property: JsonPropertyName("roles")]
	[property: Description("SysAdminUnit names or ids that see the workplace; default none. A name must match exactly one role")]
	IReadOnlyList<string> Roles = null,

	[property: JsonPropertyName("remove-from-my-applications")]
	[property: Description("Move the section's My applications placement instead of adding a second one; default true")]
	bool? RemoveFromMyApplications = null,

	[property: JsonPropertyName("position")]
	[property: Description("Optional workplace position in the switcher; the platform renumbers positions, so read it back")]
	int? Position = null,

	[property: JsonPropertyName("mode")]
	[property: Description("existing (workplace must exist), new (create it; refused if a same-named one exists that package-name does not ship), none (leave the section where it is, only report; roles and workplace-type are refused), or omit to reuse-or-create")]
	string Mode = null,

	[property: JsonPropertyName("workplace-type")]
	[property: Description("general, portal or self-service: the type a created workplace gets (omit for the platform default, General), and the type an existing workplace must already have; an existing workplace is never retyped")]
	string WorkplaceType = null
) {
	/// <summary>Overflow bag for unknown JSON fields; drives the legacy-alias rename hints.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement> ExtensionData { get; init; }
}

/// <summary>Structured result of the <c>place-section-in-workplace</c> tool.</summary>
public sealed record PlaceSectionInWorkplaceResponse {

	/// <summary>Whether the placement, grants and bindings were all written and read back.</summary>
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	/// <summary>Package that ships every row.</summary>
	[JsonPropertyName("package-name")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string PackageName { get; init; }

	/// <summary>The target workplace.</summary>
	[JsonPropertyName("workplace")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public WorkplacePlacementWorkplace Workplace { get; init; }

	/// <summary>The section placement.</summary>
	[JsonPropertyName("section")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public WorkplacePlacementSection Section { get; init; }

	/// <summary>The requested role grants.</summary>
	[JsonPropertyName("roles")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<WorkplacePlacementRole> Roles { get; init; }

	/// <summary>Non-fatal findings to relay to the user.</summary>
	[JsonPropertyName("warnings")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string> Warnings { get; init; }

	/// <summary>Every workplace that holds the section after the call, read back.</summary>
	[JsonPropertyName("section-workplaces")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<WorkplaceLocation> SectionWorkplaces { get; init; }

	/// <summary>What to tell the user after a success.</summary>
	[JsonPropertyName("next-step")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string NextStep { get; init; }

	/// <summary>
	/// Failure reason. Inputs are resolved before the first write, so an error without <see cref="WritesStarted"/>
	/// changed nothing.
	/// </summary>
	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Error { get; init; }

	/// <summary>Present (true) when the call failed after its first write; <see cref="Written"/> lists those writes.</summary>
	[JsonPropertyName("writes-started")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? WritesStarted { get; init; }

	/// <summary>After a failure past the first write: every row upsert and binding creation started, the failing one included.</summary>
	[JsonPropertyName("written")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<WorkplacePlacementWrite> Written { get; init; }
}
