using System.Collections.Generic;
using System.Linq;
using Clio.Command.RuntimeVerification;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.RuntimeVerification;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class CreatedRecordCleanerTests {

	private const string ActivityId = "9de7629a-8ffa-4abf-90eb-ce8f57f32deb";
	private const string ParticipantId = "23236814-8bf0-4be4-87e9-c309b08f6f61";

	private static readonly Dictionary<string, IReadOnlyCollection<string>> References = new() {
		["Activity"] = ["Contact", "Activity", "ActivityPriority"],
		["ActivityParticipant"] = ["Activity", "Contact"]
	};

	private static (CreatedRecordCleaner Cleaner, IApplicationClient Client, List<string> Deleted) Create(
		params string[] refusedOnce) {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(Arg.Any<string>()).Returns(call => "http://sandbox/0/" + call.Arg<string>());
		List<string> deleted = [];
		HashSet<string> refused = [.. refusedOnce];
		client.ExecuteDeleteRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(call => {
				string url = call.ArgAt<string>(0);
				string key = refused.FirstOrDefault(url.Contains);
				if (key is not null) {
					refused.Remove(key);
					return "{\"error\":{\"code\":\"\",\"message\":\"An error has occurred.\"}}";
				}
				deleted.Add(url);
				return string.Empty;
			});
		return (new CreatedRecordCleaner(client, urlBuilder), client, deleted);
	}

	[Test]
	[Description("A child entity that references another entity of the set is deleted before it, whatever order the records came in, and within an entity the newest record goes first.")]
	public void OrderForDeletion_Should_Put_Children_Before_Their_Parents() {
		// Arrange
		CreatedRecord activity = new("Activity", ActivityId, "Task") { CreatedOn = "2026-10-12T23:30:00.100" };
		CreatedRecord olderParticipant = new("ActivityParticipant", ParticipantId, null) { CreatedOn = "2026-10-12T23:30:00.200" };
		CreatedRecord newerParticipant = new("ActivityParticipant", "1a8a287a-d3a4-42c9-b4f2-a11de640381a", null) {
			CreatedOn = "2026-10-12T23:30:00.300"
		};

		// Act
		List<CreatedRecord> ordered = CreatedRecordCleaner.OrderForDeletion(
			[activity, olderParticipant, newerParticipant], References).ToList();

		// Assert
		ordered.Should().Equal([newerParticipant, olderParticipant, activity],
			because: "a participant references its activity, so it must go first, and newest-first inside an entity "
				+ "deletes a row before an older row of the same entity it may reference");
	}

	[Test]
	[Description("A reference cycle between two entities does not stop the ordering: every record is still returned.")]
	public void OrderForDeletion_Should_Return_Every_Record_When_References_Form_A_Cycle() {
		// Arrange
		Dictionary<string, IReadOnlyCollection<string>> cycle = new() { ["A"] = ["B"], ["B"] = ["A"] };
		CreatedRecord a = new("A", "1", null);
		CreatedRecord b = new("B", "2", null);

		// Act
		List<CreatedRecord> ordered = CreatedRecordCleaner.OrderForDeletion([a, b], cycle).ToList();

		// Assert
		ordered.Should().BeEquivalentTo([a, b],
			because: "a cycle has no correct order, so the caller's order is kept and the retry passes do the rest");
	}

	[Test]
	[Description("Delete removes the records children first through OData DELETE by key, and reports them as deleted.")]
	public void Delete_Should_Delete_Children_First_By_Key() {
		// Arrange
		(CreatedRecordCleaner cleaner, _, List<string> deleted) = Create();
		CreatedRecord activity = new("Activity", ActivityId, "Task");
		CreatedRecord participant = new("ActivityParticipant", ParticipantId, null);

		// Act
		CleanupReport report = cleaner.Delete([activity, participant], References, 10_000);

		// Assert
		deleted.Should().Equal([$"http://sandbox/0/odata/ActivityParticipant({ParticipantId})",
				$"http://sandbox/0/odata/Activity({ActivityId})"],
			because: "the participant references the activity and must be deleted before it");
		report.Deleted.Should().Equal([participant, activity], because: "both deletions succeeded, in that order");
		report.Failed.Should().BeEmpty(because: "nothing was refused");
	}

	[Test]
	[Description("A record refused in the first pass is tried again after the others, so a refusal caused by a row deleted later in the same pass clears itself.")]
	public void Delete_Should_Retry_A_Refused_Record_After_The_Others() {
		// Arrange
		(CreatedRecordCleaner cleaner, _, _) = Create(ParticipantId);
		CreatedRecord activity = new("Activity", ActivityId, "Task");
		CreatedRecord participant = new("ActivityParticipant", ParticipantId, null);

		// Act
		CleanupReport report = cleaner.Delete([activity, participant], References, 10_000);

		// Assert
		report.Deleted.Should().Equal([activity, participant],
			because: "the participant was refused once and deleted on the second pass");
		report.Failed.Should().BeEmpty(because: "every record was gone after the passes");
	}

	[Test]
	[Description("A record refused on every pass is reported in failed with the refusal, and the passes stop once a pass deletes nothing.")]
	public void Delete_Should_Report_A_Record_Refused_On_Every_Pass() {
		// Arrange
		(CreatedRecordCleaner cleaner, IApplicationClient client, _) = Create(ActivityId, ActivityId, ActivityId);
		CreatedRecord activity = new("Activity", ActivityId, "Task");

		// Act
		CleanupReport report = cleaner.Delete([activity], References, 10_000);

		// Assert
		report.Deleted.Should().BeEmpty(because: "the only record was refused");
		report.Failed.Should().ContainSingle(because: "the refused record must be named")
			.Which.Error.Should().Contain("An error has occurred.", because: "the refusal travels with the record");
		client.Received(1).ExecuteDeleteRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
			Arg.Any<int>());
	}

	[Test]
	[Description("No DELETE is started at or after stopAt: the records left are reported as not attempted, so nothing is deleted after the caller was answered.")]
	public void Delete_Should_Not_Start_A_Request_After_StopAt() {
		// Arrange
		(CreatedRecordCleaner cleaner, IApplicationClient client, _) = Create();
		CreatedRecord activity = new("Activity", ActivityId, "Task");

		// Act
		CleanupReport report = cleaner.Delete([activity], References, 10_000, System.DateTimeOffset.UtcNow.AddSeconds(-1));

		// Assert
		client.DidNotReceiveWithAnyArgs().ExecuteDeleteRequest(default, default, default, default, default);
		report.Failed.Should().ContainSingle(because: "the record was not attempted")
			.Which.Error.Should().Contain("not attempted", because: "the reason is the deadline, not a refusal");
	}

	[Test]
	[Description("A DELETE is never given more time than is left before stopAt, and none is started with less than a second left, so no record is deleted after the caller was answered.")]
	public void Delete_Should_Bound_Each_Request_By_StopAt() {
		// Arrange
		(CreatedRecordCleaner cleaner, IApplicationClient client, _) = Create();
		CreatedRecord activity = new("Activity", ActivityId, "Task");

		// Act
		CleanupReport late = cleaner.Delete([activity], References, 10_000, System.DateTimeOffset.UtcNow.AddMilliseconds(500));
		CleanupReport inTime = cleaner.Delete([activity], References, 10_000, System.DateTimeOffset.UtcNow.AddSeconds(5));

		// Assert
		late.Failed.Should().ContainSingle(because: "half a second is too little to start a delete")
			.Which.Error.Should().Contain("not attempted", because: "the reason is the deadline");
		inTime.Deleted.Should().ContainSingle(because: "five seconds are enough");
		client.Received(1).ExecuteDeleteRequest(Arg.Any<string>(), Arg.Any<string>(),
			Arg.Is<int>(timeout => timeout <= 5_000), Arg.Any<int>(), Arg.Any<int>());
	}
}
