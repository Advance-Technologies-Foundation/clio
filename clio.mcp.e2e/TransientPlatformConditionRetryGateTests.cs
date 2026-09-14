using System.Text.Json;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Unit tests for the pure <see cref="TransientPlatformConditionRetryGate.IsKnownTransientPlatformCondition"/>
/// and <see cref="TransientPlatformConditionRetryGate.IsLoginRejection"/> classification predicates.
/// They construct <see cref="CallToolResult"/> payloads in-memory (no MCP server, no stand, no network
/// I/O), so they validate the retry-vs-real-failure contract locally and are categorized <c>Unit</c>
/// rather than <c>McpE2E.Sandbox</c>.
/// </summary>
[TestFixture]
[Category("Unit")]
// The no-environment CI lane selects POSITIVELY on this category; "Unit" alone runs these only when the
// project is executed unfiltered, which is how they would silently stop being exercised.
[Category("McpE2E.NoEnvironment")]
[Property("Module", "McpServer")]
public sealed class TransientPlatformConditionRetryGateTests {
	private static CallToolResult TextResult(string text) =>
		new() { IsError = true, Content = [new TextContentBlock { Text = text }] };

	private static CallToolResult SuccessfulTextResult(string text) =>
		new() { IsError = false, Content = [new TextContentBlock { Text = text }] };

	[Test]
	[Description("Recognizes the OData-rebuild-window message as a known transient platform condition.")]
	public void IsKnownTransientPlatformCondition_ShouldReturnTrue_ForODataRebuildMessage() {
		// Arrange
		CallToolResult result = TextResult("Creatio is currently rebuilding the OData library, try again later.");

		// Act
		bool isTransient = TransientPlatformConditionRetryGate.IsKnownTransientPlatformCondition(result);

		// Assert
		isTransient.Should().BeTrue(
			because: "the OData rebuild window is one of the three documented known transient platform conditions");
	}

	[Test]
	[Description("Recognizes the LoginDiagnostics login-rejection prefix as a known transient platform condition.")]
	public void IsKnownTransientPlatformCondition_ShouldReturnTrue_ForLoginRejectionMessage() {
		// Arrange
		CallToolResult result = TextResult("Unauthorized someuser for https://example.creatio.com [clio-login kind=implicit]");

		// Act
		bool isTransient = TransientPlatformConditionRetryGate.IsKnownTransientPlatformCondition(result);

		// Assert
		isTransient.Should().BeTrue(
			because: "a message starting with the LoginDiagnostics rejection prefix is a known transient platform condition");
	}

	[Test]
	[Description("Recognizes the ServiceResponseJsonGuard HTML-page and login-page-redirect wording as a known transient platform condition.")]
	public void IsKnownTransientPlatformCondition_ShouldReturnTrue_ForHtmlPageMessage() {
		// Arrange
		CallToolResult result = TextResult(
			"create-app returned an HTML page instead of JSON (URL: https://example.creatio.com/0/rest). "
			+ "The request was most likely redirected to a login page, or the server raised an unhandled error.");

		// Act
		bool isTransient = TransientPlatformConditionRetryGate.IsKnownTransientPlatformCondition(result);

		// Assert
		isTransient.Should().BeTrue(
			because: "the HTML-page-instead-of-JSON / redirected-to-a-login-page wording is a known transient platform condition");
	}

	[Test]
	[Description("Does not treat a failed data assertion / business-rule message as a known transient platform condition, because it carries none of the three transient signatures.")]
	public void IsKnownTransientPlatformCondition_ShouldReturnFalse_ForBusinessRuleFailure() {
		// Arrange — deliberately marker-FREE, and this test asserts only that. Unlike the already-created,
		// create-timeout and contention cases below, a business-rule rejection has no envelope-level
		// signature to exclude on: the wording comes from the platform, not from clio, so there is no
		// stable literal to key on. It therefore falls through on marker absence, which is exactly what is
		// being pinned here — see the "NOT enforced" half of the gate's own <remarks>.
		CallToolResult result = TextResult("success:false, error: an application with this code already exists.");

		// Act
		bool isTransient = TransientPlatformConditionRetryGate.IsKnownTransientPlatformCondition(result);

		// Assert
		isTransient.Should().BeFalse(
			because: "a business-rule failure carries none of the three transient platform signatures, so it falls straight through as a real, repeatable outcome");
	}

