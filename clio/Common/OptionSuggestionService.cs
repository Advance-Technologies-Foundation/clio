using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CommandLine;

namespace Clio.Common;

/// <summary>
/// Finds the known name closest to a name the user mistyped: a command-line option of a verb, or a field of a
/// structured JSON payload.
/// </summary>
public interface IOptionSuggestionService {

	/// <summary>
	/// Returns the long name of the visible option of <paramref name="optionsType"/> that is closest to
	/// <paramref name="unknownToken"/>, or <see langword="null"/> when no option is close enough to be a useful
	/// suggestion.
	/// </summary>
	/// <param name="optionsType">Options class of the verb the user invoked.</param>
	/// <param name="unknownToken">The option name the parser rejected, without leading dashes.</param>
	/// <returns>The suggested long option name without leading dashes, or <see langword="null"/>.</returns>
	/// <remarks>
	/// Hidden options (backward-compatibility aliases) are never suggested. When two options match equally well,
	/// an option declared on the verb's own options class wins over one inherited from a shared base class, so a
	/// mistyped verb-specific option does not resolve to a generic connection option.
	/// </remarks>
	string SuggestOption(Type optionsType, string unknownToken);

	/// <summary>
	/// Returns the entry of <paramref name="knownNames"/> that is closest to <paramref name="requestedName"/>, or
	/// <see langword="null"/> when no entry is close enough to be a useful suggestion.
	/// </summary>
	/// <param name="requestedName">The name the user supplied.</param>
	/// <param name="knownNames">The names that are accepted.</param>
	/// <returns>The closest known name, or <see langword="null"/>.</returns>
	string SuggestName(string requestedName, IEnumerable<string> knownNames);
}

/// <inheritdoc />
public sealed class OptionSuggestionService : IOptionSuggestionService {

	private const int OwnOptionTier = 0;
	private const int InheritedOptionTier = 1;

	/// <inheritdoc />
	public string SuggestOption(Type optionsType, string unknownToken) {
		if (optionsType is null || string.IsNullOrWhiteSpace(unknownToken)) {
			return null;
		}
		IEnumerable<(string Name, int Tier)> candidates = optionsType
			.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Select(property => (Property: property, Option: property.GetCustomAttribute<OptionAttribute>()))
			.Where(pair => pair.Option is { Hidden: false } && !string.IsNullOrWhiteSpace(pair.Option.LongName))
			.Select(pair => (pair.Option.LongName,
				pair.Property.DeclaringType == optionsType ? OwnOptionTier : InheritedOptionTier));
		return FindClosest(unknownToken, candidates);
	}

	/// <inheritdoc />
	public string SuggestName(string requestedName, IEnumerable<string> knownNames) {
		if (string.IsNullOrWhiteSpace(requestedName) || knownNames is null) {
			return null;
		}
		return FindClosest(requestedName, knownNames
			.Where(name => !string.IsNullOrWhiteSpace(name))
			.Select(name => (name, OwnOptionTier)));
	}

	private static string FindClosest(string requested, IEnumerable<(string Name, int Tier)> candidates) {
		string[] requestedTokens = Tokenize(requested);
		string comparableRequested = Normalize(requested);
		int threshold = GetDistanceThreshold(comparableRequested.Length);
		return candidates
			.Distinct()
			.Select(candidate => (candidate.Name, candidate.Tier,
				Overlap: requestedTokens.Intersect(Tokenize(candidate.Name), StringComparer.Ordinal).Count(),
				Distance: ComputeDistance(comparableRequested, Normalize(candidate.Name))))
			.Where(score => score.Overlap > 0 || score.Distance <= threshold)
			.OrderByDescending(score => score.Overlap)
			.ThenBy(score => score.Tier)
			.ThenBy(score => score.Distance)
			.ThenBy(score => score.Name, StringComparer.OrdinalIgnoreCase)
			.Select(score => score.Name)
			.FirstOrDefault();
	}

	private static string[] Tokenize(string name) =>
		new string(name.Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray())
			.Split(' ', StringSplitOptions.RemoveEmptyEntries)
			.Select(token => token.ToLowerInvariant())
			.Distinct()
			.ToArray();

	private static string Normalize(string name) =>
		new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

	private static int GetDistanceThreshold(int length) =>
		length switch {
			<= 2 => 0,
			<= 6 => 1,
			<= 10 => 2,
			_ => 3
		};

	private static int ComputeDistance(string source, string target) {
		int[] previous = new int[target.Length + 1];
		int[] current = new int[target.Length + 1];
		for (int column = 0; column <= target.Length; column++) {
			previous[column] = column;
		}
		for (int row = 1; row <= source.Length; row++) {
			current[0] = row;
			for (int column = 1; column <= target.Length; column++) {
				int cost = source[row - 1] == target[column - 1] ? 0 : 1;
				current[column] = Math.Min(Math.Min(previous[column] + 1, current[column - 1] + 1),
					previous[column - 1] + cost);
			}
			(previous, current) = (current, previous);
		}
		return previous[target.Length];
	}
}
