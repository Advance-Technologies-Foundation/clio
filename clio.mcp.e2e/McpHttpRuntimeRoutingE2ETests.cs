using System.Net;
using System.Text;
using System.Text.Json;
using Allure.Net.Commons;
using Allure.NUnit.Attributes;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Mcp.E2E;

/// <summary>
/// Process-level HTTP contract coverage for ENG-93348. These tests use a real
/// <c>clio mcp-http</c> child process but do not require a live Creatio stand for
/// the fail-closed header cases. Live dual-runtime route proof remains owned by
/// the manual multi-tenant fixtures and requires two real stands.
/// </summary>
[TestFixture]
[Category("E2E")]
[AllureFeature("mcp-http-runtime-routing")]
[NonParallelizable]
public sealed class McpHttpRuntimeRoutingE2ETests {
	private McpE2ESettings _settings = null!;

	[SetUp]
	public void SetUp() {
		_settings = TestConfiguration.Load();
	}

	[Test]
	[AllureTag("mcp-http")]
	[AllureName("credential encoders emit explicit runtime booleans")]
	[AllureDescription("Verifies both E2E credential encoders serialize isNetCore as a JSON boolean for the Core and Framework route families.")]
	[Description("Credential encoders emit literal JSON booleans for both runtime selections.")]
	public void CredentialEncoder_ShouldEmitJsonBoolean_WhenRuntimeIsSupplied() {
		// Arrange
		string coreHeader = McpHttpServerSession.EncodeBearerCredentials(
			"https://core.example.com", "token", true);
		string frameworkHeader = McpHttpServerSession.EncodeLoginPasswordCredentials(
			"https://framework.example.com", "Supervisor", "password", false);

		// Act
		using JsonDocument core = Decode(coreHeader);
		using JsonDocument framework = Decode(frameworkHeader);

		// Assert
		core.RootElement.GetProperty("isNetCore").ValueKind.Should().Be(JsonValueKind.True,
			because: "Core encoder output must contain a JSON boolean true, not a string");
		framework.RootElement.GetProperty("isNetCore").ValueKind.Should().Be(JsonValueKind.False,
			because: "Framework encoder output must contain a JSON boolean false, not a string");
	}

	[Test]
	[AllureTag("mcp-http")]
	[AllureName("invalid runtime metadata fails before dispatch")]
	[AllureDescription("Starts a real mcp-http process and verifies missing and non-boolean isNetCore metadata returns HTTP 400 with the exact static defect message, without echoing the bearer/login/password/cookie secret, across every supported auth mode.")]
	[Description("The real mcp-http process rejects missing and non-boolean runtime metadata before MCP dispatch, for every auth mode, without leaking the secret and with the exact static error text.")]
	public async Task Passthrough_ShouldReturn400BeforeDispatch_WhenRuntimeMetadataIsMissingOrInvalid() {
		// Arrange
		const string platformApiKey = "process-runtime-routing-test-key";
		const string bearerSecret = "opaque-token";
		const string passwordSecret = "supervisor-secret-password";
		const string cookieSecret = "session-secret-cookie-value";
		(string Payload, string Secret, string ExpectedErrorBody)[] cases = [
			// Bearer auth: missing isNetCore and every non-boolean JSON kind.
			($"{{\"url\":\"https://tenant.example.com\",\"accessToken\":\"{bearerSecret}\"}}",
				bearerSecret, "{\"error\":\"Error: missing isNetCore\"}"),
			($"{{\"url\":\"https://tenant.example.com\",\"accessToken\":\"{bearerSecret}\",\"isNetCore\":null}}",
				bearerSecret, "{\"error\":\"Error: isNetCore must be a JSON boolean\"}"),
			($"{{\"url\":\"https://tenant.example.com\",\"accessToken\":\"{bearerSecret}\",\"isNetCore\":\"true\"}}",
				bearerSecret, "{\"error\":\"Error: isNetCore must be a JSON boolean\"}"),
			($"{{\"url\":\"https://tenant.example.com\",\"accessToken\":\"{bearerSecret}\",\"isNetCore\":1}}",
				bearerSecret, "{\"error\":\"Error: isNetCore must be a JSON boolean\"}"),
			// M2 (review, advisory): login/password and cookie auth modes must fail closed the
			// same way, and the 400 body must never echo the password/cookie value either.
			($"{{\"url\":\"https://tenant.example.com\",\"login\":\"admin\",\"password\":\"{passwordSecret}\"}}",
				passwordSecret, "{\"error\":\"Error: missing isNetCore\"}"),
			($"{{\"url\":\"https://tenant.example.com\",\"cookie\":\"{cookieSecret}\"}}",
				cookieSecret, "{\"error\":\"Error: missing isNetCore\"}")
		];
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(2));
		McpHttpServerSession server = await AllureApi.Step(
			"Arrange a real mcp-http process with the platform API-key gate",
			() => McpHttpServerSession.StartAsync(_settings, platformApiKey, cts.Token));
		await using (server) {
			using HttpClient client = new();
			await AllureApi.Step("Act and assert fail-closed responses for each invalid runtime payload and auth mode", async () => {
				foreach ((string payload, string secret, string expectedErrorBody) in cases) {
					string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
					using HttpRequestMessage request = new(HttpMethod.Post, server.EndpointUrl) {
						Content = new StringContent(
							"{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{}}",
							Encoding.UTF8,
							"application/json")
					};
					request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", platformApiKey);
					request.Headers.TryAddWithoutValidation(McpHttpPassthroughStand.CredentialsHeaderName, encoded);

					using HttpResponseMessage response = await client.SendAsync(request, cts.Token);
					string body = await response.Content.ReadAsStringAsync(cts.Token);
					response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
						because: "missing and non-boolean runtime metadata must fail at the HTTP middleware boundary");
					body.Should().Be(expectedErrorBody,
						because: "the process-level 400 body must be the exact static defect message, with no secret interpolated");
					body.Should().NotContain(secret,
						because: "the process-level validation response must remain secret-free regardless of auth mode");
				}
			});
		}
	}

	private static JsonDocument Decode(string encoded) =>
		JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
}
