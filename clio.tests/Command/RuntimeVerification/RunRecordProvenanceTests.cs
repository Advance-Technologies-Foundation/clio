using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.RuntimeVerification;
using Clio.Common;
using Clio.Common.EntitySchema;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.RuntimeVerification;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class RunRecordProvenanceTests {

	private const string SelectUrl = "http://sandbox/0/DataService/json/SyncReply/SelectQuery";
	private const string RunId = "e29511a0-b05e-4b8f-8155-a8a25b76754e";
	private const string OtherRunId = "5d6d8c37-50f1-4d39-8a8b-2f8f7f7d1e11";
	private const string SubRunId = "9a0a3b1e-6c0b-4f9b-9c43-1f7b8a0d9e22";
	private const string HeldActivity = "489ae227-5516-487b-aa09-77de84b6feb4";
	private const string ParallelActivity = "8b2a5bd0-138b-4d52-90f6-a50d5433b9b4";
	private const string LinkedNote = "c4ed336c-3e9b-40fe-8b82-5632650c7b8d";
	private const string ElementOfRun = "0b5c7d7e-0d6f-4c8e-9a51-6d2f0c1b9a01";
	private const string ElementOfOtherRun = "0b5c7d7e-0d6f-4c8e-9a51-6d2f0c1b9a02";

	private static RuntimeEntitySchemaResult Schema(string name, params (string Column, string Reference)[] columns) =>
		new(Guid.NewGuid(), name, Guid.NewGuid(), null, null,
			[.. columns.Select(column => new RuntimeEntitySchemaColumnResult(Guid.NewGuid(), column.Column, null, null,
				column.Reference is null ? 0 : 10, false, false, column.Reference))]);

	private sealed record Harness(RunRecordProvenance Provenance, List<JObject> Queries);

	// Answers each select by its root schema from the given rows.
	private static Harness Create(Dictionary<string, string> rowsByRoot) {
		List<JObject> queries = [];
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(SelectUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(call => {
				JObject query = JObject.Parse(call.ArgAt<string>(1));
				queries.Add(query);
				string rows = rowsByRoot.GetValueOrDefault(query.Value<string>("rootSchemaName"), "[]");
				return "{\"success\":true,\"rows\":" + rows + "}";
			});
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns(SelectUrl);
		IRuntimeEntitySchemaReader schemaReader = Substitute.For<IRuntimeEntitySchemaReader>();
		schemaReader.GetByName("Activity", Arg.Any<int>())
			.Returns(Schema("Activity", ("Owner", "Contact"), ("CreatedBy", "Contact"), ("ProcessElementId", null)));
		schemaReader.GetByName("ActivityNote", Arg.Any<int>())
			.Returns(Schema("ActivityNote", ("Activity", "Activity")));
		return new Harness(new RunRecordProvenance(client, urlBuilder, schemaReader), queries);
	}

	private static string Lookup(string id) => $"{{\"value\":\"{id}\",\"displayValue\":\"x\"}}";

	[Test]
	[Description("A record whose ProcessElementId names an element data row of the run is tied to it; a record of the same login created in the window by another run is not, and a record whose lookup points at a tied one is tied through it.")]
	public void Resolve_Should_Tie_Only_What_The_Run_Holds_And_What_Points_At_It() {
		// Arrange
		Harness harness = Create(new Dictionary<string, string> {
			["Activity"] = $"[{{\"Id\":\"{HeldActivity}\",\"Owner\":null,\"ProcessElementId\":\"{ElementOfRun}\"}},"
				+ $"{{\"Id\":\"{ParallelActivity}\",\"Owner\":null,\"ProcessElementId\":\"{ElementOfOtherRun}\"}}]",
			["ActivityNote"] = $"[{{\"Id\":\"{LinkedNote}\",\"Activity\":{Lookup(HeldActivity)}}}]",
			["SysProcessElementData"] = $"[{{\"Id\":\"{ElementOfRun}\",\"SysProcess\":{Lookup(RunId)}}},"
				+ $"{{\"Id\":\"{ElementOfOtherRun}\",\"SysProcess\":{Lookup(OtherRunId)}}}]"
		});
		List<CreatedRecord> candidates = [
			new("Activity", HeldActivity, "Task"),
			new("Activity", ParallelActivity, "Parallel"),
			new("ActivityNote", LinkedNote, null)
		];

		// Act
		RecordProvenance provenance = harness.Provenance.Resolve(candidates, [], [RunId], 10_000);

		// Assert
		provenance.Tied.Should().BeEquivalentTo([HeldActivity, LinkedNote],
			because: "only the activity the run holds, and the note pointing at it, come from this run");
		provenance.Tied.Should().NotContain(ParallelActivity,
			because: "a record of the same login that another run holds is not this run's (QA D1)");
		provenance.HoldingRun[ParallelActivity].Should().Be(OtherRunId, because: "the holding run is reported as read");
		provenance.Warnings.Should().BeEmpty(because: "every read succeeded");
	}

	[Test]
	[Description("A record held by a sub-process of the run is tied to the run: its SysProcessLog row names the run as RootId.")]
	public void Resolve_Should_Tie_A_Record_Held_By_A_Sub_Process() {
		// Arrange
		Harness harness = Create(new Dictionary<string, string> {
			["Activity"] = $"[{{\"Id\":\"{HeldActivity}\",\"Owner\":null,\"ProcessElementId\":\"{ElementOfRun}\"}}]",
			["SysProcessElementData"] = $"[{{\"Id\":\"{ElementOfRun}\",\"SysProcess\":{Lookup(SubRunId)}}}]",
			["SysProcessLog"] = $"[{{\"Id\":\"{SubRunId}\"}}]"
		});

		// Act
		RecordProvenance provenance = harness.Provenance.Resolve([new CreatedRecord("Activity", HeldActivity, "Task")], [],
			[RunId], 10_000);

		// Assert
		provenance.Tied.Should().Contain(HeldActivity, because: "a Perform task in a sub-process is part of the run");
		harness.Queries.Should().Contain(query => query.Value<string>("rootSchemaName") == "SysProcessLog",
			because: "the sub-processes are read by their RootId");
	}

	[Test]
	[Description("A record nothing ties to the run - no element of it, no lookup to a tied record - stays untied, and an entity that cannot be read ties nothing and says why.")]
	public void Resolve_Should_Leave_Untied_What_It_Cannot_Prove() {
		// Arrange
		Harness harness = Create(new Dictionary<string, string> {
			["Activity"] = $"[{{\"Id\":\"{ParallelActivity}\",\"Owner\":null,\"ProcessElementId\":null}}]"
		});
		IRuntimeEntitySchemaReader failing = Substitute.For<IRuntimeEntitySchemaReader>();
		failing.GetByName(Arg.Any<string>(), Arg.Any<int>()).Returns(_ => throw new InvalidOperationException("denied"));
		RunRecordProvenance unreadable = new(Substitute.For<IApplicationClient>(), Substitute.For<IServiceUrlBuilder>(), failing);

		// Act
		RecordProvenance untied = harness.Provenance.Resolve([new CreatedRecord("Activity", ParallelActivity, null)], [],
			[RunId], 10_000);
		RecordProvenance unread = unreadable.Resolve([new CreatedRecord("Activity", HeldActivity, null)], [], [RunId], 10_000);

		// Assert
		untied.Tied.Should().BeEmpty(because: "time alone ties nothing to the run");
		unread.Tied.Should().BeEmpty(because: "an entity that cannot be read cannot prove anything");
		unread.Warnings.Should().ContainSingle(warning => warning.Contains("denied"),
			because: "the reason a record stays untied is reported");
	}

	[Test]
	[Description("A seed id (a record the action created itself) is tied, and so is a record whose lookup chain reaches it.")]
	public void Resolve_Should_Tie_Records_Linked_To_A_Seed() {
		// Arrange
		Harness harness = Create(new Dictionary<string, string> {
			["ActivityNote"] = $"[{{\"Id\":\"{LinkedNote}\",\"Activity\":{Lookup(HeldActivity)}}}]"
		});

		// Act
		RecordProvenance provenance = harness.Provenance.Resolve([new CreatedRecord("ActivityNote", LinkedNote, null)],
			[HeldActivity], [], 10_000);

		// Assert
		provenance.Tied.Should().BeEquivalentTo([HeldActivity, LinkedNote],
			because: "the note points at the seed record");
	}
}
