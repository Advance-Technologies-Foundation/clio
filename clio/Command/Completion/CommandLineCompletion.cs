using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CommandLine;

namespace Clio.Command.Completion;

/// <summary>
/// Shell-agnostic tab-completion engine for the <c>clio</c> CLI.
///
/// It is intentionally a pure reflection pass over the already-declared <c>[Verb]</c> and
/// <c>[Option]</c> attributes, so completions always stay in lock-step with the real command set
/// without duplicating any names by hand. The engine never touches dependency injection, the
/// network, or the feature-toggle gate — it only reads static metadata.
///
/// <para><b>Contract</b></para>
/// <code>clio __complete &lt;shell&gt; &lt;currentWord&gt; [wordsBefore...]</code>
/// <list type="bullet">
///   <item><description><c>&lt;shell&gt;</c> is one of: <c>powershell</c>, <c>zsh</c>, <c>bash</c>, <c>fish</c>.</description></item>
///   <item><description><c>&lt;currentWord&gt;</c> is the token under the cursor (may be an empty string, may be omitted entirely).</description></item>
///   <item><description>When <c>wordsBefore</c> are supplied, its first element is the invoked verb and options for that verb are completed. Without it, command names are completed.</description></item>
/// </list>
/// All completions are printed one per line to standard output so every shell can split on newlines.
/// The process always exits 0; shells treat a non-zero exit as "no completion available".
/// </summary>
internal static class CommandLineCompletion {

	// The meta-command verb that shells invoke. Excluded from command-name completion so it never
	// lists itself as an answer to its own question.
	private const string CompleteVerb = "__complete";

	// Name of the executable whose completions are produced. Shells pass every token before the cursor,
	// including this program name; the engine drops one leading occurrence so the first remaining token
	// is always the invoked verb (or none, meaning "complete a command name").
	private const string ProgramName = "clio";

	// The verb whose own argument is completed from a static domain (the shell names). Every other verb
	// completes its [Option] flags only, because positional values are runtime data the engine never touches.
	private const string CompletionVerb = "completion";

	/// <summary>
	/// Handles a <c>__complete</c> invocation and writes newline-separated completions to stdout.
	/// Returns <see langword="true"/> when <paramref name="args"/> was a completion request (and has
	/// been fully consumed), so the caller can skip the normal verb parser for it.
	/// </summary>
	internal static bool TryHandle(string[] args, out int exitCode) {
		exitCode = 0;

		// args layout: ["__complete", <shell>, <currentWord>, (wordsBefore...)]
		// The current word is optional: shells that drop an empty trailing argument (Windows PowerShell 5.1
		// does) send only the shell name, which means "complete the first word".
		if (args.Length < 2 || !string.Equals(args[0], CompleteVerb, StringComparison.OrdinalIgnoreCase)) {
			return false;
		}

		// The completion payload is uniform across shells today, so the normalized name is discarded
		// here; normalizing still rejects an unrecognized shell token instead of ignoring it.
		_ = ShellCompletionScripts.Normalize(args[1]);
		string currentWord = args.Length >= 3 ? args[2] : string.Empty;
		List<string> wordsBefore = args.Skip(3).ToList();

		// Shells pass every token before the cursor, which includes the program name itself. Drop one
		// leading occurrence so wordsBefore[0] is the invoked verb (or the list is empty for command names).
		if (wordsBefore.Count > 0 && string.Equals(wordsBefore[0], ProgramName, StringComparison.OrdinalIgnoreCase)) {
			wordsBefore = wordsBefore.Skip(1).ToList();
		}

		try {
			IReadOnlyList<string> completions = wordsBefore.Count switch {
				// No verb yet: the token under the cursor is a command name.
				0 => CompleteCommands(currentWord),
				// `clio completion <TAB>` is the one verb argument with a static domain.
				1 when string.Equals(wordsBefore[0], CompletionVerb, StringComparison.OrdinalIgnoreCase)
					=> ShellCompletionScripts.MatchingShells(StripLeadingDashes(currentWord)),
				_ => CompleteOptionsForVerb(wordsBefore[0], currentWord),
			};

			// CLIO002: direct Console use is intentional and must NOT be routed through ILogger here. A
			// completion invocation short-circuits before the logger is started and before DI boots, so an
			// ILogger would enqueue into a queue that has no print thread and the candidates would never
			// reach the shell. The payload must also be raw newline-separated text, undecorated.
#pragma warning disable CLIO002
			foreach (string completion in completions) {
				Console.Out.WriteLine(completion);
			}
#pragma warning restore CLIO002
		}
		catch {
			// A completion failure must never break the user's typing; an empty result is safe.
		}

		exitCode = 0;
		return true;
	}

