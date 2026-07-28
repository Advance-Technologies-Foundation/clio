using System;

namespace ClioRing.Services;

/// <summary>
/// Thrown when <c>actions.json</c> is missing, malformed, or contains an action whose
/// declared <see cref="Models.ActionKind"/> does not match its populated typed block.
/// The loader never returns a partial/invalid catalog — it fails loudly instead.
/// </summary>
public sealed class ActionCatalogException : Exception {
	/// <summary>Creates the exception with a user-facing message.</summary>
	public ActionCatalogException(string message) : base(message) { }

	/// <summary>Creates the exception with a user-facing message and inner cause.</summary>
	public ActionCatalogException(string message, Exception inner) : base(message, inner) { }
}
