using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
public sealed class ODataPatchClientTests {

	private const string PatchUrl = "http://creatio/0/odata/Contact(8ecab4a1-0ca3-4515-9399-efe0a19390bd)";

	private static EnvironmentSettings OAuthSettings() => new() {
		Uri = "http://creatio",
		AuthAppUri = "http://auth",
		ClientId = "client-id",
		ClientSecret = "client-secret"
	};

	private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
		new(status) { Content = new StringContent(body) };

	[Test]
	[Category("Unit")]
	[Description("A blank PATCH url is rejected before any authentication or network call.")]
	public void ExecutePatch_Should_Reject_Blank_Url() {
		StubHandler handler = new(_ => throw new InvalidOperationException("no request expected"));
		using ODataPatchClient client = new(OAuthSettings(), handler);

		Action act = () => client.ExecutePatch(" ", "{}");

		act.Should().Throw<ArgumentException>();
		handler.Requests.Should().BeEmpty();
	}

	[Test]
	[Category("Unit")]
	[Description("OAuth mode authenticates once, then sends the PATCH with a Bearer token and returns the body.")]
	public void ExecutePatch_Should_Authenticate_OAuth_And_Send_Bearer() {
		string? sentAuthScheme = null;
		string? sentAuthValue = null;
		StubHandler handler = new(request => {
			if (request.RequestUri!.AbsoluteUri.Contains("connect/token")) {
				return Json(HttpStatusCode.OK, "{\"access_token\":\"tok-123\"}");
			}
			sentAuthScheme = request.Headers.Authorization?.Scheme;
			sentAuthValue = request.Headers.Authorization?.Parameter;
			return Json(HttpStatusCode.OK, "{\"ok\":true}");
		});
		using ODataPatchClient client = new(OAuthSettings(), handler);

		string body = client.ExecutePatch(PatchUrl, "{\"Name\":\"New\"}");

		body.Should().Be("{\"ok\":true}");
		sentAuthScheme.Should().Be("Bearer");
		sentAuthValue.Should().Be("tok-123");
	}

	[Test]
	[Category("Unit")]
	[Description("A failed OAuth token request surfaces as an InvalidOperationException without exposing the body.")]
	public void ExecutePatch_Should_Throw_When_OAuth_Token_Request_Fails() {
		StubHandler handler = new(request =>
			request.RequestUri!.AbsoluteUri.Contains("connect/token")
				? Json(HttpStatusCode.Unauthorized, "{\"secret\":\"should-not-leak\"}")
				: Json(HttpStatusCode.OK, "{}"));
		using ODataPatchClient client = new(OAuthSettings(), handler);

		Action act = () => client.ExecutePatch(PatchUrl, "{}");

		act.Should().Throw<InvalidOperationException>()
			.Which.Message.Should().Contain("OAuth token request failed").And.NotContain("should-not-leak");
	}

	[Test]
	[Category("Unit")]
	[Description("A token response with no access_token is rejected.")]
	public void ExecutePatch_Should_Throw_When_Token_Missing_AccessToken() {
		StubHandler handler = new(request =>
			request.RequestUri!.AbsoluteUri.Contains("connect/token")
				? Json(HttpStatusCode.OK, "{\"token_type\":\"Bearer\"}")
				: Json(HttpStatusCode.OK, "{}"));
		using ODataPatchClient client = new(OAuthSettings(), handler);

		Action act = () => client.ExecutePatch(PatchUrl, "{}");

		act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("access_token");
	}

	[Test]
	[Category("Unit")]
	[Description("A 401 on the first PATCH triggers exactly one re-authentication and one retry, then succeeds.")]
	public void ExecutePatch_Should_ReAuthenticate_Once_On_401_And_Retry() {
		int tokenCalls = 0;
		int patchCalls = 0;
		StubHandler handler = new(request => {
			if (request.RequestUri!.AbsoluteUri.Contains("connect/token")) {
				tokenCalls++;
				return Json(HttpStatusCode.OK, "{\"access_token\":\"tok\"}");
			}
			patchCalls++;
			return patchCalls == 1
				? Json(HttpStatusCode.Unauthorized, "expired")
				: Json(HttpStatusCode.NoContent, string.Empty);
		});
		using ODataPatchClient client = new(OAuthSettings(), handler);

		client.ExecutePatch(PatchUrl, "{\"Name\":\"x\"}");

		patchCalls.Should().Be(2, "the first 401 should be retried exactly once");
		tokenCalls.Should().Be(2, "re-authentication should refresh the token before the retry");
	}

	[Test]
	[Category("Unit")]
	[Description("A persistent 401 surfaces as a failure after the single retry, with no infinite loop.")]
	public void ExecutePatch_Should_Throw_When_401_Persists_After_Retry() {
		int patchCalls = 0;
		StubHandler handler = new(request => {
			if (request.RequestUri!.AbsoluteUri.Contains("connect/token")) {
				return Json(HttpStatusCode.OK, "{\"access_token\":\"tok\"}");
			}
			patchCalls++;
			return Json(HttpStatusCode.Unauthorized, "denied");
		});
		using ODataPatchClient client = new(OAuthSettings(), handler);

		Action act = () => client.ExecutePatch(PatchUrl, "{}");

		act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("401");
		patchCalls.Should().Be(2, "exactly one retry, not a loop");
	}

	[Test]
	[Category("Unit")]
	[Description("A non-success PATCH status is surfaced as a failure that includes the status code.")]
	public void ExecutePatch_Should_Throw_On_NonSuccess_Patch_Status() {
		StubHandler handler = new(request =>
			request.RequestUri!.AbsoluteUri.Contains("connect/token")
				? Json(HttpStatusCode.OK, "{\"access_token\":\"tok\"}")
				: Json(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"bad column\"}}"));
		using ODataPatchClient client = new(OAuthSettings(), handler);

		Action act = () => client.ExecutePatch(PatchUrl, "{}");

		act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("400");
	}

	private sealed class StubHandler : HttpMessageHandler {
		private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
		public List<string> Requests { get; } = [];

		public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

		protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) {
			Requests.Add($"{request.Method} {request.RequestUri}");
			return _responder(request);
		}

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
			Requests.Add($"{request.Method} {request.RequestUri}");
			return Task.FromResult(_responder(request));
		}
	}
}
