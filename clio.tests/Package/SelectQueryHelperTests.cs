using System;
using System.Text.Json.Serialization;
using System.Threading;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Package;

/// <summary>
/// Covers the transient-failure retry that <see cref="SelectQueryHelper.ExecuteSelectQuery{T}" /> applies to
/// server-reported failures (issue #1119).
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Package")]
public class SelectQueryHelperTests {

	#region Constants: Private

	private const string SelectUrl = "https://test.creatio.com/0/DataService/json/SyncReply/SelectQuery";

	private const string TransientFailureJson =
		"""{"success":false,"errorInfo":{"message":"System.InvalidOperationException: Collection was modified; enumeration operation may not execute."}}""";

	private const string SuccessJson = """{"success":true}""";

	#endregion

	#region Fields: Private

	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;

	#endregion

	#region Methods: Public

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns(SelectUrl);
	}

	[TearDown]
	public void TearDown() {
		_applicationClient.ClearReceivedCalls();
		_serviceUrlBuilder.ClearReceivedCalls();
	}

	[Test]
	[Description("Re-sends a SelectQuery whose HTTP-200 body reports a transient server failure, and returns the response of the send that succeeded.")]
	public void ExecuteSelectQuery_Should_Retry_Transient_Server_Failure() {
		// Arrange
		_applicationClient.ExecutePostRequest(
				SelectUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(TransientFailureJson, SuccessJson);

		// Act
		TestSelectResponse response = SelectQueryHelper.ExecuteSelectQuery<TestSelectResponse>(
			_applicationClient, _serviceUrlBuilder, new { rootSchemaName = "SysPackage" });

		// Assert
		response.Success.Should().BeTrue(
			because: "the second send answered with a success envelope, so that is the answer the caller gets");
		_applicationClient.Received(2).ExecutePostRequest(
			SelectUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Fails immediately, without re-sending, when the server reports a failure that a re-send cannot clear.")]
	public void ExecuteSelectQuery_Should_Not_Retry_NonTransient_Server_Failure() {
		// Arrange
		const string notFoundJson =
			"""{"success":false,"errorInfo":{"message":"Package 'UsrMissing' was not found."}}""";
		_applicationClient.ExecutePostRequest(
				SelectUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(notFoundJson);

		// Act
		Action act = () => SelectQueryHelper.ExecuteSelectQuery<TestSelectResponse>(
			_applicationClient, _serviceUrlBuilder, new { rootSchemaName = "SysPackage" });

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*was not found*",
				because: "a real answer must reach the caller unchanged instead of being retried and delayed");
		_applicationClient.Received(1).ExecutePostRequest(
			SelectUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Gives up after the transient-retry budget is spent and surfaces the server-reported failure text.")]
	public void ExecuteSelectQuery_Should_Throw_After_Transient_Retry_Budget_Is_Spent() {
		// Arrange
		_applicationClient.ExecutePostRequest(
				SelectUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(TransientFailureJson);

		// Act
		Action act = () => SelectQueryHelper.ExecuteSelectQuery<TestSelectResponse>(
			_applicationClient, _serviceUrlBuilder, new { rootSchemaName = "SysPackage" });

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*Collection was modified*",
				because: "a failure that outlives the retry budget must still name what the server reported");
		_applicationClient.Received(3).ExecutePostRequest(
			SelectUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Sends a bounded SelectQuery exactly once even when the server reports a transient failure, so a caller that budgeted the call keeps the bound it stated.")]
	public void ExecuteSelectQuery_Should_Not_Retry_When_Caller_Bounded_The_Call() {
		// Arrange
		const int boundedTimeoutMs = 30_000;
		_applicationClient.ExecutePostRequest(
				SelectUrl, Arg.Any<string>(), boundedTimeoutMs, Arg.Any<int>(), Arg.Any<int>())
			.Returns(TransientFailureJson);

		// Act
		Action act = () => SelectQueryHelper.ExecuteSelectQuery<TestSelectResponse>(
			_applicationClient, _serviceUrlBuilder, new { rootSchemaName = "SysPackage" }, boundedTimeoutMs);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "the failure must reach the caller instead of being retried past the budget it set");
		_applicationClient.Received(1).ExecutePostRequest(
			SelectUrl, Arg.Any<string>(), boundedTimeoutMs, Arg.Any<int>(), Arg.Any<int>());
	}

	#endregion

	#region Class: TestSelectResponse

	private sealed class TestSelectResponse : SelectQueryHelper.SelectQueryResponseBaseDto {

		[JsonPropertyName("rows")]
		public object[] Rows { get; init; } = [];

	}

	#endregion

}
