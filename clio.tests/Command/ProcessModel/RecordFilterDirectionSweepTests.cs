using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.ProcessModel;

/// <summary>
/// Sweeps EVERY tracked text file in the repository for the "Change access rights" record-filter
/// inversion, instead of pinning one surface at a time.
///
/// <para>Why a sweep and not more per-file pins: this one fact is restated on ~30 surfaces in this
/// repository alone — two tool <c>[Description]</c>s, two prompts, warning strings, XML docs, a
/// capability map, a knowledge record, an element catalog, a spec, and the <c>[Description]</c> and
/// <c>because:</c> text of the tests themselves. It shipped BACKWARDS on most of them. Each review
/// round found the next unswept surface and a pin was added for exactly that one, which meant the
/// surface nobody had pinned was always the one that regressed — twice in a row.</para>
///
/// <para>The fact, verified on a live stand. <c>ChangeAdminRightsUserTask.InternalExecute</c> gates on
/// <c>if (!string.IsNullOrEmpty(DataSourceFilters))</c>, so:</para>
/// <list type="bullet">
///   <item>a record filter that is ABSENT never enters that branch — the ESQ runs UNFILTERED with
///     <c>UseAdminRights=false</c>, and the permission change lands on EVERY row of the object. This is
///     the WIDE state.</item>
///   <item>a record filter that is PRESENT but carries NO CONDITIONS takes the runtime's "filters
///     empty" exit and changes nothing. This is the INERT state, and a current CrtProcessBuilder
///     refuses it at build.</item>
/// </list>
///
/// <para>The element has NO output parameters, so nothing at run time contradicts a wrong sentence:
/// prose is the entire contract. Stating the absent filter as inert tells a reader that the widest
/// permission change the feature can produce is harmless, which is the one conclusion this whole guard
/// exists to prevent.</para>
///
/// <para>A line may legitimately quote a forbidden phrasing in order to REJECT it (a NotContain
/// assertion, a "this is a FAIL" smoke-case row, a correction marker). Those carry an exemption marker
/// — see <see cref="ExemptionMarkers"/> — so the sweep stays quiet without going blind.</para>
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "ProcessModel")]
public sealed class RecordFilterDirectionSweepTests {

	// Phrasings that describe the INERT state. Paired with an absent-filter subject they invert the fact.
	private const string InertWords =
		@"match(es)?\s+no\s+records|changes?\s+nothing|changes?\s+no\s+permissions|cannot\s+act|"
		+ @"\binert\b|silent\s+no-?op|(?:does|do|did)\s+nothing|is\s+an?\s+no-?op";

	// Phrasings that describe the WIDE state. Paired with a conditionless subject they invert it too.
	private const string WideWords =
		@"EVERY\s+record|every\s+row|widest|unbounded|runs?\s+UNFILTERED";

	// Subjects. "Empty filter" is deliberately absent from both: it is the ambiguous phrase that caused
	// the inversion, and it is banned outright by EmptyFilterPhrase below.
	// Absence is described two ways and BOTH have to be here. As a noun ("no record filter") and as the ACTION
	// that produces it ("clearing its record filter", "the record filter was CLEARED"). The action phrasing is
	// the one the modify path and the package notice actually use, and leaving it out is how this sweep passed
	// over a live "CLEARING its record filter makes it match no records" that a merge had reintroduced.
	private const string AbsentSubject =
		@"no\s+record\s+filter|NO\s+filter|without\s+a\s+filter|absent\s+filter|filter\s+is\s+absent|"
		+ @"no\s+filter\s+at\s+all|clear(?:s|ed|ing)?\s+(?:\w+\s+){0,3}?record\s+filter|"
		+ @"record\s+filter\s+(?:was|is|were)\s+cleared|filter\s+(?:was|is)\s+CLEARED";

	private const string ConditionlessSubject =
		@"no\s+conditions|conditionless|carries\s+no\s+condition";

	// Used ONLY as the gap stop-token, never as a subject. Deliberately broader than the subject patterns:
	// a contrastive sentence names the other state in shorthand ("while an ABSENT one acts on every record"),
	// and the gap has to stop there even though "ABSENT one" is too loose to open a match of its own.
	private const string AbsentMention = AbsentSubject + @"|\bABSENT\b";

	private const string ConditionlessMention = ConditionlessSubject + @"|\bPRESENT\b";

