using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.McpServer;

namespace Clio.Command.WebServiceDesigner;

/// <summary>
/// Keeps the replies of the web-service tools within <see cref="McpResultSize.InlineReplyBudgetBytes"/>, measured as the
/// agent receives them. A reply over it is written to a file by Copilot CLI and read back in pieces, one model request
/// per piece; a service with many methods, or a test call with a long response, would cost that on every call.
/// </summary>
internal static class WebServiceReplyBudget {

	/// <summary>The reply size the web-service tools keep within, in bytes.</summary>
	internal const int BudgetBytes = McpResultSize.InlineReplyBudgetBytes;

	/// <summary>
	/// A service reply within the budget: every method in full when that fits; otherwise as many as fit, the methods
	/// named in <paramref name="shownFirst"/> first and then in stored order, with the rest listed by code in
	/// <c>methods-not-shown</c> and a notice saying how to read them.
	/// </summary>
	/// <param name="result">The reply as built.</param>
	/// <param name="shownFirst">Codes of the methods the caller most needs in full, e.g. the ones a change touched.</param>
	/// <returns>The reply unchanged when it fits or has no service; otherwise the narrowed reply.</returns>
	internal static WebServiceToolResult Fit(WebServiceToolResult result, IReadOnlyCollection<string> shownFirst = null) {
		if (result?.Service?.Methods is not { Count: > 0 } methods || Fits(result)) {
			return result;
		}
		var first = new HashSet<string>(shownFirst ?? [], StringComparer.OrdinalIgnoreCase);
		List<WebServiceMethodView> byPriority = [.. methods.Where(m => first.Contains(m.Code ?? string.Empty)),
			.. methods.Where(m => !first.Contains(m.Code ?? string.Empty))];
		// The largest number of methods that fit; fewer methods never make the reply larger, so the search is binary.
		int fitting = 0;
		int low = 1;
		int high = methods.Count - 1;
		while (low <= high) {
			int middle = low + (high - low) / 2;
			if (Fits(Narrowed(result, methods, byPriority, middle))) {
				fitting = middle;
				low = middle + 1;
			} else {
				high = middle - 1;
			}
		}
		return Narrowed(result, methods, byPriority, fitting);
	}

	/// <summary>
	/// A test-call reply within the budget: the response body is cut first (<c>response-body-truncated</c>), and when the
	/// parsed response values alone leave no room, they are left out (<c>response-values-omitted</c>) and the body is cut
	/// again from its start.
	/// </summary>
	/// <param name="result">The reply as built, its body already masked.</param>
	/// <returns>The reply unchanged when it fits; otherwise the cut reply.</returns>
	internal static WebServiceTestResult Fit(WebServiceTestResult result) {
		if (result is null || Fits(result)) {
			return result;
		}
		WebServiceTestResult cut = CutBody(result);
		if (Fits(cut) || result.ResponseValues is null) {
			return cut;
		}
		return CutBody(result with { ResponseValues = null, ResponseValuesOmitted = true });
	}

	/// <summary>
	/// A service list within the budget: the services in order while they fit, and a notice naming how many were left out
	/// and the filter that narrows the list.
	/// </summary>
	/// <param name="result">The list as built.</param>
	/// <returns>The list unchanged when it fits; otherwise the cut list.</returns>
	internal static WebServiceListResult Fit(WebServiceListResult result) {
		if (result?.Services is not { Count: > 0 } services || Fits(result)) {
			return result;
		}
		WebServiceListResult Cut(int count) => result with {
			Services = services.Take(count).ToList(),
			Notices = [$"{services.Count - count} of the {services.Count} services are not listed, to keep this reply within "
				+ $"{BudgetBytes:N0} bytes. Call list-web-services again with filter (text of the code, caption or package)."]
		};
		int fitting = 0;
		int low = 1;
		int high = services.Count - 1;
		while (low <= high) {
			int middle = low + (high - low) / 2;
			if (Fits(Cut(middle))) {
				fitting = middle;
				low = middle + 1;
			} else {
				high = middle - 1;
			}
		}
		return Cut(fitting);
	}

	private static bool Fits<T>(T reply) => McpResultSize.Of(reply) <= BudgetBytes;

	private static WebServiceToolResult Narrowed(WebServiceToolResult result, IReadOnlyList<WebServiceMethodView> methods,
		List<WebServiceMethodView> byPriority, int shownCount) {
		var shownCodes = new HashSet<string>(byPriority.Take(shownCount).Select(m => m.Code ?? string.Empty),
			StringComparer.OrdinalIgnoreCase);
		List<WebServiceMethodView> shown = methods.Where(m => shownCodes.Contains(m.Code ?? string.Empty)).ToList();
		List<WebServiceMethodSummary> notShown = methods.Where(m => !shownCodes.Contains(m.Code ?? string.Empty))
			.Select(m => new WebServiceMethodSummary(m.Code, m.Caption, m.RequestParameters?.Count ?? 0,
				m.ResponseParameters?.Count ?? 0))
			.ToList();
		string notice = $"{notShown.Count} of the {methods.Count} methods are listed by code only (methods-not-shown) to "
			+ $"keep this reply within {BudgetBytes:N0} bytes; their parameters are unchanged. Read the ones you need with "
			+ "describe-web-service methods, a few codes at a time.";
		return result with {
			Service = result.Service with { Methods = shown, MethodsNotShown = notShown },
			Notices = [.. result.Notices ?? [], notice]
		};
	}

	// Cuts the body by the bytes the reply is over: every character costs at least one byte, so one cut is enough unless
	// the body is all there is to cut. A surrogate pair is never split.
	private static WebServiceTestResult CutBody(WebServiceTestResult result) {
		string body = result.ResponseBody;
		WebServiceTestResult cut = result;
		while (!string.IsNullOrEmpty(body) && !Fits(cut)) {
			long excess = McpResultSize.Of(cut) - BudgetBytes;
			int keep = (int)Math.Max(0, body.Length - Math.Max(excess, 1));
			if (keep > 0 && char.IsHighSurrogate(body[keep - 1])) {
				keep--;
			}
			body = body[..keep];
			cut = result with { ResponseBody = body, ResponseBodyTruncated = true };
		}
		return cut;
	}
}
