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
/// </remarks>
public sealed class SiteResponseCapture {

	#region Fields: Private

	private static readonly AsyncLocal<SiteResponseCapture> Ambient = new();

	private SiteResponse _last;

	#endregion

	#region Properties: Public

	/// <summary>The capture the current async flow records into, or <see langword="null"/> outside one.</summary>
	public static SiteResponseCapture Current => Ambient.Value;

	/// <summary>The last refusal recorded in this capture, or <see langword="null"/>.</summary>
	public SiteResponse Last => Volatile.Read(ref _last);

	#endregion

	#region Methods: Public

	/// <summary>
	/// Opens a capture for the current async flow; disposing the returned scope restores the capture that
	/// was current before, so a batch tool can open one per item inside the tool's own.
	/// </summary>
	/// <returns>The scope, whose <see cref="Scope.Capture"/> holds what is recorded while it is open.</returns>
	public static Scope Begin() {
		SiteResponseCapture previous = Ambient.Value;
		SiteResponseCapture capture = new();
		Ambient.Value = capture;
		return new Scope(capture, previous);
	}

	/// <summary>
	/// Records the site's answer to a call the write path has classified as refused. Does nothing outside a
	/// capture, or when the client did not keep the status.
	/// </summary>
	/// <param name="response">The answer as the client returned it.</param>
	/// <returns>The site response recorded, or <see langword="null"/> when nothing was recorded.</returns>
	public static SiteResponse RecordRefusal(SiteHttpResponse response) {
		SiteResponseCapture capture = Ambient.Value;
		if (capture is null) {
			return null;
		}
		SiteResponse siteResponse = SiteResponse.FromRefusal(response);
		if (siteResponse is not null) {
			Volatile.Write(ref capture._last, siteResponse);
		}
		return siteResponse;
	}

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
