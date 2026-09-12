using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Authentication;
using System.Text.Json;
using System.Threading.Tasks;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using Clio.Common;

namespace Clio.Command;

/// <inheritdoc cref="ISysSettingFailureClassifier"/>
public sealed class SysSettingFailureClassifier : ISysSettingFailureClassifier {

	private readonly ILogger _logger;
	private readonly IOperationCorrelationIdProvider _correlationIds;

	/// <summary>
	/// Creates the classifier.
	/// </summary>
	/// <param name="logger">The sink the failure line and the debug excerpt are written to.</param>
	/// <param name="correlationIds">Mints the ID the envelope and the log line share.</param>
	public SysSettingFailureClassifier(ILogger logger, IOperationCorrelationIdProvider correlationIds) {
		_logger = logger;
		_correlationIds = correlationIds;
	}

	/// <inheritdoc/>
	public SysSettingFailure Categorize(Exception ex, string operationLabel, string correlationId) {
		//The Creatio client reaches transport faults through Task.Result, which wraps them in an
		//AggregateException. Switching on the outer type alone therefore saw the wrapper, not the
		//fault, and an aggregate carrying an AuthenticationException or a typed 401 fell through to
		//the generic "Failed ..." - losing exactly the credential diagnosis this command exists to
		//report.
		Exception fault = UnwrapTransportFault(ex);
		return fault switch {
			//A transport TIMEOUT, not a cancellation. HttpClient surfaces its own timeout as a
			//TaskCanceledException, and nothing on the sys-setting paths supplies a cancellation token,
			//so a task that "cancels" itself did so because the environment stopped answering. Without
			//this arm the type matched nothing and a timeout was reported as an unknown failure with
			//"retry the operation" - advice that makes an agent loop against a silent environment.
			//Carried over from SysSettingCodes.ClassifyReadFailure, which already made this call, so the
			//two classifiers agree on what a timeout is.
			TaskCanceledException => Network(operationLabel, correlationId),
			HttpRequestException httpEx when IsAuthenticationFailure(httpEx)
				=> Authentication(operationLabel, correlationId),
			HttpRequestException => Network(operationLabel, correlationId),
			WebException webEx when IsAuthenticationFailure(webEx)
				=> Authentication(operationLabel, correlationId),
			WebException => Network(operationLabel, correlationId),
			SocketException => Network(operationLabel, correlationId),
			UnauthorizedAccessException => Authentication(operationLabel, correlationId),
			//UNCONDITIONAL, and before the AuthenticationException arms. SessionRejectedException is
			//only ever raised where the rejection was already PROVEN (a raw body carrying Creatio's
			//auth-routing markers, or a corroborated provider verdict), so re-asking the question is
			//not merely redundant - it is wrong. The classifier would run the TLS-prose regex over
			//this exception's own message, and that message interpolates the operation label, which
			//carries the caller's operand: reading sys-setting 'SslCertificateThumbprint' matches
			///certificate/ and flipped a proven credential rejection to "Network error".
			SessionRejectedException => Authentication(operationLabel, correlationId),
			//A bare AuthenticationException is asked the same question as the wrapped ones: the framework
			//raises this type for a TLS handshake too, and a bad server certificate reported as rejected
			//credentials hides the only diagnosis that leads to the fix.
			AuthenticationException authEx when IsAuthenticationFailure(authEx)
				=> Authentication(operationLabel, correlationId),
			AuthenticationException => Network(operationLabel, correlationId),
			//An aggregate that carries several distinct faults is not unwrapped, because no single
			//inner represents it - but a credential failure among them still has to be reported as one.
			AggregateException aggregate when IsAuthenticationFailure(aggregate)
				=> Authentication(operationLabel, correlationId),
			//Issue #1378: the DIAGNOSED form of the arm below, raised by ISysSettingsManager's write
			//endpoints, which hold the raw body and so can say what arrived and keep a neutralized
			//excerpt on ServerDetail. It produces the IDENTICAL envelope - same Error, same Network
			//category, same cause and recovery - so no agent branching on error-category and no MCP
			//assertion changes; what improves is the debug line and the exception's own message.
			//Placed ABOVE the DataProviderFailureException and InvalidOperationException arms, which it
			//would otherwise be captured by: NonJsonWriteResponseException derives from
			//InvalidOperationException, and being reported as ProviderFailure would claim the data
			//provider returned an unsuccessful response - which a gateway page is not.
			//The wrong-SHAPE half keeps the same category - the request still did not reach the service
			//it was meant for - but must not repeat the not-JSON cause, which names a proxy or WAF page
			//that demonstrably is not what answered.
			NonJsonWriteResponseException {
				Kind: NonJsonWriteResponseKind.UnexpectedShape
			} => new SysSettingFailure(
				$"Creatio returned a response of an unexpected shape {operationLabel}.",
				SysSettingErrorCategories.Network, SysSettingFailureTexts.UnexpectedResponseShapeCause,
				SysSettingFailureTexts.UnexpectedResponseShapeRecovery, correlationId),
			NonJsonWriteResponseException => new SysSettingFailure(
				$"Creatio returned a non-JSON response {operationLabel}.",
				SysSettingErrorCategories.Network, SysSettingFailureTexts.NonJsonResponseCause,
				SysSettingFailureTexts.NonJsonResponseRecovery, correlationId),
			//KEPT, and still reachable (PR review). Since issue #1378 the sys-settings write endpoints
			//raise the diagnosed NonJsonWriteResponseException above, but the parser fault can also come
			//from INSIDE the client: Creatio.Client throws JsonException itself rather than returning
			//the body when the server answers an upload or a re-authenticated call with its login page -
			//the shape CreatioClientAdapterReauthTests and ReauthExecutorTests pin. That fault never
			//reaches clio's own deserialize, so the arm above cannot classify it, and without this one
			//it would fall through to Unknown ("no cause could be determined").
			JsonException => new SysSettingFailure(
				$"Creatio returned a non-JSON response {operationLabel}.",
				SysSettingErrorCategories.Network, SysSettingFailureTexts.NonJsonResponseCause,
				SysSettingFailureTexts.NonJsonResponseRecovery, correlationId),
			//BOUNDED and REDACTED wherever an exception MESSAGE is promoted into a caller-visible field.
			//These arms return the message of ANY exception of those types raised anywhere below, and such
			//messages are unbounded and can carry paths, URLs or response fragments.
			ArgumentException argEx => new SysSettingFailure(SafeDetail(argEx.Message),
				SysSettingErrorCategories.Validation, SafeDetail(argEx.Message),
				SysSettingFailureTexts.ValidationRecovery, correlationId),
			//DataProviderFailureException is the one InvalidOperationException whose message IS the
			//diagnosis - it is composed locally by ClassifyingDataProvider from a response that carries
			//no exception of its own. An ordinary InvalidOperationException keeps its message too (that
			//is the pre-existing behaviour) but is not claimed to be a provider verdict.
			DataProviderFailureException providerEx => new SysSettingFailure(SafeDetail(providerEx.Message),
				SysSettingErrorCategories.ProviderFailure, SafeDetail(providerEx.Message),
				SysSettingFailureTexts.ProviderFailureRecovery, correlationId),
			InvalidOperationException invEx => new SysSettingFailure(SafeDetail(invEx.Message),
				SysSettingErrorCategories.Unknown, SafeDetail(invEx.Message),
				SysSettingFailureTexts.UnknownRecovery, correlationId),
			//An unresolvable environment is a CONFIGURATION failure, not an unknown one. It used to
			//reach the fallback arm below and be reported as "no cause could be determined" with
			//"retry the operation" - advice that makes an agent loop, when the resolver had already
			//said exactly what to fix. The resolver's text is clio-local (EnvironmentNotFoundError,
			//settings-file paths), so it is safe as the cause; Error keeps the generic label so an
			//unregistered name is still not promoted into the headline message.
			//PR #1373 review: routed on the exception's OWN Reason, not on its type. Four of the resolver's
			//throw sites are authentication and target-URL rejections, and reporting those as
			//Configuration + "register the environment with reg-web-app" is advice a credential-passthrough
			//caller over mcp-http cannot act on - it has no environment to register - while an agent
			//branching on the category will not re-authenticate, because the category says the problem is
			//local configuration. Reason defaults to Configuration, so every unregistered-name site is
			//unchanged.
			EnvironmentResolutionException resolutionEx =>
				DescribeResolutionFailure(resolutionEx, operationLabel, correlationId),
			var _ => new SysSettingFailure($"Failed {operationLabel}.",
				SysSettingErrorCategories.Unknown, SysSettingFailureTexts.UnknownCause,
				SysSettingFailureTexts.UnknownRecovery, correlationId)

		};
	}