	[Test]
	[Description("Does not treat a contention error-class answer as a known transient platform condition even when the SAME payload also carries a transient marker, so the exclusion is proven to fire rather than merely to be unreached.")]
	public void IsKnownTransientPlatformCondition_ShouldReturnFalse_ForContentionErrorClass() {
		// Arrange — the marker phrase is deliberately IN the payload, and the error-class uses the real
		// serialized envelope shape ("error-class":"contention", from the
		// [property: JsonPropertyName("error-class")] member of the application tool responses). Without
		// both, this test would pass against a gate with zero contention logic, purely on marker absence.
		// A contention rejection raised while the stand is also rebuilding its OData library carries both
		// signals at once, which is precisely the co-occurrence the exclusion exists for.
		CallToolResult result = TextResult(
			"{\"success\":false,\"error-class\":\"contention\","
			+ "\"error\":\"the record is locked by another process. Creatio is currently rebuilding the OData library, try again later.\"}");

		// Act
		bool isTransient = TransientPlatformConditionRetryGate.IsKnownTransientPlatformCondition(result);

		// Assert
		isTransient.Should().BeFalse(
			because: "contention has its own dedicated handling elsewhere in the harness, so it must be excluded explicitly rather than only when no transient marker happens to share the payload");
	}

	[Test]
	[Description("Does not retry a SUCCESSFUL answer whose payload happens to embed a marker phrase, because create-app is not idempotent and only a FAILED answer should ever be retried.")]
	public void IsKnownTransientPlatformCondition_ShouldReturnFalse_ForSuccessfulAnswerContainingMarkerText() {
		// Arrange
		CallToolResult result = SuccessfulTextResult(
			"success:true, note: earlier in the run Creatio was rebuilding the OData library, now settled.");

		// Act
		bool isTransient = TransientPlatformConditionRetryGate.IsKnownTransientPlatformCondition(result);

		// Assert
		isTransient.Should().BeFalse(
			because: "a successful answer must never be retried just because its payload happens to contain a marker phrase, since create-app is not idempotent");
	}

	[Test]
	[Description("Recognizes the {\"success\":false} failure shape as a failure signal directly from structured content (unescaped quotes) even when the tool answer did not set IsError, so a marker still matches.")]
	public void IsKnownTransientPlatformCondition_ShouldReturnTrue_ForUnescapedSuccessFalsePayload() {
		// Arrange — StructuredContent is serialized at the top level, so its quotes stay plain (unescaped)
		// in the payload DescribePayload inspects.
		CallToolResult result = new() {
			IsError = null,
			StructuredContent = JsonSerializer.SerializeToElement(
				new { success = false, error = "Creatio is currently rebuilding the OData library" })
		};

		// Act
		bool isTransient = TransientPlatformConditionRetryGate.IsKnownTransientPlatformCondition(result);

		// Assert
		isTransient.Should().BeTrue(
			because: "the tools' own {\"success\":false,...} envelope is itself a failure signal, independent of the MCP transport-level IsError flag, and must be recognized even with plain (unescaped) quotes");
	}

	[Test]
	[Description("Recognizes the {\"success\":false} failure shape as a failure signal even when its quotes arrive escaped, because the payload is JSON-inside-JSON.")]
	public void IsKnownTransientPlatformCondition_ShouldReturnTrue_ForEscapedSuccessFalsePayload() {
		// Arrange — a TextContentBlock.Text is itself a string PROPERTY, so when DescribePayload serializes
		// the surrounding Content list, this plain-quoted JSON body comes back with its quotes re-encoded
		// (System.Text.Json's default encoder emits the Unicode escape \u0022, not \") in the final
		// payload text —
		// exercising the real escaped encoding without hand-simulating it.
		CallToolResult result = new() {
			IsError = null,
			Content = [new TextContentBlock {
				Text = "{\"success\":false,\"error\":\"Creatio is currently rebuilding the OData library\"}"
			}]
		};

		// Act
		bool isTransient = TransientPlatformConditionRetryGate.IsKnownTransientPlatformCondition(result);

		// Assert
		isTransient.Should().BeTrue(
			because: "the failure shape must be recognized whether its quotes arrived plain or escaped, since the payload is JSON serialized inside JSON");
	}

