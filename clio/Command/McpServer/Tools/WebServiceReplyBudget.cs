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
	/// as many as fit, taken in priority order (the methods named in <paramref name="shownFirst"/>, then stored order) and
	/// skipping one that no longer fits in the room left, with the rest listed by code in
	/// <c>methods-not-shown</c> and a notice saying how to read them. The notices of the reply come first and are cut,
	/// with a count, only when they alone leave no room; the usages come last and fill the room that is left.
	/// </summary>
	/// <param name="result">The reply as built.</param>
	/// <param name="shownFirst">Codes of the methods the caller most needs in full, e.g. the ones a change touched.</param>
	/// <returns>The reply unchanged when it fits or has no service; otherwise the narrowed reply.</returns>
	internal static WebServiceToolResult Fit(WebServiceToolResult result, IReadOnlyCollection<string> shownFirst = null) {
		if (result?.Service is null || Fits(result)) {
			return result;
		}
		var reply = new ServiceReply(result);
		IReadOnlyList<WebServiceMethodView> methods = reply.Methods;
		var first = new HashSet<string>(shownFirst ?? [], StringComparer.OrdinalIgnoreCase);
		List<WebServiceMethodView> byPriority = [.. methods.Where(m => first.Contains(m.Code ?? string.Empty)),
			.. methods.Where(m => !first.Contains(m.Code ?? string.Empty))];
		// A single method stays in full even when it alone passes the budget: otherwise describing it by code could never
		// show it. Usages are left out while the methods are chosen: a modify reply lists the ones the caller acknowledged,
		// and describe lists them again for fewer methods.
		bool narrow = methods.Count > 1 && !Fits(reply.Build(reply.NoticeCount, null, 0));
		HashSet<WebServiceMethodView> fewest = narrow ? NoMethods() : null;
		int notices = Fits(reply.Build(reply.NoticeCount, fewest, 0))
			? reply.NoticeCount
			: LargestFitting(0, reply.NoticeCount - 1, count => Fits(reply.Build(count, fewest, 0)));
		HashSet<WebServiceMethodView> shown = narrow
			? Shown(byPriority, set => Fits(reply.Build(notices, set, 0)))
			: null;
		int usages = LargestFitting(0, reply.UsageCount, count => Fits(reply.Build(notices, shown, count)));
		return reply.Build(notices, shown, usages);
	}

	/// <summary>
	/// A test-call reply within the budget: a platform error longer than <see cref="MaxErrorCharacters"/> is cut first, then
	/// the response body (<c>response-body-truncated</c>), and when the parsed response values alone leave no room, they
	/// are left out (<c>response-values-omitted</c>) and the body is cut again from its start.
	/// </summary>
	/// <param name="result">The reply as the designer built it, its body and error already masked.</param>
	/// <returns>The reply unchanged when it fits; otherwise the cut reply.</returns>
	internal static WebServiceTestResult Fit(WebServiceTestResult result) {
		if (result is null || Fits(result)) {
			return result;
		}
		if (result.Error is { Length: > MaxErrorCharacters } error) {
			result = result with {
				Error = KeepWhole(error, MaxErrorCharacters) + " ... (the rest of the error is cut to keep this reply inline)"
			};
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

	/// <summary>The longest platform error a test-call reply keeps when the reply does not fit; the cause is at its start.</summary>
	private const int MaxErrorCharacters = 2_000;

	// The first characters of the text, never ending in half a surrogate pair.
	private static string KeepWhole(string text, int keep) {
		if (keep > 0 && keep <= text.Length && char.IsHighSurrogate(text[keep - 1])) {
			keep--;
		}
		return text[..keep];
	}

	private static bool Fits<T>(T reply) => McpResultSize.Of(reply) <= McpResultSize.InlineReplyBudgetBytes;

	/// <summary>The most later methods in a row a narrowed reply may fail to add before it stops trying, which bounds the work.</summary>
	private const int MaxFailedTriesInARow = 20;

	private static HashSet<WebServiceMethodView> NoMethods() => new(ReferenceEqualityComparer.Instance);

	// The methods shown in full: the longest run in priority order that fits (a binary search), then each later method
	// that still fits in the room left, skipping the ones that do not, so a large method does not push the small ones
	// after it out.
	private static HashSet<WebServiceMethodView> Shown(List<WebServiceMethodView> byPriority,
		Func<HashSet<WebServiceMethodView>, bool> fits) {
		HashSet<WebServiceMethodView> Run(int count) {
			HashSet<WebServiceMethodView> set = NoMethods();
			set.UnionWith(byPriority.Take(count));
			return set;
		}
		int run = LargestFitting(0, byPriority.Count - 1, count => fits(Run(count)));
		HashSet<WebServiceMethodView> shown = Run(run);
		// With nothing shown and still over (thousands of methods by code), no method can be added.
		if (run == 0 && !fits(shown)) {
			return shown;
		}
		int failedInARow = 0;
		foreach (WebServiceMethodView method in byPriority.Skip(run + 1)) {
			shown.Add(method);
			if (fits(shown)) {
				failedInARow = 0;
			} else {
				shown.Remove(method);
				if (++failedInARow >= MaxFailedTriesInARow) {
					break;
				}
			}
		}
		return shown;
	}

	/// <summary>A service reply and the ways it can be narrowed: fewer notices, fewer methods in full, fewer usages.</summary>
	private sealed class ServiceReply(WebServiceToolResult result) {

		private readonly IReadOnlyList<string> _notices = result.Notices ?? [];

		internal IReadOnlyList<WebServiceMethodView> Methods { get; } = result.Service.Methods ?? [];

		internal int NoticeCount => _notices.Count;

		internal int UsageCount => result.Usages?.Count ?? 0;

		/// <summary>
		/// The reply with the first <paramref name="notices"/> notices, the methods in <paramref name="shown"/> in full (every
		/// method when null) and the first <paramref name="usages"/> usages, each cut announced by a notice.
		/// </summary>
		internal WebServiceToolResult Build(int notices, HashSet<WebServiceMethodView> shown, int usages) {
			List<string> kept = [.. _notices.Take(notices)];
			WebServiceView service = result.Service;
			if (shown is not null) {
				List<WebServiceMethodSummary> notShown = Methods.Where(m => !shown.Contains(m))
					.Select(m => new WebServiceMethodSummary(m.Code, m.Caption, m.RequestParameters?.Count ?? 0,
						m.ResponseParameters?.Count ?? 0))
					.ToList();
				service = service with { Methods = Methods.Where(shown.Contains).ToList(), MethodsNotShown = notShown };
				kept.Add($"{notShown.Count} of the {Methods.Count} methods are listed by code only (methods-not-shown) to "
					+ $"keep this reply within {McpResultSize.InlineReplyBudgetBytes:N0} bytes; listing a method by code does not "
					+ "change it. Read the ones you need with describe-web-service methods, a few codes at a time (one when a "
					+ "method is large); never send an entry of methods-not-shown back as a method.");
			}
			if (notices < _notices.Count) {
				kept.Add($"{_notices.Count - notices} of the {_notices.Count} notices are left out to keep this reply inline; "
					+ "describe-web-service with methods, a few codes at a time, returns the notices of those methods.");
			}
			IReadOnlyList<WebServiceUsage> shownUsages = result.Usages;
			if (usages < UsageCount) {
				shownUsages = result.Usages.Take(usages).ToList();
				kept.Add($"{UsageCount - usages} of the {UsageCount} usages are not listed, to keep this reply inline. "
					+ "describe-web-service with fewer methods and usages: true lists the usages of those methods; for a single "
					+ "method, the ones listed are all that fit.");
			}
			return result with {
				Service = service,
				Usages = shownUsages,
				Notices = kept.Count > 0 ? kept : result.Notices
			};
		}
	}

	// The longest start of the masked body that fits; a surrogate pair is never split.
	private static WebServiceTestResult CutBody(WebServiceTestResult result) {
		string body = result.ResponseBody;
		if (string.IsNullOrEmpty(body) || Fits(result)) {
			return result;
		}
		WebServiceTestResult Cut(int keep) => result with { ResponseBody = KeepWhole(body, keep), ResponseBodyTruncated = true };
		return Cut(LargestFitting(0, body.Length - 1, keep => Fits(Cut(keep))));
	}
}
