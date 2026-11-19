using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class SiteResponseCaptureTests {

	private static readonly SiteHttpResponse Refusal = new(500,
		"{\"success\":false,\"errorInfo\":{\"message\":\"boom\"}}");

	[Test]
	[Description("A refusal recorded outside any capture is dropped: the CLI path records nothing")]
	public void RecordRefusal_ShouldRecordNothing_WhenNoCaptureIsOpen() {
		// Act
		SiteResponse recorded = SiteResponseCapture.RecordRefusal(Refusal);

		// Assert
		recorded.Should().BeNull(because: "there is no tool call to report it on");
		SiteResponseCapture.Current.Should().BeNull();
	}

	[Test]
	[Description("A refusal recorded inside a capture becomes its last site response")]
	public void RecordRefusal_ShouldBeTheLastResponse_WhenACaptureIsOpen() {
		// Arrange
		using SiteResponseCapture.Scope scope = SiteResponseCapture.Begin();

		// Act
		SiteResponseCapture.RecordRefusal(new SiteHttpResponse(403, ""));
		SiteResponseCapture.RecordRefusal(Refusal);

		// Assert
		scope.Last.Should().BeEquivalentTo(new SiteResponse(500, "boom", null, SiteResponse.JsonBody),
			because: "the last refusal wins");
	}

	[Test]
	[Description("An answer with an unknown status is not recorded and does not replace an earlier refusal")]
	public void RecordRefusal_ShouldKeepTheEarlierResponse_WhenTheStatusIsUnknown() {
		// Arrange
		using SiteResponseCapture.Scope scope = SiteResponseCapture.Begin();
		SiteResponseCapture.RecordRefusal(Refusal);

		// Act
		SiteResponse recorded = SiteResponseCapture.RecordRefusal(new SiteHttpResponse(null, "{}"));

		// Assert
		recorded.Should().BeNull();
		scope.Last.Status.Should().Be(500, because: "a status-less answer cannot be reported and erases nothing");
	}

	[Test]
	[Description("A nested capture keeps its own refusals and gives the outer one back when disposed")]
	public void Begin_ShouldIsolateANestedCapture_AndRestoreTheOuterOne() {
		// Arrange
		using SiteResponseCapture.Scope outer = SiteResponseCapture.Begin();

		// Act
		using (SiteResponseCapture.Scope inner = SiteResponseCapture.Begin()) {
			SiteResponseCapture.RecordRefusal(Refusal);
			inner.Last.Should().NotBeNull(because: "the inner capture is current while it is open");
		}
		SiteResponseCapture.RecordRefusal(new SiteHttpResponse(409, ""));

		// Assert
		outer.Last.Status.Should().Be(409,
			because: "the inner refusal stays with the inner capture, and the outer one is current again");
		SiteResponseCapture.Current.Should().BeSameAs(outer.Capture);
	}

	[Test]
	[Description("Disposing a scope twice restores the previous capture only once")]
	public void Dispose_ShouldBeIdempotent() {
		// Arrange
		using SiteResponseCapture.Scope outer = SiteResponseCapture.Begin();
		SiteResponseCapture.Scope inner = SiteResponseCapture.Begin();
		inner.Dispose();
		using SiteResponseCapture.Scope sibling = SiteResponseCapture.Begin();

		// Act
		inner.Dispose();

		// Assert
		SiteResponseCapture.Current.Should().BeSameAs(sibling.Capture,
			because: "a second Dispose must not reset a capture opened after the first one");
	}

	[Test]
	[Description("Two concurrent flows never see each other's refusal")]
	public async Task RecordRefusal_ShouldStayInItsOwnFlow_WhenTwoCallsRunConcurrently() {
		// Arrange
		using Barrier bothOpen = new(2);

		async Task<SiteResponse> RunCall(int status) {
			await Task.Yield();
			using SiteResponseCapture.Scope scope = SiteResponseCapture.Begin();
			bothOpen.SignalAndWait();
			SiteResponseCapture.RecordRefusal(new SiteHttpResponse(status, ""));
			bothOpen.SignalAndWait();
			return scope.Last;
		}

		// Act
		SiteResponse[] results = await Task.WhenAll(Task.Run(() => RunCall(401)), Task.Run(() => RunCall(503)));

		// Assert
		results[0].Status.Should().Be(401, because: "each tool call reports only its own site answer");
		results[1].Status.Should().Be(503);
	}

}
