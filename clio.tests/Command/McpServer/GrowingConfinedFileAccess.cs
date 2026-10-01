using System;
using System.IO;
using Clio.Common;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// <see cref="IConfinedFileAccess"/> double whose open passes the ceiling check and then hands back a stream
/// carrying more bytes than the ceiling allows.
/// </summary>
/// <remarks>
/// This is the growth race, made deterministic. The real Unix open reads the descriptor length once and
/// returns a LIVE stream, so a writer that extends the same inode afterwards changes what the caller will
/// read without changing what the open decided. A double is the only way to pin that ordering: on a real file
/// system the two events race, and a test that depends on winning the race is a test that flakes.
/// </remarks>
internal sealed class GrowingConfinedFileAccess(long bytesAfterOpen) : IConfinedFileAccess {

	/// <inheritdoc/>
	public Stream OpenRead(string canonicalPath, long maxBytes) => new MemoryStream(new byte[bytesAfterOpen], writable: false);

	/// <inheritdoc/>
	public void WriteNew(string canonicalPath, byte[] content) =>
		throw new NotSupportedException("This double exists for the read-growth case only.");
}
