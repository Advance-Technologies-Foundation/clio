using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.ProcessModel;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.ProcessModel;

/// <summary>
/// The reply-size rules of ENG-103023, measured the way the spill line measures them: the serialized MCP reply, in
/// UTF-8 bytes (QA: a 14,552-character graph was 20,074 bytes on the wire, past the ~20 KB line, while the old
/// character threshold of 15,000 still picked full).
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "ProcessModel")]
public sealed class ProcessDigestBudgetTests {

	private static DescribeProcessResult Small() {
		DescribeProcessResult process = ProcessDigestTests.LoadFixture(ProcessDigestTests.SmallFixture);
		DescribeProcessCommand.OmitDecodedFilterPayloads(process);
		return process;
	}

	/// <summary>
	/// The first two elements of the small capture (no flows) with <paramref name="extra"/> formula elements and a
	/// flow to each appended: a base well under the line, grown one element at a time.
	/// </summary>
	private static DescribeProcessResult Grown(int extra) {
		DescribeProcessResult process = Small();
		List<DescribedElement> elements = [.. process.Elements.Take(2)];
		List<DescribedFlow> flows = [];
		for (int index = 0; index < extra; index++) {
			elements.Add(new DescribedElement {
				Name = $"ComputeStep{index}", Caption = $"Compute \"step\" {index}", BuildType = "formulatask",
				Type = "FormulaTask", Uid = Guid.NewGuid().ToString()
			});
			flows.Add(new DescribedFlow {
				Source = index == 0 ? elements[0].Name : $"ComputeStep{index - 1}", Target = $"ComputeStep{index}",
				Kind = "sequence"
			});
		}
		process.Elements = elements;
		process.Flows = flows;
		return process;
	}

	private static string Full(DescribeProcessResult process) =>
		JsonSerializer.Serialize(process, DescribeProcessCommand.OutputOptions);

	private static string Labelled(string full) => "{\"mode\":\"full\"," + full[1..];

	private static string ModeOf(string reply) => JsonNode.Parse(reply)!["mode"]?.GetValue<string>();

	[Test]
	[Description("QA ENG-103023 regression: a graph under the old 15,000-character threshold whose reply is over 18,000 bytes on the wire is answered with the summary.")]
	public void DefaultReply_ShouldBeSummary_WhenTheWireReplyIsOverTheLine_EvenUnder15000Chars() {
		// Arrange
		int extra = 0;
		while (Full(Grown(extra + 1)).Length < 15_000) {
			extra++;
		}
		DescribeProcessResult process = Grown(extra);
		string full = Full(process);

		// Act
		string reply = DescribeProcessCommand.BuildReply(process, null, null, out string refusal);
		long fullWire = McpResultSize.OfCommandReply(Labelled(full));
		TestContext.Out.WriteLine($"graph {full.Length} chars, full reply {fullWire} bytes on the wire");

		// Assert
		refusal.Should().BeNull(because: "a default describe is always answerable");
		full.Length.Should().BeLessThan(15_000, because: "anti-vacuity: the old character rule would have picked full");
		fullWire.Should().BeGreaterThan(ProcessDigest.AutoSummaryThresholdBytes,
			because: "anti-vacuity: on the wire this graph is past the line");
		ModeOf(reply).Should().Be("summary", because: "the wire size, not the character count, decides");
	}

