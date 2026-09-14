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
/// <para>
/// <see cref="InvalidOperationException"/> is the defensive entry, and it is here for the shape of failure
/// that reaches these two call sites LATEST: <c>IDataProvider</c> is registered as a
/// <c>LazyDataProvider</c>, so the real provider is constructed on first use and <see cref="System.Lazy{T}"/>
/// rethrows that construction failure at the read rather than at DI resolution. It is NOT a
/// <c>catch (Exception)</c> wearing a narrower name — <c>project-context.md</c> forbids that, and widening
/// this entry to <c>SystemException</c> or <see cref="Exception"/> would be that same prohibition broken.
/// </para>
/// <para>
/// What made it worth keeping rather than dropping is that it cannot swallow the class of defect the
/// paragraph above says must surface. Measured on ATF.Repository 2.0.3.1: a wrong call-site LINQ expression
/// raises <c>ATF.Repository.Exceptions.ExpressionConvertException</c>, which is absent from this ladder by
/// design, and a response carrying no items — a null or unsuccessful <c>IItemsResponse</c> — does not throw
/// at all, it yields no rows. So the two failures a reader would fear this entry hides are not
/// <see cref="InvalidOperationException"/>, and the entry costs nothing it should not.
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

	/// <summary>
	/// Runs a process-library read under a wall-clock budget, answering <paramref name="onExpiry"/> when it
	/// runs out.
	/// </summary>
	/// <typeparam name="T">The call site's answer type.</typeparam>
	/// <param name="budget">How long the caller is prepared to wait in total.</param>
	/// <param name="read">The read to perform.</param>
	/// <param name="onExpiry">Builds the degraded answer when the budget runs out.</param>
	/// <returns>The read's result, or <paramref name="onExpiry"/> when it did not finish in time.</returns>
	/// <remarks>
	/// <para>
	/// <see cref="Guarded{T}"/> degrades a read that FAILS and can do nothing about one that is merely SLOW,
	/// which on a surface bounded by a response deadline is the same loss with none of the diagnosis. The
	/// read has no timeout of its own to set: <c>RemoteDataProvider</c> takes none, so its ceiling is the ATF
	/// library's default.
	/// </para>
	/// <para>
	/// The abandoned read is NOT cancelled, and cannot be — neither the ATF synchronous path nor
	/// <c>Creatio.Client</c> accepts a token — so it runs to completion on a pool thread with nobody waiting.
	/// Acceptable only because every caller here is read-only and has already been answered. Its exception is
	/// observed rather than left to <c>TaskScheduler.UnobservedTaskException</c>, and a read that DID finish
	/// is unwrapped through the awaiter so an ATF expression failure still surfaces as itself rather than
	/// inside an <see cref="AggregateException"/> the ladder above was never written against.
	/// </para>
	/// </remarks>
	internal static T WithinBudget<T>(TimeSpan budget, Func<T> read, Func<T> onExpiry) {
		Task<T> running = Task.Run(read);
		bool finished;
		try {
			finished = running.Wait(budget);
		} catch (AggregateException) {
			finished = true;
		}
		if (finished) {
			return running.GetAwaiter().GetResult();
		}
		running.ContinueWith(abandoned => _ = abandoned.Exception,
			TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
		return onExpiry();
	}
}
