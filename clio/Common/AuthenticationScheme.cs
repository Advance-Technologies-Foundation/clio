namespace Clio.Common;

/// <summary>
/// Well-known HTTP authentication scheme names shared across clio's credential handling,
/// so the scheme literal is defined once rather than repeated at each comparison/default site.
/// </summary>
internal static class AuthenticationScheme
{
	/// <summary>The RFC 6750 Bearer token scheme — the only access-token type clio supports in v1.</summary>
	public const string Bearer = "Bearer";
}
