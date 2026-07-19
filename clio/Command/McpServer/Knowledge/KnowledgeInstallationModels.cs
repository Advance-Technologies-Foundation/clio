using System;
using System.Text.Json.Serialization;

namespace Clio.Command.McpServer.Knowledge;

internal sealed record KnowledgeVersionPointer(
	string PackageVersion,
	ulong Sequence,
	string RelativePath,
	string BundleDigest,
	DateTimeOffset ActivatedAtUtc);

internal sealed record KnowledgeCurrentState(
	int SchemaVersion,
	KnowledgeVersionPointer Active,
	KnowledgeVersionPointer? Previous);

internal sealed record KnowledgeInstallMetadata(
	int SchemaVersion,
	string PackageId,
	string PackageVersion,
	ulong Sequence,
	string Source,
	string BundleDigest,
	DateTimeOffset InstalledAtUtc);

internal sealed record InstalledKnowledgeCandidate(
	KnowledgeVersionPointer Pointer,
	string BundlePath,
	byte[] BundleBytes);

internal enum KnowledgeInstallationStatus {
	Installed,
	Updated,
	AlreadyInstalled,
	UpToDate,
	Deleted,
	NotInstalled,
	ConfirmationRequired,
	Unavailable,
	Rejected,
	Failed
}

internal sealed record KnowledgeInstallationResult(
	KnowledgeInstallationStatus Status,
	string Message,
	string? PackageVersion = null,
	string? RootPath = null) {
	internal bool IsSuccess => Status is KnowledgeInstallationStatus.Installed
		or KnowledgeInstallationStatus.Updated
		or KnowledgeInstallationStatus.AlreadyInstalled
		or KnowledgeInstallationStatus.UpToDate
		or KnowledgeInstallationStatus.Deleted;
}

internal enum KnowledgeUpdateAvailability {
	Unknown,
	NotInstalled,
	UpToDate,
	Available
}

internal sealed record KnowledgeInstallationInfo(
	string SettingsFilePath,
	string RootPath,
	bool IsInstalled,
	bool IsValid,
	string? ActiveVersion,
	string? PreviousVersion,
	string? ActiveContentPath,
	string? Source,
	string? PackageId,
	DateTimeOffset? InstalledAtUtc,
	KnowledgeUpdateAvailability UpdateAvailability,
	string? LatestVersion,
	string? Diagnostic);

[JsonSourceGenerationOptions(
	PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
	WriteIndented = true,
	UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(KnowledgeCurrentState))]
[JsonSerializable(typeof(KnowledgeInstallMetadata))]
internal sealed partial class KnowledgeInstallationJsonContext : JsonSerializerContext;