	[Test]
	[Description("QA ENG-103023: near the threshold, the largest graph answered in full stays at or below 18,000 bytes on the wire, and one more element switches to the summary.")]
	public void DefaultReply_ShouldSwitchExactlyAtTheWireThreshold() {
		// Arrange
		int extra = 0;
		while (ModeOf(DescribeProcessCommand.BuildReply(Grown(extra + 1), null, null, out _)) == "full") {
			extra++;
		}

		// Act
		string lastFull = DescribeProcessCommand.BuildReply(Grown(extra), null, null, out _);
		string firstSummary = DescribeProcessCommand.BuildReply(Grown(extra + 1), null, null, out _);
		long lastFullWire = McpResultSize.OfCommandReply(lastFull);
		long nextFullWire = McpResultSize.OfCommandReply(Labelled(Full(Grown(extra + 1))));
		TestContext.Out.WriteLine($"last full reply {lastFullWire} bytes; the next one would be {nextFullWire} bytes");

		// Assert
		ModeOf(lastFull).Should().Be("full", because: "this graph still fits");
		lastFullWire.Should().BeLessThanOrEqualTo(18_000, because: "a full reply never passes 18,000 bytes on the wire");
		lastFullWire.Should().BeGreaterThan(17_000, because: "anti-vacuity: the probe really is at the threshold");
		ModeOf(firstSummary).Should().Be("summary", because: "one element more crosses the line");
		nextFullWire.Should().BeGreaterThan(18_000, because: "the switch happens where the wire size crosses the line");
	}

	[Test]
	[Description("Pins the literal budgets, so changing one is a deliberate act and not a side effect of a refactor.")]
	public void Budgets_ShouldHaveTheirDocumentedValues() {
		// Assert
		ProcessDigest.AutoSummaryThresholdBytes.Should().Be(18_000, because: "~2 KB under Copilot CLI's ~20 KB spill line");
		ProcessDigest.SummaryBudgetBytes.Should().Be(3_072, because: "the summary reply is held to ~3 KB");
		ProcessDigest.MaxChangeEntries.Should().Be(25, because: "each change list carries at most 25 entries");
	}

	[TestCase("Compute the value of step", "Process caption")]
	[TestCase("Обчислити значення кроку", "Процес погодження заявки")]
	[Description("Review ENG-103023 #7: the summary REPLY - mode, fullChars and more included - stays within its budget in UTF-8 bytes, Cyrillic captions included.")]
	public void SummaryReply_ShouldStayWithinBudget_InBytes_IncludingTheEnvelope(string captionStem, string processCaption) {
		// Arrange
		DescribeProcessResult process = Small();
		process.Caption = processCaption;
		process.Elements = [.. Enumerable.Range(0, 120).Select(index => new DescribedElement {
			Name = $"FormulaStepNumber{index}", Caption = $"{captionStem} {index}", BuildType = "formulatask"
		})];
		process.Flows = [.. Enumerable.Range(0, 120).Select(index => new DescribedFlow {
			Source = $"FormulaStepNumber{index}", Target = $"FormulaStepNumber{index + 1}"
		})];

		// Act
		JsonObject reply = ProcessDigest.BuildSummaryReply(process, "abc123", 99_999);

		// Assert
		ProcessDigest.Size(reply).Should().BeLessThanOrEqualTo(ProcessDigest.SummaryBudgetBytes,
			because: "the budget covers the whole reply in the unit the spill line counts");
		reply.First().Key.Should().Be("mode", because: "every reply names its mode first");
		reply["more"]!.GetValue<string>().Should().Contain("mode=full", because: "the way to more survives the cut");
		reply["omitted"].Should().NotBeNull(because: "a cut says how much it left out");
	}

	[Test]
	[Description("Review ENG-103023 #7: a header too long for the budget on its own (a huge caption and version warning) is shortened, so even the empty cut fits.")]
	public void SummaryReply_ShouldShortenTheHeader_WhenEvenAnEmptyCutIsOverBudget() {
		// Arrange
		DescribeProcessResult process = Small();
		process.Caption = new string('C', 4_000);
		process.VersionReadWarning = new string('W', 4_000);

		// Act
		JsonObject reply = ProcessDigest.BuildSummaryReply(process, "abc123", 1);

		// Assert
		ProcessDigest.Size(reply).Should().BeLessThanOrEqualTo(ProcessDigest.SummaryBudgetBytes,
			because: "the header's free text is the last thing cut, but it is cut");
		reply["name"]!.GetValue<string>().Should().Be(process.Name, because: "the identity is never cut");
	}

