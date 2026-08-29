using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Clio.Command.McpServer.Knowledge;

/// <summary>
/// Identifies the transport used to retrieve a configured knowledge source.
/// </summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum KnowledgeSourceType {
	/// <summary>
	/// Retrieves a signed bundle from a NuGet v3 feed.
	/// </summary>
	[EnumMember(Value = "nuget")]
	NuGet,

	/// <summary>
	/// Synchronizes and reads declarative knowledge directly from a managed Git repository checkout.
	/// </summary>
	[EnumMember(Value = "git")]
	Git,

	/// <summary>
	/// Retrieves a signed bundle published as an immutable GitHub Release asset.
	/// </summary>
	/// <remarks>
	/// This transport never touches a Git checkout and never requires the Git CLI: it reads the
	/// repository's latest stable release through the GitHub REST API and downloads exactly one
	/// declared asset.
	/// </remarks>
	[EnumMember(Value = "github-release")]
	GitHubRelease
}

/// <summary>
/// Maps <see cref="KnowledgeSourceType"/> onto the stable token used on the wire, in settings, and in
/// operator-visible output.
/// </summary>
/// <remarks>
/// <c>Enum.ToString()</c> is deliberately not used: it produces <c>githubrelease</c>, which is neither
/// the persisted token nor the value <c>add-knowledge-source --type</c> accepts.
/// </remarks>
internal static class KnowledgeSourceTypeNames {

	/// <summary>The token for a signed bundle published as a GitHub Release asset.</summary>
	internal const string GitHubRelease = "github-release";

	/// <summary>The token for a managed Git repository checkout.</summary>
	internal const string Git = "git";

	/// <summary>The token for a signed bundle retrieved from a NuGet v3 feed.</summary>
	internal const string NuGet = "nuget";

	/// <summary>
	/// Returns the stable token for <paramref name="type"/>.
	/// </summary>
	/// <param name="type">The transport type.</param>
	/// <returns>The token used in settings and in command output.</returns>
	internal static string Format(KnowledgeSourceType type) => type switch {
		KnowledgeSourceType.Git => Git,
		KnowledgeSourceType.NuGet => NuGet,
		KnowledgeSourceType.GitHubRelease => GitHubRelease,
		_ => throw new ArgumentOutOfRangeException(nameof(type), type, "Knowledge source type is not supported.")
	};
}

/// <summary>
/// Stores the complete multi-source knowledge configuration persisted in appsettings.json.
/// </summary>
public sealed class KnowledgeConfiguration {
	/// <summary>
	/// Gets or sets the absolute root directory containing installed knowledge generations.
	/// </summary>
	[JsonProperty("root-path")]
	public string? RootPath { get; set; }