	/// <summary>Lists command names (canonical preferred) whose name or alias starts with <paramref name="word"/>.</summary>
	private static IReadOnlyList<string> CompleteCommands(string word) {
		string prefix = StripLeadingDashes(word);
		// List<T> has no comparer constructor; matching and de-duplication below are explicitly
		// case-insensitive, so the default comparer is fine here.
		List<string> results = new();

		foreach (VerbInfo verb in GetVerbs()) {
			// Prefix match so `clio log<TAB>` expands to `login`.
			bool canonicalMatches = verb.CanonicalName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
			bool aliasMatches = verb.Aliases.Any(alias =>
				alias.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

			if (canonicalMatches || aliasMatches) {
				// Always complete to the canonical spelling so `clio log<TAB>` expands to `login`,
				// not to an alias the user happened to start typing.
				AddUnique(results, verb.CanonicalName);
			}
		}

		return results;
	}

	/// <summary>Lists option flags for <paramref name="verbToken"/> whose flag starts with <paramref name="word"/>.</summary>
	private static IReadOnlyList<string> CompleteOptionsForVerb(string verbToken, string word) {
		VerbInfo? verb = GetVerbs().FirstOrDefault(v =>
			string.Equals(v.CanonicalName, verbToken, StringComparison.OrdinalIgnoreCase)
			|| v.Aliases.Any(a => string.Equals(a, verbToken, StringComparison.OrdinalIgnoreCase)));

		if (verb is null || verb.OptionsType is null) {
			return Array.Empty<string>();
		}

		List<string> results = new();

		// Count the dashes the user has typed so far; everything after them is the prefix to match.
		// Guarding the slice with Min keeps it safe even for an empty word (""[2..] would otherwise throw).
		int dashCount = 0;
		while (dashCount < word.Length && word[dashCount] == '-') {
			dashCount++;
		}
		string typedPrefix = word[Math.Min(dashCount, word.Length)..];

		foreach (string flag in EnumerateOptions(verb.OptionsType)) {
			bool isLong = flag.StartsWith("--", StringComparison.Ordinal);
			// The dash family the user is typing is the family they get: no dash offers both, one dash
			// offers short flags, two dashes offer long flags only. Without the short guard, a two-dash
			// word would match every short flag as well, since the prefix after the dashes is empty and
			// therefore matches anything.
			if (isLong && dashCount < 2 && word.Length != 0) {
				continue;
			}
			if (!isLong && dashCount >= 2) {
				continue;
			}

			string name = isLong ? flag[2..] : flag[1..];
			if (MatchesPrefix(name, typedPrefix)) {
				AddUnique(results, flag);
			}
		}

		return results;
	}

	private static bool MatchesPrefix(string value, string prefix) =>
		string.Equals(value, prefix, StringComparison.OrdinalIgnoreCase)
		|| value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

	/// <summary>Enumerates every advertised option flag (<c>--long</c> and optional <c>-s</c>) for an options type.</summary>
	private static IEnumerable<string> EnumerateOptions(Type optionsType) {
		foreach (PropertyInfo property in optionsType.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
			OptionAttribute option = property.GetCustomAttribute<OptionAttribute>();
			if (option is null) {
				continue;
			}

			// A Hidden option is accepted only for backward compatibility and is deliberately absent from
			// every other user-facing surface, runtime help and the published docs included (ENG-102433).
			// Completion is such a surface, so offering it would advertise a spelling users are told to stop using.
			if (option.Hidden) {
				continue;
			}

			if (!string.IsNullOrEmpty(option.LongName)) {
				yield return "--" + option.LongName;
			}

			// A populated ShortName means the option also has a short single-dash form.
			if (!string.IsNullOrEmpty(option.ShortName)) {
				yield return "-" + option.ShortName;
			}
		}
	}

	/// <summary>Reflects over the clio assembly to collect every declared verb, skipping the meta-command itself.</summary>
	private static IEnumerable<VerbInfo> GetVerbs() {
		Assembly assembly = typeof(Program).Assembly;
		Type[] types;

		try {
			types = assembly.GetTypes();
		}
		catch (ReflectionTypeLoadException loadException) {
			types = loadException.Types.Where(t => t is not null).Cast<Type>().ToArray();
		}

		foreach (Type type in types) {
			VerbAttribute verb = type.GetCustomAttribute<VerbAttribute>();
			// Hidden verbs (compatibility spellings, easter eggs, probe surfaces) are absent from the help
			// catalog and from the published docs, so completion must not name them either.
			if (verb is null
				|| verb.Hidden
				|| string.Equals(verb.Name, CompleteVerb, StringComparison.OrdinalIgnoreCase)) {
				continue;
			}

			yield return new VerbInfo(verb.Name, verb.Aliases ?? Array.Empty<string>(), type);
		}
	}

	private static string StripLeadingDashes(string value) =>
		value.TrimStart('-');

	private static void AddUnique(List<string> destination, string value) {
		if (!destination.Any(existing => string.Equals(existing, value, StringComparison.OrdinalIgnoreCase))) {
			destination.Add(value);
		}
	}

	private sealed record VerbInfo(string CanonicalName, IReadOnlyList<string> Aliases, Type? OptionsType);
}