	/// <summary>
	/// A line carrying any of these is stating the phrasing in order to forbid, quote or correct it.
	/// Kept deliberately narrow: each marker is something an author writes ON PURPOSE.
	/// </summary>
	private static readonly string[] ExemptionMarkers = [
		"NotContain", "is a FAIL", "must not come back", "CORRECTED", "is the WIDEST", "is the WIDE",
		"not the inert", "rather than wide", "NOT to none", "would be false", "must never", "rather than inert",
		// Hypotheticals: the claim is named as the MISTAKE being prevented, not asserted. Kept as exact
		// phrases rather than the bare word "would", which would exempt a real inversion phrased as a
		// prediction ("an element with no filter would match no records").
		"would tell the caller", "Calling that",
		// The prevented-failure shape: the claim names what a reader must NOT be told, not what is true.
		"gets told", "is how a caller", "invites a setFilter"
	];

	/// <summary>
	/// How far either side of a match an exemption marker still counts. Markdown paragraphs are single
	/// lines here — the capability map's describe bullet is one line of ~5000 characters — so a
	/// whole-line search let one innocuous word exempt every claim in the paragraph. That is not
	/// hypothetical: it silently swallowed a planted inversion while this fixture reported green.
	/// </summary>
	private const int ExemptionRadius = 130;

	/// <summary>
	/// Lines joined before matching. Prose here wraps constantly - C# string concatenation, XML doc comments,
	/// hard-wrapped Markdown - so the subject and its claim routinely sit on DIFFERENT lines ("...leaves the
	/// element" / "inert - it changes nothing"). Matching line by line cannot see those at all, which is half
	/// of what this sweep is for.
	/// </summary>
	private const int WindowLines = 3;

	private static readonly string[] SearchedExtensions =
		[".cs", ".md", ".json", ".txt"];

	[Test]
	[Description("No tracked file may pair an ABSENT-record-filter subject with inert phrasing. That pairing is the inversion: it presents the element's widest possible configuration - a permission change across every row of the object, run with record permissions disabled - as harmless. It shipped on most of this feature's surfaces and was removed one surface per review round; this sweep is what makes the next one impossible rather than merely unlikely.")]
	public void NoTrackedFile_MayDescribeAnAbsentRecordFilter_AsInert() {
		// Arrange
		Regex inversion = Inversion(AbsentSubject, InertWords, otherSubject: ConditionlessMention);

		// Act
		IReadOnlyList<string> offenders = Sweep(inversion);

		// Assert
		offenders.Should().BeEmpty(
			because: "an ABSENT record filter makes the element apply the permission change to EVERY record "
				+ "of its object - the runtime never enters its filter branch, so the query runs unfiltered "
				+ "with record permissions disabled. Calling that state inert is the exact sentence this "
				+ "feature shipped and spent six review rounds removing:"
				+ Environment.NewLine + string.Join(Environment.NewLine, offenders));
	}

	[Test]
	[Description("No tracked file may pair a PRESENT-but-conditionless record filter with widening phrasing. It is the mirror error of the one above, and both halves were swapped together every time this went wrong - a conditionless filter takes the runtime's 'filters empty' exit and changes nothing.")]
	public void NoTrackedFile_MayDescribeAConditionlessRecordFilter_AsWide() {
		// Arrange
		Regex inversion = Inversion(ConditionlessSubject, WideWords, otherSubject: AbsentMention);

		// Act
		IReadOnlyList<string> offenders = Sweep(inversion);

		// Assert
		offenders.Should().BeEmpty(
			because: "a record filter that is PRESENT but carries no conditions is the INERT state - the "
				+ "runtime takes its \"filters empty\" exit and changes nothing. Describing it as acting on "
				+ "every record swaps it with the absent-filter state, which is how both halves went wrong "
				+ "at once:"
				+ Environment.NewLine + string.Join(Environment.NewLine, offenders));
	}

	/// <summary>
	/// A subject followed by a claim about the OTHER state, within one clause.
	/// <para>The gap deliberately refuses to cross a mention of <paramref name="otherSubject"/>. These two
	/// states are almost always introduced together, precisely BECAUSE they are opposites - "it is not the
	/// same state as an absent filter: a conditionless filter changes nothing, while an ABSENT one acts on
	/// every record" is the correct sentence, and a naive proximity match reads its second clause as an
	/// inversion of its first. Stopping at the other subject attributes each claim to the subject it
	/// actually belongs to, instead of accumulating an exemption marker per correct sentence.</para>
	/// </summary>
	private static Regex Inversion(string subject, string claim, string otherSubject) =>
		new($@"(?<subject>{subject})(?:(?!{otherSubject})[^.;]){{0,160}}?(?<claim>{claim})",
			RegexOptions.IgnoreCase | RegexOptions.Compiled);