	[Test]
	[Description("Excludes the ApplicationCreateService \"metadata could not be loaded\" failure from the transient match, because a retry would replay a create that already happened.")]
	public void IsKnownTransientPlatformCondition_ShouldReturnFalse_ForApplicationAlreadyCreatedFailure() {
		// Arrange
		CallToolResult result = TextResult(
			"Application 'UsrCodex1234' was created but its metadata could not be loaded after 5 attempts. "
			+ "Creatio is currently rebuilding the OData library, try again later.");

		// Act
		bool isTransient = TransientPlatformConditionRetryGate.IsKnownTransientPlatformCondition(result);

		// Assert
		isTransient.Should().BeFalse(
			because: "the application was already created, so retrying would resubmit the same name/code against an application that already exists, even though the last load error embeds the OData-rebuild marker");
	}

	[Test]
	[Description("Excludes the ApplicationCreateService timeout-recovery \"could not be loaded\" failure from the transient match, because the CreateApp POST may already have landed server-side and a retry would submit a duplicate create.")]
	public void IsKnownTransientPlatformCondition_ShouldReturnFalse_ForApplicationCreateTimeoutFailure() {
		// Arrange — the sibling of the already-created prefix, produced by the SAME
		// LoadApplicationInfoWithRetry helper (clio/Command/ApplicationCreateService.cs:546) and likewise
		// carrying the appended last-load error, which is where the marker comes from. This path is
		// reached only from `catch (Exception exception) when (IsTimeout(exception))` around the CreateApp
		// POST, so the application probably exists; the marker in the same payload is the realistic case,
		// not a contrived one, because the load underneath runs SelectQuery +
		// ServiceResponseJsonGuard.Deserialize, exactly the code that fails during an OData rebuild.
		CallToolResult result = TextResult(
			"CreateApp request timed out and application 'UsrCodex1234' could not be loaded after 5 attempts. "
			+ "Last error: Creatio is currently rebuilding the OData library, try again later.");

		// Act
		bool isTransient = TransientPlatformConditionRetryGate.IsKnownTransientPlatformCondition(result);

		// Assert
		isTransient.Should().BeFalse(
			because: "the CreateApp POST timed out rather than being refused, so the application may already exist and retrying would resubmit the same name/code even though the last load error embeds the OData-rebuild marker");
	}

	[Test]
	[Description("Does not classify arbitrary 401/permission text as a known transient platform condition just because it embeds the word the login-rejection prefix starts with.")]
	public void IsKnownTransientPlatformCondition_ShouldReturnFalse_ForLoose401TextWithoutLoginRejectionShape() {
		// Arrange — the bare prefix appears, but not the " for <url>" segment that the real
		// "Unauthorized <user> for <url>" message always carries, so this is NOT a rejected login.
		CallToolResult result = TextResult(
			"{\"success\":false,\"error\":\"the operation returned 401 Unauthorized and the caller lacks the CanManageSolution permission.\"}");

		// Act
		bool isTransient = TransientPlatformConditionRetryGate.IsKnownTransientPlatformCondition(result);

		// Assert
		isTransient.Should().BeFalse(
			because: "matching the bare prefix across the whole concatenated payload would classify any 401 text, permission message or log line as a rejected login and trigger back-to-back session restarts with no wait between them");
	}

	[Test]
	[Description("Does not report a login rejection for loose 401 text lacking the message's companion segment, even when the payload arrived with escaped quotes.")]
	public void IsLoginRejection_ShouldReturnFalse_ForLoose401TextWithEscapedQuotes() {
		// Arrange — a TextContentBlock.Text is itself a string PROPERTY, so DescribePayload's serialization
		// re-encodes these quotes (System.Text.Json emits "). IsLoginRejection now normalizes that
		// encoding, which on its own would WIDEN what it matches; this pins that the companion-segment
		// requirement more than offsets it, so the net effect is tighter and not looser.
		CallToolResult result = new() {
			IsError = true,
			Content = [new TextContentBlock {
				Text = "{\"success\":false,\"error\":\"401 Unauthorized while reading https://example.creatio.com/0/rest\"}"
			}]
		};

		// Act
		bool isLoginRejection = TransientPlatformConditionRetryGate.IsLoginRejection(result);

		// Assert
		isLoginRejection.Should().BeFalse(
			because: "the payload never carries the \" for \" segment that follows the user name in the real rejection message, so re-authenticating would be the wrong response");
	}

