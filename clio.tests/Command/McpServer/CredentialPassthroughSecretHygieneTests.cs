using System;
using System.Text;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Tests.Infrastructure;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Story 13 (ENG-93208, FR-11) secret-hygiene pins. A single DISTINCTIVE secret literal is seeded as
/// the accessToken / cookie / password across every sink the credential-passthrough path touches — the
/// header-parse error, the FR-19 / FR-12 resolution errors, the cache keys, the MCP
/// <see cref="CommandExecutionResult"/> for a failing passthrough resolve, the exception messages, the
/// shared error-text redactor, and <see cref="EnvironmentSettings"/> serialization — and each test
/// asserts the seeded literal is ABSENT from that sink's output. Mirrors the
/// <c>Common/BrowserSession/CreatioAuthClient</c> "names only, never values" discipline. The exhaustive
/// cross-sink matrix is Story 15b; this fixture pins the sinks Story 13 audits.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class CredentialPassthroughSecretHygieneTests {

	// One distinctive literal so a leak into ANY sink is unambiguous and greppable.
	private const string Secret = "SUPER-SECRET-TOKEN-9c3f2a";
	private const string Url = "https://tenant.creatio.com";

	private static ToolCommandResolver CreateResolver(ICredentialContextAccessor accessor) =>
		new(
			Substitute.For<ISettingsRepository>(),
			Substitute.For<ISettingsBootstrapService>(),
			new NonInteractiveConsole(),
			accessor,
			// Default substitute allows every url, so the cookie / missing-auth / non-Bearer branches
			// (the ones under test) are reached instead of being short-circuited by the SSRF guard.
			Substitute.For<ITargetUrlValidator>(),
			new SessionContainerCache(SessionContainerCacheDefaults.IdleTtl, SessionContainerCacheDefaults.MaxSessions));

	private static ICredentialContextAccessor AccessorWith(CredentialContext context) {
		ICredentialContextAccessor accessor = Substitute.For<ICredentialContextAccessor>();
		accessor.Current.Returns(context);
		return accessor;
	}

	// ---- Sink 1: header parse (CredentialHeaderParser) --------------------------------------------

	[Test]
	[Description("The header-parse error for a payload missing the url never echoes the seeded token that rode inside the decoded payload (FR-11).")]
	public void TryParse_ShouldNotLeakSecret_WhenPayloadMissesUrl() {
		// Arrange
		ICredentialHeaderParser parser = new CredentialHeaderParser();
		string header = Convert.ToBase64String(
			Encoding.UTF8.GetBytes($"{{\"accessToken\":\"{Secret}\"}}"));

		// Act
		bool ok = parser.TryParse(header, out CredentialParseResult _, out string error);

		// Assert
		ok.Should().BeFalse(because: "a payload with no url cannot be parsed into a target");
		error.Should().NotContain(Secret,
			because: "the parse error must name the defect only and never echo the seeded token in the payload (FR-11)");
		error.Should().NotContain(header,
			because: "the raw base64 header (trivially decodable back to the seeded credential) must not be echoed either (FR-11)");
	}

	[Test]
	[Description("The header-parse error for malformed JSON never echoes the seeded token embedded in the broken payload (FR-11).")]
	public void TryParse_ShouldNotLeakSecret_WhenJsonIsMalformed() {
		// Arrange
		ICredentialHeaderParser parser = new CredentialHeaderParser();
		string header = Convert.ToBase64String(
			Encoding.UTF8.GetBytes($"{{\"accessToken\":\"{Secret}\""));

		// Act
		bool ok = parser.TryParse(header, out CredentialParseResult _, out string error);

		// Assert
		ok.Should().BeFalse(because: "truncated JSON cannot be deserialized");
		error.Should().NotContain(Secret,
			because: "the JSON-parse error must never surface a snippet of the payload carrying the seeded token (FR-11)");
		error.Should().NotContain(header,
			because: "the raw base64 header (trivially decodable back to the seeded credential) must not be echoed either (FR-11)");
	}

	// ---- Sink 2: resolution — FR-19 explicit-arg rejection (ToolCommandResolver) ------------------

	[Test]
	[Description("The FR-19 explicit-credential-argument rejection message never echoes a seeded secret supplied as a tool argument (FR-11).")]
	public void Resolve_ShouldNotLeakSecret_WhenExplicitCredentialArgsRejected() {
		// Arrange
		ToolCommandResolver resolver = CreateResolver(AccessorWith(new CredentialContext(
			Url, CredentialMaterial.FromAccessToken("header-token", "Bearer"), McpTransport.Http, true)));
		EnvironmentOptions options = new() { Uri = Url, Password = Secret };

		// Act
		Action act = () => resolver.Resolve<CreateEntitySchemaCommand>(options);

		// Assert
		act.Should().Throw<EnvironmentResolutionException>(
				because: "explicit credential args are rejected on the passthrough-over-HTTP edge (FR-19)")
			.Which.Message.Should().NotContain(Secret,
				because: "the rejection must name the channel, never the seeded value the caller supplied (FR-11)");
	}

	// ---- Sink 3: resolution — FR-12 caller-actionable errors (ToolCommandResolver) ----------------

	[Test]
	[Description("The FR-12 cookie-unsupported rejection never echoes the seeded cookie value (FR-11).")]
	public void Resolve_ShouldNotLeakSecret_WhenCookieAuthRejected() {
		// Arrange
		ToolCommandResolver resolver = CreateResolver(AccessorWith(new CredentialContext(
			Url, CredentialMaterial.FromCookie(Secret), McpTransport.Http, true)));

		// Act
		Action act = () => resolver.Resolve<CreateEntitySchemaCommand>(new EnvironmentOptions());

		// Assert
		act.Should().Throw<EnvironmentResolutionException>(
				because: "cookie auth is a caller-actionable limitation in v1 (FR-12)")
			.Which.Message.Should().NotContain(Secret,
				because: "the seeded cookie value must never leak into the error text (FR-11)");
	}

	[Test]
	[Description("The FR-12 missing-auth rejection never echoes a seeded secret and names the real missing piece (FR-11).")]
	public void Resolve_ShouldNotLeakSecret_WhenAuthMaterialMissing() {
		// Arrange — token blank so no usable auth; the seed rides on the url to prove it is not echoed.
		ToolCommandResolver resolver = CreateResolver(AccessorWith(new CredentialContext(
			$"{Url}/{Secret}", CredentialMaterial.FromAccessToken(string.Empty, "Bearer"), McpTransport.Http, true)));

		// Act
		Action act = () => resolver.Resolve<CreateEntitySchemaCommand>(new EnvironmentOptions());

		// Assert
		act.Should().Throw<EnvironmentResolutionException>(
				because: "missing auth material is a caller-actionable failure (FR-12)")
			.Which.Message.Should().NotContain(Secret,
				because: "the error names the missing piece, never a caller-supplied value (FR-11)");
	}

	[Test]
	[Description("The FR-12 non-Bearer-token rejection never echoes the seeded access token (FR-11).")]
	public void Resolve_ShouldNotLeakSecret_WhenNonBearerTokenRejected() {
		// Arrange
		ToolCommandResolver resolver = CreateResolver(AccessorWith(new CredentialContext(
			Url, CredentialMaterial.FromAccessToken(Secret, "MAC"), McpTransport.Http, true)));

		// Act
		Action act = () => resolver.Resolve<CreateEntitySchemaCommand>(new EnvironmentOptions());

		// Assert
		act.Should().Throw<EnvironmentResolutionException>(
				because: "a non-Bearer token type is a caller-actionable limitation (FR-12)")
			.Which.Message.Should().NotContain(Secret,
				because: "the seeded access token must never leak into the error text (FR-11)");
	}

	// ---- Sink 4: cache keys (ToolCommandResolver) -------------------------------------------------

	[Test]
	[Description("The passthrough cache key is SHA-256 hashed and never contains the seeded token (FR-11).")]
	public void BuildPassthroughCacheKey_ShouldNotLeakSecret_WhenTokenPresent() {
		// Arrange
		CredentialContext context = new(
			Url, CredentialMaterial.FromAccessToken(Secret, "Bearer"), McpTransport.Http, true);

		// Act
		string key = ToolCommandResolver.BuildPassthroughCacheKey(context);

		// Assert
		key.Should().NotContain(Secret,
			because: "the credential material is hashed before it is placed in the passthrough cache key (FR-11)");
	}

	[Test]
	[Description("The legacy cache key is SHA-256 hashed and never contains a seeded secret (FR-11).")]
	public void BuildCacheKey_ShouldNotLeakSecret_WhenCredentialsPresent() {
		// Arrange
		EnvironmentOptions options = new();
		EnvironmentSettings settings = new() { Uri = Url, AccessToken = Secret, Password = Secret };

		// Act
		string key = ToolCommandResolver.BuildCacheKey(options, settings);

		// Assert
		key.Should().NotContain(Secret,
			because: "the credential material is hashed before it is placed in the cache key (FR-11)");
	}

	// ---- Sink 5: MCP response envelope (CommandExecutionResult) -----------------------------------

	[Test]
	[Description("The MCP CommandExecutionResult serialized for a failing passthrough resolve never contains the seeded secret (FR-11).")]
	public void CommandExecutionResult_ShouldNotLeakSecret_WhenPassthroughResolveFails() {
		// Arrange — reproduce the exact BaseTool failure path: a passthrough resolve throws
		// EnvironmentResolutionException, which BaseTool maps via FromResolverError into the envelope.
		ToolCommandResolver resolver = CreateResolver(AccessorWith(new CredentialContext(
			Url, CredentialMaterial.FromAccessToken(Secret, "MAC"), McpTransport.Http, true)));
		CommandExecutionResult result;
		try {
			resolver.Resolve<CreateEntitySchemaCommand>(new EnvironmentOptions());
			throw new InvalidOperationException("resolve was expected to fail");
		}
		catch (EnvironmentResolutionException ex) {
			result = CommandExecutionResult.FromResolverError(ex);
		}

		// Act — assert against the actual System.Text.Json envelope that crosses the MCP boundary.
		string json = System.Text.Json.JsonSerializer.Serialize(result);

		// Assert
		json.Should().NotContain(Secret,
			because: "the seeded token must not appear in the serialized MCP response for a failing passthrough resolve (FR-11)");
	}

	[Test]
	[Description("The MCP CommandExecutionResult serialized from an unexpected exception with a secret-free resolver message stays secret-free (FR-11).")]
	public void CommandExecutionResult_ShouldNotLeakSecret_WhenBuiltFromException() {
		// Arrange — the resolver builds only secret-free EnvironmentResolutionException messages; pin that
		// the -1 FromException envelope (used by BaseTool's catch-all) does not reintroduce a secret.
		ToolCommandResolver resolver = CreateResolver(AccessorWith(new CredentialContext(
			Url, CredentialMaterial.FromCookie(Secret), McpTransport.Http, true)));
		CommandExecutionResult result;
		try {
			resolver.Resolve<CreateEntitySchemaCommand>(new EnvironmentOptions());
			throw new InvalidOperationException("resolve was expected to fail");
		}
		catch (EnvironmentResolutionException ex) {
			result = CommandExecutionResult.FromException(ex);
		}

		// Act
		string json = System.Text.Json.JsonSerializer.Serialize(result);

		// Assert
		json.Should().NotContain(Secret,
			because: "the exception-derived MCP envelope must remain secret-free (FR-11)");
	}

	// ---- Sink 6: shared error-text redactor (SensitiveErrorTextRedactor) --------------------------

	[Test]
	[Description("The shared MCP error-text redactor strips a seeded token from Bearer, cookie, and password shapes (FR-11).")]
	public void Redact_ShouldRemoveSecret_WhenSeededTokenAppearsInErrorText() {
		// Arrange — the three shapes a leaked credential could take in an inner-most exception message.
		string bearer = $"request failed: Authorization: Bearer {Secret}";
		string cookie = $"login failed with cookie={Secret}";
		string password = $"auth error password={Secret}";

		// Act
		string redactedBearer = SensitiveErrorTextRedactor.Redact(bearer);
		string redactedCookie = SensitiveErrorTextRedactor.Redact(cookie);
		string redactedPassword = SensitiveErrorTextRedactor.Redact(password);

		// Assert
		redactedBearer.Should().NotContain(Secret,
			because: "a Bearer token in an error message must be scrubbed before surfacing to the MCP client (FR-11)");
		redactedCookie.Should().NotContain(Secret,
			because: "a cookie value in an error message must be scrubbed before surfacing to the MCP client (FR-11)");
		redactedPassword.Should().NotContain(Secret,
			because: "a password value in an error message must be scrubbed before surfacing to the MCP client (FR-11)");
	}

	// ---- Sink 7: EnvironmentSettings serialization (AC-04) ----------------------------------------
	// AC-04's ShowSettingsTo / JSON / YAML absence is authoritatively covered by
	// Common/EnvironmentSettingsTests. This is a uniformity pin with the Story-13 seeded literal.

	[Test]
	[Description("Newtonsoft serialization of an ephemeral passthrough EnvironmentSettings omits the seeded token and cookie (AC-04, FR-11).")]
	public void JsonSerialize_ShouldOmitSecret_WhenSettingsCarrySeededCredentials() {
		// Arrange
		EnvironmentSettings settings = new() { Uri = Url, AccessToken = Secret, Cookie = Secret };

		// Act
		string json = JsonConvert.SerializeObject(settings);

		// Assert
		json.Should().NotContain(Secret,
			because: "[Newtonsoft.Json.JsonIgnore] keeps AccessToken/Cookie out of any settings dump (AC-04, FR-11)");
	}
}
