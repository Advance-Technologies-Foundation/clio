using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Clio.Command.McpServer;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Unit coverage for the mcp-http credential-passthrough middleware pipeline
/// (<c>EnforcePlatformApiKeyGate</c> → <c>CaptureCredentialContext</c>). Exercises the two
/// middlewares directly against a <see cref="DefaultHttpContext"/>. The literal pipeline order is
/// wired in <c>Run()</c> and belongs to mcp.e2e; here the ordering is asserted as the item-key
/// dependency contract that encodes it — the gate writes <c>PassthroughEnabledItemKey</c>, and the
/// capture middleware is inert unless that key is true (ENG-93208 Story 4/5 gap that let B1 ship).
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class CredentialPassthroughMiddlewareTests
{
	private const string HeaderName = "X-Integration-Credentials";
	private const string PlatformKey = "platform-key-abc123";
	private const string SecretAccessToken = "tenant-secret-token-xyz";

	private static DefaultHttpContext CreateContext(string authorization = null, string credentialHeader = null) {
		DefaultHttpContext context = new() {
			// WriteAsJsonAsync resolves JSON options from RequestServices; an empty provider yields
			// the framework defaults without an NRE. A writable body lets a test read the 401/400 body.
			RequestServices = new ServiceCollection().BuildServiceProvider()
		};
		context.Response.Body = new MemoryStream();
		if (authorization is not null) {
			context.Request.Headers.Authorization = authorization;
		}

		if (credentialHeader is not null) {
			context.Request.Headers[HeaderName] = credentialHeader;
		}

		return context;
	}

	private static (RequestDelegate Next, Func<bool> WasCalled) TrackingNext() {
		bool called = false;
		RequestDelegate next = _ => {
			called = true;
			return Task.CompletedTask;
		};
		return (next, () => called);
	}

	private static string ReadBody(HttpContext context) {
		context.Response.Body.Seek(0, SeekOrigin.Begin);
		using StreamReader reader = new(context.Response.Body, leaveOpen: true);
		return reader.ReadToEnd();
	}

	private static string ValidCredentialHeader() {
		string json =
			$"{{\"url\":\"https://acme.creatio.com\",\"accessToken\":\"{SecretAccessToken}\",\"accessTokenType\":\"Bearer\",\"isNetCore\":false}}";
		return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
	}

	private static ICredentialContextAccessor AccessorFor(HttpContext context) =>
		new CredentialContextAccessor(new HttpContextAccessor { HttpContext = context });

	[Test]
	[Description("With no platform API key configured, the gate disables passthrough, ignores the credential header, and forwards the request (exact 8.1.0.72 behavior).")]
	public async Task EnforcePlatformApiKeyGate_ShouldDisablePassthroughAndProceed_WhenNoPlatformKeyConfigured() {
		// Arrange
		IPlatformApiKeyGate gate = new PlatformApiKeyGate([]);
		DefaultHttpContext context = CreateContext(credentialHeader: ValidCredentialHeader());
		(RequestDelegate next, Func<bool> wasCalled) = TrackingNext();

		// Act
		await McpHttpServerCommand.EnforcePlatformApiKeyGate(context, next, gate, HeaderName, authorizationEnabled: false);

		// Assert
		context.Items[McpHttpServerCommand.PassthroughEnabledItemKey].Should().Be(false,
			because: "with no configured key passthrough is fail-closed, so the item flag must be false");
		wasCalled().Should().BeTrue(
			because: "an untrusted request must still flow to the pipeline exactly as 8.1.0.72 (AC-02)");
		context.Response.StatusCode.Should().Be(StatusCodes.Status200OK,
			because: "no key configured is not an error condition — the request is not short-circuited");
	}

	[Test]
	[Description("When a key is configured but the request carries no credential header, the gate marks passthrough disabled and forwards (pre-registered -e path).")]
	public async Task EnforcePlatformApiKeyGate_ShouldDisablePassthroughAndProceed_WhenKeyConfiguredButNoCredentialHeader() {
		// Arrange
		IPlatformApiKeyGate gate = new PlatformApiKeyGate([PlatformKey]);
		DefaultHttpContext context = CreateContext(authorization: $"Bearer {PlatformKey}");
		(RequestDelegate next, Func<bool> wasCalled) = TrackingNext();

		// Act
		await McpHttpServerCommand.EnforcePlatformApiKeyGate(context, next, gate, HeaderName, authorizationEnabled: false);

		// Assert
		context.Items[McpHttpServerCommand.PassthroughEnabledItemKey].Should().Be(false,
			because: "a passthrough-capable server still treats a request without the credential header as non-passthrough (AC-02)");
		wasCalled().Should().BeTrue(
			because: "the pre-registered environment path must proceed unaffected");
	}

	[Test]
	[Description("Review #1 (security): when passthrough is enabled, a request that omits the credential header AND presents no/invalid platform key is rejected with HTTP 401 — the whole MCP surface is fail-closed, not only credential-bearing requests.")]
	public async Task EnforcePlatformApiKeyGate_ShouldReturn401AndShortCircuit_WhenPassthroughEnabledAndNoCredentialHeaderAndUnauthenticated() {
		// Arrange
		IPlatformApiKeyGate gate = new PlatformApiKeyGate([PlatformKey]);
		// No credential header and no Authorization at all: this is the previously-unauthenticated path
		// (registered-environment tools / reg-web-app) that must now be gated on a passthrough edge.
		DefaultHttpContext context = CreateContext();
		(RequestDelegate next, Func<bool> wasCalled) = TrackingNext();

		// Act
		await McpHttpServerCommand.EnforcePlatformApiKeyGate(context, next, gate, HeaderName, authorizationEnabled: false);

		// Assert
		context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized,
			because: "with passthrough enabled the API key is required on EVERY request, so an unauthenticated no-header request must be rejected (review #1)");
		wasCalled().Should().BeFalse(
			because: "an unauthenticated request must never reach the MCP tool surface on a passthrough edge");
		context.Items[McpHttpServerCommand.PassthroughEnabledItemKey].Should().Be(false,
			because: "a rejected request never enables passthrough downstream");
	}

	[Test]
	[Description("A configured key + credential header + matching 'Authorization: Bearer <key>' marks passthrough enabled and the capture middleware records the per-request context.")]
	public async Task GateThenCapture_ShouldCaptureContext_WhenKeyConfiguredAndAuthorizedWithCredentialHeader() {
		// Arrange
		IPlatformApiKeyGate gate = new PlatformApiKeyGate([PlatformKey]);
		ICredentialHeaderParser parser = new CredentialHeaderParser();
		DefaultHttpContext context =
			CreateContext(authorization: $"Bearer {PlatformKey}", credentialHeader: ValidCredentialHeader());
		ICredentialContextAccessor accessor = AccessorFor(context);
		(RequestDelegate terminal, Func<bool> terminalCalled) = TrackingNext();
		RequestDelegate captureNext = ctx =>
			McpHttpServerCommand.CaptureCredentialContext(ctx, terminal, parser, accessor, HeaderName, requireAuthenticatedPrincipal: false);

		// Act
		await McpHttpServerCommand.EnforcePlatformApiKeyGate(context, captureNext, gate, HeaderName, authorizationEnabled: false);

		// Assert
		context.Items[McpHttpServerCommand.PassthroughEnabledItemKey].Should().Be(true,
			because: "a matching platform key authorizes passthrough for this request (FR-09)");
		accessor.Current.Should().NotBeNull(
			because: "a trusted request with a valid credential header must produce a captured per-request context");
		accessor.Current!.Url.Should().Be("https://acme.creatio.com",
			because: "the parsed target url must flow onto the captured context");
		accessor.Current.Auth.Kind.Should().Be(CredentialKind.AccessToken,
			because: "the access-token payload must resolve to access-token auth material");
		accessor.Current.IsNetCore.Should().BeFalse(
			because: "the middleware must copy the explicit runtime boolean into request context");
		accessor.Current.PassthroughModeEnabled.Should().BeTrue(
			because: "the captured context must carry the gate's passthrough decision");
		terminalCalled().Should().BeTrue(
			because: "a successfully captured request continues down the pipeline");
	}

	[Test]
	[Description("A configured key + credential header + wrong 'Authorization: Bearer' short-circuits with HTTP 401, does not call next, and does not echo any secret.")]
	public async Task EnforcePlatformApiKeyGate_ShouldReturn401AndShortCircuit_WhenCredentialHeaderPresentButKeyWrong() {
		// Arrange
		IPlatformApiKeyGate gate = new PlatformApiKeyGate([PlatformKey]);
		DefaultHttpContext context =
			CreateContext(authorization: "Bearer wrong-key", credentialHeader: ValidCredentialHeader());
		(RequestDelegate next, Func<bool> wasCalled) = TrackingNext();

		// Act
		await McpHttpServerCommand.EnforcePlatformApiKeyGate(context, next, gate, HeaderName, authorizationEnabled: false);

		// Assert
		context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized,
			because: "a mismatched platform key must be rejected at the edge (FR-10)");
		wasCalled().Should().BeFalse(
			because: "an unauthorized passthrough request must be short-circuited, never forwarded");
		context.Items[McpHttpServerCommand.PassthroughEnabledItemKey].Should().Be(false,
			because: "a rejected request never enables passthrough downstream");
		string body = ReadBody(context);
		body.Should().NotContain(PlatformKey,
			because: "the response body must never echo platform key material (FR-11)");
		body.Should().NotContain("wrong-key",
			because: "the response body must never echo the presented credential (FR-11)");
	}

	[Test]
	[Description("A trusted request whose credential header is malformed short-circuits with HTTP 400, does not call next, captures nothing, and never echoes the raw header.")]
	public async Task GateThenCapture_ShouldReturn400AndShortCircuit_WhenCredentialHeaderMalformed() {
		// Arrange
		const string malformedHeader = "!!!not-valid-base64!!!";
		IPlatformApiKeyGate gate = new PlatformApiKeyGate([PlatformKey]);
		ICredentialHeaderParser parser = new CredentialHeaderParser();
		DefaultHttpContext context =
			CreateContext(authorization: $"Bearer {PlatformKey}", credentialHeader: malformedHeader);
		ICredentialContextAccessor accessor = AccessorFor(context);
		(RequestDelegate terminal, Func<bool> terminalCalled) = TrackingNext();
		RequestDelegate captureNext = ctx =>
			McpHttpServerCommand.CaptureCredentialContext(ctx, terminal, parser, accessor, HeaderName, requireAuthenticatedPrincipal: false);

		// Act
		await McpHttpServerCommand.EnforcePlatformApiKeyGate(context, captureNext, gate, HeaderName, authorizationEnabled: false);

		// Assert
		context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest,
			because: "a trusted request with an unparseable credential header is a caller-actionable defect (AC-ERR)");
		terminalCalled().Should().BeFalse(
			because: "a malformed credential header must short-circuit before the pipeline continues");
		accessor.Current.Should().BeNull(
			because: "no context is captured when the credential header cannot be parsed");
		string body = ReadBody(context);
		body.Should().NotContain(malformedHeader,
			because: "the defect body names the failure only and never echoes the raw header value (FR-11)");
		body.Should().Contain("Error:",
			because: "the 400 body carries a secret-free, caller-facing error message");
	}

	[Test]
	[Description("Review M1 (advisory): a credential header with an invalid isNetCore AND an unconditionally SSRF-blocked (cloud-metadata) url still short-circuits on the isNetCore defect — next() (and everything downstream of it, including TargetUrlValidator/ResolvePassthrough) never runs, so the runtime-metadata check is strictly ordered before any url/egress validation by construction, not just by convention.")]
	public async Task GateThenCapture_ShouldReturn400WithIsNetCoreError_WhenIsNetCoreInvalidAndUrlWouldFailSsrfCheck() {
		// Arrange
		// 169.254.169.254 is the cloud-metadata (IMDS) address TargetUrlValidator blocks
		// unconditionally, regardless of the server's bound host (unlike a loopback address,
		// which TargetUrlValidator allows when the server itself is bound to loopback) — see
		// TargetUrlValidator.cs. This keeps the test's "SSRF-blocked url" premise unconditionally true.
		string payload =
			$"{{\"url\":\"http://169.254.169.254/internal\",\"accessToken\":\"{SecretAccessToken}\",\"accessTokenType\":\"Bearer\",\"isNetCore\":\"true\"}}";
		string header = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
		IPlatformApiKeyGate gate = new PlatformApiKeyGate([PlatformKey]);
		ICredentialHeaderParser parser = new CredentialHeaderParser();
		DefaultHttpContext context =
			CreateContext(authorization: $"Bearer {PlatformKey}", credentialHeader: header);
		ICredentialContextAccessor accessor = AccessorFor(context);
		(RequestDelegate terminal, Func<bool> terminalCalled) = TrackingNext();
		RequestDelegate captureNext = ctx =>
			McpHttpServerCommand.CaptureCredentialContext(ctx, terminal, parser, accessor, HeaderName, requireAuthenticatedPrincipal: false);

		// Act
		await McpHttpServerCommand.EnforcePlatformApiKeyGate(context, captureNext, gate, HeaderName, authorizationEnabled: false);

		// Assert
		context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest,
			because: "an invalid isNetCore value must fail closed regardless of the url's SSRF risk");
		terminalCalled().Should().BeFalse(
			because: "the request must short-circuit at header parsing — TargetUrlValidator, which only runs downstream of next(), must never be reached");
		accessor.Current.Should().BeNull(
			because: "no context is captured, and therefore no client can ever be built, when isNetCore fails validation");
		string body = ReadBody(context);
		body.Should().Contain("isNetCore must be a JSON boolean",
			because: "the surfaced defect must be the runtime-metadata error, not a url/SSRF error, proving isNetCore is validated first");
		body.Should().NotContain(SecretAccessToken,
			because: "the 400 body must never echo credential material (FR-11)");
	}

	[Test]
	[Description("The capture middleware is inert (forwards, captures nothing, never returns 400) when the gate did not enable passthrough — encoding the gate-before-capture ordering contract.")]
	public async Task CaptureCredentialContext_ShouldBeInert_WhenPassthroughItemKeyAbsent() {
		// Arrange
		// No PassthroughEnabledItemKey is set: this models a request the gate did NOT authorize
		// (or a mis-ordered pipeline). Even a malformed header must be ignored, proving capture
		// strictly depends on the gate having run and enabled passthrough first (AC-02).
		ICredentialHeaderParser parser = new CredentialHeaderParser();
		DefaultHttpContext context = CreateContext(credentialHeader: "!!!not-valid-base64!!!");
		ICredentialContextAccessor accessor = AccessorFor(context);
		(RequestDelegate next, Func<bool> wasCalled) = TrackingNext();

		// Act
		await McpHttpServerCommand.CaptureCredentialContext(context, next, parser, accessor, HeaderName, requireAuthenticatedPrincipal: false);

		// Assert
		wasCalled().Should().BeTrue(
			because: "without the gate's passthrough flag the credential header is ignored and the request proceeds (AC-02)");
		accessor.Current.Should().BeNull(
			because: "no context is captured when passthrough was not enabled by the gate");
		context.Response.StatusCode.Should().Be(StatusCodes.Status200OK,
			because: "an unauthorized/ungated request is never rejected with 400 by the capture middleware");
	}
}
