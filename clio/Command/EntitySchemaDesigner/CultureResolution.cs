namespace Clio.Command.EntitySchemaDesigner;

/// <summary>
/// Outcome of a profile-culture resolution probe.
/// </summary>
/// <remarks>
/// <para>
/// INVARIANT: consumers MUST branch on <see cref="Success"/> before reading <see cref="Culture"/>.
/// On failure <see cref="Culture"/> is deliberately set to the <c>en-US</c> fallback
/// (<see cref="EntitySchemaDesignerSupport.DefaultCultureName"/>) so the non-fatal creation path
/// (an explicit <c>--caption-culture</c> override or a localization map that already contains the
/// needed key) can use it directly. A consumer on the hard-abort path (no override and no usable
/// map entry) must check <see cref="Success"/> first — otherwise the ask-user / CLI-error path is
/// silently bypassed by a careless <see cref="Culture"/> read.
/// </para>
/// </remarks>
/// <param name="Culture">
/// The resolved, validated .NET culture name (e.g. <c>en-US</c>, <c>uk-UA</c>) on success, or the
/// <c>en-US</c> fallback on failure. Never null.
/// </param>
/// <param name="Success">Whether the profile culture was resolved and validated.</param>
/// <param name="FailureReason">
/// A short machine-readable reason when <see cref="Success"/> is <c>false</c>
/// (e.g. <c>userCulture-missing</c>, <c>userCulture-invalid</c>, <c>unreachable</c>,
/// <c>unauthorized</c>, <c>no-active-environment</c>); <c>null</c> on success.
/// </param>
public sealed record CultureResolution(string Culture, bool Success, string? FailureReason)
{
	/// <summary>Creates a successful resolution for a validated culture name.</summary>
	public static CultureResolution Resolved(string culture) => new(culture, true, null);

	/// <summary>
	/// Creates a failed resolution. <see cref="Culture"/> is set to the <c>en-US</c> fallback so the
	/// non-fatal creation path can proceed; callers on the hard-abort path must check
	/// <see cref="Success"/> first.
	/// </summary>
	public static CultureResolution Failed(string reason) =>
		new(EntitySchemaDesignerSupport.DefaultCultureName, false, reason);
}
