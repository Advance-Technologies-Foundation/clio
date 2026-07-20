using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using System.Threading;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Exposes the non-resident knowledge lifecycle and trusted-source commands through
/// <c>clio-run</c> without putting the complete management surface into the resident catalog.
/// </summary>
[McpServerToolType]
internal sealed class KnowledgeManagementTools {
	internal const string InstallKnowledgeToolName = "install-knowledge";
	internal const string UpdateKnowledgeToolName = "update-knowledge";
	internal const string InfoKnowledgeToolName = "info-knowledge";
	internal const string DeleteKnowledgeToolName = "delete-knowledge";
	internal const string AddKnowledgeSourceToolName = "add-knowledge-source";
	internal const string RemoveKnowledgeSourceToolName = "remove-knowledge-source";
	internal const string EnableKnowledgeSourceToolName = "enable-knowledge-source";
	internal const string DisableKnowledgeSourceToolName = "disable-knowledge-source";
	internal const string ListKnowledgeSourcesToolName = "list-knowledge-sources";
	internal const string ListKnowledgeExamplesToolName = "list-knowledge-examples";

	private readonly IKnowledgeSourceManagementService _service;
	private readonly IKnowledgeReferenceExampleService _referenceExamples;

	public KnowledgeManagementTools(
		IKnowledgeSourceManagementService service,
		IKnowledgeReferenceExampleService referenceExamples) {
		_service = service ?? throw new ArgumentNullException(nameof(service));
		_referenceExamples = referenceExamples ?? throw new ArgumentNullException(nameof(referenceExamples));
	}

	[McpServerTool(Name = InstallKnowledgeToolName, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = true)]
	[Description("Installs verified local knowledge for one configured source, or all enabled sources when source is omitted.")]
	public KnowledgeSourceBatchResult Install(
		KnowledgeSourceSelectorArgs args,
		CancellationToken cancellationToken = default) => _service.Install(args.Source, cancellationToken);

	[McpServerTool(Name = UpdateKnowledgeToolName, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = true)]
	[Description("Updates verified local knowledge for one configured source, or all enabled sources when source is omitted.")]
	public KnowledgeSourceBatchResult Update(
		KnowledgeSourceSelectorArgs args,
		CancellationToken cancellationToken = default) => _service.Update(args.Source, cancellationToken);

