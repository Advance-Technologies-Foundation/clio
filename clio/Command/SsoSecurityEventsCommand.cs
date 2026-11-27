using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

#region Class: SsoSecurityEventsRequirement

/// <summary>
///     What the commands over the site's security-events settings service require before they run: the Creatio version
///     that ships the service, and the clio feature flag that hides these operator commands by default.
/// </summary>
public static class SsoSecurityEventsRequirement
{

	/// <summary>
	///     Minimum Creatio core version that ships the SsoSecurityEvents package and its settings service.
	/// </summary>
	public const string MinVersion = "10.2.0";

	/// <summary>
	///     Guidance shown when the target site runs an older Creatio.
	/// </summary>
	public const string MinVersionHint =
		"The security events settings service ships with the SsoSecurityEvents package of Creatio 10.2.";

	/// <summary>
	///     Feature key that gates every security-events command; enable it with
	///     <c>clio experimental --name sso-security-events --enable</c>.
	/// </summary>
	public const string FeatureName = "sso-security-events";

}

#endregion

#region Class: SsoSecurityEventsOptions

/// <summary>
///     Base options for the commands over the site's security-events settings service
///     (<c>SsoSecurityEventsSettingsService</c>, package SsoSecurityEvents, Creatio 10.2+).
/// </summary>
/// <remarks>
///     The version floor is declared here and inherited by every command; the feature flag is declared on each
///     command's own options class, because <see cref="FeatureToggleAttribute" /> is not inherited.
/// </remarks>
[RequiresCreatioVersion(SsoSecurityEventsRequirement.MinVersion, Hint = SsoSecurityEventsRequirement.MinVersionHint)]
public abstract class SsoSecurityEventsOptions : IdentityCommandOptions
{

	/// <summary>
	///     Code of the SSO provider the call is about; Creatio ID is always <c>identity-v3</c>.
	/// </summary>
	[Option("code", Required = true,
		HelpText = "Code of the SSO provider (Creatio ID is always identity-v3)")]
	public string Code { get; set; }

}

#endregion

#region Class: SsoSecurityEventsCommand

