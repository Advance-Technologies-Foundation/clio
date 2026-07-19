using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Clio.Command.McpServer.Knowledge;

/// <summary>Controls whether a trusted knowledge source participates in logical-topic resolution.</summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum KnowledgeSourceParticipation {
	/// <summary>The source is available only by exact namespaced identity.</summary>
	[EnumMember(Value = "isolated")]
	Isolated,
	/// <summary>The source can fill topics that have no eligible authoritative provider.</summary>
	[EnumMember(Value = "supplement")]
	Supplement,
	/// <summary>The source participates as an authoritative provider selected by pin or priority.</summary>
	[EnumMember(Value = "authoritative")]
	Authoritative
}

internal sealed record KnowledgeLibrarySnapshot(
	string SourceAlias,
	string LibraryId,
	int Priority,
	KnowledgeSourceParticipation Participation,
	ulong Sequence,
	string BundleDigest,
	IReadOnlyList<KnowledgeArticle> Articles);

internal interface IKnowledgeResolver {
	KnowledgeArticleLookup Find(
		string identifier,
		IReadOnlyCollection<KnowledgeLibrarySnapshot> libraries,
		IReadOnlyDictionary<string, string> topicPins);

	IReadOnlyList<string> GetNames(IReadOnlyCollection<KnowledgeLibrarySnapshot> libraries);
}

internal sealed class KnowledgeResolver : IKnowledgeResolver {
	internal const string NamespacedUriPrefix = "docs://knowledge/";

	public KnowledgeArticleLookup Find(
		string identifier,
		IReadOnlyCollection<KnowledgeLibrarySnapshot> libraries,
		IReadOnlyDictionary<string, string> topicPins) {
		ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
		ArgumentNullException.ThrowIfNull(libraries);
		ArgumentNullException.ThrowIfNull(topicPins);

		if (TryParseNamespacedUri(identifier, out string? libraryId, out string? itemId)) {
			return FindExact(libraryId!, itemId!, libraries);
		}
		KnowledgeArticleLookup legacy = FindLegacyUri(identifier, libraries);
		if (legacy.Status != KnowledgeArticleLookupStatus.NotFound) {
			return legacy;
		}

		return FindTopic(identifier, libraries, topicPins);
	}

	private static KnowledgeArticleLookup FindLegacyUri(
		string identifier,
		IReadOnlyCollection<KnowledgeLibrarySnapshot> libraries) {
		(KnowledgeLibrarySnapshot Library, KnowledgeArticle Article)[] matches = libraries
			.SelectMany(library => library.Articles
				.Where(article => article.LegacyUris?.Contains(identifier, StringComparer.Ordinal) == true)
				.Select(article => (library, article)))
			.ToArray();
		return matches.Length switch {
			0 => NotFound(libraries.Count == 0 ? null : libraries.Max(library => library.Sequence)),
			1 => Active(matches[0].Article, matches[0].Library),
			_ => Ambiguous(
				$"Legacy knowledge URI '{identifier}' is ambiguous between libraries: "
				+ string.Join(", ", matches.Select(match => match.Library.LibraryId)
					.Distinct(StringComparer.Ordinal)
					.OrderBy(value => value, StringComparer.Ordinal))
				+ ". Use a namespaced knowledge URI.")
		};
	}

	public IReadOnlyList<string> GetNames(IReadOnlyCollection<KnowledgeLibrarySnapshot> libraries) {
		ArgumentNullException.ThrowIfNull(libraries);
		return libraries
			.Where(library => library.Participation != KnowledgeSourceParticipation.Isolated)
			.SelectMany(library => library.Articles)
			.Where(article => string.Equals(article.Role, KnowledgeArticle.DefaultRole, StringComparison.Ordinal))
			.Select(article => article.TopicId)
			.Distinct(StringComparer.Ordinal)
			.OrderBy(topic => topic, StringComparer.Ordinal)
			.ToArray();
	}

	private static KnowledgeArticleLookup FindExact(
		string libraryId,
		string itemId,
		IReadOnlyCollection<KnowledgeLibrarySnapshot> libraries) {
		KnowledgeLibrarySnapshot? library = libraries.SingleOrDefault(candidate =>
			string.Equals(candidate.LibraryId, libraryId, StringComparison.Ordinal));
		if (library is null) {
			return NotFound();
		}
		KnowledgeArticle? article = library.Articles.SingleOrDefault(candidate =>
			string.Equals(candidate.ItemId, itemId, StringComparison.Ordinal));
		return article is null ? NotFound(library.Sequence) : Active(article, library);
	}