	/// <inheritdoc/>
	public SysSettingFailure CategorizeAndLog(Exception ex, string operationLabel) {
		SysSettingFailure failure = Categorize(ex, operationLabel, _correlationIds.New());
		LogFailureLine(failure);
		//Here too, not only in the command's instance ReportFailure (PR #1374 review): this member exists
		//BECAUSE other callers use it - the MCP tools' catch blocks - and those paths were getting a
		//correlation ID on the envelope with no matching debug line to bridge to.
		LogServerDetail(ex, failure.CorrelationId);
		return failure;
	}

	/// <inheritdoc/>
	public void LogFailureLine(SysSettingFailure failure) {
		string line = DescribeFailureForLog(failure);
		_logger.WriteError(line);
		McpLogNotifier.ForwardMessages([new ErrorMessage(line)], failure.CorrelationId);
	}

	/// <inheritdoc/>
	public void LogServerDetail(Exception ex, string correlationId) {
		//The WHOLE chain, not a single unwrap (PR #1374 review). UnwrapTransportFault only steps
		//through single-inner aggregates and TargetInvocationException, so a carrier re-wrapped by a
		//domain or transport exception - a SessionRejectedException inside an environment failure -
		//lost its excerpt silently: the envelope still looked complete and the operator grepped the
		//correlation ID and found nothing.
		string detail = FindServerDetail(ex);
		//Scrubbed and fenced even here, and that is load-bearing rather than belt-and-braces:
		//ConsoleLogger.WriteDebug suppresses the console DRAIN under MCP server mode but still
		//CAPTURES into the per-flow buffer BaseTool harvests into CommandExecutionResult.Messages. The
		//excerpt reaching this line is only control-character normalized and length-capped, so a
		//bearer token, a target URI or a credential pair inside it would otherwise be intact.
		string safeDetail = SensitiveErrorTextRedactor.RedactUntrustedOrNull(detail);
		if (safeDetail is null) {
			return;
		}
		_logger.WriteDebug($"(correlation-id: {correlationId}) server detail: {safeDetail}");
	}

