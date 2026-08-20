using System;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Threading;

namespace Clio.Common;

/// <inheritdoc cref="ILoginDiagnostics" />
internal sealed class LoginDiagnostics : ILoginDiagnostics {
	#region Constants: Internal

	/// <summary>
	/// Prefix of the message <c>Creatio.Client.CreatioClient.Login()</c> throws when the login response
	/// body contains <c>"Code":1</c> (verified against creatio.client 1.0.38, which builds it as
	/// <c>"Unauthorized " + userName + " for " + AppUrl</c>). Matching it is how an implicit login
	/// rejection is told apart from an ordinary request failure — the NuGet client offers no typed
	/// signal for it. A future client that reworded the message would simply stop the implicit-login
	/// decoration; nothing else changes, and the explicit paths keep working.
	/// </summary>
	internal const string LoginRejectionMessagePrefix = "Unauthorized ";

	#endregion

	#region Fields: Private

	private readonly LoginAttemptScoreboard _scoreboard;
	private readonly string _clientId;
	private readonly long _createdAtTimestamp;
	private int _clientLoginCount;
	private int _clientRequestCount;

	#endregion

	#region Constructors: Public

	/// <summary>
	/// Creates a new <see cref="LoginDiagnostics"/> for one client instance.
	/// </summary>
	/// <param name="scoreboard">
	/// The process-wide counters. Defaults to <see cref="LoginAttemptScoreboard.Shared"/>, which is what
	/// makes the recorded in-flight figures span independently constructed clients — the exact situation
	/// issue #1106 needs measured. Tests pass their own instance for isolation.
	/// </param>
	public LoginDiagnostics(LoginAttemptScoreboard scoreboard = null) {
		_scoreboard = scoreboard ?? LoginAttemptScoreboard.Shared;
		// Short, log-friendly correlation token. Only has to be unique among the clients alive in one
		// process, so eight hex characters are plenty and keep the failure message readable.
		_clientId = Guid.NewGuid().ToString("N")[..8];
		_createdAtTimestamp = Stopwatch.GetTimestamp();
	}

	#endregion

	#region Methods: Public

	/// <inheritdoc />
	public void Track(Action login, LoginAttemptKind kind) {
		ArgumentNullException.ThrowIfNull(login);
		AttemptRecord record = BeginAttempt(kind);
		try {
			login();
		} catch (Exception exception)
			when (TryFindLoginRejection(exception, out UnauthorizedAccessException rejection)) {
			// Only the login-rejection shape is decorated, exactly like the request path below. Every
			// other failure — a transport fault, a timeout, a cancellation, a fatal type — propagates as
			// the same instance, so the callers that key on its type keep working: the remote command's
			// transport-fault arm still returns its exit code, the data-context command still renders its
			// not-found diagnostic, the readable-message extension still finds the inner transport fault,
			// the info command's fatal-type blocklist still recognises a fatal failure, and the MCP tool
			// error filter still lets a cancellation through for the ENG-93373 read-response deadline.
			throw Decorate(exception, rejection, record);
		} finally {
			EndAttempt(record);
		}
	}

	/// <inheritdoc />
	public T TrackRequest<T>(Func<T> request) {
		ArgumentNullException.ThrowIfNull(request);
		AttemptRecord record = BeginAttempt(LoginAttemptKind.Implicit);
		try {
			return request();
		} catch (Exception exception)
			when (TryFindLoginRejection(exception, out UnauthorizedAccessException rejection)) {
			throw Decorate(exception, rejection, record);
		} finally {
			EndAttempt(record);
		}
	}

	/// <inheritdoc />
	public void TrackRequest(Action request) {
		ArgumentNullException.ThrowIfNull(request);
		TrackRequest<object>(() => {
			request();
			return null;
		});
	}

	/// <summary>
	/// Finds the Creatio login rejection inside <paramref name="exception"/>, if there is one.
	/// </summary>
	/// <remarks>
	/// The chain is walked and every <see cref="AggregateException"/> is flattened because the NuGet
	/// client runs its transport through <c>Task.Result</c>, so faults can arrive wrapped — the same
	/// arrival shape this repository already unwraps in
	/// <c>EntitySchemaPublishHelper</c>, <c>TransientNetworkFailureClassifier</c>,
	/// <c>GetCreatioInfoCommand</c>, <c>ApplicationSectionCreateCommand</c> and <c>UserThemeApplier</c>.
	/// Matching them here means the decoration fires on the production arrival shape and not only on the
	/// bare exception a unit test constructs.
	/// </remarks>
	/// <param name="exception">The failure to inspect. May be <c>null</c>.</param>
	/// <param name="rejection">The rejection found, or <c>null</c>.</param>
	/// <returns><c>true</c> when a login rejection was found.</returns>
	internal static bool TryFindLoginRejection(Exception exception,
		out UnauthorizedAccessException rejection) {
		for (Exception current = exception; current is not null; current = current.InnerException) {
			if (current is AggregateException aggregate) {
				foreach (Exception inner in aggregate.Flatten().InnerExceptions) {
					if (TryFindLoginRejection(inner, out rejection)) {
						return true;
					}
				}
				// Flatten() already reached every branch; InnerException is just InnerExceptions[0].
				break;
			}
			if (current is UnauthorizedAccessException unauthorized
				&& unauthorized.Message.StartsWith(LoginRejectionMessagePrefix, StringComparison.Ordinal)) {
				rejection = unauthorized;
				return true;
			}
		}
		rejection = null;
		return false;
	}

