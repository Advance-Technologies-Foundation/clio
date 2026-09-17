using System;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class SequenceEnrollmentCommandTests : BaseCommandTests<SequenceEnrollmentOptions> {
	private IApplicationClient _client;
	private SequenceEnrollmentCommand _command;
	private Guid _sequence;
	private Guid _contact;
	protected override void AdditionalRegistrations(IServiceCollection services) {
		_client = Substitute.For<IApplicationClient>();
		services.AddSingleton(_client);
	}
	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<SequenceEnrollmentCommand>();
		_sequence = Guid.NewGuid();
		_contact = Guid.NewGuid();
		_client.ExecuteNonReplayablePostRequest(Arg.Any<string>(), Arg.Any<string>(), 30000, 1, 1)
			.Returns("{\"success\":true,\"addedCount\":1,\"failedCount\":0,\"errorMessages\":[]}");
		_client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), 10000, 1, 1).Returns(
			"{\"success\":true,\"rows\":[{\"id\":\"" + Guid.NewGuid() + "\",\"contact-id\":\"" + _contact
			+ "\",\"status-id\":\"" + Guid.NewGuid() + "\",\"status-name\":\"Pending\",\"stage-name\":\"Cold\"}]}");
	}
	public override void TearDown() {
		_client.ClearReceivedCalls();
		base.TearDown();
	}

	[TestCase(0)]
	[TestCase(101)]
	[Description("Empty and oversized audience selections fail before any request.")]
	public void InvalidAudience_DoesNotWrite(int count) {
		// Arrange
		var options = new SequenceEnrollmentOptions { SequenceId = _sequence,
			ContactIds = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray() };
		// Act
		Action act = () => _command.Enroll(options);
		// Assert
		act.Should().Throw<ArgumentException>(because: "a bounded explicit audience is required");
		_client.ReceivedCalls().Should().BeEmpty(because: "invalid selections must never reach Creatio");
	}

	[TestCase(true)]
	[TestCase(false)]
	[Description("Empty identifiers and repeated contacts cannot be submitted.")]
	public void InvalidIds_DoesNotWrite(bool empty) {
		// Arrange
		var options = new SequenceEnrollmentOptions { SequenceId = _sequence,
			ContactIds = empty ? new[] { Guid.Empty } : new[] { _contact, _contact } };
		// Act
		Action act = () => _command.Enroll(options);
		// Assert
		act.Should().Throw<ArgumentException>(because: "all identifiers must be meaningful and unique");
		_client.ReceivedCalls().Should().BeEmpty(because: "invalid IDs cannot authorize a write");
	}

	[Test]
	[Description("Native enrollment is one non-replayable write with an exact contact filter followed by scoped DataService status readback.")]
	public void Success_ReturnsPendingWithoutClaimingActivation() {
		// Arrange
		var options = new SequenceEnrollmentOptions { SequenceId = _sequence, ContactIds = [_contact] };
		// Act
		var result = _command.Enroll(options);
		// Assert
		result.Success.Should().BeTrue(because: "the native submission and status snapshot completed");
		result.AddedCount.Should().Be(1, because: "native counts must be preserved");
		result.Readback.Participants[0].GetProperty("status-name").GetString().Should().Be("Pending",
			because: "enrollment is not proof of activation");
		var write = _client.ReceivedCalls().Single(call => call.GetMethodInfo().Name == "ExecuteNonReplayablePostRequest");
		using var body = JsonDocument.Parse((string)write.GetArguments()[1]);
		var request = body.RootElement.GetProperty("request");
		request.GetProperty("sequenceId").GetGuid().Should().Be(_sequence, because: "the explicit sequence is the only target");
		using var filter = JsonDocument.Parse(request.GetProperty("filter").GetString());
		filter.RootElement.GetProperty("rootSchemaName").GetString().Should().Be("Contact", because: "the native service selects contacts");
		filter.RootElement.GetProperty("items").EnumerateObject().Single().Value.GetProperty("rightExpression")
			.GetProperty("parameter").GetProperty("value").GetGuid().Should().Be(_contact, because: "the filter must select exactly the supplied contact");
		var read = _client.ReceivedCalls().Single(call => call.GetMethodInfo().Name == "ExecutePostRequest");
		((string)read.GetArguments()[1]).Should().Contain("Participant.Id", because: "the native participant object calls its Contact lookup Participant");
		((string)read.GetArguments()[1]).Should().Contain(_sequence.ToString(), because: "readback must remain in the requested sequence");
	}

	[Test]
	[Description("A partially successful platform response keeps applied counts and does not retry.")]
	public void PartialFailure_PreservesCounts() {
		// Arrange
		_client.ExecuteNonReplayablePostRequest(Arg.Any<string>(), Arg.Any<string>(), 30000, 1, 1)
			.Returns("{\"success\":false,\"addedCount\":1,\"failedCount\":1,\"errorMessages\":[\"permission denied password=secret-value\"]}");
		// Act
		var result = _command.Enroll(new() { SequenceId = _sequence, ContactIds = [_contact, Guid.NewGuid()] });
		// Assert
		result.Completion.Should().Be("completed", because: "a valid response is different from uncertain completion");
		result.AddedCount.Should().Be(1, because: "a partial failure must not hide a committed enrollment");
		result.FailedCount.Should().Be(1, because: "the native failed count remains explicit");
		result.Success.Should().BeFalse(because: "partial application is not complete success");
		string.Join(" ", result.Errors).Should().NotContain("secret-value", because: "platform errors may contain sensitive values");
		_client.ReceivedCalls().Count(call => call.GetMethodInfo().Name == "ExecuteNonReplayablePostRequest")
			.Should().Be(1, because: "partial failures must not cause duplicate submissions");
	}

	[TestCase("<html>login</html>")]
	[TestCase("{\"success\":true}")]
	[TestCase("{\"success\":true,\"addedCount\":2147483647,\"failedCount\":1}")]
	[Description("Malformed or incompatible responses preserve uncertain completion and perform only one write.")]
	public void UnknownResponse_IsUncertain(string response) {
		// Arrange
		_client.ExecuteNonReplayablePostRequest(Arg.Any<string>(), Arg.Any<string>(), 30000, 1, 1).Returns(response);
		// Act
		var result = _command.Enroll(new() { SequenceId = _sequence, ContactIds = [_contact] });
		// Assert
		result.Completion.Should().Be("uncertain", because: "an unusable response cannot prove whether the write applied");
		result.AddedCount.Should().BeNull(because: "untrusted counts must not be invented");
		result.NextStep.Should().Contain("No retry", because: "operators must verify before resubmitting");
		_client.ReceivedCalls().Count(call => call.GetMethodInfo().Name == "ExecuteNonReplayablePostRequest")
			.Should().Be(1, because: "uncertainty must never trigger another write");
	}

	[TestCase(false)]
	[TestCase(true)]
	[Description("A failed or invisible status readback preserves known native counts and never resubmits.")]
	public void ReadbackFailure_DoesNotRetry(bool invisible) {
		// Arrange
		_client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), 10000, 1, 1)
			.Returns(invisible ? "{\"success\":true,\"rows\":[]}" : "{\"success\":false}");
		// Act
		var result = _command.Enroll(new() { SequenceId = _sequence, ContactIds = [_contact] });
		// Assert
		result.Completion.Should().Be("completed", because: "readback failure cannot erase a valid write response");
		result.AddedCount.Should().Be(1, because: "the platform already acknowledged an enrollment");
		result.Readback.State.Should().Be(invisible ? "incomplete" : "failed", because: "unverified records cannot become a complete snapshot");
		result.Success.Should().BeFalse(because: "status verification did not complete");
		_client.ReceivedCalls().Count(call => call.GetMethodInfo().Name == "ExecuteNonReplayablePostRequest")
			.Should().Be(1, because: "readback must never cause re-enrollment");
	}

	[Test]
	[Description("Transport timeout remains uncertain, with bounded diagnostic text and no replay.")]
	public void Timeout_IsUncertainAndBounded() {
		// Arrange
		_client.ExecuteNonReplayablePostRequest(Arg.Any<string>(), Arg.Any<string>(), 30000, 1, 1)
			.Returns(_ => throw new TimeoutException(new string('x', 300000)));
		// Act
		var result = _command.Enroll(new() { SequenceId = _sequence, ContactIds = [_contact] });
		// Assert
		result.Completion.Should().Be("uncertain", because: "timeout does not imply rollback");
		JsonSerializer.Serialize(result).Length.Should().BeLessThan(3000, because: "exception content must be bounded");
		_client.ReceivedCalls().Count(call => call.GetMethodInfo().Name == "ExecuteNonReplayablePostRequest")
			.Should().Be(1, because: "timeouts must not replay a write");
	}
}
