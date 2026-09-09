using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clio.Common;

/// <summary>
/// Reads Creatio's <c>ErrorsWarnings</c> payload on a compilation-history row.
/// </summary>
/// <remarks>
/// A static helper rather than an injected service: it is a pure parse of one string with no
/// collaborators and no state, so it is a value-level utility in the sense the DI policy exempts,
/// and every caller wants the identical answer. It exists as its own type only because a second
/// caller appeared (<see cref="CompilationActivityWatcher"/>) and a copied private method is how the
/// warning/error distinction below silently drifts apart.
/// </remarks>
internal static class CompilationDiagnostics {

	private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

	/// <summary>
	/// Determines whether the payload carries at least one diagnostic that is not merely a warning.
	/// </summary>
	/// <param name="errorsWarnings">The row's raw <c>ErrorsWarnings</c> JSON.</param>
	/// <returns><see langword="true"/> when a non-warning diagnostic is present.</returns>
	/// <remarks>
	/// <c>ErrorsWarnings</c> legitimately holds warning-only entries on a successful compile (observed
	/// live: a full <c>compile-configuration --all</c> that finished successfully carried a CS0114
	/// "hides inherited member" warning), so only a non-warning entry counts as failure. Content that
	/// cannot be parsed is treated as an error: where this feeds an exit code, under-claiming success
	/// is the safe direction.
	/// </remarks>
	internal static bool HasRealError(string errorsWarnings) {
		if (string.IsNullOrWhiteSpace(errorsWarnings)
			|| string.Equals(errorsWarnings, "[]", StringComparison.OrdinalIgnoreCase)) {
			return false;
		}
		try {
			List<CompilationLogEntry> entries =
				JsonSerializer.Deserialize<List<CompilationLogEntry>>(errorsWarnings, JsonOptions);
			return entries is not null && entries.Exists(entry => !entry.IsWarning);
		} catch (JsonException) {
			return true;
		}
	}

	private sealed record CompilationLogEntry([property: JsonPropertyName("IsWarning")] bool IsWarning);

}
