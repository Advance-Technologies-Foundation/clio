using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class GuidanceAccessLedgerTests {
	[Test]
	[Category("Unit")]
	[Description("Reports a recorded guidance name as fetched.")]
	public void WasFetched_Should_Return_True_When_Name_Was_Recorded() {
		// Arrange
		IGuidanceAccessLedger ledger = new GuidanceAccessLedger();

		// Act
		ledger.Record("ui-page-layout");

		// Assert
		ledger.WasFetched("ui-page-layout").Should().BeTrue(
			because: "a name recorded via Record must be reported as fetched");
	}

	[Test]
	[Category("Unit")]
	[Description("Matches recorded guidance names case-insensitively to mirror the catalog comparer.")]
	public void WasFetched_Should_Match_Case_Insensitively_When_Casing_Differs() {
		// Arrange
		IGuidanceAccessLedger ledger = new GuidanceAccessLedger();

		// Act
		ledger.Record("ui-page-layout");

		// Assert
		ledger.WasFetched("UI-PAGE-LAYOUT").Should().BeTrue(
			because: "guidance names are compared OrdinalIgnoreCase elsewhere, so the ledger must match the same way");
	}

	[Test]
	[Category("Unit")]
	[Description("Reports an unrecorded guidance name as not fetched.")]
	public void WasFetched_Should_Return_False_When_Name_Was_Not_Recorded() {
		// Arrange
		IGuidanceAccessLedger ledger = new GuidanceAccessLedger();

		// Act
		bool result = ledger.WasFetched("ui-page-layout");

		// Assert
		result.Should().BeFalse(
			because: "an unknown name was never recorded and must not be reported as fetched");
	}

	[Test]
	[Category("Unit")]
	[Description("Reports a not-fetched result for null, empty, or whitespace queries.")]
	[TestCase(null)]
	[TestCase("")]
	[TestCase("   ")]
	public void WasFetched_Should_Return_False_When_Name_Is_Blank(string name) {
		// Arrange
		IGuidanceAccessLedger ledger = new GuidanceAccessLedger();

		// Act
		bool result = ledger.WasFetched(name);

		// Assert
		result.Should().BeFalse(
			because: "a blank guidance name cannot have been recorded and must not throw");
	}

	[Test]
	[Description("Ignores null, empty, or whitespace names passed to Record.")]
	[TestCase(null)]
	[TestCase("")]
	[TestCase("   ")]
	public void Record_Should_Ignore_Blank_Name_When_Name_Is_Blank(string name) {
		// Arrange
		IGuidanceAccessLedger ledger = new GuidanceAccessLedger();

		// Act
		ledger.Record(name);

		// Assert
		ledger.Fetched.Should().BeEmpty(
			because: "Record must ignore blank names rather than store an unusable entry");
	}

	[Test]
	[Category("Unit")]
	[Description("Stores a recorded guidance name only once even when recorded repeatedly.")]
	public void Record_Should_Store_Name_Once_When_Recorded_Multiple_Times() {
		// Arrange
		IGuidanceAccessLedger ledger = new GuidanceAccessLedger();

		// Act
		ledger.Record("esq-filters");
		ledger.Record("ESQ-FILTERS");
		ledger.Record("esq-filters");

		// Assert
		ledger.Fetched.Should().ContainSingle(name => name == "esq-filters",
			because: "the same guidance name recorded repeatedly (any casing) must collapse to one distinct entry");
	}

	[Test]
	[Category("Unit")]
	[Description("Exposes a snapshot of all distinct recorded guidance names.")]
	public void Fetched_Should_Return_All_Distinct_Recorded_Names_When_Several_Are_Recorded() {
		// Arrange
		IGuidanceAccessLedger ledger = new GuidanceAccessLedger();

		// Act
		ledger.Record("ui-page-layout");
		ledger.Record("esq-filters");

		// Assert
		ledger.Fetched.Should().BeEquivalentTo(new[] { "ui-page-layout", "esq-filters" },
			because: "Fetched must expose every distinct guidance name recorded so far");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a point-in-time snapshot from Fetched that is unaffected by later records.")]
	public void Fetched_Should_Return_Independent_Snapshot_When_Recorded_After_Read() {
		// Arrange
		IGuidanceAccessLedger ledger = new GuidanceAccessLedger();
		ledger.Record("ui-page-layout");

		// Act
		IReadOnlyCollection<string> snapshot = ledger.Fetched;
		ledger.Record("esq-filters");

		// Assert
		snapshot.Should().ContainSingle(name => name == "ui-page-layout",
			because: "Fetched returns a copy, so a record made after the read must not mutate the earlier snapshot");
	}

	[Test]
	[Category("Unit")]
	[Description("Records concurrent guidance names without loss under parallel writers.")]
	public void Record_Should_Be_Thread_Safe_When_Recorded_Concurrently() {
		// Arrange
		IGuidanceAccessLedger ledger = new GuidanceAccessLedger();
		string[] names = Enumerable.Range(0, 200).Select(index => $"guide-{index}").ToArray();

		// Act
		Parallel.ForEach(names, name => ledger.Record(name));

		// Assert
		ledger.Fetched.Should().HaveCount(names.Length,
			because: "a thread-safe ledger must record every distinct name written by concurrent writers without loss");
		names.All(ledger.WasFetched).Should().BeTrue(
			because: "every concurrently recorded name must be reported as fetched");
	}
}