	[Test]
	[Description("Reports null as not a known transient platform condition.")]
	public void IsKnownTransientPlatformCondition_ShouldReturnFalse_ForNullResult() {
		// Arrange
		CallToolResult? result = null;

		// Act
		bool isTransient = TransientPlatformConditionRetryGate.IsKnownTransientPlatformCondition(result);

		// Assert
		isTransient.Should().BeFalse(
			because: "there is no payload to classify, so it cannot be a known transient platform condition");
	}

	[Test]
	[Description("Identifies the login-rejection signature specifically, distinct from the other known transient conditions.")]
	public void IsLoginRejection_ShouldReturnTrue_OnlyForLoginRejectionMessage() {
		// Arrange
		CallToolResult loginRejection = TextResult("Unauthorized someuser for https://example.creatio.com");
		CallToolResult odataRebuild = TextResult("Creatio is currently rebuilding the OData library.");

		// Act
		bool loginRejectionIsLoginRejection = TransientPlatformConditionRetryGate.IsLoginRejection(loginRejection);
		bool odataRebuildIsLoginRejection = TransientPlatformConditionRetryGate.IsLoginRejection(odataRebuild);

		// Assert
		loginRejectionIsLoginRejection.Should().BeTrue(
			because: "the message carries the LoginDiagnostics login-rejection prefix");
		odataRebuildIsLoginRejection.Should().BeFalse(
			because: "the OData rebuild window is a known transient condition but not specifically a login rejection, so it must not trigger re-authentication");
	}

	[Test]
	[Description("Classifies the VERBATIM clio-login-decorated implicit login rejection recorded for the muted ApplicationCreate and ApplicationSectionCreate tests as a known transient platform condition.")]
	public void IsKnownTransientPlatformCondition_ShouldReturnTrue_ForRecordedImplicitLoginRejection() {
		// Arrange - the message shape LoginDiagnostics actually produced on the stand (issue #1106), kept
		// verbatim so a change to the decoration that broke the classifier would fail here rather than
		// silently unmute nothing.
		CallToolResult result = TextResult(
			"Unauthorized Supervisor for https://example.creatio.com [clio-login kind=implicit client=9390d9fa "
			+ "client-request=1 process-request=1 in-flight-logins=0/0 in-flight-requests=1/1 "
			+ "started-at=2026-08-19T13:13:53.9366930Z elapsed-ms=28 since-client-created-ms=63 "
			+ "original-type=UnauthorizedAccessException]");

		// Act
		bool isTransient = TransientPlatformConditionRetryGate.IsKnownTransientPlatformCondition(result);
		bool isLoginRejection = TransientPlatformConditionRetryGate.IsLoginRejection(result);

		// Assert
		isTransient.Should().BeTrue(
			because: "the decorated rejection still carries the login-rejection prefix and its companion separator, and the trailing diagnostics block must not defeat the match");
		isLoginRejection.Should().BeTrue(
			because: "the caller distinguishes this condition from the others to decide whether to re-authenticate rather than merely wait");
	}

	[Test]
	[Description("Classifies the VERBATIM runtime-entity-schema HTML answer recorded for the muted ApplicationSectionUpdate test as a known transient platform condition.")]
	public void IsKnownTransientPlatformCondition_ShouldReturnTrue_ForRecordedSelectQueryHtmlAnswer() {
		// Arrange - the ServiceResponseJsonGuard wording as it reaches the harness, redacted URL included
		// (issue #1106). The envelope is the tools' own success:false shape rather than an MCP-level error,
		// which is how this failure actually arrives.
		CallToolResult result = SuccessfulTextResult(
			"{\"success\":false,\"error\":\"SelectQuery returned an HTML page instead of JSON "
			+ "(URL: [redacted-uri]). The request was most likely redirected to a login page, or the server "
			+ "raised an unhandled error and answered with an HTML error page.\"}");

		// Act
		bool isTransient = TransientPlatformConditionRetryGate.IsKnownTransientPlatformCondition(result);

		// Assert
		isTransient.Should().BeTrue(
			because: "the answer carries the guard's HTML-page wording and the tools' own success:false failure signal, so it must be retried even though the transport did not flag the call as an error");
	}