	[Test]
	[Description("Review ENG-103023 #1: each post-write change list is capped at 25 entries with an omitted count, and the whole write-digest line between two very different large processes stays under the 18,000-byte wire line.")]
	public void WriteDigest_ShouldCapChangeLists_AndStayUnderTheWireLine() {
		// Arrange
		DescribeProcessResult before = Grown(150);
		DescribeProcessResult after = Grown(0);
		after.Elements = [.. after.Elements, .. Enumerable.Range(0, 150).Select(index => new DescribedElement {
			Name = $"ReplacementStepWithALongerName{index}", Caption = $"Replacement {index}", BuildType = "formulatask"
		})];
		ILogger logger = Substitute.For<ILogger>();
		List<string> infos = [];
		logger.When(l => l.WriteInfo(Arg.Any<string>())).Do(call => infos.Add(call.Arg<string>()));

		// Act
		JsonObject changes = ProcessDigest.BuildChanges(before, after);
		ProcessWriteDigest.Report(logger, after, before, versionFactsRead: true);
		long wire = McpResultSize.OfCommandReply(infos.Single());
		TestContext.Out.WriteLine($"write-digest line: {infos.Single().Length} chars, {wire} bytes on the wire");

		// Assert
		changes["elementsAdded"]!.AsArray().Should().HaveCount(25, because: "a list is capped");
		changes["elementsAddedOmitted"]!.GetValue<int>().Should().Be(125, because: "the rest is counted, not dropped silently");
		changes["elementsRemoved"]!.AsArray().Should().HaveCount(25, because: "every list is capped");
		changes["elementsRemovedOmitted"]!.GetValue<int>().Should().Be(125,
			because: "listed plus omitted is the whole list of 150 removed elements");
		wire.Should().BeLessThanOrEqualTo(ProcessDigest.AutoSummaryThresholdBytes,
			because: "the write reply must not spill by itself");
	}

	[Test]
	[Description("Review ENG-103023 #3: the checksum ignores culture-resolved text (captions, descriptions, flow labels, display values), so a write digest read in the profile culture and a default en-US describe agree on an unchanged process; a structural change still moves it.")]
	public void Checksum_ShouldIgnoreLocalizedText_ButNotStructure() {
		// Arrange
		DescribeProcessResult english = Grown(3);
		DescribeProcessResult ukrainian = Grown(3);
		ukrainian.Caption = "Перевірити контакт";
		foreach (DescribedElement element in ukrainian.Elements) {
			element.Caption = "Крок " + element.Name;
		}
		foreach (DescribedFlow flow in ukrainian.Flows) {
			flow.Label = "Так";
		}
		DescribeProcessResult restructured = Grown(4);

		// Act
		string en = ProcessDigest.Checksum(english);
		string uk = ProcessDigest.Checksum(ukrainian);
		string other = ProcessDigest.Checksum(restructured);

		// Assert
		uk.Should().Be(en, because: "the same process read in another culture is the same process");
		other.Should().NotBe(en, because: "an added element is a real change");
		en.Should().MatchRegex("^[0-9a-f]{12}$", because: "the checksum is 12 lower-case hex characters");
	}

	[Test]
	[Description("Review ENG-103023 #13: an element-not-found refusal lists at most 30 names and points at the summary for the rest.")]
	public void ElementNotFound_ShouldCapTheListedNames() {
		// Arrange
		DescribeProcessResult process = Grown(100);

		// Act
		string reply = DescribeProcessCommand.BuildReply(process, null, "NoSuchElement", out string refusal);

		// Assert
		reply.Should().BeNull(because: "the element does not exist");
		refusal.Should().Contain("and ", because: "the rest is counted").And.Contain("mode=summary",
			because: "the caller is told where the full list is");
		refusal.Should().NotContain("ComputeStep99", because: "names past the cap are not listed");
	}
}
