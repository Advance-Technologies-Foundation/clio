using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>
/// The redaction differential against MASTER, as a committed asset: every token of two or more letters or digits that
/// master removed from a corpus string, at an entry point or at a sink that carries a site's text, must not be visible in
/// this build's output of the same sink for that string. Master's real sink outputs are recorded once, through the same
/// seams (<see cref="RedactionSinks"/>), in <c>Fixtures/redaction-master-baseline.txt.gz</c>, so the check runs without
/// master's binary. The baseline also pins master's chain output for every string (<see cref="MasterChain_ShouldReproduceMastersOutput_ForEveryCorpusString"/>).
/// </summary>
[TestFixture]
[Property("Module", "Common")]
public sealed class RedactionMasterDifferentialTests {

	/// <summary>The recorded baseline, read from the source tree like the site-text fixture.</summary>
	internal static readonly string BaselinePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
		"..", "..", "..", "..", "clio.tests", "Common", "Fixtures", "redaction-master-baseline.txt.gz"));

	// At most four workers, so the run stays one process's worth of load.
	private static readonly ParallelOptions Workers = new() { MaxDegreeOfParallelism = 4 };

	internal sealed record BaselineLine(string Hash, string RedactPin, string CredentialsPin, Dictionary<char, Dictionary<string, int>> Removed,
		HashSet<char> Threw);

	internal static Dictionary<string, BaselineLine> ReadBaseline(out string header) {
		Dictionary<string, BaselineLine> lines = new(StringComparer.Ordinal);
		StringBuilder headerText = new();
		using GZipStream unzip = new(File.OpenRead(BaselinePath), CompressionMode.Decompress);
		using StreamReader reader = new(unzip, Encoding.UTF8);
		while (reader.ReadLine() is { } line) {
			if (line.StartsWith('#')) {
				headerText.AppendLine(line);
				continue;
			}
			string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			Dictionary<char, Dictionary<string, int>> removed = [];
			HashSet<char> threw = [];
			foreach (string entry in parts.Skip(4)) {
				if (entry[0] == '!') {
					threw.UnionWith(entry[2..]);
					continue;
				}
				removed[entry[0]] = entry[2] == '^'
					? removed[entry[3]]
					: entry[2..].Split(',').Select(token => token.Split('*'))
						.ToDictionary(pair => pair[0], pair => pair.Length > 1 ? int.Parse(pair[1]) : 1, StringComparer.Ordinal);
			}
			lines[parts[0]] = new BaselineLine(parts[1], parts[2][1..], parts[3][1..], removed, threw);
		}
		header = headerText.ToString();
		return lines;
	}

	/// <summary>The first sixteen hex digits of the SHA-256 of a chain's output: the output pin.</summary>
	internal static string Pin(string output) =>
		Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(output ?? string.Empty)))[..16].ToLowerInvariant();

	/// <summary>The tokens <paramref name="output"/> no longer holds of <paramref name="text"/>, with how many of each.</summary>
	internal static Dictionary<string, int> RemovedTokens(string text, string output) {
		Dictionary<string, int> input = RedactionDifferentialCorpus.Tokens(text);
		Dictionary<string, int> kept = RedactionDifferentialCorpus.Tokens(output);
		return input.Where(pair => kept.GetValueOrDefault(pair.Key) < pair.Value)
			.ToDictionary(pair => pair.Key, pair => pair.Value - kept.GetValueOrDefault(pair.Key), StringComparer.Ordinal);
	}

	// What both removed: the smaller count of each token in both.
	private static Dictionary<string, int> Both(Dictionary<string, int> first, Dictionary<string, int> second) =>
		first.Where(pair => second.ContainsKey(pair.Key))
			.ToDictionary(pair => pair.Key, pair => Math.Min(pair.Value, second[pair.Key]), StringComparer.Ordinal);

	/// <summary>Every token master removed that the output shows again, after the sink's own prose is subtracted.</summary>
	internal static IEnumerable<string> ShownAgain(string text, Dictionary<string, int> masterRemoved, string output,
			Dictionary<string, int> prose = null) {
		Dictionary<string, int> input = RedactionDifferentialCorpus.Tokens(text);
		Dictionary<string, int> shown = RedactionDifferentialCorpus.Tokens(output);
		foreach ((string token, int removed) in masterRemoved) {
			int masterKept = input.GetValueOrDefault(token) - removed;
			if (shown.GetValueOrDefault(token) - (prose?.GetValueOrDefault(token) ?? 0) > masterKept) {
				yield return token;
			}
		}
	}

	/// <summary>One sink call: its output, whether it threw, and whether a redaction inside it failed closed (the
	/// redactor's <see cref="SensitiveErrorTextRedactor.FailedClosed"/> counter on this thread; on master's build, which has
	/// no counter, never). A fail-closed output shows no token and a throw shows none either, so neither may pass silently.</summary>
	internal static (string Output, bool Threw, bool FailedClosed) Call(Func<RedactionSinks, string, string> sink, RedactionSinks sinks, string text) {
		long failed = SensitiveErrorTextRedactor.FailedClosed;
		try {
			string output = sink(sinks, text);
			return (output, false, SensitiveErrorTextRedactor.FailedClosed != failed);
		}
		catch (Exception exception) {
			return ("[threw " + exception.GetType().Name + "]", true, SensitiveErrorTextRedactor.FailedClosed != failed);
		}
	}

	private static string Run(Func<RedactionSinks, string, string> sink, RedactionSinks sinks, string text) => Call(sink, sinks, text).Output;

	[Test]
	[Category("Unit")]
	[Description("THE DIFFERENTIAL AGAINST MASTER: for every corpus string - the fixture in three spellings, the edge "
		+ "strings across every clamp and cut, the strings whose prefix shrinks under redaction with a token across a cut, "
		+ "and 3 x 4,000 seeded strings - no token of two or more letters or digits that master removed at an entry point "
		+ "or at a site sink is visible in this build's output of the same sink: Redact, RedactCredentials, "
		+ "RedactJsonCredentialProperties, both fenced renderings and the console one, site-response's message and code, "
		+ "the non-JSON preview, the OData error call sites, the keyed write, the DataService failure, the designer's save "
		+ "error, the batch item error, compile-status' diagnostic and message tail, a refused delete's inner message and "
		+ "DataWriteDiagnostic. Option B declares no trade; what this build removes MORE is allowed and counted.")]
	public void EveryEntryPointAndSink_ShouldShowNoTokenMasterRemoved() {
		// Arrange
		Dictionary<string, BaselineLine> baseline = ReadBaseline(out string header);
		RedactionSinks sinks = new(typeof(SensitiveErrorTextRedactor).Assembly);
		Dictionary<string, Dictionary<string, int>> prose = RedactionSinks.Compared.ToDictionary(compared => compared.Name,
			compared => RedactionDifferentialCorpus.Tokens(Run(compared.Run, sinks, "-")));
		List<(string Id, string Text)> corpus = RedactionDifferentialCorpus.Strings().ToList();
		ConcurrentBag<string> drift = [];
		ConcurrentBag<string> shown = [];
		ConcurrentBag<string> failedClosed = [];
		ConcurrentBag<string> throws = [];
		ConcurrentDictionary<string, int> removedMore = new();
		ConcurrentDictionary<string, byte> seen = new();

		// Act
		Parallel.ForEach(corpus, Workers, entry => {
			(string id, string text) = entry;
			if (!baseline.TryGetValue(id, out BaselineLine line) || line.Hash != RedactionDifferentialCorpus.Hash(text)) {
				drift.Add(id);
				return;
			}
			seen[id] = 0;
			foreach ((string name, char code, Func<RedactionSinks, string, string> run) in RedactionSinks.Compared) {
				Dictionary<string, int> removed = line.Removed.GetValueOrDefault(code) ?? [];
				(string output, bool threw, bool failed) = Call(run, sinks, text);
				if (failed) {
					failedClosed.Add($"{name} failed closed on {id}: \"{text}\"");
				}
				if (threw != line.Threw.Contains(code)) {
					throws.Add($"{name} {(threw ? "threw" : "did not throw")} on {id}, master {(line.Threw.Contains(code) ? "threw" : "did not")}: \"{text}\"");
				}
				foreach (string token in ShownAgain(text, removed, output, prose[name])) {
					shown.Add($"{name} shows \"{token}\" of {id}: \"{text}\" -> \"{output}\"");
				}
				if (RemovedTokens(text, output).Any(pair => pair.Value - (prose[name].GetValueOrDefault(pair.Key)) > removed.GetValueOrDefault(pair.Key))) {
					removedMore.AddOrUpdate(name, 1, (_, count) => count + 1);
				}
			}
		});
		TestContext.Out.WriteLine($"Strings visited: {seen.Count} of {corpus.Count}; baseline lines: {baseline.Count}");
		TestContext.Out.WriteLine("Removes more than master (strings): " + string.Join(", ",
			RedactionSinks.Compared.Select(compared => $"{compared.Name} {removedMore.GetValueOrDefault(compared.Name)}")));

		// Assert
		header.Should().Contain(MasterChainHashTests.MasterCommit, because: "the baseline names the master build it records");
		drift.Should().BeEmpty(because: "the corpus generator must match the recorded baseline; regenerate it after a change");
		seen.Count.Should().Be(corpus.Count, because: "every corpus string is compared");
		baseline.Keys.Should().BeSubsetOf(seen.Keys, because: "every recorded string must have been seen");
		corpus.Count.Should().Be(RedactionDifferentialCorpus.ExpectedCount, because: "the corpus has the stated size");
		failedClosed.Should().BeEmpty(because: "a fail-closed output shows no token, so the comparison would pass vacuously; "
			+ $"{failedClosed.Count} found:\n" + string.Join('\n', failedClosed.Take(20)));
		throws.Should().BeEmpty(because: "a sink throws exactly where master's threw; " + string.Join('\n', throws.Take(20)));
		shown.Should().BeEmpty(because: $"a token master hid must stay hidden at every sink, and option B trades none; "
			+ $"{shown.Count} shown:\n" + string.Join('\n', shown.Take(40)));
	}

	[Test]
	[Category("Unit")]
	[Description("Condition 1, the OUTPUT PIN: for every corpus string, the frozen chain's Redact and RedactCredentials "
		+ "return exactly what master's build returned (the 16-hex SHA-256 prefix recorded in the baseline). A changed "
		+ "evaluator, a dropped or reordered call, or a pattern edit fails here even where the pattern hash cannot see it.")]
	public void MasterChain_ShouldReproduceMastersOutput_ForEveryCorpusString() {
		// Arrange
		Dictionary<string, BaselineLine> baseline = ReadBaseline(out _);
		List<(string Id, string Text)> corpus = RedactionDifferentialCorpus.Strings().ToList();
		ConcurrentBag<string> differs = [];

		// Act
		Parallel.ForEach(corpus, Workers, entry => {
			if (!baseline.TryGetValue(entry.Id, out BaselineLine line)) {
				differs.Add(entry.Id + " has no baseline line");
				return;
			}
			if (Pin(SensitiveErrorTextRedactor.ExecuteRegex(() => SensitiveErrorTextRedactor.MasterChain.Redact(entry.Text))) != line.RedactPin) {
				differs.Add($"Redact of {entry.Id}: \"{entry.Text}\"");
			}
			if (Pin(SensitiveErrorTextRedactor.ExecuteRegex(() => SensitiveErrorTextRedactor.MasterChain.RedactCredentials(entry.Text))) != line.CredentialsPin) {
				differs.Add($"RedactCredentials of {entry.Id}: \"{entry.Text}\"");
			}
		});

		// Assert
		differs.Should().BeEmpty(because: $"the frozen chain is master {MasterChainHashTests.MasterCommit}'s, output for "
			+ $"output; {differs.Count} differ:\n" + string.Join('\n', differs.Take(20)));
	}

	[Test]
	[Explicit("Regenerates Fixtures/redaction-master-baseline.txt.gz from a master build: set CLIO_MASTER_DLL to its "
		+ "clio.dll and CLIO_MASTER_COMMIT to its full commit id.")]
	[Category("Unit")]
	[Description("Rebuilds the recorded master baseline: loads master's clio.dll into its own load context, runs every "
		+ "sink of RedactionSinks.All through master's own seams over the corpus, and records, per sink, the tokens master's "
		+ "output removed - for a sink that cuts after its chain, those its uncut companion removed too - and the output pins "
		+ "of master's Redact and RedactCredentials.")]
	public void RegenerateMasterBaseline() {
		// Arrange
		string dll = Environment.GetEnvironmentVariable("CLIO_MASTER_DLL");
		string commit = Environment.GetEnvironmentVariable("CLIO_MASTER_COMMIT");
		if (string.IsNullOrEmpty(dll) || string.IsNullOrEmpty(commit)) {
			Assert.Ignore("set CLIO_MASTER_DLL and CLIO_MASTER_COMMIT");
		}
		Assembly master = new AssemblyLoadContext("master-redactor").LoadFromAssemblyPath(Path.GetFullPath(dll));
		RedactionSinks sinks = new(master);
		sinks.SiteBuild.Should().BeFalse(because: "the baseline is recorded from master's build");
		List<(string Id, string Text)> corpus = RedactionDifferentialCorpus.Strings().ToList();
		string[] lines = new string[corpus.Count];

		// Act
		Parallel.For(0, corpus.Count, Workers, index => {
			(string id, string text) = corpus[index];
			StringBuilder line = new();
			line.Append(id).Append(' ').Append(RedactionDifferentialCorpus.Hash(text))
				.Append(" P").Append(Pin(sinks.Redact(text))).Append(" Q").Append(Pin(sinks.RedactCredentials(text)));
			List<(char Code, string Encoded)> written = [];
			StringBuilder threw = new();
			foreach (RedactionSinks.Sink sink in RedactionSinks.All) {
				(string output, bool thrown, bool _) = Call(sink.Run, sinks, text);
				if (thrown) {
					threw.Append(sink.Code);
				}
				Dictionary<string, int> removed = RemovedTokens(text, output);
				if (sink.Uncut is not null) {
					removed = Both(removed, RemovedTokens(text, Run(sink.Uncut, sinks, text)));
				}
				if (removed.Count == 0) {
					continue;
				}
				string encoded = string.Join(',', removed.OrderBy(pair => pair.Key, StringComparer.Ordinal)
					.Select(pair => pair.Value == 1 ? pair.Key : $"{pair.Key}*{pair.Value}"));
				(char Code, string Encoded) same = written.FirstOrDefault(earlier => earlier.Encoded == encoded);
				line.Append(' ').Append(sink.Code).Append('=').Append(same.Code != default ? "^" + same.Code : encoded);
				written.Add((sink.Code, encoded));
			}
			if (threw.Length > 0) {
				line.Append(" !=").Append(threw);
			}
			lines[index] = line.ToString();
		});
		using (GZipStream zip = new(File.Create(BaselinePath), CompressionLevel.SmallestSize))
		using (StreamWriter writer = new(zip, new UTF8Encoding(false))) {
			writer.NewLine = "\n";
			writer.WriteLine("# Redaction differential baseline: per corpus string, the tokens MASTER's sinks removed, and pins of master's chain output.");
			writer.WriteLine($"# master {commit}, clio Debug net10.0");
			writer.WriteLine("# corpus RedactionDifferentialCorpus: fixture rows x 3 spellings + the edge strings + the shrinking-prefix strings + seeds "
				+ $"{string.Join(',', RedactionDifferentialCorpus.Seeds)} x {RedactionDifferentialCorpus.StringsPerSeed}");
			writer.WriteLine("# sinks RedactionSinks.All: " + string.Join("; ", RedactionSinks.All.Select(sink => $"{sink.Code}={sink.Name}")));
			writer.WriteLine("# line: <id> <sha256-8 of input> P<pin of Redact> Q<pin of RedactCredentials> [<sink>=<token>[*count],... | <sink>=^<earlier sink with the same set>] [!=<sinks that threw on master>]");
			writer.WriteLine("# regenerate: RedactionMasterDifferentialTests.RegenerateMasterBaseline (README.md next to this file)");
			foreach (string line in lines) {
				writer.WriteLine(line);
			}
		}

		// Assert
		File.Exists(BaselinePath).Should().BeTrue();
	}
}