	[Test]
	[Description("Does NOT classify the bare \"Select query failed.\" answer recorded for the muted ApplicationSectionUpdate test, pinning the deliberate limit of the gate's coverage.")]
	public void IsKnownTransientPlatformCondition_ShouldReturnFalse_ForBareSelectQueryFailedAnswer() {
		// Arrange - the SECOND shape that test produced (issue #1106). It is built from a SUCCESSFUL HTTP
		// response carrying success:false and names no cause, so it is indistinguishable from a genuine
		// query failure.
		CallToolResult result = SuccessfulTextResult("{\"success\":false,\"error\":\"Select query failed.\"}");

		// Act
		bool isTransient = TransientPlatformConditionRetryGate.IsKnownTransientPlatformCondition(result);

		// Assert
		isTransient.Should().BeFalse(
			because: "an opaque query failure carries none of the three transient signatures, and promoting it to one would retry every real regression in the suite instead of failing it");
	}

	[TestCase("in-progress", TestName = "IsKnownTransientPlatformCondition_ShouldReturnFalse_ForSectionAlreadyCreated(in-progress)")]
	[TestCase("unknown", TestName = "IsKnownTransientPlatformCondition_ShouldReturnFalse_ForSectionAlreadyCreated(unknown)")]
	[TestCase("true", TestName = "IsKnownTransientPlatformCondition_ShouldReturnFalse_ForSectionAlreadyCreated(true)")]
	[Description("Excludes a create-app-section answer whose section-created field says the insert already landed or may still be landing, even when the SAME payload also carries a transient marker, so the exclusion is proven to fire rather than merely to be unreached.")]
	public void IsKnownTransientPlatformCondition_ShouldReturnFalse_ForSectionAlreadyCreated(string sectionCreated) {
		// Arrange - the in-progress envelope is produced by a response deadline, and a stand rebuilding its
		// OData library is exactly what makes that deadline fire, so both signals really do arrive together.
		// The section insert carries a client-generated id, so a retry would insert a SECOND section.
		// Built with SuccessfulTextResult because a CLASSIFIED tool failure arrives that way - the transport
		// does not flag it, the success:false in the body is the failure signal.
		CallToolResult result = SuccessfulTextResult(
			"{\"success\":false,\"error-class\":\"creatio-timeout\",\"section-created\":\"" + sectionCreated + "\","
			+ "\"error\":\"Creatio is currently rebuilding the OData library.\"}");

		// Act
		bool isTransient = TransientPlatformConditionRetryGate.IsKnownTransientPlatformCondition(result);

		// Assert
		isTransient.Should().BeFalse(
			because: "a section whose insert landed or may still be landing must never be retried, whatever else the payload says - its own retry guidance states that a retry would create a duplicate section. Only the verified-absent 'false' is safe, and it is covered by the counter-case below");
	}

	[Test]
	[Description("Still retries a create-app-section answer that failed BEFORE the insert, so the section exclusion narrows the gate rather than disabling it for section creation.")]
	public void IsKnownTransientPlatformCondition_ShouldReturnTrue_ForSectionFailureBeforeInsert() {
		// Arrange - section-created false means the insert did not happen, so nothing was left behind and
		// the call can be repeated safely. SuccessfulTextResult for the same reason as the exclusion case
		// above: a classified tool failure does not set IsError.
		CallToolResult result = SuccessfulTextResult(
			"{\"success\":false,\"section-created\":\"false\","
			+ "\"error\":\"Creatio is currently rebuilding the OData library.\"}");

		// Act
		bool isTransient = TransientPlatformConditionRetryGate.IsKnownTransientPlatformCondition(result);

		// Assert
		isTransient.Should().BeTrue(
			because: "an answer that failed before the insert leaves no section behind, so the transient condition it reports is safe to retry");
	}
}
