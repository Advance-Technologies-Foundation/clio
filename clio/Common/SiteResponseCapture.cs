using System;
using System.Threading;

namespace Clio.Common;

/// <summary>
/// Holds, for one MCP tool call, the last answer a write path classified as the Creatio site refusing it, so
/// the tool can put it on its result as <c>site-response</c>.
/// </summary>
/// <remarks>
/// <para>
/// The call site that reads the site's answer is several layers below the tool that builds the result, and
/// the commands between them report failure through the logger and an exit code. Threading a value through
/// every one of those signatures would touch each command; instead the tool opens a capture around the work,
/// and the write path records into it. The capture flows with the async context, so concurrent tool calls
/// never see each other's answer, and a write path that runs outside any capture (the CLI) records nothing.
/// </para>
/// <para>
/// Only refusals are recorded, and only at the point where the write path turns the site's answer into its
/// failure, so a capture holds an answer that caused the failure the tool reports. The last refusal wins.
/// </para>
/// <para>
/// Each refusal also carries whether it proves the call applied nothing on the site; see
/// <see cref="RecordRefusalOfSoleWrite"/>. A refusal recorded through <see cref="RecordRefusal"/> never does.
/// </para>
/// </remarks>
public sealed class SiteResponseCapture {

	#region Fields: Private

	private static readonly AsyncLocal<SiteResponseCapture> Ambient = new();

	private readonly bool _soleWriteCall;

	private Recorded _last;

	#endregion

	#region Constructors: Private

	private SiteResponseCapture(bool soleWriteCall) => _soleWriteCall = soleWriteCall;

	#endregion

	#region Properties: Public

	/// <summary>The capture the current async flow records into, or <see langword="null"/> outside one.</summary>
	public static SiteResponseCapture Current => Ambient.Value;

	/// <summary>The last refusal recorded in this capture, or <see langword="null"/>.</summary>
	public SiteResponse Last => Volatile.Read(ref _last)?.Response;

	/// <summary>
	/// Whether the last refusal recorded in this capture proves the call applied nothing on the site; see
	/// <see cref="RecordRefusalOfSoleWrite"/>. <see langword="false"/> when nothing was recorded.
	/// </summary>
	public bool LastProvesNothingApplied => Volatile.Read(ref _last)?.NothingApplied ?? false;

	#endregion

	#region Methods: Public

	/// <summary>
	/// Opens a capture for the current async flow; disposing the returned scope restores the capture that
	/// was current before, so a batch tool can open one per item inside the tool's own.
	/// </summary>
	/// <param name="soleWriteCall">
	/// The tool's declaration that the work this capture covers - one tool call, or one item of a batch - sends
	/// exactly one write, with only reads before it. Only then can a refusal recorded through
	/// <see cref="RecordRefusalOfSoleWrite"/> prove that nothing was applied. A tool declares it only after
	/// checking its whole request sequence. A command never can: the same command may run inside a longer
	/// sequence of another tool, after that tool has written.
	/// </param>
	/// <returns>The scope, whose <see cref="Scope.Capture"/> holds what is recorded while it is open.</returns>
	public static Scope Begin(bool soleWriteCall = false) {
		SiteResponseCapture previous = Ambient.Value;
		SiteResponseCapture capture = new(soleWriteCall);
		Ambient.Value = capture;
		return new Scope(capture, previous);
	}

	/// <summary>
	/// Records the site's answer to a call the write path has classified as refused. Does nothing outside a
	/// capture, or when the client did not keep the status. The refusal does not prove that nothing was
	/// applied.
	/// </summary>
	/// <param name="response">The answer as the client returned it.</param>
	/// <returns>The site response recorded, or <see langword="null"/> when nothing was recorded.</returns>
	public static SiteResponse RecordRefusal(SiteHttpResponse response) => Record(response, false);

