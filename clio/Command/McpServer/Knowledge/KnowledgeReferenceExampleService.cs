using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Clio.Command;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Clio.Command.McpServer.Knowledge;

internal interface IKnowledgeReferenceExampleParser {
	bool TryParse(string yaml, out KnowledgeReferenceExampleDocument? document, out string? diagnostic);
}

internal sealed class KnowledgeReferenceExampleParser : IKnowledgeReferenceExampleParser {
	private readonly IDeserializer _deserializer = new DeserializerBuilder()
		.WithNamingConvention(CamelCaseNamingConvention.Instance)
		.Build();

	public bool TryParse(
		string yaml,
		out KnowledgeReferenceExampleDocument? document,
		out string? diagnostic) {
		document = null;
		diagnostic = null;
		try {
			document = _deserializer.Deserialize<KnowledgeReferenceExampleDocument>(yaml);
			return document is not null;
		} catch (YamlException exception) {
			diagnostic = exception.Message;
			return false;
		}
	}
}

internal sealed class KnowledgeReferenceExampleService : IKnowledgeReferenceExampleService {
	internal const string ReferenceExampleRole = "reference-example";
	private const int MaxFilterLength = 200;
	private const int MaxTextLength = 4096;
	private const int MaxRepositoryLength = 2048;
	private const int MaxPathLength = 512;
	private const int MaxCollectionCount = 128;
	private static readonly Regex ImmutableRevisionPattern = new(
		"^(?:[0-9a-fA-F]{40}|[0-9a-fA-F]{64})$",
		RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
	private static readonly Regex StableIdPattern = new(
		"^[a-z0-9]+(?:[.-][a-z0-9]+)*$",
		RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
	private static readonly Regex EntryPointKeyPattern = new(
		"^[a-z][A-Za-z0-9]*$",
		RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
	private readonly IKnowledgeBundleActivator _activator;
	private readonly IKnowledgeBundleRuntime _runtime;
	private readonly IKnowledgeReferenceExampleParser _parser;

	public KnowledgeReferenceExampleService(
		IKnowledgeBundleActivator activator,
		IKnowledgeBundleRuntime runtime,
		IKnowledgeReferenceExampleParser parser) {
		_activator = activator ?? throw new ArgumentNullException(nameof(activator));
		_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
		_parser = parser ?? throw new ArgumentNullException(nameof(parser));
	}

	public KnowledgeReferenceExampleListResult List(KnowledgeReferenceExampleQuery query) {
		ArgumentNullException.ThrowIfNull(query);
		if (!TryNormalizeQuery(query, out KnowledgeReferenceExampleQuery normalized, out string? queryDiagnostic)) {
			return new KnowledgeReferenceExampleListResult(false, [], [queryDiagnostic!]);
		}

		_activator.EnsureActivated();
		IReadOnlyList<KnowledgeRoleArticle> articles = _runtime.GetArticlesByRole(ReferenceExampleRole);
		List<KnowledgeReferenceExample> examples = [];
		List<string> diagnostics = [];
		if (!string.IsNullOrWhiteSpace(_activator.LastDiagnostic)) {
			diagnostics.Add(_activator.LastDiagnostic);
		}
		foreach (KnowledgeRoleArticle item in articles) {
			if (normalized.SourceAlias is not null
					&& !string.Equals(item.Provenance.SourceAlias, normalized.SourceAlias, StringComparison.OrdinalIgnoreCase)) {
				continue;
			}
			if (!_parser.TryParse(item.Article.Text, out KnowledgeReferenceExampleDocument? document, out string? parseDiagnostic)
					|| document is null) {
				diagnostics.Add($"Catalog item '{item.Article.Uri}' is invalid YAML: {Safe(parseDiagnostic)}");
				continue;
			}
			if (!TryMap(item, document, out KnowledgeReferenceExample? example, out string? validationDiagnostic)) {
				diagnostics.Add($"Catalog item '{item.Article.Uri}' is invalid: {validationDiagnostic}");
				continue;
			}
			if (Matches(example!, normalized)) {
				examples.Add(example!);
			}
		}

		KnowledgeReferenceExample[] ordered = examples
			.OrderByDescending(example => example.SourcePriority)
			.ThenBy(example => example.LibraryId, StringComparer.Ordinal)
			.ThenBy(example => example.Id, StringComparer.Ordinal)
			.ToArray();
		return new KnowledgeReferenceExampleListResult(diagnostics.Count == 0, ordered, diagnostics);
	}

	private static bool TryNormalizeQuery(
		KnowledgeReferenceExampleQuery query,
		out KnowledgeReferenceExampleQuery normalized,
		out string? diagnostic) {
		diagnostic = null;
		string? source = TrimToNull(query.SourceAlias);
		string? search = TrimToNull(query.SearchText);
		string? capability = TrimToNull(query.Capability);
		string? status = TrimToNull(query.Status);
		if (new[] { source, search, capability, status }.Any(value => value?.Length > MaxFilterLength)) {
			normalized = query;
			diagnostic = $"Reference-example filters cannot exceed {MaxFilterLength} characters.";
			return false;
		}
		normalized = new KnowledgeReferenceExampleQuery(source, search, capability, status);
		return true;
	}

	private static bool TryMap(
		KnowledgeRoleArticle item,
		KnowledgeReferenceExampleDocument document,
		out KnowledgeReferenceExample? example,
		out string? diagnostic) {
		example = null;
		diagnostic = Validate(document);
		if (diagnostic is not null) {
			return false;
		}
		example = new KnowledgeReferenceExample(
			item.Provenance.SourceAlias,
			item.Provenance.LibraryId,
			item.Priority,
			item.Participation.ToString().ToLowerInvariant(),
			item.Provenance.Sequence,
			item.Provenance.BundleDigest,
			item.Article.ItemId,
			document.SchemaVersion,
			document.Id.Trim(),
			document.Title.Trim(),
			document.Status.Trim(),
			new KnowledgeReferenceExampleUseCase(
				document.PrimaryUseCase!.Id.Trim(),
				document.PrimaryUseCase.Summary.Trim()),
			new KnowledgeReferenceExampleSource(
				document.Source!.Repository.Trim(),
				document.Source.Revision.Trim().ToLowerInvariant(),
				document.Source.DefaultBranch.Trim()),
			document.EntryPoints!.OrderBy(pair => pair.Key, StringComparer.Ordinal)
				.ToDictionary(pair => pair.Key.Trim(), pair => pair.Value.Trim(), StringComparer.Ordinal),
			document.SupportingCapabilities!.Select(value => value.Trim())
				.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
			new KnowledgeReferenceExampleCompatibility(
				document.Compatibility!.Status.Trim(),
				document.Compatibility.Details.Trim()),
			new KnowledgeReferenceExampleTrust(
				document.Trust!.Publisher.Trim(),
				document.Trust.Level.Trim()),
			document.Notes?.Select(value => value.Trim()).ToArray() ?? []);
		return true;
	}

	private static string? Validate(KnowledgeReferenceExampleDocument document) {
		if (document.SchemaVersion != 0) {
			return $"unsupported schemaVersion '{document.SchemaVersion}'";
		}
		if (!Stable(document.Id) || !SafeText(document.Title)
				|| !Stable(document.Status)) {
			return "id, title, and status are required";
		}
		if (document.PrimaryUseCase is null
				|| !Stable(document.PrimaryUseCase.Id)
				|| !SafeText(document.PrimaryUseCase.Summary)) {
			return "primaryUseCase.id and primaryUseCase.summary are required";
		}
		if (document.Source is null
				|| !TryValidateRepository(document.Source.Repository)
				|| !SafeText(document.Source.DefaultBranch)
				|| Blank(document.Source.Revision)
				|| !ImmutableRevisionPattern.IsMatch(document.Source.Revision.Trim())) {
			return "source must contain a credential-free HTTPS repository, default branch, and immutable full commit revision";
		}
		if (document.EntryPoints is null || document.EntryPoints.Count == 0
				|| document.EntryPoints.Count > MaxCollectionCount
				|| document.EntryPoints.Any(pair => !EntryPointKey(pair.Key) || !IsSafeRepositoryPath(pair.Value))
				|| document.EntryPoints.Keys.Select(key => key.Trim()).Distinct(StringComparer.Ordinal).Count()
					!= document.EntryPoints.Count) {
			return "entryPoints must contain named safe repository-relative paths";
		}
		if (document.SupportingCapabilities is null
				|| document.SupportingCapabilities.Count == 0
				|| document.SupportingCapabilities.Count > MaxCollectionCount
				|| document.SupportingCapabilities.Any(value => !Stable(value))
				|| document.SupportingCapabilities.Select(value => value.Trim()).Distinct(StringComparer.Ordinal).Count()
					!= document.SupportingCapabilities.Count) {
			return "supportingCapabilities must contain unique non-empty stable tags";
		}
		if (document.Compatibility is null
				|| !Stable(document.Compatibility.Status)
				|| !SafeText(document.Compatibility.Details)) {
			return "compatibility status and details are required";
		}
		if (document.Trust is null
				|| !SafeText(document.Trust.Publisher)
				|| !Stable(document.Trust.Level)) {
			return "trust publisher and level are required";
		}
		if (document.Notes is { Count: > MaxCollectionCount }
				|| document.Notes?.Any(value => !SafeText(value)) == true) {
			return "notes cannot contain empty values";
		}
		return null;
	}

	private static bool Matches(KnowledgeReferenceExample example, KnowledgeReferenceExampleQuery query) {
		if (query.Capability is not null
				&& !example.SupportingCapabilities.Contains(query.Capability, StringComparer.OrdinalIgnoreCase)) {
			return false;
		}
		if (query.Status is not null
				&& !string.Equals(example.Status, query.Status, StringComparison.OrdinalIgnoreCase)) {
			return false;
		}
		if (query.SearchText is null) {
			return true;
		}
		string search = query.SearchText;
		return Contains(example.Id, search)
			|| Contains(example.Title, search)
			|| Contains(example.PrimaryUseCase.Id, search)
			|| Contains(example.PrimaryUseCase.Summary, search)
			|| Contains(example.SourceAlias, search)
			|| Contains(example.LibraryId, search)
			|| example.SupportingCapabilities.Any(value => Contains(value, search));
	}

	private static bool TryValidateRepository(string? value) =>
		!Blank(value)
		&& value!.Trim().Length <= MaxRepositoryLength
		&& !HasControlCharacters(value)
		&& Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri)
		&& string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
		&& string.IsNullOrEmpty(uri.UserInfo)
		&& string.IsNullOrEmpty(uri.Query)
		&& string.IsNullOrEmpty(uri.Fragment);

	private static bool IsSafeRepositoryPath(string? value) {
		if (Blank(value)) {
			return false;
		}
		string path = value!.Trim();
		if (path.Length > MaxPathLength) {
			return false;
		}
		return !path.StartsWith("/", StringComparison.Ordinal)
			&& !HasControlCharacters(path)
			&& !path.Contains('\\')
			&& !path.Split('/').Any(segment => segment is "" or "." or "..");
	}

	private static bool Contains(string value, string search) =>
		value.Contains(search, StringComparison.OrdinalIgnoreCase);

	private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);

	private static bool TooLong(string? value) => value?.Trim().Length > MaxTextLength;

	private static bool SafeText(string? value) =>
		!Blank(value) && !TooLong(value) && !HasControlCharacters(value!);

	private static bool HasControlCharacters(string value) => value.Any(char.IsControl);

	private static bool Stable(string? value) =>
		!Blank(value) && value!.Trim().Length <= 160 && StableIdPattern.IsMatch(value.Trim());

	private static bool EntryPointKey(string? value) =>
		!Blank(value) && value!.Trim().Length <= 160 && EntryPointKeyPattern.IsMatch(value.Trim());

	private static string? TrimToNull(string? value) => Blank(value) ? null : value!.Trim();

	private static string Safe(string? value) => Blank(value) ? "unknown parsing error" : value!.Trim();
}

internal sealed class KnowledgeReferenceExampleDocument {
	public int SchemaVersion { get; init; }
	public string Id { get; init; } = string.Empty;
	public string Title { get; init; } = string.Empty;
	public string Status { get; init; } = string.Empty;
	public KnowledgeReferenceExampleUseCaseDocument? PrimaryUseCase { get; init; }
	public KnowledgeReferenceExampleSourceDocument? Source { get; init; }
	public Dictionary<string, string>? EntryPoints { get; init; }
	public List<string>? SupportingCapabilities { get; init; }
	public KnowledgeReferenceExampleCompatibilityDocument? Compatibility { get; init; }
	public KnowledgeReferenceExampleTrustDocument? Trust { get; init; }
	public List<string>? Notes { get; init; }
}

internal sealed class KnowledgeReferenceExampleUseCaseDocument {
	public string Id { get; init; } = string.Empty;
	public string Summary { get; init; } = string.Empty;
}

internal sealed class KnowledgeReferenceExampleSourceDocument {
	public string Repository { get; init; } = string.Empty;
	public string Revision { get; init; } = string.Empty;
	public string DefaultBranch { get; init; } = string.Empty;
}

internal sealed class KnowledgeReferenceExampleCompatibilityDocument {
	public string Status { get; init; } = string.Empty;
	public string Details { get; init; } = string.Empty;
}

internal sealed class KnowledgeReferenceExampleTrustDocument {
	public string Publisher { get; init; } = string.Empty;
	public string Level { get; init; } = string.Empty;
}