	private static KnowledgeArticleLookup FindTopic(
		string topicId,
		IReadOnlyCollection<KnowledgeLibrarySnapshot> libraries,
		IReadOnlyDictionary<string, string> topicPins) {
		KnowledgeLibrarySnapshot[] candidates = libraries
			.Where(library => library.Participation != KnowledgeSourceParticipation.Isolated)
			.Where(library => library.Articles.Any(article =>
				string.Equals(article.TopicId, topicId, StringComparison.Ordinal)
				&& string.Equals(article.Role, KnowledgeArticle.DefaultRole, StringComparison.Ordinal)))
			.ToArray();
		if (candidates.Length == 0) {
			return NotFound(libraries.Count == 0 ? null : libraries.Max(library => library.Sequence));
		}

		if (topicPins.TryGetValue(topicId, out string? pinnedLibraryId)) {
			KnowledgeLibrarySnapshot? pinned = candidates.SingleOrDefault(candidate =>
				string.Equals(candidate.LibraryId, pinnedLibraryId, StringComparison.Ordinal));
			return pinned is null
				? Ambiguous($"Topic '{topicId}' is pinned to unavailable or ineligible library '{pinnedLibraryId}'.")
				: Active(SelectTopicArticle(pinned, topicId), pinned);
		}

		KnowledgeLibrarySnapshot[] authoritative = candidates
			.Where(candidate => candidate.Participation == KnowledgeSourceParticipation.Authoritative)
			.ToArray();
		KnowledgeLibrarySnapshot[] eligible = authoritative.Length > 0
			? authoritative
			: candidates.Where(candidate => candidate.Participation == KnowledgeSourceParticipation.Supplement).ToArray();
		int highestPriority = eligible.Max(candidate => candidate.Priority);
		KnowledgeLibrarySnapshot[] winners = eligible
			.Where(candidate => candidate.Priority == highestPriority)
			.ToArray();
		if (winners.Length != 1) {
			return Ambiguous(
				$"Topic '{topicId}' is ambiguous between equally prioritized libraries: "
				+ string.Join(", ", winners.Select(winner => winner.LibraryId).OrderBy(value => value, StringComparer.Ordinal))
				+ ". Configure a topic pin or distinct priorities.");
		}
		return Active(SelectTopicArticle(winners[0], topicId), winners[0]);
	}

	private static KnowledgeArticle SelectTopicArticle(KnowledgeLibrarySnapshot library, string topicId) =>
		library.Articles.Single(article =>
			string.Equals(article.TopicId, topicId, StringComparison.Ordinal)
			&& string.Equals(article.Role, KnowledgeArticle.DefaultRole, StringComparison.Ordinal));

	private static KnowledgeArticleLookup Active(KnowledgeArticle article, KnowledgeLibrarySnapshot library) =>
		new(
			KnowledgeArticleLookupStatus.Active,
			article,
			library.Sequence,
			new KnowledgeArticleProvenance(
				library.SourceAlias,
				library.LibraryId,
				article.ItemId,
				article.TopicId,
				library.Sequence,
				library.BundleDigest,
				article.LocalPath),
			null);

	private static KnowledgeArticleLookup NotFound(ulong? sequence = null) =>
		new(KnowledgeArticleLookupStatus.NotFound, null, sequence, null, null);

	private static KnowledgeArticleLookup Ambiguous(string diagnostic) =>
		new(KnowledgeArticleLookupStatus.Ambiguous, null, null, null, diagnostic);

	private static bool TryParseNamespacedUri(string identifier, out string? libraryId, out string? itemId) {
		libraryId = null;
		itemId = null;
		if (!identifier.StartsWith(NamespacedUriPrefix, StringComparison.Ordinal)) {
			return false;
		}
		string remainder = identifier[NamespacedUriPrefix.Length..];
		int separator = remainder.IndexOf('/');
		if (separator <= 0 || separator == remainder.Length - 1 || remainder[(separator + 1)..].Contains('/')) {
			return false;
		}
		libraryId = Uri.UnescapeDataString(remainder[..separator]);
		itemId = Uri.UnescapeDataString(remainder[(separator + 1)..]);
		return !string.IsNullOrWhiteSpace(libraryId) && !string.IsNullOrWhiteSpace(itemId);
	}
}