	#endregion

	#region Methods: Private

	private AttemptRecord BeginAttempt(LoginAttemptKind kind) {
		// An implicit login cannot be observed as a call, only as the leading part of the request that
		// triggers it, so it is counted and gauged as a request. Keeping the login and request counters
		// apart means no figure has to be qualified when it is read off a failure message: a login
		// ordinal never silently counts requests that performed no login at all.
		int clientOrdinal;
		int processOrdinal;
		if (kind == LoginAttemptKind.Implicit) {
			clientOrdinal = Interlocked.Increment(ref _clientRequestCount);
			processOrdinal = _scoreboard.NextRequest();
			_scoreboard.EnterRequest();
		} else {
			clientOrdinal = Interlocked.Increment(ref _clientLoginCount);
			processOrdinal = _scoreboard.NextLogin();
			_scoreboard.EnterLogin();
		}
		return new AttemptRecord(kind, clientOrdinal, processOrdinal, _scoreboard.LoginsInFlight,
			_scoreboard.RequestsInFlight, DateTime.UtcNow, Stopwatch.GetTimestamp());
	}

	private void EndAttempt(AttemptRecord record) {
		if (record.Kind == LoginAttemptKind.Implicit) {
			_scoreboard.LeaveRequest();
		} else {
			_scoreboard.LeaveLogin();
		}
	}

	/// <param name="surfaced">The exception the callback actually threw; may wrap the rejection.</param>
	/// <param name="rejection">The login rejection found inside <paramref name="surfaced"/>.</param>
	/// <param name="record">What was captured when the attempt started.</param>
	private CreatioLoginFailedException Decorate(Exception surfaced, UnauthorizedAccessException rejection,
		AttemptRecord record) {
		// Read the gauges BEFORE the finally block releases this attempt, so the figures describe the
		// concurrency this login actually competed with rather than what is left after it.
		string context = BuildContext(surfaced, rejection, record, _scoreboard.LoginsInFlight,
			_scoreboard.RequestsInFlight);
		// The message is built from the rejection, not from the wrapper: an AggregateException's own
		// message would replace the server's "Unauthorized <user> for <url>" — the primary signal — with
		// "One or more errors occurred.".
		CreatioLoginFailedException failure = new($"{rejection.Message} [{context}]");
		failure.Data[CreatioLoginFailedException.OriginalExceptionDataKey] = surfaced.ToString();
		failure.Data[CreatioLoginFailedException.DiagnosticContextDataKey] = context;
		return failure;
	}

	private string BuildContext(Exception surfaced, UnauthorizedAccessException rejection,
		AttemptRecord record, int loginsInFlightAtFailure,
		int requestsInFlightAtFailure) {
		long now = Stopwatch.GetTimestamp();
		StringBuilder builder = new("clio-login");
		Append(builder, "kind", DescribeKind(record.Kind));
		Append(builder, "client", _clientId);
		// Named after what the ordinal counts, so an implicit-login failure never claims to be the
		// client's Nth *login* when it is really its Nth request.
		string ordinalScope = record.Kind == LoginAttemptKind.Implicit ? "request" : "login";
		Append(builder, $"client-{ordinalScope}", Number(record.ClientOrdinal));
		Append(builder, $"process-{ordinalScope}", Number(record.ProcessOrdinal));
		Append(builder, "in-flight-logins",
			$"{Number(record.LoginsInFlightAtStart)}/{Number(loginsInFlightAtFailure)}");
		Append(builder, "in-flight-requests",
			$"{Number(record.RequestsInFlightAtStart)}/{Number(requestsInFlightAtFailure)}");
		Append(builder, "started-at", record.StartedAtUtc.ToString("O", CultureInfo.InvariantCulture));
		Append(builder, "elapsed-ms", Milliseconds(now - record.StartedAtTimestamp));
		Append(builder, "since-client-created-ms", Milliseconds(now - _createdAtTimestamp));
		Append(builder, "original-type", rejection.GetType().Name);
		if (!ReferenceEquals(surfaced, rejection)) {
			// The rejection arrived wrapped, so record the wrapper too — otherwise the message would
			// claim a bare exception was thrown where a Task.Result fault was.
			Append(builder, "wrapped-in", surfaced.GetType().Name);
		}
		// The readable-message extension folds an inner WebException's transport status into its output.
		// This exception is intentionally inner-less (see CreatioLoginFailedException), so fold the same
		// signal in here — otherwise a 401-vs-connect-failure distinction would be lost on the CLI path.
		if (TryDescribeTransport(surfaced, out string transport)) {
			Append(builder, "transport", transport);
		}
		return builder.ToString();
	}

