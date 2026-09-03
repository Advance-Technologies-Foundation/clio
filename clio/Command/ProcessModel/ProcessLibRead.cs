namespace Clio.Command.ProcessModel;

using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

/// <summary>
/// The one place that decides which exceptions a read of the process-library view degrades on.
/// </summary>
/// <remarks>
/// <para>
/// Two call sites read <c>VwProcessLib</c> and must both answer rather than throw — the version reader and
/// the describe caption resolver. Keeping the list here is not tidiness: the two ladders were written
/// separately and drifted apart on the first edit, and a caller that catches one type fewer turns a
/// degraded answer into an unhandled exception inside the MCP server.
/// </para>
/// <para>
/// <see cref="OperationCanceledException"/> is the load-bearing entry. <c>Creatio.Client</c> posts through
/// <see cref="System.Net.Http.HttpClient"/>, whose timeout raises <see cref="TaskCanceledException"/> —
/// derived from it — and NOT <see cref="TimeoutException"/>. Catching only the latter reads as timeout
/// coverage while providing none. <see cref="TimeoutException"/> stays because the synchronous
/// <see cref="WebRequest"/> path under ATF can still raise it. Neither path takes a cancellation token, so
/// nothing here can swallow a caller's deliberate cancellation.
/// </para>
/// <para>
/// ATF's own expression exceptions are deliberately absent: they mean the LINQ at the call site is wrong,
/// which is a defect to surface rather than a fact that could not be established.
/// </para>
/// </remarks>
internal static class ProcessLibRead {

	/// <summary>
	/// Runs a process-library read, mapping the DataService failure surface onto an answer.
	/// </summary>
	/// <typeparam name="T">The call site's answer type.</typeparam>
	/// <param name="read">The read to perform.</param>
	/// <param name="onFailure">Builds the degraded answer from the failure.</param>
	/// <returns>The read's result, or <paramref name="onFailure"/> applied to the failure it raised.</returns>
	internal static T Guarded<T>(Func<T> read, Func<Exception, T> onFailure) {
		try {
			return read();
		} catch (WebException e) {
			return onFailure(e);
		} catch (HttpRequestException e) {
			return onFailure(e);
		} catch (OperationCanceledException e) {
			return onFailure(e);
		} catch (JsonException e) {
			return onFailure(e);
		} catch (TimeoutException e) {
			return onFailure(e);
		} catch (InvalidOperationException e) {
			return onFailure(e);
		}
	}
}
