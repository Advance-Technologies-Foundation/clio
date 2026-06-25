namespace Clio.Command;

/// <summary>
/// A deterministic, fail-safe transform applied to a Freedom UI page body just before it is persisted
/// (update-page / sync-pages / CLI update-page). Each preprocessor is <b>self-scoping</b>: it inspects the
/// body and rewrites only the config shapes it owns, returning the body unchanged when nothing applies.
/// Preprocessors MUST be pure (no side effects) and SHOULD NOT throw — a preprocessing step can never be
/// allowed to block or corrupt a save (<see cref="PageBodyBeforeSavePreprocessingPipeline"/> also guards
/// against throws defensively). Register implementations in <see cref="PageBodyBeforeSavePreprocessingPipeline"/>.
/// </summary>
internal interface IPageBodyPreprocessor {

	/// <summary>Returns <paramref name="body"/> with this preprocessor's transform applied, or unchanged.</summary>
	string Preprocess(string body);
}
