using System.Threading.Tasks;
using Clio.Common.Telemetry;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class ServedContentMeterTests {

	[Test]
	[Description("Reports nothing until something is served, so a process that served nothing stamps no row of zeros.")]
	public void TryGetSnapshot_ShouldReturnFalse_WhenNothingIsServed() {
		// Arrange
		ServedContentMeter meter = new();

		// Act
		bool reported = meter.TryGetSnapshot(out ServedContentSnapshot snapshot);

		// Assert
		reported.Should().BeFalse(
			because: "the CAADT hook records its events through clio processes that serve nothing, and zeros from them would read as sessions that cost nothing");
		snapshot.Should().BeNull(
			because: "there is no total to report before the first response");
	}

	[Test]
	[Description("Counts a served article as a read and the bytes of every get-guidance response, a refusal included.")]
	public void RecordGuidance_ShouldCountReadsAndBytes_WhenArticlesAndRefusalsAreServed() {
		// Arrange
		ServedContentMeter meter = new();

		// Act
		meter.RecordGuidance("process-modeling", "1.16.2", 1_000);
		meter.RecordGuidance(null, null, 300);
		bool reported = meter.TryGetSnapshot(out ServedContentSnapshot snapshot);

		// Assert
		reported.Should().BeTrue(
			because: "two responses were served");
		snapshot.GuidanceReads.Should().Be(1,
			because: "only a response that served an article is a read; a refusal served none");
		snapshot.GuidanceBytes.Should().Be(1_300,
			because: "a refusal still put its text into the agent's context, so its bytes are part of what was served");
		snapshot.GuidanceRereads.Should().Be(0,
			because: "nothing was served twice");
		snapshot.ContractReads.Should().Be(0,
			because: "no contract was served");
	}

	[Test]
	[Description("Counts a second read of an article the session already received as a re-read - the trace a context compaction leaves.")]
	public void RecordGuidance_ShouldCountReread_WhenTheSameArticleIsServedAgain() {
		// Arrange
		ServedContentMeter meter = new();

		// Act
		meter.RecordGuidance("process-modeling", "1.16.2", 1_000);
		meter.RecordGuidance("process-parameters", "1.16.2", 800);
		meter.RecordGuidance("process-modeling", "1.16.2", 1_000);
		bool reported = meter.TryGetSnapshot(out ServedContentSnapshot snapshot);

		// Assert
		reported.Should().BeTrue(
			because: "three articles were served");
		snapshot.GuidanceReads.Should().Be(3,
			because: "every served article is a read, the repeated one included");
		snapshot.GuidanceRereads.Should().Be(1,
			because: "exactly one article was fetched again after the session had already received it");
		snapshot.GuidanceBytes.Should().Be(2_800,
			because: "a re-read costs the agent the whole article again");
	}

	[Test]
	[Description("Reports the library version of the latest response that named one, ignoring a later response that named none.")]
	public void RecordGuidance_ShouldKeepTheLatestLibraryVersion_WhenALaterResponseNamesNone() {
		// Arrange
		ServedContentMeter meter = new();

		// Act
		meter.RecordGuidance("routing", "1.16.1", 100);
		meter.RecordGuidance("routing", "1.16.2", 100);
		meter.RecordGuidance(null, null, 50);
		bool reported = meter.TryGetSnapshot(out ServedContentSnapshot snapshot);

		// Assert
		reported.Should().BeTrue(
			because: "three responses were served");
		snapshot.GuidanceLibraryVersion.Should().Be("1.16.2",
			because: "a guidance read can activate a newer generation mid-session, and the newest one is what the agent is reading now");
	}

	[Test]
	[Description("Counts contract responses on their own, so a session that read only contracts still reports.")]
	public void RecordContract_ShouldReport_WhenNoGuidanceWasServed() {
		// Arrange
		ServedContentMeter meter = new();

		// Act
		meter.RecordContract(4_000);
		meter.RecordContract(500);
		bool reported = meter.TryGetSnapshot(out ServedContentSnapshot snapshot);

		// Assert
		reported.Should().BeTrue(
			because: "a contract response is served content too");
		snapshot.ContractReads.Should().Be(2,
			because: "both get-tool-contract responses were served");
		snapshot.ContractBytes.Should().Be(4_500,
			because: "the bytes of every contract response add up");
		snapshot.GuidanceReads.Should().Be(0,
			because: "no guidance was served");
		snapshot.GuidanceLibraryVersion.Should().BeNull(
			because: "no article named a library version");
	}

	[Test]
	[Description("Keeps exact totals under concurrent calls, because the MCP server dispatches tool calls in parallel.")]
	public void RecordGuidance_ShouldKeepExactTotals_WhenCalledConcurrently() {
		// Arrange
		ServedContentMeter meter = new();

		// Act
		Parallel.For(0, 1_000, index => {
			meter.RecordGuidance("routing", "1.16.2", 10);
			meter.RecordContract(3);
		});
		bool reported = meter.TryGetSnapshot(out ServedContentSnapshot snapshot);

		// Assert
		reported.Should().BeTrue(
			because: "two thousand responses were served");
		snapshot.GuidanceReads.Should().Be(1_000,
			because: "no concurrent read may be lost");
		snapshot.GuidanceRereads.Should().Be(999,
			because: "every read after the first one of the same article is a re-read, however the calls interleave");
		snapshot.GuidanceBytes.Should().Be(10_000,
			because: "no concurrent byte count may be lost");
		snapshot.ContractReads.Should().Be(1_000,
			because: "no concurrent contract read may be lost");
		snapshot.ContractBytes.Should().Be(3_000,
			because: "no concurrent contract byte count may be lost");
	}

	[Test]
	[Description("Tells callers whether measuring is worth doing: the counting meter counts, the inert one does not, so a host that does not count never serializes a response twice.")]
	public void IsCounting_ShouldReflectWhetherTheMeterCounts_WhenAskedBeforeMeasuring() {
		// Arrange
		IServedContentMeter counting = new ServedContentMeter();
		IServedContentMeter inert = NullServedContentMeter.Instance;

		// Act
		bool countingCounts = counting.IsCounting;
		bool inertCounts = inert.IsCounting;

		// Assert
		countingCounts.Should().BeTrue(
			because: "the stdio host's meter needs every response measured");
		inertCounts.Should().BeFalse(
			because: "measuring for the inert meter would serialize every response a second time for nothing");
	}

	[Test]
	[Description("The inert meter registered outside the stdio host records nothing, so a multi-session host can never stamp a mixed count.")]
	public void NullServedContentMeter_ShouldNeverReport_WhenContentIsRecorded() {
		// Arrange
		NullServedContentMeter meter = NullServedContentMeter.Instance;

		// Act
		meter.RecordGuidance("routing", "1.16.2", 100);
		meter.RecordContract(100);
		bool reported = meter.TryGetSnapshot(out ServedContentSnapshot snapshot);

		// Assert
		reported.Should().BeFalse(
			because: "mcp-http serves many sessions from one process, so it must stamp nothing rather than a mixed count");
		snapshot.Should().BeNull(
			because: "the inert meter has no totals");
	}
}