	/// <summary>Renders a classified failure as one log line, correlation ID last.</summary>
	/// <remarks>
	/// Not on <see cref="ISysSettingFailureClassifier"/>: <see cref="LogFailureLine"/> is the only caller,
	/// and a second way to render the same line is how the two would drift.
	/// <para>
	/// The cause is omitted when it is the SAME string as the headline (PR #1374 review). Three arms of
	/// <see cref="Categorize"/> put one composed diagnostic into both <c>Error</c> and <c>Cause</c>, so
	/// this line printed it twice - and where that diagnostic carries the fenced server excerpt, twice
	/// meant two untrusted-source-text fences on one line.
	/// </para>
	/// </remarks>
	/// <param name="failure">The classified failure to render.</param>
	/// <returns>The single log line.</returns>
	internal string DescribeFailureForLog(SysSettingFailure failure) {
		string cause = string.Equals(failure.Error, failure.Cause, StringComparison.Ordinal)
			? string.Empty
			: $"Cause: {failure.Cause} ";
		return $"{failure.Error} {cause}Action: {failure.RecoveryAction} "
			+ $"(correlation-id: {failure.CorrelationId})";
	}

	/// <summary>
	/// Classifies an <see cref="EnvironmentResolutionException"/> by what it is actually about. The
	/// resolver's text is clio-local (a settings-file path, an allowlist reason, the missing auth kind), so
	/// it stays safe as the cause; <c>Error</c> keeps the generic label either way, so an unregistered name
	/// is still not promoted into the headline message.
	/// </summary>
	private static SysSettingFailure DescribeResolutionFailure(EnvironmentResolutionException resolutionEx,
		string operationLabel, string correlationId) {
		(string category, string recovery) = resolutionEx.Reason switch {
			EnvironmentResolutionReason.Authentication => (SysSettingErrorCategories.Authentication,
				SysSettingFailureTexts.PassthroughAuthenticationRecovery),
			EnvironmentResolutionReason.Validation => (SysSettingErrorCategories.Validation,
				SysSettingFailureTexts.RefusedTargetRecovery),
			var _ => (SysSettingErrorCategories.Configuration,
				SysSettingFailureTexts.ConfigurationRecovery),
		};
		//SafeDetail, like the three sibling arms of Categorize. "clio-local" is not the same as
		//"safe to emit": two resolver throw sites embed an absolute settings-file path verbatim, and on
		//Windows that path carries the OS account name. Redact turns it into [redacted-path] and leaves
		//the sentence ("clio settings bootstrap is broken. Repair ...") fully actionable.
		return new SysSettingFailure($"Failed {operationLabel}.", category, SafeDetail(resolutionEx.Message),
			recovery, correlationId);
	}

