using System;
using System.Collections.Generic;
using System.IO;
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
/// Story 13 + Story 15b (ENG-93208, FR-11/FR-16) secret-hygiene matrix. A single DISTINCTIVE secret
/// literal is seeded as the accessToken / cookie / password across every sink the
/// credential-passthrough path touches, and each test asserts the seeded literal is ABSENT from that
/// sink's output. Story 13 pinned the per-sink audits (header-parse error, FR-19 / FR-12 resolution
/// errors, cache keys, the MCP <see cref="CommandExecutionResult"/> for a failing passthrough resolve,
/// the shared error-text redactor, and <see cref="EnvironmentSettings"/> serialization). Story 15b
/// completes the EXHAUSTIVE cross-sink matrix by adding the sinks Story 13 left open:
/// <list type="bullet">
/// <item><description>the MCP tool response + <c>execution-log-messages</c> for a command whose
/// <c>Execute</c> THROWS an exception carrying the seeded secret — the
/// <see cref="CommandExecutionResult.FromException"/> catch-all path that
/// <see cref="Clio.Command.McpServer.McpToolErrorFilter"/> never sees because the envelope is RETURNED,
/// not thrown. Redaction here is scoped to PASSTHROUGH requests (FIX B, ENG-93208): the passthrough
/// tenant-key signal drives <c>redactSensitive</c>, so the passthrough -1 is scrubbed while the trusted
/// stdio / -e -1 stays full-fidelity;</description></item>
/// <item><description>the inner-exception-chain variant of the same path (FormatExceptionChain walks up
/// to depth 5);</description></item>
/// <item><description>CLI stdout — the <c>ShowSettingsTo</c> serializer configuration used to dump the
/// settings file.</description></item>
/// </list>
/// Mirrors the <c>Common/BrowserSession/CreatioAuthClient</c> "names only, never values" discipline.
/// Console-log / file-log absence and the no-write-to-disk contract are authoritatively covered by
/// <c>ToolCommandResolverNoWriteTests</c> and <c>Common/EnvironmentSettingsTests</c> respectively.
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

	// ---- Sink 8: CLI stdout — ShowSettingsTo dump (AC-04, FR-11) ----------------------------------

	[Test]
	[Description("The ShowSettingsTo serializer configuration used to dump settings to CLI stdout never emits the seeded passthrough access token or cookie (AC-04, FR-11).")]
	public void ShowSettingsSerializer_ShouldOmitSecret_WhenSettingsCarrySeededCredentials() {
		// Arrange — mirror the exact JsonSerializer configuration ShowSettingsTo uses to write settings to
		// a TextWriter (CLI stdout), so this pins the real CLI dump path, not a bespoke serialization. Only
		// the transient passthrough-injected secrets (AccessToken / Cookie) are seeded: those carry
		// [Newtonsoft.Json.JsonIgnore] so they never reach the dump. Password is a deliberately-persisted
		// field (it round-trips to appsettings.json) and is out of scope for the passthrough matrix.
		EnvironmentSettings settings = new() { Uri = Url, AccessToken = Secret, Cookie = Secret };
		JsonSerializer serializer = new() {
			Formatting = Formatting.Indented,
			NullValueHandling = NullValueHandling.Ignore
		};
		StringWriter writer = new();

		// Act
		serializer.Serialize(writer, settings);
		string stdout = writer.ToString();

		// Assert
		stdout.Should().NotContain(Secret,
			because: "the CLI 'show-settings' dump omits [Newtonsoft.Json.JsonIgnore]-marked AccessToken/Cookie, so no seeded passthrough secret reaches stdout (AC-04, FR-11)");
	}

	// ---- Sink 9: MCP tool response + execution-log-messages via the FromException catch-all --------
	// The -1 FromException envelope (BaseTool's ExecuteLocked catch-all) is NOT run through the redactor
	// by McpToolErrorFilter — the envelope is RETURNED, not thrown, so the filter never sees it. The
	// redaction is scoped to PASSTHROUGH requests only (FIX B, ENG-93208): the same tenant-key signal
	// RedactForPassthrough uses. On a passthrough request a command exception carrying the seeded secret
	// reaches NEITHER the serialized MCP response NOR its execution-log-messages; on the trusted
	// stdio / -e path the -1 text is preserved full-fidelity (no diagnosability regression).

	[Test]
	[Description("On a PASSTHROUGH request, a command whose Execute throws an exception carrying the seeded secret does not leak it into the MCP response or execution-log-messages (FR-11, FIX B).")]
	public void ExecuteLocked_ShouldRedactException_WhenPassthroughRequest() {
		// Arrange — the seeded secret rides inside a REDACTABLE shape (target URI + Bearer token) exactly
		// as an inner-most HTTP/data-layer failure would surface it, resolved under a PASSTHROUGH tenant key.
		Exception thrown = new InvalidOperationException(
			$"POST {Url}/0/ServiceModel/EntitySchemaService.svc failed — Authorization: Bearer {Secret}");
		ThrowingEnvToolHarness tool = ThrowingEnvToolHarness.WithTenantKey(
			$"{ToolCommandResolver.PassthroughKeyPrefix}{Url}:ABC123", thrown);

		// Act
		CommandExecutionResult result = tool.Execute(new PassthroughLogToolOptions { Uri = Url });
		string json = System.Text.Json.JsonSerializer.Serialize(result);

		// Assert
		result.ExitCode.Should().Be(-1,
			because: "an unanticipated command exception surfaces on the -1 FromException catch-all");
		json.Should().NotContain(Secret,
			because: "on a passthrough request the -1 FromException envelope is redacted before it crosses the MCP boundary (FR-11, FIX B)");
		foreach (LogMessage message in result.Output) {
			(message.Value?.ToString() ?? string.Empty).Should().NotContain(Secret,
				because: "no execution-log message in the returned passthrough envelope may carry the seeded secret (FR-11)");
		}
	}

	[Test]
	[Description("On a PASSTHROUGH request, a command exception whose INNER exception carries the seeded secret does not leak it through the FormatExceptionChain walk into the MCP response (FR-11, FIX B).")]
	public void ExecuteLocked_ShouldRedactInnerException_WhenPassthroughRequest() {
		// Arrange — the secret rides on the inner exception (chain depth 2) in a credential-pair shape,
		// resolved under a PASSTHROUGH tenant key.
		Exception inner = new InvalidOperationException($"auth handshake rejected password={Secret}");
		Exception outer = new InvalidOperationException("command failed while contacting the environment", inner);
		ThrowingEnvToolHarness tool = ThrowingEnvToolHarness.WithTenantKey(
			$"{ToolCommandResolver.PassthroughKeyPrefix}{Url}:DEF456", outer);

		// Act
		CommandExecutionResult result = tool.Execute(new PassthroughLogToolOptions { Uri = Url });
		string json = System.Text.Json.JsonSerializer.Serialize(result);

		// Assert
		json.Should().NotContain(Secret,
			because: "on a passthrough request FromException redacts the full formatted exception chain, so a secret on any inner exception is scrubbed (FR-11, FIX B)");
	}

	[Test]
	[Description("On a NON-passthrough (registry/stdio) request the -1 exception chain carrying the same secret text is preserved verbatim — no redaction, no diagnosability regression (FIX B boundary).")]
	public void ExecuteLocked_ShouldPreserveException_WhenNotPassthroughRequest() {
		// Arrange — identical secret-bearing exception, but resolved under a REGISTRY key (not passthrough).
		Exception thrown = new InvalidOperationException(
			$"POST {Url}/0/ServiceModel/EntitySchemaService.svc failed — Authorization: Bearer {Secret}");
		ThrowingEnvToolHarness tool = ThrowingEnvToolHarness.WithTenantKey("registry-env-key", thrown);

		// Act
		CommandExecutionResult result = tool.Execute(new PassthroughLogToolOptions { Uri = Url });
		string json = System.Text.Json.JsonSerializer.Serialize(result);

		// Assert
		result.ExitCode.Should().Be(-1,
			because: "an unanticipated command exception surfaces on the -1 FromException catch-all");
		json.Should().Contain(Secret,
			because: "the trusted stdio/-e path keeps full-fidelity -1 exception text; redaction is scoped to passthrough keys only (FIX B)");
	}

	// ---- Sink 10: passthrough log-channel redaction boundary (FIX 2, ENG-93208) ------------------
	// Story 13/15b redacted only the THROWN exception chain (FromException) and the per-call-site error
	// paths. FIX 2 closes the remaining channels: the SUCCESS-path flushedMessages, the FromException
	// priorLogs it prepends, and the McpLogNotifier payload — all previously UN-redacted. On a PASSTHROUGH
	// request those log-message values (which routinely carry the target URI/host) are now scrubbed before
	// the CommandExecutionResult crosses the MCP boundary; on the trusted stdio/-e path they are preserved
	// (no diagnosability regression). These two tests pin both sides of that boundary by seeding the secret
	// into the LOG BUFFER (not a thrown exception) of a command that SUCCEEDS.

	[Test]
	[Description("On a passthrough request, a redactable secret sitting in the command LOG BUFFER (not a thrown exception) is scrubbed from the returned execution-log-messages (FIX 2, FR-11).")]
	public void ExecuteLocked_ShouldRedactLogBuffer_WhenPassthroughRequest() {
		// Arrange — a successful command whose log buffer carries a Bearer/URI-shaped secret, resolved under
		// a PASSTHROUGH tenant key (the signal BaseTool uses to scope redaction to the public edge).
		string logLine = $"POST {Url}/0/ServiceModel/EntitySchemaService.svc — Authorization: Bearer {Secret}";
		PassthroughLogToolHarness tool = PassthroughLogToolHarness.WithTenantKey(
			$"{ToolCommandResolver.PassthroughKeyPrefix}{Url}:ABC123", logLine);

		// Act
		CommandExecutionResult result = tool.Execute(new PassthroughLogToolOptions { Uri = Url });

		// Assert
		result.ExitCode.Should().Be(0,
			because: "the command succeeded; only its log-message values are redacted, not the outcome");
		foreach (LogMessage message in result.Output) {
			(message.Value?.ToString() ?? string.Empty).Should().NotContain(Secret,
				because: "on a passthrough request every returned log-message value is redacted before it crosses the MCP boundary (FIX 2, FR-11)");
		}
	}

	[Test]
	[Description("On a NON-passthrough (registry/stdio) request the same log content is preserved verbatim — no redaction, no diagnosability regression (FIX 2 boundary).")]
	public void ExecuteLocked_ShouldPreserveLogBuffer_WhenNotPassthroughRequest() {
		// Arrange — identical secret-bearing log line, but resolved under a REGISTRY key (not passthrough).
		string logLine = $"POST {Url}/0/ServiceModel/EntitySchemaService.svc — Authorization: Bearer {Secret}";
		PassthroughLogToolHarness tool = PassthroughLogToolHarness.WithTenantKey("registry-env-key", logLine);

		// Act
		CommandExecutionResult result = tool.Execute(new PassthroughLogToolOptions { Uri = Url });

		// Assert
		bool secretPreserved = false;
		foreach (LogMessage message in result.Output) {
			if ((message.Value?.ToString() ?? string.Empty).Contains(Secret)) {
				secretPreserved = true;
			}
		}
		secretPreserved.Should().BeTrue(
			because: "the trusted stdio/-e path keeps full-fidelity logs; redaction is scoped to passthrough keys only (FIX 2)");
	}

	// ---- FIX 2 boundary harness -------------------------------------------------------------------
	// Options with a URI so the env-SENSITIVE path is taken (UsesEnvironmentlessResolution → false),
	// which threads the resolver's LastResolvedTenantKey into ExecuteLocked — the passthrough signal.
	private sealed class PassthroughLogToolOptions : EnvironmentOptions { }

	private sealed class NoopCommand : Command<PassthroughLogToolOptions> {
		public override int Execute(PassthroughLogToolOptions options) => 0;
	}

	// A command whose Execute throws, so ExecuteLocked's catch-all builds the -1 FromException envelope
	// under test on the env-sensitive path.
	private sealed class ThrowingEnvCommand(Exception toThrow) : Command<PassthroughLogToolOptions> {
		public override int Execute(PassthroughLogToolOptions options) => throw toThrow;
	}

	// FIX B boundary harness for the -1 catch-all. Exercises the env-sensitive
	// InternalExecute<TCommand> → ExecuteLocked path with a substitute resolver whose GetTenantKey decides
	// passthrough vs registry — the SAME signal FromException's redactSensitive keys off (the InternalExecute
	// path reserves the guard BEFORE Acquire using GetTenantKey, review #5) — and a throwing command so the
	// catch-all produces the -1 envelope. The logger is silent (empty LogMessages) so the only channel for
	// the seeded secret is the thrown exception chain.
	private sealed class ThrowingEnvToolHarness(
		Command<PassthroughLogToolOptions> command, ILogger logger, IToolCommandResolver resolver)
		: BaseTool<PassthroughLogToolOptions>(command, logger, resolver) {

		public CommandExecutionResult Execute(PassthroughLogToolOptions options) =>
			InternalExecute<ThrowingEnvCommand>(options);

		public static ThrowingEnvToolHarness WithTenantKey(string tenantKey, Exception toThrow) {
			ThrowingEnvCommand command = new(toThrow);
			ILogger logger = Substitute.For<ILogger>();
			logger.LogMessages.Returns(new List<LogMessage>());
			IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
			resolver.Resolve<ThrowingEnvCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);
			// In production GetTenantKey and LastResolvedTenantKey yield the SAME key (both go through
			// BuildPassthroughCacheKey / ResolveSettingsAndKey); the InternalExecute path keys off GetTenantKey.
			resolver.GetTenantKey(Arg.Any<EnvironmentOptions>()).Returns(tenantKey);
			resolver.LastResolvedTenantKey.Returns(tenantKey);
			return new ThrowingEnvToolHarness(command, logger, resolver);
		}
	}

	// Exercises the env-sensitive InternalExecute<TCommand> → ExecuteLocked path with a substitute resolver
	// whose GetTenantKey decides passthrough vs registry, and a substitute logger pre-seeded with a
	// secret-bearing log line so ExecuteLocked flushes it into the returned envelope.
	private sealed class PassthroughLogToolHarness(
		Command<PassthroughLogToolOptions> command, ILogger logger, IToolCommandResolver resolver)
		: BaseTool<PassthroughLogToolOptions>(command, logger, resolver) {

		public CommandExecutionResult Execute(PassthroughLogToolOptions options) =>
			InternalExecute<NoopCommand>(options);

		public static PassthroughLogToolHarness WithTenantKey(string tenantKey, string seededLogLine) {
			NoopCommand command = new();
			ILogger logger = Substitute.For<ILogger>();
			logger.LogMessages.Returns(new List<LogMessage> { new InfoMessage(seededLogLine) });
			IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
			resolver.Resolve<NoopCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);
			// In production GetTenantKey and LastResolvedTenantKey yield the SAME key; the InternalExecute
			// path keys the lock / in-flight guard / redaction off GetTenantKey (review #5).
			resolver.GetTenantKey(Arg.Any<EnvironmentOptions>()).Returns(tenantKey);
			resolver.LastResolvedTenantKey.Returns(tenantKey);
			return new PassthroughLogToolHarness(command, logger, resolver);
		}
	}
}
