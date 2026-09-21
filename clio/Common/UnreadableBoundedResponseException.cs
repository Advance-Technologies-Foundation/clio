using System;

namespace Clio.Common;

/// <summary>
/// Raised when a bounded GET completed without leaving a body behind, which is how the transport
/// reports a final non-success HTTP status on this path.
/// </summary>
/// <remarks>
/// <para>
/// <c>Creatio.Client</c> 2.0.2 writes only SUCCESSFUL bodies to the destination it is given. On a final
/// 4xx/5xx it drains the body into a <c>MemoryStream</c> instead, creates no file and returns normally,
/// so the caller's own read of that destination raises a <see cref="System.IO.FileNotFoundException"/>
/// naming clio's temporary path. Measured against a stand: a file-mode read of an entity with no OData
/// controller answered <c>"Could not find file '&lt;scratch&gt;'."</c> — a sentence about a file the caller
/// never asked for, with nothing in it about the server. This type exists so that outcome is reported as
/// what it is instead.
/// </para>
/// <para>
/// The STATUS is deliberately not part of it, because this path cannot see one: the transport has
/// already discarded the response by the time it returns. Classifying the failure properly needs a
/// bounded read that keeps error bodies, which is
/// <see href="https://github.com/Advance-Technologies-Foundation/creatioclient/pull/23">creatioclient#23</see>;
/// until a release carrying it is referenced, the inline <c>odata-read</c> is the read path that can
/// tell a 404 from a proxy failure.
/// </para>
/// </remarks>
public sealed class UnreadableBoundedResponseException : Exception {

	/// <summary>Creates the exception with the caller-facing diagnostic.</summary>
	public UnreadableBoundedResponseException()
		: base("Creatio answered this request with a non-success HTTP status and no body reached clio, so "
			+ "the response could not be written. This path cannot report which status it was; call "
			+ "odata-read with the same query - the inline read classifies the failure (a missing OData "
			+ "entity set, an expired session, a proxy error) and names what to do about it.") {
	}
}
