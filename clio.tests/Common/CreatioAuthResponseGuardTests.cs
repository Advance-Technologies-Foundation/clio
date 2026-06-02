using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Property("Module", "Common")]
[Category("Unit")]
public class CreatioAuthResponseGuardTests {

	[Test]
	[Description("A valid JSON object response is treated as a real service response, never an auth redirect.")]
	public void IsLikelyAuthRedirect_JsonObject_ReturnsFalse() {
		CreatioAuthResponseGuard.IsLikelyAuthRedirect("{\"success\":true,\"schema\":{}}")
			.Should().BeFalse("because a JSON body is a real service response");
	}

	[Test]
	[Description("A valid JSON array response is treated as a real service response.")]
	public void IsLikelyAuthRedirect_JsonArray_ReturnsFalse() {
		CreatioAuthResponseGuard.IsLikelyAuthRedirect("[{\"id\":1}]")
			.Should().BeFalse("because a JSON array is a real service response");
	}

	[Test]
	[Description("Leading whitespace before JSON does not trip the non-JSON heuristic.")]
	public void IsLikelyAuthRedirect_JsonWithLeadingWhitespace_ReturnsFalse() {
		CreatioAuthResponseGuard.IsLikelyAuthRedirect("\r\n\t  {\"ok\":1}")
			.Should().BeFalse("because the first non-whitespace character is '{'");
	}

	[Test]
	[Description("The empirically captured NuiLogin.aspx body is detected as an auth redirect.")]
	public void IsLikelyAuthRedirect_NuiLoginPage_ReturnsTrue() {
		// Shape captured from a real .NET Framework target: XHTML starting with <!DOCTYPE,
		// <title>Creatio</title>, and a NuiLogin reference.
		string loginHtml =
			"\n\n<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Transitional//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd\">\n" +
			"<html xmlns=\"http://www.w3.org/1999/xhtml\" dir=\"ltr\" culture=\"en-US\">\n" +
			"<head><title>\n\tCreatio\n</title>" +
			"<script src=\"../Login/NuiLogin.aspx?ReturnUrl=%2f\"></script></head><body></body></html>";
		CreatioAuthResponseGuard.IsLikelyAuthRedirect(loginHtml)
			.Should().BeTrue("because the body is the NuiLogin login page, not a service response");
	}

	[Test]
	[Description("A generic IIS / 5xx HTML error page is NOT treated as an auth redirect so the original error surfaces.")]
	public void IsLikelyAuthRedirect_GenericHtmlError_ReturnsFalse() {
		string iisError =
			"<!DOCTYPE html><html><head><title>500 - Internal Server Error</title></head>" +
			"<body><h1>Server Error</h1></body></html>";
		CreatioAuthResponseGuard.IsLikelyAuthRedirect(iisError)
			.Should().BeFalse("because relogin cannot fix a 500 and the real error must surface");
	}

	[TestCase(null)]
	[TestCase("")]
	[TestCase("   \r\n ")]
	[Description("Null, empty, or whitespace responses are not auth redirects (e.g. an empty ResetScriptCache reply).")]
	public void IsLikelyAuthRedirect_NullOrWhitespace_ReturnsFalse(string response) {
		CreatioAuthResponseGuard.IsLikelyAuthRedirect(response)
			.Should().BeFalse("because there is no login marker to act on");
	}

	[Test]
	[Description("The JSON 401 auth-failure body returned by ServiceModel endpoints is detected even though it is valid JSON.")]
	public void IsLikelyAuthRedirect_Json401AuthenticationFailed_ReturnsTrue() {
		// Exact body captured from a real .NET Framework target for an expired/invalid session.
		string body = "{\"Message\":\"Authentication failed.\",\"StackTrace\":null,\"ExceptionType\":\"System.InvalidOperationException\"}";
		CreatioAuthResponseGuard.IsLikelyAuthRedirect(body)
			.Should().BeTrue("because a 401 'Authentication failed' payload means the session must be renewed");
	}

	[Test]
	[Description("A genuine JSON service error (not an auth failure) is left alone so the original error surfaces.")]
	public void IsLikelyAuthRedirect_JsonServiceError_ReturnsFalse() {
		string body = "{\"success\":false,\"errorInfo\":{\"message\":\"Schema with UId not found\"}}";
		CreatioAuthResponseGuard.IsLikelyAuthRedirect(body)
			.Should().BeFalse("because re-login must not be triggered by ordinary service errors");
	}

	[Test]
	[Description("A valid JSON body that merely mentions 'Authentication failed' in a non-Message field is NOT an auth failure (structural match prevents re-running an executed write).")]
	public void IsLikelyAuthRedirect_JsonMentioningAuthFailedInOtherField_ReturnsFalse() {
		string body = "{\"description\":\"Authentication failed for impersonated user\",\"success\":false}";
		CreatioAuthResponseGuard.IsLikelyAuthRedirect(body)
			.Should().BeFalse("because only a top-level Message equal to the 401 value is an auth failure");
	}

	[Test]
	[Description("Exact-match discriminator: a fault envelope whose top-level Message merely STARTS WITH 'Authentication failed' but is not equal to it is a real error, not the 401 envelope. This is the case a substring match would wrongly retry — a write duplication.")]
	public void IsLikelyAuthRedirect_FaultEnvelopeWithDifferentMessage_ReturnsFalse() {
		string body = "{\"Message\":\"Authentication failed for the linked account.\",\"StackTrace\":null,\"ExceptionType\":\"System.InvalidOperationException\"}";
		CreatioAuthResponseGuard.IsLikelyAuthRedirect(body)
			.Should().BeFalse("because the top-level Message must EQUAL 'Authentication failed.', not just contain the phrase");
	}

	[Test]
	[Description("Regression guard pinning the Creatio login-failure signatures the detector depends on. If a Creatio upgrade or localization renames these tokens, this test fails as a prompt to re-capture against the live login contract.")]
	public void IsLikelyAuthRedirect_PinsKnownCreatioLoginContract() {
		CreatioAuthResponseGuard.IsLikelyAuthRedirect(
				"{\"Message\":\"Authentication failed.\",\"StackTrace\":null,\"ExceptionType\":\"System.InvalidOperationException\"}")
			.Should().BeTrue("the JSON-401 'Authentication failed.' envelope is a pinned login-failure signature");
		CreatioAuthResponseGuard.IsLikelyAuthRedirect(
				"<!DOCTYPE html><html><head><title>Creatio</title></head><body>NuiLogin</body></html>")
			.Should().BeTrue("the NuiLogin login page is a pinned login-failure signature");
		CreatioAuthResponseGuard.IsLikelyAuthRedirect(
				"<!DOCTYPE html><html><a href=\"/Login/NuiLogin.aspx\">login</a></html>")
			.Should().BeTrue("the /Login/ path marker is a pinned login-failure signature");
	}
}
