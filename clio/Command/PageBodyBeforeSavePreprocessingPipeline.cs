using System.Collections.Generic;

namespace Clio.Command;

/// <summary>
/// The general before-save page-body preprocessing mechanism: a fixed, ordered set of
/// <see cref="IPageBodyPreprocessor"/> applied inside <see cref="PageUpdateCommand.TryUpdatePage"/> — the
/// single chokepoint shared by update-page, sync-pages, and the CLI — just before the body is persisted. Add
/// a new preprocessor by appending it to <see cref="Preprocessors"/>; each is invoked in turn and is
/// individually fail-safe (a preprocessor that throws is skipped, never failing the save). Preprocessors are
/// self-scoping, so a body they do not apply to passes through unchanged.
/// </summary>
internal static class PageBodyBeforeSavePreprocessingPipeline {

	// Registered preprocessors, applied in order. Each must be self-scoping (a no-op when not applicable).
	private static readonly IReadOnlyList<IPageBodyPreprocessor> Preprocessors = new IPageBodyPreprocessor[] {
		// TODO(ENG-91251, ENG-92198): remove this registration together with ChartConfigKeyOrderPreprocessor once the
		// Freedom UI json-differ needFlatten fix ships.
		new ChartConfigKeyOrderPreprocessor(),
	};

	/// <summary>
	/// Runs every registered preprocessor over <paramref name="body"/> and returns the result. Fail-safe: an
	/// empty body is returned as-is, and any preprocessor that throws is skipped so it can never block or
	/// corrupt a save.
	/// </summary>
	public static string Preprocess(string body) => Preprocess(body, Preprocessors);

	// Overload taking an explicit preprocessor set — lets the chaining / fail-safe behaviour be unit-tested
	// with test doubles without touching the registered production set.
	internal static string Preprocess(string body, IReadOnlyList<IPageBodyPreprocessor> preprocessors) {
		if (string.IsNullOrEmpty(body)) {
			return body;
		}
		string current = body;
		foreach (IPageBodyPreprocessor preprocessor in preprocessors) {
			try {
				current = preprocessor.Preprocess(current) ?? current;
			} catch {
				// A preprocessor must never fail the save — skip it and keep the last good body.
			}
		}
		return current;
	}
}