/// <summary>
///     Calls one method of the site's security-events settings service and reports its answer.
/// </summary>
/// <remarks>
///     The service answers every call with HTTP 200 and a reason code (<c>reason</c>, empty on success), never with a
///     failure. The commands therefore print the reason and exit 0 whatever it is; they exit 1 only when the call itself
///     failed: a refused option, no answer, an answer that is not the service's, or a site without the package (404).
///     Before any of that, clio's dispatch refuses the command with its own exit code when the site runs Creatio below
///     <see cref="SsoSecurityEventsRequirement.MinVersion" />, and refuses it outright while the
///     <see cref="SsoSecurityEventsRequirement.FeatureName" /> flag is off.
///     A call that changes the site is sent once, never retried or replayed, so a timed-out turn-off, test event or
///     remove is not repeated behind the caller's back.
/// </remarks>
/// <typeparam name="TOptions">The command's options.</typeparam>
public abstract class SsoSecurityEventsCommand<TOptions> : RemoteCommand<TOptions>
	where TOptions : SsoSecurityEventsOptions
{

	private const int MaxResponseExcerpt = 500;

	// The service's members are lower camel case by its data contract; a host that serialized them otherwise must
	// not turn a call that already changed the site into a reported failure.
	private static readonly JsonNodeOptions AnswerParseOptions = new() { PropertyNameCaseInsensitive = true };

	private static readonly HashSet<string> FreeTextFields = ["reasonDetail", "oauthError", "lastError"];

	private static readonly string[] AnswerFields = [
		"reasonDetail", "oauthError", "providerId", "isCreated", "testResult", "streamDeleted"
	];

	private static readonly string[] StatusFields = [
		"providerExists", "isCreatioId", "featureEnabled", "enabled", "siteAddress", "siteStatus",
		"streamStatus", "lastError", "lastTestResult", "lastTestOn", "changedByOperation", "changedOn",
		"isDisablePending"
	];

	private readonly IServiceUrlBuilder _serviceUrlBuilder;

	/// <summary>
	///     Initializes a new instance of the <see cref="SsoSecurityEventsCommand{TOptions}" /> class.
	/// </summary>
	protected SsoSecurityEventsCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
		IServiceUrlBuilder serviceUrlBuilder)
		: base(applicationClient, settings) {
		_serviceUrlBuilder = serviceUrlBuilder;
	}

	/// <summary>
	///     The service method this command calls.
	/// </summary>
	protected abstract ServiceUrlBuilder.KnownRoute Route { get; }

	/// <summary>
	///     Whether the call changes the site. Such a call is sent once and never replayed after a re-login.
	/// </summary>
	protected virtual bool ChangesSite => true;

	/// <inheritdoc />
	protected override string ServicePath => _serviceUrlBuilder.Build(Route);

	/// <summary>
	///     The named members of the request body; a <see langword="null" /> value is left out, and the service then
	///     uses the member's default.
	/// </summary>
	protected abstract IDictionary<string, object> BuildBody(TOptions options);

	/// <summary>
	///     Refuses options the service would refuse or misread, before anything is sent.
	/// </summary>
	/// <returns>The message to print, or <see langword="null" /> when the options are fine.</returns>
	protected virtual string Validate(TOptions options) => null;

	/// <summary>
	///     An option value with its surrounding spaces removed, or <see langword="null" /> when it is blank, so the
	///     member is left out and the site keeps or defaults the value.
	/// </summary>
	protected static string NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

	/// <summary>
	///     The request timeout in milliseconds.
	/// </summary>
	protected virtual int GetRequestTimeout(TOptions options) => RequestTimeout;

	/// <inheritdoc />
	protected override string GetRequestData(TOptions options) {
		Dictionary<string, object> body = BuildBody(options)
			.Where(member => member.Value is not null)
			.ToDictionary(member => member.Key, member => member.Value);
		return JsonSerializer.Serialize(body);
	}

	/// <inheritdoc />
	protected override void ExecuteRemoteCommand(TOptions options) {
		string refusal = string.IsNullOrWhiteSpace(options.Code) ? "--code must not be empty." : Validate(options);
		if (refusal is not null) {
			CommandSuccess = false;
			Logger.WriteError(refusal);
			return;
		}
		string requestData = GetRequestData(options);
		int timeout = GetRequestTimeout(options);
		string response;
		try {
			response = ChangesSite
				? ApplicationClient.ExecuteNonReplayablePostRequest(ServiceUri, requestData, timeout, 1, DelaySec)
				: ApplicationClient.ExecutePostRequest(ServiceUri, requestData, timeout, MaxAttempts, DelaySec);
		}
		catch (Exception exception) when (ChangesSite && !IsNotFound(exception)) {
			// The request may have reached the site before the answer was lost, so the caller must not read the
			// failure as "nothing changed" and repeat a turn-off, a test event or a remove.
			Logger.WriteWarning("The site may have applied the change before the call failed; check with "
				+ "get-sso-security-events before repeating it.");
			throw;
		}
		catch (Exception exception) when (IsNotFound(exception)) {
			CommandSuccess = false;
			Logger.WriteError("The site does not offer SsoSecurityEventsSettingsService (HTTP 404): security events "
				+ "are not supported there. The service comes with the SsoSecurityEvents package of Creatio 10.2 and "
				+ "later; check the version and that the environment's runtime flag (--IsNetCore) matches the site.");
			Logger.WriteError($"Endpoint: {ServiceUri}");
			return;
		}
		ProceedResponse(response, options);
	}

	/// <inheritdoc />
	protected override void ProceedResponse(string response, TOptions options) {
		if (string.IsNullOrWhiteSpace(response)) {
			CommandSuccess = false;
			Logger.WriteError("Empty response received from the site.");
			Logger.WriteError($"Endpoint: {ServiceUri}");
			return;
		}
		JsonObject answer = TryParseObject(response);
		if (answer is null || !answer.ContainsKey("reason")) {
			// An answer without a reason code is not the settings service's: a login page after a lost session, a
			// platform error page, or a proxy. Nothing can be said about what the site did.
			CommandSuccess = false;
			Logger.WriteError("Unexpected response; it is not an answer of SsoSecurityEventsSettingsService.");
			Logger.WriteError($"Endpoint: {ServiceUri}");
			Logger.WriteError(
				"Verify the environment is reachable, the credentials are valid, and the runtime flag "
				+ "(--IsNetCore) matches the target instance.");
			Logger.WriteError($"Response (first {MaxResponseExcerpt} chars): "
				+ UntrustedText.ForConsole(Truncate(response)));
			return;
		}
		if (options.Format == IdentityOutputFormat.Json) {
			Logger.WriteLine(IdentityOutput.Pretty(response));
			return;
		}
		WriteSummary(answer);
	}

	private void WriteSummary(JsonObject answer) {
		string reason = ReadText(answer, "reason")?.ReplaceLineEndings(" ");
		Logger.WriteLine($"reason: {(string.IsNullOrEmpty(reason) ? "none" : reason)}");
		foreach (string field in AnswerFields) {
			WriteField(answer, field);
		}
		if (answer["status"] is JsonObject status) {
			if (answer["providerId"] is null) {
				WriteField(status, "providerId");
			}
			foreach (string field in StatusFields) {
				WriteField(status, field);
			}
		}
	}

	private void WriteField(JsonObject source, string name) {
		string raw = ReadText(source, name);
		// The provider's own words can carry anything, a credential included, so they are scrubbed; the site's
		// structured values are not, because the operator acts on them (the site address after TestNotReceived).
		string value = FreeTextFields.Contains(name) ? UntrustedText.ForConsole(raw) : raw?.ReplaceLineEndings(" ");
		if (!string.IsNullOrEmpty(value)) {
			Logger.WriteLine($"{name}: {value}");
		}
	}

	private static string ReadText(JsonObject source, string name) {
		JsonNode node = source[name];
		if (node is not JsonValue value) {
			return null;
		}
		return value.GetValueKind() switch {
			JsonValueKind.String => value.GetValue<string>(),
			JsonValueKind.True => "true",
			JsonValueKind.False => "false",
			JsonValueKind.Number => value.ToJsonString(),
			var _ => null
		};
	}

	private static JsonObject TryParseObject(string raw) {
		try {
			return JsonNode.Parse(raw, AnswerParseOptions) as JsonObject;
		}
		catch (JsonException) {
			return null;
		}
	}

	private static string Truncate(string value) =>
		value.Length <= MaxResponseExcerpt ? value : value[..MaxResponseExcerpt] + "…";

	// Both transports clio runs on surface a 404 this way: HttpClient as HttpRequestException.StatusCode and the
	// legacy WebRequest path as WebException.Response, anywhere in the chain.
	private static bool IsNotFound(Exception exception) {
		Stack<Exception> pending = new();
		pending.Push(exception);
		while (pending.Count > 0) {
			Exception current = pending.Pop();
			if (current is HttpRequestException { StatusCode: HttpStatusCode.NotFound }
				|| current is WebException { Response: HttpWebResponse { StatusCode: HttpStatusCode.NotFound } }) {
				return true;
			}
			if (current is AggregateException aggregate) {
				foreach (Exception inner in aggregate.InnerExceptions) {
					pending.Push(inner);
				}
			}
			else if (current.InnerException is not null) {
				pending.Push(current.InnerException);
			}
		}
		return false;
	}

}

#endregion
