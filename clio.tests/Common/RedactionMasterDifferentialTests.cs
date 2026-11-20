using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.RuntimeVerification;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>
/// The redaction differential against MASTER, as a committed asset: every token of two or more letters or digits that
/// master's redactor removed from a corpus string must not be visible in clio's output for that string, at any entry
/// point and at any sink that carries a site's text. Master's removals are recorded once in
/// <c>Fixtures/redaction-master-baseline.txt</c>, so the check runs without master's binary. Option B builds every entry
/// point as master's frozen chain followed by span replacements, so the named trades are none: 0 by construction.
/// </summary>
[TestFixture]
[Property("Module", "Common")]
public sealed class RedactionMasterDifferentialTests {

	/// <summary>The recorded baseline, read from the source tree like the site-text fixture.</summary>
	internal static readonly string BaselinePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
		"..", "..", "..", "..", "clio.tests", "Common", "Fixtures", "redaction-master-baseline.txt"));

	// The master entry points, by the one-letter code the baseline file uses.
	private static readonly (char Code, string Name)[] MasterEntries = [
		('R', "Redact"), ('C', "RedactCredentials"), ('F', "RedactUntrustedOrNull")
	];

	// Each clio entry point and the master entry point it is compared with. A fenced rendering is compared with what
	// master's Redact AND master's fenced rendering removed: a token only master's 300-character cut dropped was never
	// redacted, only cut.
	private static readonly (string Name, Func<string, string> Run, string Master)[] ComparedEntries = [
		("Redact", SensitiveErrorTextRedactor.Redact, "R"),
		("RedactCredentials", SensitiveErrorTextRedactor.RedactCredentials, "C"),
		("RedactSiteText", SensitiveErrorTextRedactor.RedactSiteText, "R"),
		("RedactUntrustedOrNull", text => SensitiveErrorTextRedactor.RedactUntrustedOrNull(text) ?? string.Empty, "RF"),
		("RedactUntrustedSiteTextOrNull", text => SensitiveErrorTextRedactor.RedactUntrustedSiteTextOrNull(text) ?? string.Empty, "RF"),
		("RedactForConsoleOrNull", text => SensitiveErrorTextRedactor.RedactForConsoleOrNull(text) ?? string.Empty, "RF"),
	];

	// The sinks that carry a site's text, each compared with master's Redact, which is what master's site sinks ran.
	// A sink's own prose is subtracted: the tokens it writes for a text with no tokens.
	private static readonly (string Name, Func<string, string> Run)[] ComparedSinks = [
		("site-response.message", text => SiteResponse.FromRefusal(new SiteHttpResponse(500,
			JsonSerializer.Serialize(new { errorInfo = new { message = text } }))).Message ?? string.Empty),
		("non-JSON preview", CreatioResponseError.DescribeNonJsonResponse),
		("keyed write", text => ODataKeyedWrite.ValidateWriteResponse(JsonSerializer.Serialize(new { error = new { code = "", message = text } })) ?? string.Empty),
		("designer save error", text => PageSchemaMetadataHelper.ParseSaveErrorMessage(
			JObject.Parse(JsonSerializer.Serialize(new { success = false, errorInfo = new { message = text } })), "-")),
		("DataService failure", text => {
			try {
				DataServiceResponse.ThrowIfUnsuccessful(JsonSerializer.Serialize(new { success = false, errorInfo = new { message = text } }), "Write");
				return string.Empty;
			}
			catch (InvalidOperationException exception) {
				return exception.Message;
			}
		}),
		("DataWriteDiagnostic.message", text => DataWriteDiagnostic.Create("insert", "Account", null, true, true, false, text).Message ?? string.Empty),
		("refused delete's inner message", text => RecordDeletionService.ReadInnerMessage(JsonSerializer.Serialize(new {
			error = new { message = "An error has occurred.", innererror = new { message = text } }
		})) ?? string.Empty),
		("compile-status diagnostic", text => CompileStatusTool.RenderError(new PackageBuildDiagnostic("CS0103", text, "x.cs", 1, 2, false))),
	];

	private sealed record BaselineLine(string Hash, Dictionary<char, Dictionary<string, int>> Removed);

	private static Dictionary<string, BaselineLine> ReadBaseline(out string header) {
		Dictionary<string, BaselineLine> lines = new(StringComparer.Ordinal);
		StringBuilder headerText = new();
		foreach (string line in File.ReadLines(BaselinePath)) {
			if (line.StartsWith('#')) {
				headerText.AppendLine(line);
				continue;
			}
			string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			Dictionary<char, Dictionary<string, int>> removed = [];
			foreach (string entry in parts.Skip(2)) {
				removed[entry[0]] = entry[2..].Split(',').Select(token => token.Split('*'))
					.ToDictionary(pair => pair[0], pair => pair.Length > 1 ? int.Parse(pair[1]) : 1, StringComparer.Ordinal);
			}
			lines[parts[0]] = new BaselineLine(parts[1], removed);
		}
		header = headerText.ToString();
		return lines;
	}

	/// <summary>The tokens <paramref name="output"/> no longer holds of <paramref name="text"/>, with how many of each.</summary>
	internal static Dictionary<string, int> RemovedTokens(string text, string output) {
		Dictionary<string, int> input = RedactionDifferentialCorpus.Tokens(text);
		Dictionary<string, int> kept = RedactionDifferentialCorpus.Tokens(output);
		return input.Where(pair => kept.GetValueOrDefault(pair.Key) < pair.Value)
			.ToDictionary(pair => pair.Key, pair => pair.Value - kept.GetValueOrDefault(pair.Key), StringComparer.Ordinal);
	}

	// What master removed for a compared entry: one baseline entry, or the smaller count of two.
	private static Dictionary<string, int> MasterRemoved(BaselineLine line, string codes) {
		Dictionary<string, int> first = line.Removed.GetValueOrDefault(codes[0]) ?? [];
		if (codes.Length == 1) {
			return first;
		}
		Dictionary<string, int> second = line.Removed.GetValueOrDefault(codes[1]) ?? [];
		return first.Where(pair => second.ContainsKey(pair.Key))
			.ToDictionary(pair => pair.Key, pair => Math.Min(pair.Value, second[pair.Key]), StringComparer.Ordinal);
	}

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

	[Test]
	[Category("Unit")]
	[Description("THE DIFFERENTIAL AGAINST MASTER: for every corpus string, no token of two or more letters or digits that "
		+ "master's redactor removed is visible in clio's output - at every entry point, and at every sink that carries a "
		+ "site's text, long values across each clamp and the cut cases included. Option B declares no trade; what clio "
		+ "removes MORE is allowed and counted. A corpus string whose hash differs from the baseline's means the generator "
		+ "changed: regenerate the baseline.")]
	public void EveryEntryPointAndSink_ShouldShowNoTokenMasterRemoved() {
		// Arrange
		Dictionary<string, BaselineLine> baseline = ReadBaseline(out string header);
		Dictionary<string, Dictionary<string, int>> sinkProse = ComparedSinks.ToDictionary(sink => sink.Name,
			sink => RedactionDifferentialCorpus.Tokens(sink.Run("-")));
		List<string> drift = [];
		List<string> shown = [];
		Dictionary<string, int> removedMore = ComparedEntries.Select(entry => entry.Name).ToDictionary(name => name, _ => 0);

		// Act
		int index = 0;
		foreach ((string id, string text) in RedactionDifferentialCorpus.Strings()) {
			if (!baseline.TryGetValue(id, out BaselineLine line) || line.Hash != RedactionDifferentialCorpus.Hash(text)) {
				drift.Add(id);
				continue;
			}
			foreach ((string name, Func<string, string> run, string master) in ComparedEntries) {
				Dictionary<string, int> removed = MasterRemoved(line, master);
				string output = run(text);
				shown.AddRange(ShownAgain(text, removed, output).Select(token => $"{name} shows \"{token}\" of {id}: \"{text}\" -> \"{output}\""));
				if (RemovedTokens(text, output).Any(pair => pair.Value > removed.GetValueOrDefault(pair.Key))) {
					removedMore[name]++;
				}
			}
			bool sinkSample = !id.StartsWith("s:", StringComparison.Ordinal) || index % 8 == 0;
			index++;
			if (!sinkSample || !line.Removed.TryGetValue('R', out Dictionary<string, int> masterRedact)) {
				continue;
			}
			foreach ((string name, Func<string, string> run) in ComparedSinks) {
				string output = run(text);
				shown.AddRange(ShownAgain(text, masterRedact, output, sinkProse[name])
					.Select(token => $"{name} shows \"{token}\" of {id}: \"{text}\" -> \"{output}\""));
			}
		}
		TestContext.Out.WriteLine("Removes more than master (strings): " + string.Join(", ", removedMore.Select(pair => $"{pair.Key} {pair.Value}")));

		// Assert
		header.Should().Contain(MasterChainHashTests.MasterCommit, because: "the baseline names the master build it records");
		drift.Should().BeEmpty(because: "the corpus generator must match the recorded baseline; regenerate it after a change");
		shown.Should().BeEmpty(because: $"a token master hid must stay hidden, and option B trades none; {shown.Count} shown:\n"
			+ string.Join('\n', shown.Take(40)));
	}

	[Test]
	[Explicit("Regenerates Fixtures/redaction-master-baseline.txt from a master build: set CLIO_MASTER_DLL to its clio.dll "
		+ "and CLIO_MASTER_COMMIT to its full commit id.")]
	[Category("Unit")]
	[Description("Rebuilds the recorded master baseline: loads master's clio.dll into its own load context, runs its "
		+ "Redact, RedactCredentials and RedactUntrustedOrNull over the corpus, and records the tokens each removed.")]
	public void RegenerateMasterBaseline() {
		// Arrange
		string dll = Environment.GetEnvironmentVariable("CLIO_MASTER_DLL");
		string commit = Environment.GetEnvironmentVariable("CLIO_MASTER_COMMIT");
		if (string.IsNullOrEmpty(dll) || string.IsNullOrEmpty(commit)) {
			Assert.Ignore("set CLIO_MASTER_DLL and CLIO_MASTER_COMMIT");
		}
		Type master = new AssemblyLoadContext("master-redactor").LoadFromAssemblyPath(Path.GetFullPath(dll))
			.GetType("Clio.Common.SensitiveErrorTextRedactor", throwOnError: true);
		Dictionary<char, MethodInfo> entries = MasterEntries.ToDictionary(entry => entry.Code, entry =>
			master.GetMethod(entry.Name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, [typeof(string)]));
		StringBuilder file = new();
		file.AppendLine("# Redaction differential baseline: the tokens MASTER's SensitiveErrorTextRedactor removed from each corpus string.");
		file.AppendLine($"# master {commit}, clio Debug net10.0");
		file.AppendLine("# corpus RedactionDifferentialCorpus: fixture rows x 3 spellings + the edge strings + seeds "
			+ $"{string.Join(',', RedactionDifferentialCorpus.Seeds)} x {RedactionDifferentialCorpus.StringsPerSeed}");
		file.AppendLine("# entries R=Redact C=RedactCredentials F=RedactUntrustedOrNull; line: <id> <sha256-8 of input> [<entry>=<token>[*count],...]");
		file.AppendLine("# regenerate: RedactionMasterDifferentialTests.RegenerateMasterBaseline (README.md next to this file)");

		// Act
		foreach ((string id, string text) in RedactionDifferentialCorpus.Strings()) {
			file.Append(id).Append(' ').Append(RedactionDifferentialCorpus.Hash(text));
			foreach ((char code, MethodInfo method) in entries) {
				Dictionary<string, int> removed = RemovedTokens(text, (string)method.Invoke(null, [text]) ?? string.Empty);
				if (removed.Count > 0) {
					file.Append(' ').Append(code).Append('=').AppendJoin(',',
						removed.OrderBy(pair => pair.Key, StringComparer.Ordinal)
							.Select(pair => pair.Value == 1 ? pair.Key : $"{pair.Key}*{pair.Value}"));
				}
			}
			file.Append('\n');
		}
		File.WriteAllText(BaselinePath, file.ToString());

		// Assert
		File.Exists(BaselinePath).Should().BeTrue();
	}
}