	// Walks the repository's tracked files. Uses `git ls-files` rather than a directory walk so build
	// output, packages and any local scratch file can never turn this red, and so a file added to the
	// repository is swept the moment it is tracked.
	private static IReadOnlyList<string> Sweep(Regex inversion) {
		string root = FindRepositoryRoot();
		IReadOnlyList<string> tracked = TrackedFiles(root);

		// A sweep that silently reads NOTHING reports green forever, which is strictly WORSE than having no
		// sweep at all: it converts "nobody checked" into "checked and clean". This caught itself - the first
		// version shelled out to git, got an empty list on this machine, and passed with a planted inversion
		// sitting in the tree.
		tracked.Count.Should().BeGreaterThan(500,
			because: "the repository has thousands of tracked files, so a near-empty list means the enumeration "
				+ "failed rather than that there is nothing left to sweep");

		List<string> offenders = [];
		foreach (string relative in tracked) {
			if (!SearchedExtensions.Contains(Path.GetExtension(relative), StringComparer.OrdinalIgnoreCase)) {
				continue;
			}

			// This fixture necessarily contains both halves of every pattern it forbids.
			if (relative.EndsWith(nameof(RecordFilterDirectionSweepTests) + ".cs", StringComparison.Ordinal)) {
				continue;
			}

			string absolute = Path.Combine(root, relative);
			string[] lines;
			try {
				lines = File.ReadAllLines(absolute);
			} catch (IOException) {
				continue;
			}

			for (int i = 0; i < lines.Length; i++) {
				string line = Window(lines, i);
				if (!inversion.IsMatch(line)) {
					continue;
				}

				// EVERY match in the window, not just the first. A window often holds a correct contrastive
				// sentence AND, further along, a real inversion; stopping at the first match let an exempted
				// one hide the offender behind it.
				for (Match match = inversion.Match(line); match.Success; match = match.NextMatch()) {
					int from = Math.Max(0, match.Index - ExemptionRadius);
					int to = Math.Min(line.Length, match.Index + match.Length + ExemptionRadius);
					string vicinity = line[from..to];
					if (ExemptionMarkers.Any(marker => vicinity.Contains(marker, StringComparison.OrdinalIgnoreCase))) {
						continue;
					}

					offenders.Add($"  {relative}:{i + 1}: ...{vicinity.Trim()}...");
					break;
				}
			}
		}

		return offenders;
	}

	// Prefers `git ls-files` so build output and untracked scratch can never turn this red, and falls back to
	// a filtered directory walk wherever git is not resolvable from the test host. The fallback is what keeps
	// the sweep honest on a machine where the process launch fails, instead of leaving it silently empty.
	private static IReadOnlyList<string> TrackedFiles(string root) {
		try {
			using Process git = new() {
				StartInfo = new ProcessStartInfo("git", "ls-files") {
					WorkingDirectory = root,
					RedirectStandardOutput = true,
					UseShellExecute = false,
					CreateNoWindow = true
				}
			};
			git.Start();
			string output = git.StandardOutput.ReadToEnd();
			git.WaitForExit();
			string[] listed = [.. output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
				.Select(entry => entry.Trim().Replace('/', Path.DirectorySeparatorChar))];
			if (listed.Length > 500) {
				return listed;
			}
		} catch (Exception) {
			// Fall through to the walk below.
		}

		return [.. Directory
			.EnumerateFiles(root, "*", SearchOption.AllDirectories)
			.Where(path => !ExcludedPathSegments.Any(segment =>
				path.Contains(segment, StringComparison.OrdinalIgnoreCase)))
			.Select(path => Path.GetRelativePath(root, path))];
	}

	private static readonly string[] ExcludedPathSegments = [
		$"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
		$"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
		$"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}",
		$"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}",
		$"{Path.DirectorySeparatorChar}.vs{Path.DirectorySeparatorChar}"
	];

	// One logical line: this line plus the next few, with the syntax that only exists because the text wraps
	// (quote marks and leading concatenation plus signs) flattened away, so a sentence split across a string
	// concatenation reads as the sentence it is.
	private static string Window(string[] lines, int index) {
		StringBuilder joined = new();
		for (int offset = 0; offset < WindowLines && index + offset < lines.Length; offset++) {
			joined.Append(lines[index + offset].Replace("\"", " ").TrimStart('	', ' ', '+', '/')).Append(' ');
		}

		return joined.ToString();
	}

	private static bool IsRepositoryRoot(DirectoryInfo directory) {
		string marker = Path.Combine(directory.FullName, ".git");
		return Directory.Exists(marker) || File.Exists(marker);
	}

	private static string FindRepositoryRoot() {
		DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
		// A git WORKTREE carries .git as a FILE rather than a directory, so a directory-only check walks
		// past the root and returns null.
		while (directory is not null && !IsRepositoryRoot(directory)) {
			directory = directory.Parent;
		}

		directory.Should().NotBeNull(because: "the sweep runs from inside a git checkout");
		return directory!.FullName;
	}
}
