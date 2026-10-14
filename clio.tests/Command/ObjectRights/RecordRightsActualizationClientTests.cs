using System;
using System.Text.Json;
using Clio.Command;
using Clio.Command.ObjectRights;
using Clio.Common;
using Clio.Common.ObjectRights;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.ObjectRights;

/// <summary>
/// <see cref="RecordRightsActualizationClient"/>: the record count query, the one-shot launch of
/// ObjectRecordRightsActualizationProcess, and the status read from SysProcessLog — wire shapes checked on 10.2.370.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public class RecordRightsActualizationClientTests {

	private const string SelectUrl = "http://host/0/DataService/json/SyncReply/SelectQuery";
	private const string RunUrl = "http://host/0/ServiceModel/ProcessEngineService.svc/RunProcess";
	private static readonly Guid SchemaUId = Guid.Parse("35a9057f-800d-414b-acfb-c919c215857a");
	private static readonly Guid ProcessId = Guid.Parse("6e719418-f149-47a5-8fbe-9faa37080438");

	private IApplicationClient _applicationClient;
	private RecordRightsActualizationClient _client;
	private ObjectRecordCounter _counter;
	private string _body;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns(SelectUrl);
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.RunProcess).Returns(RunUrl);
		_body = null;
		_client = new RecordRightsActualizationClient(_applicationClient, urlBuilder);
		_counter = new ObjectRecordCounter(_applicationClient, urlBuilder);
	}

	private void Answers(string url, string response) =>
		_applicationClient.ExecutePostRequest(url, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(call => { _body = (string)call[1]; return response; });

	[Test]
	[Description("CountRecords sends one aggregation COUNT over Id on the object, once and with a short timeout (it is informational), and reads the Count column.")]
	public void CountRecords_ShouldSendCountAggregation() {
		// Arrange
		Answers(SelectUrl, "{\"success\":true,\"rows\":[{\"Count\":2}]}");

		// Act
		long count = _counter.CountRecords("Account", new CreatioRequestOptions { TimeOut = 100_000, MaxAttempts = 3 });

		// Assert
		count.Should().Be(2, because: "the Count column of the single row is the answer");
		JsonElement expression = JsonDocument.Parse(_body).RootElement.GetProperty("columns").GetProperty("items")
			.GetProperty("Count").GetProperty("expression");
		expression.GetProperty("expressionType").GetInt32().Should().Be(1, because: "a function expression");
		expression.GetProperty("functionType").GetInt32().Should().Be(2, because: "an aggregation");
		expression.GetProperty("aggregationType").GetInt32().Should().Be(1, because: "a COUNT");
		_applicationClient.Received(1).ExecutePostRequest(SelectUrl, Arg.Any<string>(),
			ObjectRecordCounter.CountTimeOutMilliseconds, 1, Arg.Any<int>());
	}

	[Test]
	[Description("Start sends ObjectRecordRightsActualizationProcess with EntitySchemaUId exactly once (a retry would start a second run) and returns the running process id.")]
	public void Start_ShouldLaunchOnce_WithSchemaUId() {
		// Arrange
		Answers(RunUrl, "{\"processId\":\"" + ProcessId + "\",\"processStatus\":1,\"success\":true}");

		// Act
		RunProcessResponse response = _client.Start(SchemaUId, new CreatioRequestOptions { MaxAttempts = 3 });

		// Assert
		response.Error.Should().BeNull(because: "the launch was accepted");
		response.ProcessId.Should().Be(ProcessId.ToString(), because: "the id is what the run is followed by");
		response.Status.Should().Be("running", because: "the process keeps going after the call returns");
		_applicationClient.Received(1).ExecutePostRequest(RunUrl, Arg.Any<string>(), Arg.Any<int>(), 1, Arg.Any<int>());
		JsonElement args = JsonDocument.Parse(_body).RootElement;
		args.GetProperty("schemaName").GetString().Should().Be("ObjectRecordRightsActualizationProcess", because: "the platform process");
		args.GetProperty("parameterValues")[0].GetProperty("value").GetString().Should().Be(SchemaUId.ToString(),
			because: "the process takes the object's schema UId");
	}

	[Test]
	[Description("A refused launch is returned as an error, not as a started run.")]
	public void Start_ShouldReturnError_WhenRefused() {
		// Arrange
		Answers(RunUrl, "{\"processId\":\"00000000-0000-0000-0000-000000000000\",\"processStatus\":0,\"success\":false,"
			+ "\"errorInfo\":{\"message\":\"denied\"}}");

		// Act
		RunProcessResponse response = _client.Start(SchemaUId, new CreatioRequestOptions());

		// Assert
		response.Error.Should().Contain("denied", because: "the platform's reason is kept");
	}

	[Test]
	[Description("ReadStatus reads the Status lookup of the run's SysProcessLog row.")]
	public void ReadStatus_ShouldParseLookup() {
		// Arrange
		Answers(SelectUrl, "{\"success\":true,\"rows\":[{\"Status\":{\"value\":\"815c9586-b6e2-df11-971b-001d60e938c6\","
			+ "\"displayValue\":\"Completed\"}}]}");

		// Act
		ProcessRunStatus status = _client.ReadStatus(ProcessId, new CreatioRequestOptions());

		// Assert
		status.StatusId.Should().Be(ProcessRunStatus.Completed, because: "the lookup value is the status id");
		status.IsFinal.Should().BeTrue(because: "completed ends the run");
		_body.Should().Contain(ProcessId.ToString(), because: "the row is read by the process id");
	}

	[Test]
	[Description("A run without a SysProcessLog row reads as null, not as a status.")]
	public void ReadStatus_ShouldReturnNull_WhenNoRow() {
		// Arrange
		Answers(SelectUrl, "{\"success\":true,\"rows\":[]}");

		// Act
		ProcessRunStatus status = _client.ReadStatus(ProcessId, new CreatioRequestOptions());

		// Assert
		status.Should().BeNull(because: "there is no row to read a status from");
	}

	[TestCase("", "empty response", TestName = "Start_ShouldReturnError_WhenBodyIsEmpty")]
	[TestCase("<html>error</html>", "could not read", TestName = "Start_ShouldReturnError_WhenBodyIsNotJson")]
	[Description("A launch answered with an empty or a non-JSON body is an error, never a started run.")]
	public void Start_ShouldReturnError_WhenBodyIsUnreadable(string body, string expected) {
		// Arrange
		Answers(RunUrl, body);

		// Act
		RunProcessResponse response = _client.Start(SchemaUId, new CreatioRequestOptions());

		// Assert
		response.Error.Should().Contain(expected, because: "an unreadable answer is reported as such");
		response.Status.Should().Be(RecordRightsActualizationClient.OutcomeUnknownStatus,
			because: "the run may have started though the answer was lost");
		response.ProcessId.Should().BeNull(because: "no run can be followed");
	}

	[Test]
	[Description("A count answered with no row is an error, not zero records.")]
	public void CountRecords_ShouldThrow_WhenNoRow() {
		// Arrange
		Answers(SelectUrl, "{\"success\":true,\"rows\":[]}");

		// Act
		Action count = () => _counter.CountRecords("Account", new CreatioRequestOptions());

		// Assert
		count.Should().Throw<InvalidOperationException>(because: "a missing row must never read as 'no records'")
			.WithMessage("*returned no row*");
	}

	[Test]
	[Description("FindRunning reads the running runs of ObjectRecordRightsActualizationProcess from SysProcessLog, filtered by the process schema and the Running status, newest first.")]
	public void FindRunning_ShouldFilterByProcessAndRunningStatus() {
		// Arrange
		Answers(SelectUrl, "{\"success\":true,\"rows\":[{\"Id\":\"" + ProcessId + "\",\"StartDate\":\"2026-10-05T10:00:00\"}]}");

		// Act
		var running = _client.FindRunning(new CreatioRequestOptions());

		// Assert
		running.Should().ContainSingle(because: "one run is going").Which.ProcessId.Should().Be(ProcessId, because: "its id is read");
		_body.Should().Contain("SysSchema.Name").And.Contain("ObjectRecordRightsActualizationProcess",
			because: "only runs of the update are looked at");
		System.Text.Json.JsonDocument.Parse(_body).RootElement.GetProperty("columns").GetProperty("items")
			.TryGetProperty("Id", out _).Should().BeTrue(because: "the warning names the running process by its Id");
		_body.Should().Contain(ProcessRunStatus.Running.ToString(), because: "only running runs count");
	}
}