	private static SysSettingFailure Authentication(string operationLabel, string correlationId) =>
		new($"Authentication error {operationLabel}.", SysSettingErrorCategories.Authentication,
			SysSettingFailureTexts.AuthenticationCause,
			SysSettingFailureTexts.AuthenticationRecovery, correlationId);

	private static SysSettingFailure Network(string operationLabel, string correlationId) =>
		new($"Network error {operationLabel}.", SysSettingErrorCategories.Network,
			SysSettingFailureTexts.NetworkCause, SysSettingFailureTexts.NetworkRecovery,
			correlationId);

	/// <summary>
	/// The server excerpt of the first <see cref="IServerDetailCarrier"/> anywhere in the exception
	/// chain, including inside single-fault aggregates, or <see langword="null"/> when there is none.
	/// </summary>
	private static string FindServerDetail(Exception exception) {
		for (Exception current = exception; current is not null; current = current.InnerException) {
			if (current is IServerDetailCarrier carrier) {
				return carrier.ServerDetail;
			}
			if (current is AggregateException { InnerExceptions.Count: 1 } aggregate
					&& aggregate.InnerExceptions[0] is IServerDetailCarrier innerCarrier) {
				return innerCarrier.ServerDetail;
			}
		}
		return null;
	}

	// Cap on a message promoted into a user-visible field. 300 is what DataProviderFailureException's
	// detail already uses, so the two paths expose the same amount.
	private const int MaxPromotedMessageLength = 300;

	// Redaction runs BEFORE the cap, deliberately: SensitiveErrorTextRedactor matches a token as a whole
	// unit, so capping first can split one in half and leave the visible fragment unredacted. This is the
	// same order ServiceResponseJsonGuard.BuildPreview uses.
	// The cap itself goes through ClampPreservingFence rather than a raw slice, on two counts.
	// Surrogates: Redact only scrubs secrets, it does not touch surrogates, so an astral character
	// straddling the cap point would leave a lone high surrogate in SysSettingFailure.Error/.Cause - and
	// System.Text.Json throws on invalid UTF-16, failing the whole tool response instead of truncating
	// one message. Fences: DataProviderFailureException.Message arrives already composed AND fenced by
	// ServerReportedFailureText.ComposeMessage, so a blind cut removed the closing marker whenever the
	// platform's own ErrorMessage ran past roughly 214 characters - a length the SERVER chooses - and
	// every field emitted after it then read as untrusted to anything keying on the markers.
	private static string SafeDetail(string message) {
		if (string.IsNullOrEmpty(message)) {
			return message;
		}
		string redacted = SensitiveErrorTextRedactor.Redact(message);
		return SensitiveErrorTextRedactor.ClampPreservingFence(redacted, MaxPromotedMessageLength);
	}

	// Bounds every walk over an exception chain. A chain this deep is not something a transport
	// produces, and the bound is what keeps a hand-built or self-referencing chain from looping.
	private const int MaxExceptionUnwrapDepth = 16;

	/// <summary>
	/// Returns the exception that should be classified: a wrapper carrying exactly one fault unwraps
	/// to that fault, and everything else is returned unchanged.
	/// </summary>
	/// <remarks>
	/// A multi-fault <see cref="AggregateException"/> is deliberately NOT unwrapped - picking its first
	/// inner would report one of several failures as if it were the whole story. Those are handled by
	/// the aggregate arm in <see cref="Categorize"/> instead.
	/// </remarks>
	private static Exception UnwrapTransportFault(Exception exception) {
		Exception current = exception;
		for (int depth = 0; depth < MaxExceptionUnwrapDepth; depth++) {
			Exception inner = current switch {
				AggregateException aggregate when aggregate.InnerExceptions.Count == 1
					=> aggregate.InnerExceptions[0],
				TargetInvocationException { InnerException: { } target } => target,
				var _ => null
			};
			if (inner is null) {
				return current;
			}
			current = inner;
		}
		return current;
	}

	// A bounded 401 token, not any occurrence of the digits. "Connection refused at
	// http://localhost:40124" is a network error, and reporting it as rejected credentials sends the
	// operator off to fix a working login. The token must also stand alone, so a port or an id containing
	// 401 does not qualify.
	// Delegates to the one shared classifier so this layer and SysSettingsManager cannot answer the
	// same question differently. See AuthenticationFailureClassifier for why that mattered.
	private static bool IsAuthenticationFailure(Exception exception) =>
		AuthenticationFailureClassifier.IsAuthenticationFailure(exception);
}