	/// <summary>
	/// Gets or sets sources keyed by their operator-friendly alias.
	/// </summary>
	[JsonProperty("sources")]
	public Dictionary<string, KnowledgeSourceConfiguration> Sources { get; set; } =
		new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Gets or sets logical-topic pins keyed by topic ID and containing stable library IDs.
	/// </summary>
	[JsonProperty("topic-pins")]
	public Dictionary<string, string> TopicPins { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Describes one trusted knowledge source independently of its installed state.
/// </summary>
public sealed class KnowledgeSourceConfiguration {
	/// <summary>
	/// Gets or sets the stable reverse-DNS identity published by the library.
	/// </summary>
	[JsonProperty("library-id")]
	public string LibraryId { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the source transport type.
	/// </summary>
	[JsonProperty("type")]
	public KnowledgeSourceType Type { get; set; }

	/// <summary>
	/// Gets or sets the credential-free Git repository or NuGet service-index URI.
	/// </summary>
	[JsonProperty("location")]
	public string Location { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the signature key identifier authorized for bundles from a NuGet source.
	/// </summary>
	/// <remarks>This value is required for NuGet sources and is not used for Git sources.</remarks>
	[JsonProperty("trusted-key-id")]
	public string? TrustedKeyId { get; set; }

	/// <summary>
	/// Gets or sets the absolute local path to trusted public-key material for a NuGet source.
	/// </summary>
	/// <remarks>
	/// This value is required for NuGet sources and is not used for Git sources. The referenced file
	/// contains public verification material and must never contain a private key.
	/// </remarks>
	[JsonProperty("trusted-public-key-path")]
	public string? TrustedPublicKeyPath { get; set; }

	/// <summary>
	/// Gets or sets the NuGet package ID when <see cref="Type"/> is <see cref="KnowledgeSourceType.NuGet"/>.
	/// </summary>
	[JsonProperty("package-id")]
	public string? PackageId { get; set; }

	/// <summary>
	/// Gets or sets the GitHub repository owner when <see cref="Type"/> is
	/// <see cref="KnowledgeSourceType.GitHubRelease"/>.
	/// </summary>
	[JsonProperty("repository-owner")]
	public string? RepositoryOwner { get; set; }

	/// <summary>
	/// Gets or sets the GitHub repository name when <see cref="Type"/> is
	/// <see cref="KnowledgeSourceType.GitHubRelease"/>.
	/// </summary>
	[JsonProperty("repository-name")]
	public string? RepositoryName { get; set; }

	/// <summary>
	/// Gets or sets the exact release asset file name to retrieve.
	/// </summary>
	/// <remarks>
	/// The name is matched exactly and the release must expose it exactly once. A release that
	/// carries no match, or more than one, is refused rather than guessed at.
	/// </remarks>
	[JsonProperty("asset-name")]
	public string? AssetName { get; set; }

	/// <summary>
	/// Gets or sets the Git branch to follow.
	/// </summary>
	[JsonProperty("branch")]
	public string? Branch { get; set; }

	/// <summary>
	/// Gets or sets the Git tag to resolve.
	/// </summary>
	[JsonProperty("tag")]
	public string? Tag { get; set; }

	/// <summary>
	/// Gets or sets the immutable Git commit to retrieve. A commit takes precedence over tag and branch.
	/// </summary>
	[JsonProperty("commit")]
	public string? Commit { get; set; }

	/// <summary>
	/// Gets or sets whether the source participates in lifecycle and resolution operations.
	/// </summary>
	[JsonProperty("enabled")]
	public bool Enabled { get; set; } = true;

	/// <summary>
	/// Gets or sets the deterministic resolution priority. Higher values win among eligible sources.
	/// </summary>
	[JsonProperty("priority")]
	public int Priority { get; set; }

	/// <summary>
	/// Gets or sets how the source participates in logical-topic resolution.
	/// </summary>
	[JsonProperty("participation")]
	public KnowledgeSourceParticipation Participation { get; set; } = KnowledgeSourceParticipation.Supplement;
}

internal static partial class KnowledgeSourceConfigurationValidator {

	private static readonly Regex AliasPattern = AliasRegex();
	private static readonly Regex LibraryIdPattern = LibraryIdRegex();
	private static readonly Regex PackageIdPattern = PackageIdRegex();
	private static readonly Regex RepositoryOwnerPattern = RepositoryOwnerRegex();
	private static readonly Regex RepositoryNamePattern = RepositoryNameRegex();
	private static readonly Regex AssetNamePattern = AssetNameRegex();
	private static readonly Regex GitCommitPattern = GitCommitRegex();
	private static readonly Regex TopicIdPattern = TopicIdRegex();

	internal static KnowledgeConfiguration ValidateAndClone(KnowledgeConfiguration configuration) {
		ArgumentNullException.ThrowIfNull(configuration);
		KnowledgeConfiguration result = new() {
			RootPath = NormalizeOptionalRoot(configuration.RootPath),
			Sources = new Dictionary<string, KnowledgeSourceConfiguration>(StringComparer.OrdinalIgnoreCase),
			TopicPins = new Dictionary<string, string>(StringComparer.Ordinal)
		};
		foreach ((string alias, KnowledgeSourceConfiguration source) in configuration.Sources
				?? new Dictionary<string, KnowledgeSourceConfiguration>()) {
			ValidateAlias(alias);
			KnowledgeSourceConfiguration clone = ValidateAndClone(source);
			if (result.Sources.Values.Any(existing => string.Equals(
				existing.LibraryId,
				clone.LibraryId,
				StringComparison.OrdinalIgnoreCase))) {
				throw new ArgumentException($"Knowledge library ID '{clone.LibraryId}' is already configured.",
					nameof(configuration));
			}
			result.Sources.Add(alias, clone);
		}
		foreach ((string topicId, string libraryId) in configuration.TopicPins
				?? new Dictionary<string, string>()) {
			if (string.IsNullOrWhiteSpace(topicId) || !TopicIdPattern.IsMatch(topicId)) {
				throw new ArgumentException($"Knowledge topic pin '{topicId}' is invalid.", nameof(configuration));
			}
			ValidateLibraryId(libraryId);
			result.TopicPins.Add(topicId, libraryId);
		}
		return result;
	}

	internal static KnowledgeSourceConfiguration ValidateAndClone(KnowledgeSourceConfiguration source) {
		ArgumentNullException.ThrowIfNull(source);
		ValidateLibraryId(source.LibraryId);
		Uri location = ValidateRemoteUri(source.Location);
		if (!Enum.IsDefined(source.Participation)) {
			throw new ArgumentOutOfRangeException(nameof(source), source.Participation,
				"Knowledge source participation is not supported.");
		}
		KnowledgeSourceConfiguration result = new() {
			LibraryId = source.LibraryId.Trim(),
			Type = source.Type,
			Location = location.AbsoluteUri,
			Enabled = source.Enabled,
			Priority = source.Priority,
			Participation = source.Participation
		};
		if (source.Type == KnowledgeSourceType.NuGet) {
			if (source.Branch is not null || source.Tag is not null || source.Commit is not null) {
				throw new ArgumentException("Git references are not valid for a NuGet knowledge source.", nameof(source));
			}
			RejectGitHubReleaseSettings(source, "NuGet");
			result.TrustedKeyId = NormalizeTrustedKeyId(source.TrustedKeyId);
			result.TrustedPublicKeyPath = NormalizeTrustedPublicKeyPath(source.TrustedPublicKeyPath);
			if (string.IsNullOrWhiteSpace(source.PackageId) || !PackageIdPattern.IsMatch(source.PackageId)) {
				throw new ArgumentException("A valid package-id is required for a NuGet knowledge source.",
					nameof(source));
			}
			result.PackageId = source.PackageId.Trim();
			return result;
		}
		if (source.Type == KnowledgeSourceType.GitHubRelease) {
			return CompleteGitHubReleaseSource(source, result);
		}
		if (source.Type != KnowledgeSourceType.Git) {
			throw new ArgumentOutOfRangeException(nameof(source), source.Type,
				"Knowledge source type is not supported.");
		}
		RejectGitHubReleaseSettings(source, "Git");
		if (source.PackageId is not null
				|| source.TrustedKeyId is not null
				|| source.TrustedPublicKeyPath is not null) {
			throw new ArgumentException("NuGet package and signing settings are not valid for a Git knowledge source.",
				nameof(source));
		}
		result.Branch = NormalizeGitReference(source.Branch, "branch");
		result.Tag = NormalizeGitReference(source.Tag, "tag");
		result.Commit = NormalizeCommit(source.Commit);
		return result;
	}

	/// <summary>
	/// Completes validation for a <see cref="KnowledgeSourceType.GitHubRelease"/> entry.
	/// </summary>
	/// <remarks>
	/// The transport addresses a structured repository identity rather than an arbitrary URL, so the
	/// location carries only the API origin and the owner, repository, and asset name are validated
	/// separately. Publisher trust is optional here — unlike NuGet — because Clio pins the built-in
	/// library's key in the binary; a source that carries no key material can only be verified by that
	/// pinned trust.
	/// </remarks>
	/// <param name="source">The caller-supplied entry.</param>
	/// <param name="result">The partially populated clone to complete.</param>
	/// <returns>The completed clone.</returns>
	private static KnowledgeSourceConfiguration CompleteGitHubReleaseSource(
		KnowledgeSourceConfiguration source,
		KnowledgeSourceConfiguration result) {
		if (source.Branch is not null || source.Tag is not null || source.Commit is not null) {
			throw new ArgumentException("Git references are not valid for a GitHub release knowledge source.",
				nameof(source));
		}
		if (source.PackageId is not null) {
			throw new ArgumentException("A NuGet package ID is not valid for a GitHub release knowledge source.",
				nameof(source));
		}
		if (string.IsNullOrWhiteSpace(source.RepositoryOwner)
				|| !RepositoryOwnerPattern.IsMatch(source.RepositoryOwner)) {
			throw new ArgumentException(
				"A valid repository-owner is required for a GitHub release knowledge source.", nameof(source));
		}
		// The owner, repository, and asset name are interpolated into a REST path and matched against a
		// release asset name, so a relative segment must never survive validation.
		if (string.IsNullOrWhiteSpace(source.RepositoryName)
				|| !RepositoryNamePattern.IsMatch(source.RepositoryName)
				|| source.RepositoryName.Contains("..", StringComparison.Ordinal)) {
			throw new ArgumentException(
				"A valid repository-name is required for a GitHub release knowledge source.", nameof(source));
		}
		if (string.IsNullOrWhiteSpace(source.AssetName)
				|| !AssetNamePattern.IsMatch(source.AssetName)
				|| source.AssetName.Contains("..", StringComparison.Ordinal)
				|| !source.AssetName.EndsWith(".zip", StringComparison.Ordinal)) {
			throw new ArgumentException(
				"A GitHub release knowledge source must declare an exact '.zip' asset-name.", nameof(source));
		}
		result.RepositoryOwner = source.RepositoryOwner.Trim();
		result.RepositoryName = source.RepositoryName.Trim();
		result.AssetName = source.AssetName.Trim();
		if (source.TrustedKeyId is null && source.TrustedPublicKeyPath is null) {
			return result;
		}
		result.TrustedKeyId = NormalizeTrustedKeyId(source.TrustedKeyId);
		result.TrustedPublicKeyPath = NormalizeTrustedPublicKeyPath(source.TrustedPublicKeyPath);
		return result;
	}

	private static void RejectGitHubReleaseSettings(KnowledgeSourceConfiguration source, string transportLabel) {
		if (source.RepositoryOwner is not null
				|| source.RepositoryName is not null
				|| source.AssetName is not null) {
			throw new ArgumentException(
				$"GitHub release settings are not valid for a {transportLabel} knowledge source.", nameof(source));
		}
	}

	internal static void ValidateAlias(string alias) {
		if (string.IsNullOrWhiteSpace(alias) || !AliasPattern.IsMatch(alias)) {
			throw new ArgumentException("Knowledge source alias must contain only lowercase letters, digits, dots, and hyphens.",
				nameof(alias));
		}
	}

	private static void ValidateLibraryId(string libraryId) {
		if (string.IsNullOrWhiteSpace(libraryId) || libraryId.Length > 255 || !LibraryIdPattern.IsMatch(libraryId)) {
			throw new ArgumentException("Knowledge library ID must be a lowercase reverse-DNS identifier.",
				nameof(libraryId));
		}
	}

	// Each rejection names the ONE thing that is wrong. Collapsing these into a single sentence made a
	// location with a stray query string report a scheme problem, which sends the reader to fix the half
	// that was already right.
	private static Uri ValidateRemoteUri(string location) {
		if (!Uri.TryCreate(location, UriKind.Absolute, out Uri? uri)) {
			throw new ArgumentException(
				$"Knowledge source location '{location}' is not an absolute URI.", nameof(location));
		}
		if (uri.Scheme != Uri.UriSchemeHttps && (uri.Scheme != Uri.UriSchemeHttp || !uri.IsLoopback)) {
			throw new ArgumentException(
				$"Knowledge source location must use HTTPS, or HTTP when the host is loopback; '{uri.Scheme}' "
				+ $"to host '{uri.Host}' is neither.",
				nameof(location));
		}
		if (!string.IsNullOrEmpty(uri.UserInfo)) {
			throw new ArgumentException(
				"Knowledge source location must not carry credentials in the URI (the 'user:password@' part). "
				+ "Remove them; this transport is credential-free.",
				nameof(location));
		}
		if (!string.IsNullOrEmpty(uri.Query)) {
			throw new ArgumentException(
				$"Knowledge source location must not carry a query string, but '{location}' has '{uri.Query}'.",
				nameof(location));
		}
		if (!string.IsNullOrEmpty(uri.Fragment)) {
			throw new ArgumentException(
				$"Knowledge source location must not carry a fragment, but '{location}' has '{uri.Fragment}'.",
				nameof(location));
		}
		return uri;
	}

	private static string? NormalizeOptionalRoot(string? rootPath) {
		if (string.IsNullOrWhiteSpace(rootPath)) {
			return null;
		}
		if (!System.IO.Path.IsPathFullyQualified(rootPath)) {
			throw new ArgumentException("Knowledge root path must be absolute.", nameof(rootPath));
		}
		return System.IO.Path.GetFullPath(rootPath);
	}

	private static string NormalizeTrustedKeyId(string? value) {
		if (string.IsNullOrWhiteSpace(value)) {
			throw new ArgumentException("A trusted-key-id is required for every NuGet knowledge source.", nameof(value));
		}
		string keyId = value.Trim();
		if (keyId.Length > 255 || keyId.Any(char.IsControl)) {
			throw new ArgumentException("Knowledge trusted-key-id must be a printable value of at most 255 characters.",
				nameof(value));
		}
		return keyId;
	}

	private static string NormalizeTrustedPublicKeyPath(string? value) {
		if (!EnvironmentKnowledgeBundleTrustStore.TryNormalizeLocalPublicKeyPath(
				value,
				requireExisting: false,
				out string normalizedPath)) {
			throw new ArgumentException("Knowledge trusted-public-key-path must be an absolute local file path.",
				nameof(value));
		}
		return normalizedPath;
	}

	private static string? NormalizeGitReference(string? value, string kind) {
		if (string.IsNullOrWhiteSpace(value)) {
			return null;
		}
		string reference = value.Trim();
		if (reference.Length > 200
				|| reference.StartsWith("-", StringComparison.Ordinal)
				|| reference.Contains("..", StringComparison.Ordinal)
				|| reference.Contains("@{", StringComparison.Ordinal)
				|| reference.EndsWith(".", StringComparison.Ordinal)
				|| reference.EndsWith("/", StringComparison.Ordinal)
				|| reference.Any(character => char.IsControl(character)
					|| char.IsWhiteSpace(character)
					|| character is '~' or '^' or ':' or '?' or '*' or '[' or '\\' or '"')) {
			throw new ArgumentException($"Knowledge Git {kind} is not a safe reference name.", kind);
		}
		return reference;
	}

	private static string? NormalizeCommit(string? value) {
		if (string.IsNullOrWhiteSpace(value)) {
			return null;
		}
		string commit = value.Trim().ToLowerInvariant();
		if (!GitCommitPattern.IsMatch(commit)) {
			throw new ArgumentException("Knowledge Git commit must be a complete SHA-1 or SHA-256 object ID.",
				nameof(value));
		}
		return commit;
	}

	[GeneratedRegex("^[a-z0-9](?:[a-z0-9.-]{0,62}[a-z0-9])?$", RegexOptions.CultureInvariant)]
	private static partial Regex AliasRegex();

	[GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?(?:\\.[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?)+$",
		RegexOptions.CultureInvariant)]
	private static partial Regex LibraryIdRegex();

	[GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$", RegexOptions.CultureInvariant)]
	private static partial Regex PackageIdRegex();

	[GeneratedRegex("^(?:[0-9a-fA-F]{40}|[0-9a-fA-F]{64})$", RegexOptions.CultureInvariant)]
	private static partial Regex GitCommitRegex();

	[GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?$", RegexOptions.CultureInvariant)]
	private static partial Regex RepositoryOwnerRegex();

	[GeneratedRegex("^[A-Za-z0-9_](?:[A-Za-z0-9._-]{0,98}[A-Za-z0-9_-])?$", RegexOptions.CultureInvariant)]
	private static partial Regex RepositoryNameRegex();

	[GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
	private static partial Regex AssetNameRegex();

	[GeneratedRegex("^[a-z0-9](?:[a-z0-9._-]{0,126}[a-z0-9])?$", RegexOptions.CultureInvariant)]
	private static partial Regex TopicIdRegex();
}
