using System;

namespace Clio.Common.ExternalAccess;

/// <summary>
/// The terms of a support access grant, as the external-access token itself states them.
/// </summary>
/// <param name="AccessId">The grant identifier (<c>prop:ResourceId</c>).</param>
/// <param name="OwnerClientId">The customer site the grant belongs to (<c>prop:OwnerClientId</c>).</param>
/// <param name="IsDataIsolationEnabled">Business-data access is restricted for this grant.</param>
/// <param name="IsSystemOperationsRestricted">Configuration operations are restricted for this grant.</param>
/// <param name="GrantExpiresOnUtc">When the grant itself ends, days away from the token's own expiry.</param>
/// <param name="TokenExpiresOnUtc">When this token stops being accepted — 120 seconds after it was minted.</param>
public sealed record ExternalAccessGrant(
	string AccessId,
	string OwnerClientId,
	bool IsDataIsolationEnabled,
	bool IsSystemOperationsRestricted,
	DateTimeOffset? GrantExpiresOnUtc,
	DateTimeOffset? TokenExpiresOnUtc);
