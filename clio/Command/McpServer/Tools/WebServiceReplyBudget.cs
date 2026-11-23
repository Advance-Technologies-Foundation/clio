using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.WebServiceDesigner;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Keeps the replies of the web-service tools within <see cref="McpResultSize.InlineReplyBudgetBytes"/>, measured as the
/// agent receives them. A reply over it is written to a file by Copilot CLI and read back in pieces, one model request
/// per piece; a service with many methods, or a test call with a long response, would cost that on every call.
/// </summary>
/// <remarks>
/// Applied at the tool boundary, never inside <see cref="IWebServiceDesigner"/>: read-wsdl reads an existing service through
/// the designer and needs every method of it, and the test-call body is cut only after the designer masked it.
/// </remarks>
internal static class WebServiceReplyBudget {

	/// <summary>
	/// A service reply within the budget: every method in full when that fits; a single method always in full; otherwise
	/// as many as fit, the methods named in <paramref name="shownFirst"/> first and then in stored order, with the rest listed by code in
	/// <c>methods-not-shown</c> and a notice saying how to read them. Usages that still do not fit are cut last, with a
	/// notice.
	/// </summary>
	/// <param name="result">The reply as built.</param>
	/// <param name="shownFirst">Codes of the methods the caller most needs in full, e.g. the ones a change touched.</param>
	/// <returns>The reply unchanged when it fits or has no service; otherwise the narrowed reply.</returns>
	internal static WebServiceToolResult Fit(WebServiceToolResult result, IReadOnlyCollection<string> shownFirst = null) {
		if (result?.Service is null || Fits(result)) {
			return result;
		}
		IReadOnlyList<WebServiceMethodView> methods = result.Service.Methods ?? [];
		var first = new HashSet<string>(shownFirst ?? [], StringComparer.OrdinalIgnoreCase);
		List<WebServiceMethodView> byPriority = [.. methods.Where(m => first.Contains(m.Code ?? string.Empty)),
			.. methods.Where(m => !first.Contains(m.Code ?? string.Empty))];
		// A single method stays in full even when it alone passes the budget: otherwise describing it by code could never
		// show it. With several, a large first one must not drag the reply over: then every method is listed by code.
		WebServiceToolResult narrowed = methods.Count <= 1
			? result
			: Narrowed(result, methods, byPriority,
				LargestFitting(0, methods.Count - 1, count => Fits(Narrowed(result, methods, byPriority, count))));
		if (Fits(narrowed) || narrowed.Usages is not { Count: > 0 } usages) {
			return narrowed;
		}
		return WithUsages(narrowed, usages, LargestFitting(0, usages.Count - 1,
			count => Fits(WithUsages(narrowed, usages, count))));
	}

	/// <summary>
	/// A test-call reply within the budget: the response body is cut first (<c>response-body-truncated</c>), and when the
	/// parsed response values alone leave no room, they are left out (<c>response-values-omitted</c>) and the body is cut
	/// again from its start.
	/// </summary>
	/// <param name="result">The reply as the designer built it, its body already masked.</param>
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
				+ $"{McpResultSize.InlineReplyBudgetBytes:N0} bytes. Call list-web-services again with filter (text of the "
				+ "code, caption or package)."]
		};
		return Cut(LargestFitting(0, services.Count - 1, count => Fits(Cut(count))));
	}

	/// <summary>
	/// The largest count in <paramref name="min"/>..<paramref name="max"/> whose reply fits, or <paramref name="min"/> when
	/// none does. A smaller count never makes a reply larger, so the search is binary.
	/// </summary>
	private static int LargestFitting(int min, int max, Func<int, bool> fits) {
		int best = min;
		int low = min + 1;
		int high = max;
		while (low <= high) {
			int middle = low + (high - low) / 2;
			if (fits(middle)) {
				best = middle;
				low = middle + 1;
			} else {
				high = middle - 1;
			}
		}
		return best;
	}

	private static bool Fits<T>(T reply) => McpResultSize.Of(reply) <= McpResultSize.InlineReplyBudgetBytes;

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
			+ $"keep this reply within {McpResultSize.InlineReplyBudgetBytes:N0} bytes; their parameters are unchanged. Read "
			+ "the ones you need with describe-web-service methods, a few codes at a time (one when a method is large); never "
			+ "send an entry of methods-not-shown back as a method.";
		return result with {
			Service = result.Service with { Methods = shown, MethodsNotShown = notShown },
			Notices = [.. result.Notices ?? [], notice]
		};
	}

	private static WebServiceToolResult WithUsages(WebServiceToolResult result, IReadOnlyList<WebServiceUsage> usages,
		int count) => result with {
			Usages = usages.Take(count).ToList(),
			Notices = [.. result.Notices ?? [], $"{usages.Count - count} of the {usages.Count} usages are not listed, to keep "
				+ "this reply inline. describe-web-service with methods and usages: true lists the usages of those methods."]
		};

	// The longest start of the masked body that fits; a surrogate pair is never split.
	private static WebServiceTestResult CutBody(WebServiceTestResult result) {
		string body = result.ResponseBody;
		if (string.IsNullOrEmpty(body) || Fits(result)) {
			return result;
		}
		WebServiceTestResult Cut(int keep) {
			if (keep > 0 && char.IsHighSurrogate(body[keep - 1])) {
				keep--;
			}
			return result with { ResponseBody = body[..keep], ResponseBodyTruncated = true };
		}
		return Cut(LargestFitting(0, body.Length - 1, keep => Fits(Cut(keep))));
	}
}
