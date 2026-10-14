using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.Common.BrowserSession;

namespace Clio.Command.RuntimeVerification;

/// <summary>A console error, uncaught exception or failed request the page logged, with how often it repeated.</summary>
/// <param name="Kind"><c>exception</c> (uncaught), <c>console</c> (console.error) or <c>log</c> (a browser log entry,
/// for example a failed request).</param>
/// <param name="Text">The message: page-authored, so scrubbed, flattened, capped and fenced as data.</param>
/// <param name="Count">How many times the same message was logged.</param>
public sealed record PageConsoleError(
	[property: JsonPropertyName("kind")] string Kind,
	[property: JsonPropertyName("text")] string Text,
	[property: JsonPropertyName("count")] int Count) {

	/// <summary>The request path for a failed request (no host, no query); null otherwise.</summary>
	[JsonPropertyName("url"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Url { get; init; }
}

/// <summary>
/// Collects, from the CDP events of one page, the errors it logged and the navigations it made, in order, so a caller
/// can read what happened after a given moment (a click).
/// </summary>
internal sealed class PageEventCollector {

	// Distinct messages kept per phase (load, then click); a repeat of a kept message is always counted.
	internal const int MaxDistinctPerPhase = 200;
	// A page looping on an error is still bounded.
	internal const int MaxStoredEvents = 20_000;
	// A reported address (a failed request's path, a navigation) is cut to this many characters.
	internal const int MaxUrlLength = 120;
	// A navigation loop is reported by its first and last few steps and a count of the rest.
	internal const int NavigationsKeptAtEachEnd = 3;

	private readonly object _gate = new();
	// Kind is null for an occurrence that was dropped by a cap; it is counted as omitted.
	private readonly List<(int Sequence, string Kind, string Text, string Url)> _errors = [];
	private readonly HashSet<(string Kind, string Text, string Url)> _phaseKeys = [];
	// Occurrences dropped once _errors is full, as runs that a Mark() closes, so each phase still counts its own.
	private readonly List<DroppedRun> _droppedAtCap = [];
	private readonly List<(int Sequence, string Url)> _navigations = [];
	private readonly List<(int Sequence, string Text)> _dialogs = [];
	private readonly Func<string, string> _relativeUrl;
	private int _sequence;
	private int _navigationCount;

	/// <param name="relativeUrl">Shortens a navigated URL for the report.</param>
	public PageEventCollector(Func<string, string> relativeUrl) {
		_relativeUrl = relativeUrl;
	}

	/// <summary>Raised when the page opens a native dialog (alert, confirm, prompt, beforeunload), which blocks it.</summary>
	public event Action NativeDialogOpened;

	/// <summary>The number of navigations seen so far; a change means the page moved.</summary>
	public int NavigationCount {
		get {
			lock (_gate) {
				return _navigationCount;
			}
		}
	}

	/// <summary>A point in the event stream that also starts a new phase for the distinct-message cap.</summary>
	public int Mark() {
		lock (_gate) {
			_phaseKeys.Clear();
			if (_droppedAtCap.Count > 0) {
				_droppedAtCap[^1].Closed = true;
			}
			return _sequence;
		}
	}

	/// <summary>Handles one CDP event; subscribe it to <see cref="ICdpSession.EventReceived"/>.</summary>
	public void OnEvent(object sender, CdpEvent cdpEvent) {
		switch (cdpEvent.Method) {
			case "Runtime.exceptionThrown":
				AddError("exception", ReadException(cdpEvent.Params), null);
				break;
			case "Runtime.consoleAPICalled" when Text(cdpEvent.Params, "type") is "error" or "assert":
				AddError("console", ReadConsoleArguments(cdpEvent.Params), null);
				break;
			case "Log.entryAdded" when cdpEvent.Params.TryGetProperty("entry", out JsonElement entry)
				&& Text(entry, "level") == "error":
				AddError("log", Text(entry, "text"), Text(entry, "url"));
				break;
			case "Page.frameNavigated" when cdpEvent.Params.TryGetProperty("frame", out JsonElement frame)
				&& !frame.TryGetProperty("parentId", out _):
				AddNavigation(Text(frame, "url"));
				break;
			case "Page.navigatedWithinDocument":
				AddNavigation(Text(cdpEvent.Params, "url"));
				break;
			case "Page.javascriptDialogOpening":
				AddDialog($"{Text(cdpEvent.Params, "type") ?? "dialog"}: {Text(cdpEvent.Params, "message")}");
				NativeDialogOpened?.Invoke();
				break;
		}
	}

	/// <summary>The distinct errors logged after <paramref name="mark"/>, in the order they were first seen.</summary>
	/// <param name="mark">From <see cref="Mark"/>; 0 for everything.</param>
	/// <param name="max">Most distinct errors returned.</param>
	/// <param name="omitted">How many more errors there were: distinct ones past <paramref name="max"/>, and
	/// occurrences a cap dropped.</param>
	public IReadOnlyList<PageConsoleError> ErrorsSince(int mark, int max, out int omitted) =>
		ErrorsBetween(mark, int.MaxValue, max, out omitted);

	/// <summary>The distinct errors logged after <paramref name="after"/> and up to <paramref name="upTo"/>.</summary>
	public IReadOnlyList<PageConsoleError> ErrorsBetween(int after, int upTo, int max, out int omitted) {
		List<(int Sequence, string Kind, string Text, string Url)> errors;
		int droppedAtCap;
		lock (_gate) {
			errors = _errors.Where(error => error.Sequence > after && error.Sequence <= upTo).ToList();
			// A run never spans a Mark(), so a window that starts and ends at marks counts each run whole.
			droppedAtCap = _droppedAtCap.Where(run => run.First > after && run.First <= upTo).Sum(run => run.Count);
		}
		int dropped = errors.Count(error => error.Kind is null) + droppedAtCap;
		List<PageConsoleError> distinct = errors
			.Where(error => error.Kind is not null)
			.GroupBy(error => (error.Kind, error.Text, error.Url))
			.Select(group => new PageConsoleError(group.Key.Kind, UntrustedText.Fenced(group.Key.Text) ?? "(empty)",
				group.Count()) { Url = ShortPath(group.Key.Url) })
			.ToList();
		omitted = Math.Max(0, distinct.Count - max) + dropped;
		return distinct.Take(max).ToList();
	}

	/// <summary>
	/// The URLs the main frame moved to after <paramref name="mark"/>, consecutive repeats folded; a longer list keeps its
	/// first and last <see cref="NavigationsKeptAtEachEnd"/> and counts the rest in <paramref name="omitted"/>.
	/// </summary>
	/// <remarks>A page whose click starts a route ping-pong or a pushState loop must not grow the reply without bound.</remarks>
	public IReadOnlyList<string> NavigationsSince(int mark, out int omitted) {
		List<string> urls;
		lock (_gate) {
			urls = _navigations.Where(navigation => navigation.Sequence > mark).Select(navigation => navigation.Url).ToList();
		}
		List<string> folded = [];
		foreach (string url in urls.Where(url => url is not null)) {
			if (folded.Count == 0 || folded[^1] != url) {
				folded.Add(url);
			}
		}
		const int kept = NavigationsKeptAtEachEnd * 2;
		omitted = Math.Max(0, folded.Count - kept);
		return omitted == 0
			? folded
			: [.. folded.Take(NavigationsKeptAtEachEnd), .. folded.Skip(folded.Count - NavigationsKeptAtEachEnd)];
	}

	/// <summary>The native dialogs the page opened after <paramref name="mark"/> (<c>type: message</c>), at most three.</summary>
	public IReadOnlyList<string> NativeDialogsSince(int mark) {
		lock (_gate) {
			return _dialogs.Where(dialog => dialog.Sequence > mark).Select(dialog => dialog.Text).Take(3).ToList();
		}
	}

	private void AddError(string kind, string text, string url) {
		if (string.IsNullOrWhiteSpace(text)) {
			return;
		}
		string firstLines = FirstLines(text);
		lock (_gate) {
			_sequence++;
			if (_errors.Count >= MaxStoredEvents) {
				CountDroppedAtCap(_sequence);
				return;
			}
			bool known = _phaseKeys.Contains((kind, firstLines, url));
			if (!known && _phaseKeys.Count >= MaxDistinctPerPhase) {
				_errors.Add((_sequence, null, null, null));
				return;
			}
			_phaseKeys.Add((kind, firstLines, url));
			_errors.Add((_sequence, kind, firstLines, url));
		}
	}

	private void CountDroppedAtCap(int sequence) {
		if (_droppedAtCap.Count > 0 && !_droppedAtCap[^1].Closed) {
			_droppedAtCap[^1].Count++;
			return;
		}
		_droppedAtCap.Add(new DroppedRun { First = sequence, Count = 1 });
	}

	private sealed class DroppedRun {
		public int First { get; init; }

		public int Count { get; set; }

		public bool Closed { get; set; }
	}

	private void AddNavigation(string url) {
		if (string.IsNullOrWhiteSpace(url) || url.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) {
			return;
		}
		lock (_gate) {
			_sequence++;
			_navigationCount++;
			if (_navigations.Count < MaxStoredEvents) {
				_navigations.Add((_sequence, Cap(_relativeUrl(url))));
			}
		}
	}

	private void AddDialog(string text) {
		lock (_gate) {
			_sequence++;
			if (_dialogs.Count < MaxStoredEvents) {
				_dialogs.Add((_sequence, text));
			}
		}
	}

	// The message and the first stack frame are enough to place an error; the rest of a stack is noise.
	private static string FirstLines(string text) {
		string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		return string.Join(" | ", lines.Take(2));
	}

	private static string ReadException(JsonElement parameters) {
		if (!parameters.TryGetProperty("exceptionDetails", out JsonElement details)) {
			return null;
		}
		if (details.TryGetProperty("exception", out JsonElement exception)) {
			string description = Text(exception, "description") ?? Text(exception, "value");
			if (!string.IsNullOrWhiteSpace(description)) {
				return description;
			}
		}
		return Text(details, "text");
	}

	private static string ReadConsoleArguments(JsonElement parameters) {
		if (!parameters.TryGetProperty("args", out JsonElement args) || args.ValueKind != JsonValueKind.Array) {
			return null;
		}
		IEnumerable<string> parts = args.EnumerateArray().Take(3).Select(ReadConsoleArgument);
		return string.Join(" ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
	}

	// A primitive argument carries its value; an object only a description.
	private static string ReadConsoleArgument(JsonElement arg) {
		if (!arg.TryGetProperty("value", out JsonElement value)) {
			return Text(arg, "description");
		}
		return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
	}

	private static string Text(JsonElement element, string property) =>
		element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement value)
			&& value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	// A failed request is reported by its path only: the host is the environment's, and a query can carry a token.
	internal static string ShortPath(string url) {
		if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out Uri uri)) {
			return null;
		}
		return Cap(uri.AbsolutePath);
	}

	/// <summary>Cuts an address to <see cref="MaxUrlLength"/> characters, marking the cut.</summary>
	internal static string Cap(string url) =>
		url is not null && url.Length > MaxUrlLength ? url[..(MaxUrlLength - 1)] + "…" : url;
}
