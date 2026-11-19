using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class StatusPreservingClientTests {

	private const string LoginPageBody =
		"<!DOCTYPE html><html><head><title>Login</title></head>" +
		"<body><form action=\"/0/Login/NuiLogin.aspx\"><input/></form></body></html>";

	private static Task<HttpResponseMessage> Answer(HttpStatusCode status, string body) =>
		Task.FromResult(new HttpResponseMessage(status) {
			Content = new StringContent(body, Encoding.UTF8, "application/json")
		});

	#region Tests: CreatioClientAdapter.SendKeepingStatus

	[Test]
	[Description("A POST keeps the status and the body, and passes every argument to the transport")]
	public void SendKeepingStatus_ShouldReturnStatusAndBody_WhenThePostIsAnswered() {
		// Arrange
		ICreatioClientTransport transport = Substitute.For<ICreatioClientTransport>();
		transport.ExecutePostRequestAsync("/x", "body", 7_000, 3, 5, Arg.Any<CancellationToken>())
			.Returns(_ => Answer(HttpStatusCode.InternalServerError, "{\"success\":false}"));
		CreatioClientAdapter adapter = new(transport, reauthExecutor: null);

		// Act
		SiteHttpResponse answer = adapter.SendKeepingStatus(SiteRequestVerb.Post, "/x", "body", 7_000, 3, 5);

		// Assert
		answer.Should().BeEquivalentTo(new SiteHttpResponse(500, "{\"success\":false}"),
			because: "the status is what the string method drops and this method exists to keep");
		transport.DidNotReceive().ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
			Arg.Any<int>(), Arg.Any<int>());
	}

	[TestCase(SiteRequestVerb.Patch, TestName = "SendKeepingStatus_ShouldSendAPatch_WhenTheVerbIsPatch")]
	[TestCase(SiteRequestVerb.Delete, TestName = "SendKeepingStatus_ShouldSendADelete_WhenTheVerbIsDelete")]
	[Description("PATCH and DELETE go to their own transport method")]
	public void SendKeepingStatus_ShouldUseTheVerbsTransportMethod(SiteRequestVerb verb) {
		// Arrange
		ICreatioClientTransport transport = Substitute.For<ICreatioClientTransport>();
		transport.ExecutePatchRequestAsync(default, default, default, default, default, default)
			.ReturnsForAnyArgs(_ => Answer(HttpStatusCode.BadRequest, "patch"));
		transport.ExecuteDeleteRequestAsync(default, default, default, default, default, default)
			.ReturnsForAnyArgs(_ => Answer(HttpStatusCode.Conflict, "delete"));
		CreatioClientAdapter adapter = new(transport, reauthExecutor: null);

		// Act
		SiteHttpResponse answer = adapter.SendKeepingStatus(verb, "/x", "body", replayAllowed: false);

		// Assert
		answer.Should().BeEquivalentTo(verb == SiteRequestVerb.Patch
			? new SiteHttpResponse(400, "patch")
			: new SiteHttpResponse(409, "delete"));
		transport.DidNotReceiveWithAnyArgs().ExecutePostRequestAsync(default, default, default, default, default,
			default);
	}

	[Test]
	[Description("A replayable POST answered with the sign-in page signs in again and is sent once more")]
	public void SendKeepingStatus_ShouldReplay_WhenAReplayablePostIsAnsweredWithTheSignInPage() {
		// Arrange
		ICreatioClientTransport transport = Substitute.For<ICreatioClientTransport>();
		transport.ExecutePostRequestAsync(default, default, default, default, default, default)
			.ReturnsForAnyArgs(_ => Answer(HttpStatusCode.OK, LoginPageBody), _ => Answer(HttpStatusCode.OK, "{}"));
		CreatioClientAdapter adapter = new(transport, reauthExecutor: null);

		// Act
		SiteHttpResponse answer = adapter.SendKeepingStatus(SiteRequestVerb.Post, "/x", "body");

		// Assert
		transport.Received(1).Login();
		transport.ReceivedWithAnyArgs(2).ExecutePostRequestAsync(default, default, default, default, default, default);
		answer.Body.Should().Be("{}", because: "the session recovery of the string POST is kept");
		answer.Resent.Should().BeTrue(because: "the first send was judged by its body, so it may have been applied");
	}

	[Test]
	[Description("A 405 to the GET creatio.client sent after following a redirect of the write proves nothing about the write")]
	public void SendKeepingStatus_ShouldMarkARedirect_WhenTheAnswerIsToAnotherRequest() {
		// Arrange
		ICreatioClientTransport transport = Substitute.For<ICreatioClientTransport>();
		transport.ExecutePostRequestAsync(default, default, default, default, default, default)
			.ReturnsForAnyArgs(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.MethodNotAllowed) {
				Content = new StringContent(""),
				RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://site/0/Shell/")
			}));
		CreatioClientAdapter adapter = new(transport, reauthExecutor: null);

		// Act
		SiteHttpResponse answer = adapter.SendKeepingStatus(SiteRequestVerb.Post, "http://site/0/x", "body");

		// Assert
		answer.StatusCode.Should().Be(405);
		answer.Redirected.Should().BeTrue();
		answer.IsRefusedWithoutProcessing.Should().BeFalse(
			because: "the write may have been applied before it was answered with the redirect the client followed");
	}

	[Test]
	[Description("A DELETE without a body is always read as possibly redirected: the client can repeat it through a 307 or 308 to the same address unseen")]
	public void SendKeepingStatus_ShouldMarkABodylessDeleteAsPossiblyRedirected() {
		// Arrange
		ICreatioClientTransport transport = Substitute.For<ICreatioClientTransport>();
		transport.ExecuteDeleteRequestAsync(default, default, default, default, default, default)
			.ReturnsForAnyArgs(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.MethodNotAllowed) {
				Content = new StringContent(""),
				RequestMessage = new HttpRequestMessage(HttpMethod.Delete, "http://site/0/odata/Contact(1)")
			}));
		CreatioClientAdapter adapter = new(transport, reauthExecutor: null);

		// Act
		SiteHttpResponse answer = adapter.SendKeepingStatus(SiteRequestVerb.Delete, "http://site/0/odata/Contact(1)",
			string.Empty, replayAllowed: false);

		// Assert
		answer.Redirected.Should().BeTrue();
		answer.IsRefusedWithoutProcessing.Should().BeFalse();
	}

	[TestCase(SiteRequestVerb.Delete, "{\"id\":1}", TestName = "SendKeepingStatus_ShouldNotMarkARedirect_WhenADeleteCarriesABody")]
	[TestCase(SiteRequestVerb.Post, "", TestName = "SendKeepingStatus_ShouldNotMarkARedirect_WhenAnEmptyPostIsAnswered")]
	[Description("Only a DELETE without a body counts as possibly redirected: anything sent with content cannot be repeated with its method kept")]
	public void SendKeepingStatus_ShouldNotMarkARedirect_WhenTheRequestIsNotABodylessDelete(SiteRequestVerb verb, string data) {
		// Arrange
		ICreatioClientTransport transport = Substitute.For<ICreatioClientTransport>();
		HttpMethod method = verb == SiteRequestVerb.Delete ? HttpMethod.Delete : HttpMethod.Post;
		Task<HttpResponseMessage> Respond() => Task.FromResult(new HttpResponseMessage(HttpStatusCode.MethodNotAllowed) {
			Content = new StringContent(""),
			RequestMessage = new HttpRequestMessage(method, "http://site/0/x")
		});
		transport.ExecuteDeleteRequestAsync(default, default, default, default, default, default)
			.ReturnsForAnyArgs(_ => Respond());
		transport.ExecutePostRequestAsync(default, default, default, default, default, default)
			.ReturnsForAnyArgs(_ => Respond());
		CreatioClientAdapter adapter = new(transport, reauthExecutor: null);

		// Act
		SiteHttpResponse answer = adapter.SendKeepingStatus(verb, "http://site/0/x", data, replayAllowed: false);

		// Assert
		answer.Redirected.Should().BeFalse();
		answer.IsRefusedWithoutProcessing.Should().BeTrue();
	}

	[Test]
	[Description("A 405 to the write itself is a refusal without processing")]
	public void SendKeepingStatus_ShouldNotMarkARedirect_WhenTheAnswerIsToTheWrite() {
		// Arrange
		ICreatioClientTransport transport = Substitute.For<ICreatioClientTransport>();
		transport.ExecutePostRequestAsync(default, default, default, default, default, default)
			.ReturnsForAnyArgs(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.MethodNotAllowed) {
				Content = new StringContent(""),
				RequestMessage = new HttpRequestMessage(HttpMethod.Post, "http://site/0/x")
			}));
		CreatioClientAdapter adapter = new(transport, reauthExecutor: null);

		// Act
		SiteHttpResponse answer = adapter.SendKeepingStatus(SiteRequestVerb.Post, "http://site/0/x", "body");

		// Assert
		answer.Redirected.Should().BeFalse();
		answer.IsRefusedWithoutProcessing.Should().BeTrue();
	}

	[Test]
	[Description("A 413 page that follows clio's own re-send proves nothing about the first send")]
	public void SendKeepingStatus_ShouldNotProveARefusal_WhenItFollowsAReSend() {
		// Arrange
		ICreatioClientTransport transport = Substitute.For<ICreatioClientTransport>();
		transport.ExecutePostRequestAsync(default, default, default, default, default, default)
			.ReturnsForAnyArgs(_ => Answer(HttpStatusCode.OK, LoginPageBody),
				_ => Answer(HttpStatusCode.RequestEntityTooLarge, "<html>Request Entity Too Large</html>"));
		CreatioClientAdapter adapter = new(transport, reauthExecutor: null);

		// Act
		SiteHttpResponse answer = adapter.SendKeepingStatus(SiteRequestVerb.Post, "/x", "body");

		// Assert
		answer.StatusCode.Should().Be(413);
		answer.IsRefusedWithoutProcessing.Should().BeFalse(
			because: "GitHub #1313: a committed write can answer with a body that looks like the sign-in page");
	}

	[Test]
	[Description("A non-replayable write answered with the sign-in page signs in again but is never re-sent")]
	public void SendKeepingStatus_ShouldNotReplay_WhenReplayIsNotAllowed() {
		// Arrange
		ICreatioClientTransport transport = Substitute.For<ICreatioClientTransport>();
		transport.ExecutePatchRequestAsync(default, default, default, default, default, default)
			.ReturnsForAnyArgs(_ => Answer(HttpStatusCode.OK, LoginPageBody));
		CreatioClientAdapter adapter = new(transport, reauthExecutor: null);

		// Act
		SiteHttpResponse answer = adapter.SendKeepingStatus(SiteRequestVerb.Patch, "/x", "body", replayAllowed: false);

		// Assert
		transport.ReceivedWithAnyArgs(1).ExecutePatchRequestAsync(default, default, default, default, default,
			default);
		answer.Body.Should().Be(LoginPageBody, because: "a write is never committed twice by a replay");
		answer.Resent.Should().BeFalse();
	}

	[Test]
	[Description("A transport fault surfaces wrapped in AggregateException, as from the string methods")]
	public void SendKeepingStatus_ShouldThrowAggregateException_WhenTheTransportFaults() {
		// Arrange
		ICreatioClientTransport transport = Substitute.For<ICreatioClientTransport>();
		transport.ExecutePostRequestAsync(default, default, default, default, default, default)
			.ReturnsForAnyArgs(Task.FromException<HttpResponseMessage>(new HttpRequestException("refused")));
		CreatioClientAdapter adapter = new(transport, reauthExecutor: null);

		// Act
		Action act = () => adapter.SendKeepingStatus(SiteRequestVerb.Post, "/x", "body");

		// Assert
		act.Should().Throw<AggregateException>()
			.WithInnerException<HttpRequestException>()
			.WithMessage("refused");
	}

	#endregion

	#region Tests: SiteCall fallback

	[Test]
	[Description("A client that does not keep the status is called through its string POST, and the answer has no status")]
	public void Post_ShouldFallBackToTheStringMethod_WhenTheClientDoesNotKeepTheStatus() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest("/x", "body", 7_000, 3, 5).Returns("{\"ok\":true}");

		// Act
		SiteHttpResponse answer = SiteCall.Post(client, "/x", "body", 7_000, 3, 5);

		// Assert
		answer.Should().BeEquivalentTo(new SiteHttpResponse(null, "{\"ok\":true}"),
			because: "an unknown status yields no site response, so nothing false is reported");
	}

	[Test]
	[Description("The fallback keeps each verb's own string method and replay rule")]
	public void SiteCall_ShouldUseTheMatchingStringMethod_ForEachVerb_WhenTheClientDoesNotKeepTheStatus() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecuteNonReplayablePostRequest("/n", "b", 1, 1, 1).Returns("n");
		client.ExecutePatchRequest("/p", "b", 1, 1, 1).Returns("p");
		client.ExecuteDeleteRequest("/d", "b", 1, 1, 1).Returns("d");

		// Act + Assert
		SiteCall.NonReplayablePost(client, "/n", "b", 1, 1, 1).Body.Should().Be("n");
		SiteCall.Patch(client, "/p", "b", 1, 1, 1).Body.Should().Be("p");
		SiteCall.Delete(client, "/d", "b", 1, 1, 1).Body.Should().Be("d");
		client.DidNotReceiveWithAnyArgs().ExecutePostRequest(default, default, default, default, default);
	}

	[TestCase(SiteRequestVerb.Post, true, TestName = "SiteCall_ShouldSendAReplayablePost_WhenTheClientKeepsTheStatus")]
	[TestCase(SiteRequestVerb.Post, false, TestName = "SiteCall_ShouldSendANonReplayablePost_WhenTheClientKeepsTheStatus")]
	[TestCase(SiteRequestVerb.Patch, false, TestName = "SiteCall_ShouldSendANonReplayablePatch_WhenTheClientKeepsTheStatus")]
	[TestCase(SiteRequestVerb.Delete, false, TestName = "SiteCall_ShouldSendANonReplayableDelete_WhenTheClientKeepsTheStatus")]
	[Description("A client that keeps the status gets the verb and the same replay rule as the string method")]
	public void SiteCall_ShouldPassTheVerbAndReplayRule(SiteRequestVerb verb, bool replayAllowed) {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient, IStatusPreservingApplicationClient>();
		IStatusPreservingApplicationClient keeping = (IStatusPreservingApplicationClient)client;
		keeping.SendKeepingStatus(default, default, default, default, default, default, default)
			.ReturnsForAnyArgs(new SiteHttpResponse(201, "{}"));

		// Act
		SiteHttpResponse answer = verb switch {
			SiteRequestVerb.Post when replayAllowed => SiteCall.Post(client, "/x", "b", 9, 2, 4),
			SiteRequestVerb.Post => SiteCall.NonReplayablePost(client, "/x", "b", 9, 2, 4),
			SiteRequestVerb.Patch => SiteCall.Patch(client, "/x", "b", 9, 2, 4),
			_ => SiteCall.Delete(client, "/x", "b", 9, 2, 4)
		};

		// Assert
		answer.StatusCode.Should().Be(201);
		keeping.Received(1).SendKeepingStatus(verb, "/x", "b", 9, 2, 4, replayAllowed);
	}

	#endregion

	#region Tests: ApplicationClientLease

	[TestCase(SiteRequestVerb.Post, true, TestName = "Lease_ShouldForwardAReplayablePost")]
	[TestCase(SiteRequestVerb.Post, false, TestName = "Lease_ShouldForwardANonReplayablePost")]
	[TestCase(SiteRequestVerb.Patch, false, TestName = "Lease_ShouldForwardAPatch")]
	[TestCase(SiteRequestVerb.Delete, false, TestName = "Lease_ShouldForwardADelete")]
	[Description("A leased client that keeps the status gets the same verb and replay rule")]
	public void Lease_ShouldForwardTheVerbAndReplayRule(SiteRequestVerb verb, bool replayAllowed) {
		// Arrange
		IApplicationClient inner = Substitute.For<IApplicationClient, IStatusPreservingApplicationClient>();
		IStatusPreservingApplicationClient keeping = (IStatusPreservingApplicationClient)inner;
		keeping.SendKeepingStatus(default, default, default, default, default, default, default)
			.ReturnsForAnyArgs(new SiteHttpResponse(409, "{}"));
		ApplicationClientLease lease = new(inner);

		// Act
		SiteHttpResponse answer = lease.SendKeepingStatus(verb, "/x", "b", 9, 2, 4, replayAllowed);

		// Assert
		answer.StatusCode.Should().Be(409);
		keeping.Received(1).SendKeepingStatus(verb, "/x", "b", 9, 2, 4, replayAllowed);
	}

	[Test]
	[Description("A leased client that does not keep the status is called through the matching non-replayable string method")]
	public void Lease_ShouldUseTheNonReplayableStringPost_WhenTheInnerClientDoesNotKeepTheStatus() {
		// Arrange
		IApplicationClient inner = Substitute.For<IApplicationClient>();
		inner.ExecuteNonReplayablePostRequest("/x", "b", 9, 2, 4).Returns("{}");
		ApplicationClientLease lease = new(inner);

		// Act
		SiteHttpResponse answer = lease.SendKeepingStatus(SiteRequestVerb.Post, "/x", "b", 9, 2, 4, replayAllowed: false);

		// Assert
		answer.Should().BeEquivalentTo(new SiteHttpResponse(null, "{}"));
		inner.DidNotReceiveWithAnyArgs().ExecutePostRequest(default, default, default, default, default);
	}

	#endregion

	#region Tests: SiteHttpResponse

	[TestCase(200, false)]
	[TestCase(204, false)]
	[TestCase(299, false)]
	[TestCase(199, true)]
	[TestCase(302, true)]
	[TestCase(404, true)]
	[TestCase(500, true)]
	[TestCase(null, false)]
	[Description("Only a known status outside 2xx is a failure status")]
	public void IsKnownFailureStatus_ShouldBeTrueOnlyForAKnownNon2xxStatus(int? status, bool expected) {
		new SiteHttpResponse(status, "").IsKnownFailureStatus.Should().Be(expected);
	}

	#endregion

}