	/// <summary>
	/// Records the refusal of a write whose refusal ends the writing of the work that sent it. The refusal proves
	/// that nothing was applied only when all of these hold: the capture was opened with the sole-write
	/// declaration (<see cref="Begin"/>); the request was sent with a single attempt; and the answer shows the
	/// site refused it without processing it - a refusal status, a body that is no answer of the application, and
	/// no re-send by clio (<see cref="SiteHttpResponse.IsRefusedWithoutProcessing"/>). Otherwise it is recorded
	/// like <see cref="RecordRefusal"/>.
	/// </summary>
	/// <param name="response">The answer as the client returned it.</param>
	/// <param name="maxAttempts">
	/// The attempt count the request was sent with. With more than one, the client may have sent it again after
	/// a timeout, and the attempt that timed out may have been applied, so the refusal proves nothing.
	/// </param>
	/// <returns>The site response recorded, or <see langword="null"/> when nothing was recorded.</returns>
	/// <remarks>
	/// clio's own re-send after a body that looks like an expired session is a guess from the body, so such an answer
	/// is marked <see cref="SiteHttpResponse.Resent"/> and never proves anything. creatio.client's own re-send - after
	/// a 401, a sign-in redirect, or a 403 when the sign-in changed meanwhile - is not visible here. That is why only
	/// statuses decided by the request's shape count (<see cref="SiteHttpResponse.HasRefusalStatus"/>): the hidden
	/// first send of the same request was refused the same way.
	/// </remarks>
	public static SiteResponse RecordRefusalOfSoleWrite(SiteHttpResponse response, int maxAttempts) {
		bool refusedOnceWithoutProcessing = maxAttempts <= 1 && response is { IsRefusedWithoutProcessing: true };
		return Record(response, refusedOnceWithoutProcessing && Ambient.Value is { _soleWriteCall: true });
	}

	#endregion

	#region Methods: Private

	private static SiteResponse Record(SiteHttpResponse response, bool nothingApplied) {
		SiteResponseCapture capture = Ambient.Value;
		if (capture is null) {
			return null;
		}
		SiteResponse siteResponse = SiteResponse.FromRefusal(response);
		if (siteResponse is not null) {
			Volatile.Write(ref capture._last, new Recorded(siteResponse, nothingApplied));
		}
		return siteResponse;
	}

	private sealed record Recorded(SiteResponse Response, bool NothingApplied);

	#endregion

	#region Class: Scope

	/// <summary>An open capture; disposing it makes the previous capture current again.</summary>
	public sealed class Scope : IDisposable {

		private readonly SiteResponseCapture _previous;
		private bool _disposed;

		internal Scope(SiteResponseCapture capture, SiteResponseCapture previous) {
			Capture = capture;
			_previous = previous;
		}

		/// <summary>The capture this scope opened.</summary>
		public SiteResponseCapture Capture { get; }

		/// <summary>The last refusal recorded while this scope was open, or <see langword="null"/>.</summary>
		public SiteResponse Last => Capture.Last;

		/// <summary>Whether that refusal proves the call applied nothing on the site.</summary>
		public bool LastProvesNothingApplied => Capture.LastProvesNothingApplied;

		/// <summary>
		/// The <c>diagnostic</c> that goes beside <see cref="Last"/>: side effect <c>none</c> when the refusal
		/// proves the call applied nothing, otherwise <c>unknown</c>; <see langword="null"/> when nothing was
		/// recorded.
		/// </summary>
		public DataWriteDiagnostic LastDiagnostic {
			get {
				(SiteResponse response, bool nothingApplied) = LastRefusal;
				return response is null ? null : DataWriteDiagnostic.ForSiteRefusal(null, nothingApplied);
			}
		}

		/// <summary>
		/// The last refusal and whether it proves the call applied nothing, read together so they always belong
		/// to the same refusal.
		/// </summary>
		public (SiteResponse Response, bool NothingApplied) LastRefusal {
			get {
				Recorded recorded = Volatile.Read(ref Capture._last);
				return (recorded?.Response, recorded?.NothingApplied ?? false);
			}
		}

		/// <inheritdoc />
		public void Dispose() {
			if (_disposed) {
				return;
			}
			_disposed = true;
			Ambient.Value = _previous;
		}

	}

	#endregion

}