	private static string DescribeKind(LoginAttemptKind kind) => kind switch {
		LoginAttemptKind.Initial => "initial",
		LoginAttemptKind.Reauthentication => "reauth",
		_ => "implicit"
	};

	private static void Append(StringBuilder builder, string name, string value) {
		builder.Append(' ').Append(name).Append('=').Append(value);
	}

	private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

	private static string Milliseconds(long elapsedTimestampTicks) =>
		((double)elapsedTimestampTicks / Stopwatch.Frequency * 1000d)
		.ToString("F0", CultureInfo.InvariantCulture);

	private static bool TryDescribeTransport(Exception exception, out string description) {
		for (Exception current = exception; current is not null; current = current.InnerException) {
			if (current is not WebException webException) {
				continue;
			}
			description = webException.Response is HttpWebResponse httpResponse
				? $"{webException.Status}/{Number((int)httpResponse.StatusCode)}-{httpResponse.StatusCode}"
				: webException.Status.ToString();
			return true;
		}
		description = null;
		return false;
	}

	#endregion

	#region Classes: Private

	/// <summary>
	/// Everything captured at the start of one attempt. A data-only carrier; a struct because one is
	/// created per request and it never outlives the call.
	/// </summary>
	private readonly record struct AttemptRecord(
		LoginAttemptKind Kind,
		int ClientOrdinal,
		int ProcessOrdinal,
		int LoginsInFlightAtStart,
		int RequestsInFlightAtStart,
		DateTime StartedAtUtc,
		long StartedAtTimestamp);

	#endregion

	#region Classes: Internal

	/// <summary>
	/// Process-wide login and request counters. A plain state carrier: it holds numbers and hands them
	/// out, so it is created with <c>new</c> rather than resolved from DI.
	/// </summary>
	internal sealed class LoginAttemptScoreboard {
		#region Fields: Private

		private int _loginCount;
		private int _requestCount;
		private int _loginsInFlight;
		private int _requestsInFlight;

		#endregion

		#region Properties: Internal

		/// <summary>
		/// The scoreboard shared by every production <see cref="LoginDiagnostics"/> instance.
		/// </summary>
		internal static LoginAttemptScoreboard Shared { get; } = new();

		/// <summary>
		/// Number of clio-driven logins (explicit or re-authentication) currently in flight process-wide.
		/// </summary>
		internal int LoginsInFlight => Volatile.Read(ref _loginsInFlight);

		/// <summary>
		/// Number of requests currently in flight process-wide. A request that authenticates implicitly
		/// does so at its very beginning, so this is the closest observable proxy for concurrent implicit
		/// logins.
		/// </summary>
		/// <remarks>
		/// A <see cref="LoginAttemptKind.Reauthentication"/> record excludes its own triggering request:
		/// <c>ReauthExecutor.Execute</c> calls the request first and re-authenticates only after it
		/// returned or threw, by which point <see cref="TrackRequest{T}"/>'s <c>finally</c> has already
		/// released that request. So a reauth failure reports one fewer request than were logically
		/// active — typically <c>0/0</c> in an otherwise idle process. Read it as "besides the request
		/// that triggered this re-login".
		/// </remarks>
		internal int RequestsInFlight => Volatile.Read(ref _requestsInFlight);

		#endregion

		#region Methods: Internal

		/// <summary>Registers a clio-driven login as started.</summary>
		internal void EnterLogin() => Interlocked.Increment(ref _loginsInFlight);

		/// <summary>Registers a clio-driven login as finished, successfully or not.</summary>
		internal void LeaveLogin() => Interlocked.Decrement(ref _loginsInFlight);

		/// <summary>Registers a request as started.</summary>
		internal void EnterRequest() => Interlocked.Increment(ref _requestsInFlight);

		/// <summary>Registers a request as finished, successfully or not.</summary>
		internal void LeaveRequest() => Interlocked.Decrement(ref _requestsInFlight);

		/// <summary>Returns the process-wide ordinal of the next clio-driven login (1-based).</summary>
		internal int NextLogin() => Interlocked.Increment(ref _loginCount);

		/// <summary>Returns the process-wide ordinal of the next request (1-based).</summary>
		internal int NextRequest() => Interlocked.Increment(ref _requestCount);

		#endregion
	}

	#endregion
}
