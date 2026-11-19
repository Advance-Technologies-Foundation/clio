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

	[TestCase(403, 1, true, true, TestName = "RecordRefusalOfSoleWrite_ShouldProve_WhenDeclaredSentOnceAndRefusedWithoutProcessing")]
	[TestCase(401, 1, true, true, TestName = "RecordRefusalOfSoleWrite_ShouldProve_WhenA401RefusedTheDeclaredWrite")]
	[TestCase(403, 1, false, false, TestName = "RecordRefusalOfSoleWrite_ShouldNotProve_WhenTheCaptureIsNotDeclared")]
	[TestCase(403, 3, true, false, TestName = "RecordRefusalOfSoleWrite_ShouldNotProve_WhenTheWriteMayHaveBeenSentAgain")]
	[TestCase(404, 1, true, false, TestName = "RecordRefusalOfSoleWrite_ShouldNotProve_WhenTheStatusIs404")]
	[TestCase(503, 1, true, false, TestName = "RecordRefusalOfSoleWrite_ShouldNotProve_WhenTheStatusIs503")]
	[Description("A refusal proves nothing was applied only with the tool's declaration, one attempt and a status that refuses without processing")]
	public void RecordRefusalOfSoleWrite_ShouldProveNothingApplied_OnlyWhenAllConditionsHold(int status, int attempts,
		bool declared, bool proves) {
		// Arrange
		using SiteResponseCapture.Scope scope = SiteResponseCapture.Begin(soleWriteCall: declared);

		// Act
		SiteResponseCapture.RecordRefusalOfSoleWrite(new SiteHttpResponse(status, ""), attempts);

		// Assert
		scope.Last.Status.Should().Be(status, because: "the answer is recorded either way");
		scope.LastProvesNothingApplied.Should().Be(proves);
		scope.LastDiagnostic.SideEffect.Should().Be(proves ? "none" : "unknown");
	}

	[Test]
	[Description("An ordinary refusal never proves anything, even in a declared capture")]
	public void RecordRefusal_ShouldNotProve_EvenInADeclaredCapture() {
		// Arrange
		using SiteResponseCapture.Scope scope = SiteResponseCapture.Begin(soleWriteCall: true);

		// Act
		SiteResponseCapture.RecordRefusal(new SiteHttpResponse(403, ""));

		// Assert
		scope.LastProvesNothingApplied.Should().BeFalse(because: "only the declared sole write can be proof");
	}

	[Test]
	[Description("A later refusal replaces the proof of an earlier one")]
	public void RecordRefusal_ShouldReplaceTheProof_WhenALaterRefusalIsRecorded() {
		// Arrange
		using SiteResponseCapture.Scope scope = SiteResponseCapture.Begin(soleWriteCall: true);
		SiteResponseCapture.RecordRefusalOfSoleWrite(new SiteHttpResponse(403, ""), 1);

		// Act
		SiteResponseCapture.RecordRefusal(new SiteHttpResponse(500, ""));

		// Assert
		scope.LastRefusal.Should().Be((new SiteResponse(500, null, null, SiteResponse.EmptyBody), false),
			because: "the refusal reported is the last one, and the proof belongs to that one");
	}

	[Test]
	[Description("A capture nested in a declared one is not declared unless its own opener says so")]
	public void Begin_ShouldNotInheritTheDeclaration_InANestedCapture() {
		// Arrange
		using SiteResponseCapture.Scope outer = SiteResponseCapture.Begin(soleWriteCall: true);

		// Act
		using SiteResponseCapture.Scope inner = SiteResponseCapture.Begin();
		SiteResponseCapture.RecordRefusalOfSoleWrite(new SiteHttpResponse(403, ""), 1);

		// Assert
		inner.LastProvesNothingApplied.Should().BeFalse();
	}

	[Test]
	[Description("No refusal recorded, no diagnostic")]
	public void LastDiagnostic_ShouldBeNull_WhenNothingWasRecorded() {
		using SiteResponseCapture.Scope scope = SiteResponseCapture.Begin(soleWriteCall: true);
		scope.LastDiagnostic.Should().BeNull();
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
