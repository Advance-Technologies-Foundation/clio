using System.Threading;

namespace Clio.Common;

/// <summary>The HTTP verb of a request sent through <see cref="IStatusPreservingApplicationClient"/>.</summary>
public enum SiteRequestVerb {

	/// <summary>POST.</summary>
	Post,

	/// <summary>PATCH.</summary>
	Patch,

	/// <summary>DELETE.</summary>
	Delete

}

/// <summary>
/// A client that can send a request and hand back the HTTP status together with the body.
/// </summary>
/// <remarks>
/// The string request methods of <see cref="IApplicationClient"/> return the body only: the Creatio client
/// underneath reads it for any status and drops the status. The write paths that report the site's answer
/// as <c>site-response</c> need that status, so they send through this interface instead, by way of
/// <see cref="SiteCall"/>, which falls back to the string methods - with an unknown status - for a client
/// that does not implement it.
/// </remarks>
public interface IStatusPreservingApplicationClient {

	/// <summary>
	/// Sends a request like the matching string method and returns its body together with its status.
	/// </summary>
	/// <param name="verb">The HTTP verb.</param>
	/// <param name="url">The absolute request URL.</param>
	/// <param name="requestData">The JSON body.</param>
	/// <param name="requestTimeout">The timeout in milliseconds, or <see cref="Timeout.Infinite"/>.</param>
	/// <param name="maxAttempts">How many times the transport may send it.</param>
	/// <param name="delaySec">The delay between attempts, in seconds.</param>
	/// <param name="replayAllowed">Whether an expired-session answer may re-send it after signing in again;
	/// <see langword="false"/> for a write that must not be committed twice.</param>
	/// <returns>The body and the status of the response the call ended with.</returns>
	SiteHttpResponse SendKeepingStatus(SiteRequestVerb verb, string url, string requestData,
		int requestTimeout = Timeout.Infinite, int maxAttempts = 1, int delaySec = 1, bool replayAllowed = true);

}

/// <summary>
/// Sends the write requests whose answer a tool reports as <c>site-response</c>, keeping the HTTP status when
/// the client can.
/// </summary>
/// <remarks>
/// Each method mirrors one string method of <see cref="IApplicationClient"/> - same verb, same replay rule - so
/// a call site can switch to it without changing what it sends. A client that does not implement
/// <see cref="IStatusPreservingApplicationClient"/> (a test double, a legacy client) is called through that
/// string method, and the answer then carries no status, so no <c>site-response</c> is reported for it.
/// </remarks>
public static class SiteCall {

	/// <summary>Sends a POST that may be replayed after a session expiry, like
	/// <see cref="IApplicationClient.ExecutePostRequest(string,string,int,int,int)"/>.</summary>
	public static SiteHttpResponse Post(IApplicationClient client, string url, string requestData,
		int requestTimeout = Timeout.Infinite, int maxAttempts = 1, int delaySec = 1) =>
		client is IStatusPreservingApplicationClient keeping
			? keeping.SendKeepingStatus(SiteRequestVerb.Post, url, requestData, requestTimeout, maxAttempts, delaySec,
				replayAllowed: true)
			: new SiteHttpResponse(null,
				client.ExecutePostRequest(url, requestData, requestTimeout, maxAttempts, delaySec));

	/// <summary>Sends a POST that is never replayed, like
	/// <see cref="IApplicationClient.ExecuteNonReplayablePostRequest"/>.</summary>
	public static SiteHttpResponse NonReplayablePost(IApplicationClient client, string url, string requestData,
		int requestTimeout = Timeout.Infinite, int maxAttempts = 1, int delaySec = 1) =>
		client is IStatusPreservingApplicationClient keeping
			? keeping.SendKeepingStatus(SiteRequestVerb.Post, url, requestData, requestTimeout, maxAttempts, delaySec,
				replayAllowed: false)
			: new SiteHttpResponse(null,
				client.ExecuteNonReplayablePostRequest(url, requestData, requestTimeout, maxAttempts, delaySec));

	/// <summary>Sends a PATCH, never replayed, like <see cref="IApplicationClient.ExecutePatchRequest"/>.</summary>
	public static SiteHttpResponse Patch(IApplicationClient client, string url, string requestData,
		int requestTimeout = Timeout.Infinite, int maxAttempts = 1, int delaySec = 1) =>
		client is IStatusPreservingApplicationClient keeping
			? keeping.SendKeepingStatus(SiteRequestVerb.Patch, url, requestData, requestTimeout, maxAttempts, delaySec,
				replayAllowed: false)
			: new SiteHttpResponse(null,
				client.ExecutePatchRequest(url, requestData, requestTimeout, maxAttempts, delaySec));

	/// <summary>Sends a DELETE, never replayed, like <see cref="IApplicationClient.ExecuteDeleteRequest"/>.</summary>
	public static SiteHttpResponse Delete(IApplicationClient client, string url, string requestData,
		int requestTimeout = Timeout.Infinite, int maxAttempts = 1, int delaySec = 1) =>
		client is IStatusPreservingApplicationClient keeping
			? keeping.SendKeepingStatus(SiteRequestVerb.Delete, url, requestData, requestTimeout, maxAttempts,
				delaySec, replayAllowed: false)
			: new SiteHttpResponse(null,
				client.ExecuteDeleteRequest(url, requestData, requestTimeout, maxAttempts, delaySec));

}
