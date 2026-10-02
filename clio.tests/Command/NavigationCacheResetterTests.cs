using System;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class NavigationCacheResetterTests {
	private const string GetDataUrl = "https://example.invalid/rest/ConfigurationDataService/GetData";
	private IServiceUrlBuilder _serviceUrlBuilder = null!;
	private IApplicationClient _client = null!;
	private EnvironmentSettings _environmentSettings = null!;
	private NavigationCacheResetter _sut = null!;

	[SetUp]
	public void SetUp() {
		// Arrange
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_client = Substitute.For<IApplicationClient>();
		_environmentSettings = new EnvironmentSettings { Uri = "https://example.invalid", IsNetCore = true };
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetConfigurationData, _environmentSettings)
			.Returns(GetDataUrl);
		_sut = new NavigationCacheResetter(_serviceUrlBuilder);
	}

	[Test]
	[Description("Posts the bare JSON boolean true to ConfigurationDataService/GetData, so the server takes the forceGet branch that clears the session's navigation cache.")]
	public void TryReset_Should_Post_ForceGet_True_To_GetData_Route() {
		// Arrange
		_client.ExecutePostRequest(GetDataUrl, "true", Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success":true,"data":{"modulesStructure":{}}}""");

		// Act
		string? warning = _sut.TryReset(_client, _environmentSettings);

		// Assert
		warning.Should().BeNull(because: "a success:true answer means the session cache was cleared");
		_client.Received(1).ExecutePostRequest(GetDataUrl, "true", Arg.Is<int>(timeout => timeout > 0), 1, 0);
	}

	[Test]
	[Description("A success:false answer, which the service returns with HTTP 200 for any server exception, becomes a warning that carries errorInfo.message.")]
	public void TryReset_Should_Return_Warning_With_Server_Message_When_Success_Is_False() {
		// Arrange
		_client.ExecutePostRequest(GetDataUrl, "true", Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success":false,"errorInfo":{"message":"Access denied"}}""");

		// Act
		string? warning = _sut.TryReset(_client, _environmentSettings);

		// Assert
		warning.Should().Be(NavigationCacheResetter.WarningPrefix + "Access denied",
			because: "the caller must see that the reset failed and why");
	}

	[TestCase("", TestName = "TryReset_Should_Return_Warning_When_Response_Is_Empty")]
	[TestCase("<html>Login</html>", TestName = "TryReset_Should_Return_Warning_When_Response_Is_Not_Json")]
	[TestCase("""{"success":false}""", TestName = "TryReset_Should_Return_Warning_When_Failure_Has_No_Message")]
	[Description("Any answer that does not report success:true becomes a warning, because the client returns error bodies without an HTTP status.")]
	public void TryReset_Should_Return_Warning_When_Response_Does_Not_Report_Success(string response) {
		// Arrange
		_client.ExecutePostRequest(GetDataUrl, "true", Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(response);

		// Act
		string? warning = _sut.TryReset(_client, _environmentSettings);

		// Assert
		warning.Should().StartWith(NavigationCacheResetter.WarningPrefix,
			because: "an answer without success:true must not be read as a cleared cache");
	}

	[Test]
	[Description("A transport exception becomes a warning instead of propagating, so a failed reset cannot hide the change that was already made.")]
	public void TryReset_Should_Return_Warning_When_Request_Throws() {
		// Arrange
		_client.ExecutePostRequest(GetDataUrl, "true", Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Throws(new InvalidOperationException("connection reset"));

		// Act
		string? warning = _sut.TryReset(_client, _environmentSettings);

		// Assert
		warning.Should().Be(NavigationCacheResetter.WarningPrefix + "connection reset",
			because: "the transport failure must be reported as a warning with its message");
	}
}
