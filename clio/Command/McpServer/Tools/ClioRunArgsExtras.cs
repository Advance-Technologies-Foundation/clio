using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// ENG-90312 Phase 2 — derived <see cref="ClioRunArgs"/> variants that do not have a
/// one-to-one Phase-1 record they can simply be renamed from. Includes split records for
/// commands that previously shared an args record, and wrapper records for tools whose
/// Phase-1 entry point took primitive arguments only.
/// </summary>

// --- Split of DataForgeMaintenanceArgs (shared by dataforge-initialize + dataforge-update) ---

/// <summary>clio-run variant for <c>dataforge-initialize</c>. Same field shape as
/// <see cref="DataForgeMaintenanceArgs"/>; split into a distinct record so the polymorphic
/// dispatcher can route by discriminator.</summary>
public sealed record DataForgeInitializeRunArgs(
	[property: JsonPropertyName("environment-name"), Description("Registered clio environment name."), Required]
	string EnvironmentName
) : ClioRunArgs;

/// <summary>clio-run variant for <c>dataforge-update</c>. Same field shape as
/// <see cref="DataForgeMaintenanceArgs"/>; split into a distinct record so the polymorphic
/// dispatcher can route by discriminator.</summary>
public sealed record DataForgeUpdateRunArgs(
	[property: JsonPropertyName("environment-name"), Description("Registered clio environment name."), Required]
	string EnvironmentName
) : ClioRunArgs;

// --- Split of PackageHotfixArgs (shared by finish-hotfix + unlock-for-hotfix) ---

/// <summary>clio-run variant for <c>finish-hotfix</c>. Same field shape as
/// <see cref="PackageHotfixArgs"/>; split into a distinct record for polymorphic dispatch.</summary>
public sealed record FinishHotfixRunArgs(
	[property: JsonPropertyName("package-name"), Description("Package name"), Required]
	string PackageName,
	[property: JsonPropertyName("environment-name"), Description("Target Creatio environment name"), Required]
	string EnvironmentName
) : ClioRunArgs;

/// <summary>clio-run variant for <c>unlock-for-hotfix</c>. Same field shape as
/// <see cref="PackageHotfixArgs"/>; split into a distinct record for polymorphic dispatch.</summary>
public sealed record UnlockForHotfixRunArgs(
	[property: JsonPropertyName("package-name"), Description("Package name"), Required]
	string PackageName,
	[property: JsonPropertyName("environment-name"), Description("Target Creatio environment name"), Required]
	string EnvironmentName
) : ClioRunArgs;

// --- Wrapper records for Phase-1 primitive-arg tools ---

/// <summary>clio-run variant for <c>start-creatio</c>. Wraps the previously-bare
/// <c>environmentName</c> string parameter.</summary>
public sealed record StartCreatioRunArgs(
	[property: JsonPropertyName("environment-name"), Description("Target Creatio environment name."), Required]
	string EnvironmentName
) : ClioRunArgs;

/// <summary>clio-run variant for <c>stop-creatio</c>. Wraps the previously-bare
/// <c>environmentName</c> string parameter.</summary>
public sealed record StopCreatioRunArgs(
	[property: JsonPropertyName("environment-name"), Description("Target Creatio environment name."), Required]
	string EnvironmentName
) : ClioRunArgs;

/// <summary>clio-run variant for <c>stop-all-creatio</c>. No arguments — but
/// <see cref="ClioRunArgs"/> derived records must be concrete classes for the polymorphic
/// dispatcher to instantiate, so we keep an empty record here.</summary>
public sealed record StopAllCreatioRunArgs : ClioRunArgs;
