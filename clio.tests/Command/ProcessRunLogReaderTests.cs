using System;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Unit tests for <see cref="ProcessRunLogReader"/>: the SelectQuery it sends for a run's log row, and the line it
/// takes from the logged error.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class ProcessRunLogReaderTests {

	private const string SelectUrl = "http://sandbox/0/DataService/json/SyncReply/SelectQuery";
	private static readonly Guid ProcessId = Guid.Parse("0f5e3a2a-2c8f-4f1e-9d0b-6d4b2f1a7c31");

	private static (ProcessRunLogReader Reader, IApplicationClient Client) Create(string selectResponse) {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(SelectUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(selectResponse);
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns(SelectUrl);
		return (new ProcessRunLogReader(client, urlBuilder), client);
	}

	[Test]
	[Description("The run's log row is read by its Id - the processId RunProcess returned - as a Guid, one row, once and with a bound, and the first non-empty line of the logged error is returned: the rest is the stack trace.")]
	public void ReadErrorSummary_Should_Read_The_Runs_Row_And_Return_The_First_Line() {
		// Arrange
		(ProcessRunLogReader reader, IApplicationClient client) = Create(
			"{\"success\":true,\"rows\":[{\"ErrorDescription\":\"\\r\\nSystem.ArgumentException: Term must be positive\\r\\n   at Terrasoft.Core.Process.Foo()\"}]}");

		// Act
		string summary = reader.ReadErrorSummary(ProcessId, 60_000);

		// Assert
		summary.Should().Be("System.ArgumentException: Term must be positive",
			because: "the first line is the exception's type and message, the part a caller can act on");
		client.Received(1).ExecutePostRequest(SelectUrl, Arg.Is<string>(body => IsTheRunsRowQuery(body)), 10_000, 1,
			Arg.Any<int>());
	}

	[Test]
	[Description("A run whose row carries no error, or no row at all, reads as no summary rather than as an empty string a caller would print.")]
	[TestCase("{\"success\":true,\"rows\":[]}")]
	[TestCase("{\"success\":true,\"rows\":[{\"ErrorDescription\":\"\"}]}")]
	[TestCase("{\"success\":true,\"rows\":[{\"ErrorDescription\":\"\\r\\n  \\r\\n\"}]}")]
	[TestCase("{\"success\":true,\"rows\":[{\"ErrorDescription\":null}]}")]
	public void ReadErrorSummary_Should_Return_Null_When_Nothing_Was_Logged(string selectResponse) {
		// Arrange
		(ProcessRunLogReader reader, _) = Create(selectResponse);

		// Act
		string summary = reader.ReadErrorSummary(ProcessId, 60_000);

		// Assert
		summary.Should().BeNull(because: "there is no logged error to name");
	}

	[Test]
	[Description("The read spends no more than the caller has left of its deadline, and never more than its own 10 s bound.")]
	[TestCase(2_500, 2_500)]
	[TestCase(60_000, 10_000)]
	public void ReadErrorSummary_Should_Bound_The_Read_By_The_Time_The_Caller_Has_Left(int given, int expected) {
		// Arrange
		(ProcessRunLogReader reader, IApplicationClient client) = Create("{\"success\":true,\"rows\":[]}");

		// Act
		reader.ReadErrorSummary(ProcessId, given);

		// Assert
		client.Received(1).ExecutePostRequest(SelectUrl, Arg.Any<string>(), expected, 1, Arg.Any<int>());
	}

	[Test]
	[Description("A SelectQuery the server refused throws, as the interface documents, instead of reading as 'nothing was logged': the caller turns it into a warning that names the run.")]
	public void ReadErrorSummary_Should_Throw_When_The_SelectQuery_Failed() {
		// Arrange
		(ProcessRunLogReader reader, _) = Create(
			"{\"success\":false,\"errorInfo\":{\"message\":\"Access denied\"}}");

		// Act
		Action read = () => reader.ReadErrorSummary(ProcessId, 60_000);

		// Assert
		read.Should().Throw<InvalidOperationException>(
			because: "a refused read is not an empty log, and the caller must be able to tell the two apart");
	}

	private static bool IsTheRunsRowQuery(string body) {
		JObject query = JObject.Parse(body);
		JToken filter = query["filters"]?["items"]?["byId"];
		return (string)query["rootSchemaName"] == "SysProcessLog"
			&& (int?)query["rowCount"] == 1
			&& (string)filter?["leftExpression"]?["columnPath"] == "Id"
			&& (int?)filter?["rightExpression"]?["parameter"]?["dataValueType"] == 0
			&& (string)filter?["rightExpression"]?["parameter"]?["value"] == ProcessId.ToString()
			&& query["columns"]?["items"]?["ErrorDescription"] is not null;
	}
}