	[McpServerTool(Name = InfoKnowledgeToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
	[Description("Reports local trusted-source and installed-generation status; set checkUpdates=true to contact configured transports.")]
	public KnowledgeSourceInfoResult Info(
		KnowledgeInfoArgs args,
		CancellationToken cancellationToken = default) =>
		_service.GetInfo(args.Source, args.CheckUpdates, cancellationToken);

	[McpServerTool(Name = DeleteKnowledgeToolName, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
	[Description("Deletes Clio-managed installed knowledge for one source or all enabled sources, retaining source configuration.")]
	public KnowledgeSourceBatchResult Delete(
		KnowledgeConfirmedSourceArgs args,
		CancellationToken cancellationToken = default) =>
		_service.Delete(args.Source, args.Confirmed, cancellationToken);

	[McpServerTool(Name = AddKnowledgeSourceToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true)]
	[Description("Adds one trusted Git repository or signed NuGet publisher after confirmation.")]
	public KnowledgeSourceCommandResult Add(KnowledgeSourceAddArgs args) => !args.Confirmed
		? new KnowledgeSourceCommandResult(
			false,
			"Adding a trusted knowledge source requires explicit confirmation.",
			args.Alias)
		: _service.Add(new KnowledgeSourceAddRequest(
		args.Alias,
		args.LibraryId,
		args.Type,
		args.Location,
		args.TrustedKeyId,
		args.TrustedPublicKeyPath,
		args.PackageId,
		args.Branch,
		args.Tag,
		args.Commit,
		args.Enabled,
		args.Priority,
		args.Participation));

	[McpServerTool(Name = RemoveKnowledgeSourceToolName, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
	[Description("Removes one trusted source and its Clio-managed cache after explicit confirmation. The built-in com.creatio.clio source cannot be removed; disable it instead.")]
	public KnowledgeSourceCommandResult Remove(KnowledgeConfirmedAliasArgs args) =>
		_service.Remove(args.Alias, args.Confirmed);

	[McpServerTool(Name = EnableKnowledgeSourceToolName, ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Enables a configured knowledge source without deleting or reinstalling its cache.")]
	public KnowledgeSourceCommandResult Enable(KnowledgeAliasArgs args) => _service.Enable(args.Alias);

	[McpServerTool(Name = DisableKnowledgeSourceToolName, ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Immediately stops serving a configured knowledge source while retaining its cache.")]
	public KnowledgeSourceCommandResult Disable(KnowledgeAliasArgs args) => _service.Disable(args.Alias);

	[McpServerTool(Name = ListKnowledgeSourcesToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Lists all configured knowledge sources, including disabled sources.")]
	public KnowledgeSourceListResult List() => _service.List();

	[McpServerTool(Name = ListKnowledgeExamplesToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Lists reference examples registered in active local knowledge catalogs, including immutable repository coordinates, without cloning repositories or contacting remote services.")]
	public KnowledgeReferenceExampleListResult ListExamples(KnowledgeReferenceExampleListArgs args) =>
		_referenceExamples.List(new KnowledgeReferenceExampleQuery(
			args.Source,
			args.Search,
			args.Capability,
			args.Status));
}

/// <summary>Filters reference examples registered in active local knowledge catalogs.</summary>
public sealed record KnowledgeReferenceExampleListArgs(
	[property: JsonPropertyName("source")]
	[property: Description("Configured source alias; omit to inspect every active source.")]
	string? Source = null,
	[property: JsonPropertyName("search")]
	[property: Description("Case-insensitive text matched against IDs, titles, use cases, sources, and capabilities.")]
	string? Search = null,
	[property: JsonPropertyName("capability")]
	[property: Description("Exact supporting-capability tag; omit to include every capability.")]
	string? Capability = null,
	[property: JsonPropertyName("status")]
	[property: Description("Exact catalog publication status; omit to include every status.")]
	string? Status = null);

/// <summary>Identifies an optional trusted source for a knowledge lifecycle operation.</summary>
public sealed record KnowledgeSourceSelectorArgs(
	[property: JsonPropertyName("source")]
	[property: Description("Configured source alias; omit to target all enabled sources.")]
	string? Source = null);

/// <summary>Requests local knowledge status with optional explicit remote update checks.</summary>
public sealed record KnowledgeInfoArgs(
	[property: JsonPropertyName("source")]
	[property: Description("Configured source alias; omit to inspect all configured sources.")]
	string? Source = null,
	[property: JsonPropertyName("checkUpdates")]
	[property: Description("When true, contacts configured transports to check update availability; defaults to local-only.")]
	bool CheckUpdates = false);

/// <summary>Confirms deletion of installed knowledge for an optional source selection.</summary>
public sealed record KnowledgeConfirmedSourceArgs(
	[property: JsonPropertyName("source")]
	[property: Description("Configured source alias; omit to target all enabled sources.")]
	string? Source,
	[property: JsonPropertyName("confirmed")]
	[property: Description("Must be true after the user approves deletion.")]
	[property: Required]
	bool Confirmed);

/// <summary>Identifies one required configured source alias.</summary>
public sealed record KnowledgeAliasArgs(
	[property: JsonPropertyName("alias")]
	[property: Description("Configured source alias.")]
	[property: Required]
	string Alias);

/// <summary>Identifies and confirms removal of one configured source.</summary>
public sealed record KnowledgeConfirmedAliasArgs(
	[property: JsonPropertyName("alias")]
	[property: Description("Configured source alias.")]
	[property: Required]
	string Alias,
	[property: JsonPropertyName("confirmed")]
	[property: Description("Must be true after the user approves source and cache removal.")]
	[property: Required]
	bool Confirmed);

/// <summary>Describes one trusted Git or NuGet source to persist in Clio settings.</summary>
public sealed record KnowledgeSourceAddArgs(
	[property: JsonPropertyName("alias")]
	[property: Description("Unique lowercase operator-friendly alias.")]
	[property: Required]
	string Alias,
	[property: JsonPropertyName("libraryId")]
	[property: Description("Stable lowercase reverse-DNS library ID.")]
	[property: Required]
	string LibraryId,
	[property: JsonPropertyName("type")]
	[property: Description("Transport type: git or nuget.")]
	[property: Required]
	string Type,
	[property: JsonPropertyName("location")]
	[property: Description("Credential-free Git repository or NuGet v3 service-index URI.")]
	[property: Required]
	string Location,
	[property: JsonPropertyName("trustedKeyId")]
	[property: Description("NuGet bundle signing-key ID; omit for Git sources.")]
	string? TrustedKeyId = null,
	[property: JsonPropertyName("trustedPublicKeyPath")]
	[property: Description("Absolute NuGet public verification-key path; omit for Git sources.")]
	string? TrustedPublicKeyPath = null,
	[property: JsonPropertyName("packageId")]
	[property: Description("Required NuGet package ID for a nuget source.")]
	string? PackageId = null,
	[property: JsonPropertyName("branch")]
	[property: Description("Git branch to follow; omit all refs to persist the discovered default branch.")]
	string? Branch = null,
	[property: JsonPropertyName("tag")]
	[property: Description("Git tag to resolve to an immutable commit.")]
	string? Tag = null,
	[property: JsonPropertyName("commit")]
	[property: Description("Immutable complete Git commit ID; takes precedence over tag and branch.")]
	string? Commit = null,
	[property: JsonPropertyName("enabled")]
	[property: Description("Whether the source participates immediately.")]
	bool Enabled = true,
	[property: JsonPropertyName("priority")]
	[property: Description("Resolution priority; higher eligible values win.")]
	int Priority = 0,
	[property: JsonPropertyName("participation")]
	[property: Description("Resolution participation: isolated, supplement, or authoritative.")]
	string Participation = "supplement",
	[property: JsonPropertyName("confirmed")]
	[property: Description("Must be true after the user approves adding this trusted source.")]
	[property: Required]
	bool Confirmed = false);
