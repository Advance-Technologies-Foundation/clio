namespace Clio.Tests.Command;

using System;
using System.Net.Http;
using Clio.Command.EntityConnections;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class EntityConnectionsReaderTests {
	private const string ConnectionsUrl = "http://test/rest/EntityConnectionsService/GetConnections";
	private const string SelectQueryUrl = "http://test/DataService/json/SyncReply/SelectQuery";
	private static readonly Guid EntityUId = Guid.Parse("bb000000-0000-0000-0000-000000000002");
	private static readonly Guid A = Guid.Parse("cc000000-0000-0000-0000-000000000001");
	private static readonly Guid B = Guid.Parse("cc000000-0000-0000-0000-000000000002");

	private IApplicationClient _applicationClient = null!;
	private EntityConnectionsReader _reader = null!;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetEntityConnections).Returns(ConnectionsUrl);
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns(SelectQueryUrl);
		_reader = new EntityConnectionsReader(_applicationClient, serviceUrlBuilder, Substitute.For<ILogger>());
	}

	private void StubTable(string body) =>
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Returns(body);

	[Test]
	[Description("Reads the ordered connections from EntityConnectionsService for the requested object and drops duplicates.")]
	public void Read_ShouldReturnServiceOrder_WhenServiceAnswers() {
		// Arrange
		_applicationClient.ExecutePostRequest(ConnectionsUrl, Arg.Any<string>()).Returns(
			$$"""{"success":true,"connections":[{"columnUId":"{{B}}","position":0},{"columnUId":"{{A}}","position":1},{"columnUId":"{{B}}","position":2}]}""");

		// Act
		EntityConnectionsSnapshot snapshot = _reader.Read(EntityUId);

		// Assert
		snapshot.ColumnUIds.Should().Equal([B, A], because: "the server order is kept and a repeated column is listed once");
		snapshot.IsTableFallback.Should().BeFalse(because: "the service answered");
		snapshot.Warning.Should().BeNull(because: "nothing went wrong");
		_applicationClient.Received(1).ExecutePostRequest(ConnectionsUrl,
			Arg.Is<string>(body => body.Contains(EntityUId.ToString("D"))));
		_applicationClient.DidNotReceive().ExecutePostRequest(SelectQueryUrl, Arg.Any<string>());
	}

	[TestCase("<html>404</html>")]
	[TestCase("""{"success":false,"errorInfo":{"message":"boom"}}""")]
	[TestCase("")]
	[Description("Falls back to the EntityConnection rows ordered by Position when the service is missing or fails, and warns that the add-on is not reflected.")]
	public void Read_ShouldFallBackToTable_WhenServiceIsUnavailable(string serviceBody) {
		// Arrange
		_applicationClient.ExecutePostRequest(ConnectionsUrl, Arg.Any<string>()).Returns(serviceBody);
		StubTable($$"""{"success":true,"rows":[{"ColumnUId":"{{A}}","Position":0},{"ColumnUId":"{{B}}","Position":1}]}""");

		// Act
		EntityConnectionsSnapshot snapshot = _reader.Read(EntityUId);

		// Assert
		snapshot.ColumnUIds.Should().Equal([A, B], because: "the table rows are returned in their position order");
		snapshot.IsTableFallback.Should().BeTrue(because: "the list did not come from the service");
		snapshot.Warning.Should().Contain("NOT reflected", because: "a caller must learn that the add-on is not applied");
		_applicationClient.Received(1).ExecutePostRequest(SelectQueryUrl, Arg.Is<string>(body =>
			body.Contains("\"EntityConnection\"") && body.Contains("SysEntitySchemaUId")
			&& body.Contains(EntityUId.ToString("D")) && body.Contains("orderDirection")));
	}

	[Test]
	[Description("Falls back to the table when the service call fails in transport.")]
	public void Read_ShouldFallBackToTable_WhenServiceCallThrows() {
		// Arrange
		_applicationClient.ExecutePostRequest(ConnectionsUrl, Arg.Any<string>())
			.Returns(_ => throw new HttpRequestException("connection reset"));
		StubTable($$"""{"success":true,"rows":[{"ColumnUId":"{{A}}","Position":0}]}""");

		// Act
		EntityConnectionsSnapshot snapshot = _reader.Read(EntityUId);

		// Assert
		snapshot.ColumnUIds.Should().Equal([A], because: "the table rows are used when the service cannot be reached");
		snapshot.Warning.Should().Contain("connection reset", because: "the warning says why the service was skipped");
	}

	[Test]
	[Description("Fails with both causes when the service and the table read both fail.")]
	public void Read_ShouldThrow_WhenServiceAndTableBothFail() {
		// Arrange
		_applicationClient.ExecutePostRequest(ConnectionsUrl, Arg.Any<string>()).Returns("<html/>");
		StubTable("""{"success":false}""");

		// Act
		Action act = () => _reader.Read(EntityUId);

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*EntityConnectionsService*EntityConnection table*",
			because: "an empty list must never stand in for a read that failed twice");
	}
}
