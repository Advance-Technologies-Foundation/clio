using System;
using Clio.Common;
using Clio.Utilities;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Utilities;

[TestFixture]
public class WebBrowserTests
{
	#region Fields: Private

	private IProcessExecutor _processExecutorMock;
	private IOSPlatformChecker _platformCheckerMock;
	private IWebBrowser _sut;

	#endregion

	#region Methods: Private

	[SetUp]
	public void Setup() {
		_processExecutorMock = Substitute.For<IProcessExecutor>();
		_platformCheckerMock = Substitute.For<IOSPlatformChecker>();
		_sut = new WebBrowser(_processExecutorMock, _platformCheckerMock);
	}

	#endregion

	#region Tests: Enabled Property

	[Test]
	[Category("Unit")]
	[Description("Verifies that Enabled property returns true when platform checker indicates Windows")]
	public void Enabled_ShouldReturn_True_WhenPlatformIsWindows() {
		// Arrange
		_platformCheckerMock.IsWindowsEnvironment.Returns(true);

		// Act
		bool result = _sut.Enabled;

		// Assert
		result.Should().BeTrue(because: "the Enabled property should return true when running on Windows");
	}

	[Test]
	[Category("Unit")]
	[Description("Verifies that Enabled property returns false when platform checker indicates non-Windows")]
	public void Enabled_ShouldReturn_False_WhenPlatformIsNotWindows() {
		// Arrange
		_platformCheckerMock.IsWindowsEnvironment.Returns(false);

		// Act
		bool result = _sut.Enabled;

		// Assert
		result.Should().BeFalse(because: "the Enabled property should return false when not running on Windows");
	}

	#endregion

	#region Tests: CheckUrl Method

	[Test]
	[Category("Unit")]
	[Description("Verifies that CheckUrl returns false for an invalid URL")]
	public void CheckUrl_ShouldReturn_False_WhenUrlIsInvalid() {
		// Arrange
		const string invalidUrl = "not-a-valid-url";

		// Act
		bool result = _sut.CheckUrl(invalidUrl);

		// Assert
		result.Should().BeFalse(because: "an invalid URL should not pass the check");
	}

	[Test]
	[Category("Unit")]
	[Description("Verifies that CheckUrl returns false for a non-existent URL")]
	public void CheckUrl_ShouldReturn_False_WhenUrlDoesNotExist() {
		// Arrange
		const string nonExistentUrl = "http://this-domain-definitely-does-not-exist-12345.com";

		// Act
		bool result = _sut.CheckUrl(nonExistentUrl);

		// Assert
		result.Should().BeFalse(because: "a non-existent URL should not be reachable");
	}

	[Test]
	[Category("Unit")]
	[Description("Verifies that CheckUrl returns false for a URL that returns an error status code")]
	public void CheckUrl_ShouldReturn_False_WhenUrlReturnsErrorStatusCode() {
		// Arrange
		const string urlWithError = "https://httpstat.us/404";

		// Act
		bool result = _sut.CheckUrl(urlWithError);

		// Assert
		result.Should().BeFalse(because: "a URL that returns a 404 status code should not pass the check");
	}

	[Test]
	[Category("Unit")]
	[Description("Verifies that CheckUrl handles URLs with redirects correctly")]
	public void CheckUrl_ShouldReturn_False_WhenUrlRedirects() {
		// Arrange
		const string redirectUrl = "http://httpstat.us/301";

		// Act
		bool result = _sut.CheckUrl(redirectUrl);

		// Assert
		result.Should()
			  .BeFalse(
				  because: "the HttpClient is configured with AllowAutoRedirect=false, so redirects should fail the URI comparison check");
	}

	[Test]
	[Category("Unit")]
	[Description("Verifies that CheckUrl handles malformed URI schemes gracefully")]
	public void CheckUrl_ShouldReturn_False_WhenUrlHasMalformedScheme() {
		// Arrange
		const string malformedUrl = "ht!tp://invalid";

		// Act
		bool result = _sut.CheckUrl(malformedUrl);

		// Assert
		result.Should().BeFalse(because: "a malformed URL scheme should not pass the check");
	}

	[Test]
	[Category("Unit")]
	[Description("Verifies that CheckUrl handles empty strings gracefully")]
	public void CheckUrl_ShouldReturn_False_WhenUrlIsEmpty() {
		// Arrange
		const string emptyUrl = "";

		// Act
		bool result = _sut.CheckUrl(emptyUrl);

		// Assert
		result.Should().BeFalse(because: "an empty URL string should not pass the check");
	}

	[Test]
	[Category("Unit")]
	[Description("Verifies that CheckUrl handles null strings gracefully")]
	public void CheckUrl_ShouldReturn_False_WhenUrlIsNull() {
		// Arrange
		string nullUrl = null;

		// Act
		bool result = _sut.CheckUrl(nullUrl);

		// Assert
		result.Should().BeFalse(because: "a null URL should be handled gracefully and return false");
	}

	[Test]
	[Category("Integration")]
	[Description("Verifies that CheckUrl returns true for a valid reachable URL")]
	[Explicit("Requires network connectivity")]
	public void CheckUrl_ShouldReturn_True_WhenUrlIsValidAndReachable() {
		// Arrange
		const string validUrl = "https://www.google.com";

		// Act
		bool result = _sut.CheckUrl(validUrl);

		// Assert
		result.Should().BeTrue(because: "a valid and reachable URL should pass the check");
	}

	#endregion

	#region Tests: OpenUrl Method

	[Test]
	[Category("Unit")]
	[Description("Verifies that OpenUrl executes process on Windows platform")]
	public void OpenUrl_Should_ExecuteProcess_OnWindowsPlatform() {
		// Arrange
		const string url = "https://www.example.com";
		_platformCheckerMock.IsWindowsEnvironment.Returns(true);

		// Act
		_sut.OpenUrl(url);

		// Assert
		_processExecutorMock.Received(1).Execute(
			"cmd",
			"/c start https://www.example.com",
			false,
			null,
			false,
			Arg.Any<bool>());
	}

	[Test]
	[Category("Unit")]
	[Description("Verifies that OpenUrl handles special characters in URLs on Windows")]
	public void OpenUrl_Should_ExecuteProcess_WithSpecialCharactersInUrl() {
		// Arrange
		const string urlWithSpecialChars = "https://www.example.com/search?q=test%20query";
		_platformCheckerMock.IsWindowsEnvironment.Returns(true);

		// Act
		_sut.OpenUrl(urlWithSpecialChars);

		// Assert
		_processExecutorMock.Received(1).Execute(
			"cmd",
			$"/c start {urlWithSpecialChars}",
			false,
			null,
			false,
			Arg.Any<bool>());
	}

	[Test]
	[Category("Unit")]
	[Description("Verifies that OpenUrl throws NotFiniteNumberException on non-Windows platforms")]
	public void OpenUrl_ShouldThrow_NotFiniteNumberException_OnNonWindowsPlatform() {
		// Arrange
		const string url = "https://www.example.com";
		_platformCheckerMock.IsWindowsEnvironment.Returns(false);

		// Act
		Action act = () => _sut.OpenUrl(url);

		// Assert
		act.Should().Throw<NotFiniteNumberException>(because: "OpenUrl should throw on non-Windows platforms")
		   .WithMessage("*not supported for current platform*");
		_processExecutorMock.DidNotReceiveWithAnyArgs().Execute(default, default, default, default, default, default);
	}

	#endregion
}


