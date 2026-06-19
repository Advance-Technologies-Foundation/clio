using System;
using System.Collections.Generic;
using System.Linq;
using CommandLine;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Maps a CLI verb name (and every alias) to the <see cref="VerbAttribute"/>-carrying options
/// <see cref="Type"/> that declares it, using the same set of options types the CLI parser
/// reflects over.
/// </summary>
/// <remarks>
/// This is the single <c>command → optionsType</c> resolution layer that the generic
/// <c>clio-run</c> executor relies on instead of a hardcoded per-type switch. Lookups are
/// case-insensitive on the verb/alias string.
/// </remarks>
public interface ICommandOptionsRegistry {
	/// <summary>
	/// Attempts to resolve the options <see cref="Type"/> for a verb name or alias.
	/// </summary>
	/// <param name="command">The verb name or alias (kebab-case canonical or any declared alias).</param>
	/// <param name="optionsType">The resolved options type when the lookup hits; otherwise <c>null</c>.</param>
	/// <returns><c>true</c> when a registered verb/alias matches; otherwise <c>false</c> (no throw on miss).</returns>
	bool TryResolveOptionsType(string command, out Type optionsType);

	/// <summary>
	/// All canonical verb names known to the registry, in registration order.
	/// </summary>
	IReadOnlyCollection<string> KnownCommands { get; }
}

/// <summary>
/// Builds the <c>command → optionsType</c> map by reflecting <see cref="VerbAttribute"/> on the
/// options classes the CLI parser uses. A duplicate verb name or alias claimed by two distinct
/// options types is a startup-time configuration error and throws from the constructor.
/// </summary>
public sealed class CommandOptionsRegistry : ICommandOptionsRegistry {
	private readonly Dictionary<string, Type> _map;
	private readonly List<string> _canonicalNames;
	// Tokens that came from an alias (not a canonical [Verb] name). Canonical names always win over
	// aliases; an alias that collides with another verb's alias is ambiguous and is removed so it
	// resolves to neither target rather than silently dispatching to the wrong command.
	private readonly HashSet<string> _aliasTokens;
	private readonly HashSet<string> _ambiguousAliasTokens;

	/// <summary>
	/// Builds the registry from the production verb-options set
	/// (<see cref="Program.GetCommandOptionTypes"/>) — the same source the CLI parser reflects over.
	/// </summary>
	public CommandOptionsRegistry()
		: this(Program.GetCommandOptionTypes()) {
	}

	/// <summary>
	/// Builds the registry from an explicit set of options types. Exposed for testability so
	/// colliding or synthetic verb types can be injected without polluting the real assembly scan.
	/// </summary>
	/// <param name="optionsTypes">Options types that may carry a <see cref="VerbAttribute"/>.</param>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="optionsTypes"/> is <c>null</c>.</exception>
	/// <exception cref="InvalidOperationException">
	/// Thrown when two distinct options types claim the same verb name or alias.
	/// </exception>
	public CommandOptionsRegistry(IEnumerable<Type> optionsTypes) {
		ArgumentNullException.ThrowIfNull(optionsTypes);
		_map = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
		_canonicalNames = [];
		_aliasTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		_ambiguousAliasTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		List<Type> verbTypes = [.. optionsTypes];

		// Pass 1: canonical verb names. A canonical-name collision across distinct types is a genuine
		// parser-breaking ambiguity and is a hard startup error.
		foreach ((Type optionsType, VerbAttribute verb) in EnumerateVerbs(verbTypes)) {
			_canonicalNames.Add(verb.Name);
			RegisterCanonical(verb.Name, optionsType);
		}

		// Pass 2: aliases. A canonical name always wins; an alias that collides with another verb's
		// alias is ambiguous (cannot deterministically dispatch) and is removed so it resolves to a
		// miss rather than silently routing to the wrong command.
		foreach ((Type optionsType, VerbAttribute verb) in EnumerateVerbs(verbTypes)) {
			if (verb.Aliases is null) {
				continue;
			}
			foreach (string alias in verb.Aliases.Where(a => !string.IsNullOrWhiteSpace(a))) {
				RegisterAlias(alias, optionsType);
			}
		}
	}

	private static IEnumerable<(Type OptionsType, VerbAttribute Verb)> EnumerateVerbs(IEnumerable<Type> optionsTypes) {
		foreach (Type optionsType in optionsTypes) {
			VerbAttribute verb = optionsType.GetCustomAttributes(typeof(VerbAttribute), inherit: false)
				.OfType<VerbAttribute>()
				.FirstOrDefault();
			if (verb is not null) {
				yield return (optionsType, verb);
			}
		}
	}

	/// <inheritdoc />
	public IReadOnlyCollection<string> KnownCommands => _canonicalNames;

	/// <inheritdoc />
	public bool TryResolveOptionsType(string command, out Type optionsType) {
		if (string.IsNullOrWhiteSpace(command)) {
			optionsType = null;
			return false;
		}
		return _map.TryGetValue(command.Trim(), out optionsType);
	}

	private void RegisterCanonical(string verbToken, Type optionsType) {
		if (_map.TryGetValue(verbToken, out Type existing)) {
			if (existing == optionsType) {
				return;
			}
			throw new InvalidOperationException(
				$"Duplicate CLI verb '{verbToken}' is claimed by both '{existing.FullName}' and " +
				$"'{optionsType.FullName}'. Canonical verb names must be unique across options types.");
		}
		_map[verbToken] = optionsType;
	}

	private void RegisterAlias(string aliasToken, Type optionsType) {
		// A canonical name with the same token wins outright; never let an alias shadow a verb.
		if (_map.TryGetValue(aliasToken, out Type existing) && !_aliasTokens.Contains(aliasToken)) {
			return;
		}
		if (_ambiguousAliasTokens.Contains(aliasToken)) {
			return;
		}
		if (existing is not null && existing != optionsType) {
			// Two distinct verbs declare the same alias: ambiguous. Detect it and remove the entry so
			// the alias resolves to a miss instead of an arbitrary (silent last-wins) target. Callers
			// can still use either canonical verb name.
			_map.Remove(aliasToken);
			_aliasTokens.Remove(aliasToken);
			_ambiguousAliasTokens.Add(aliasToken);
			return;
		}
		_map[aliasToken] = optionsType;
		_aliasTokens.Add(aliasToken);
	}
}
