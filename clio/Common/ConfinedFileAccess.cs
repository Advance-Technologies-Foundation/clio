using System;
using System.IO;

namespace Clio.Common;

/// <summary>
/// Opens a file whose path has already been approved by confinement, WITHOUT following a symbolic link at
/// any component of it.
/// </summary>
/// <remarks>
/// Approving a pathname and then opening that pathname are two separate operations, and between them any
/// directory in the path can be replaced with a link pointing somewhere else - the check passes while the
/// open lands outside the approved area. Canonicalizing the path at approval time does not close this: the
/// canonical path still NAMES its parent directories, and a parent can be swapped after it was checked.
/// <para>
/// Because the path is already canonical, no component legitimately IS a link, so a link found during the
/// descent means the path changed after it was approved - and the operation fails closed instead of reading
/// or writing whatever the link now points at.
/// </para>
/// <para>
/// This cannot be expressed through <c>System.IO.Abstractions</c>, which has no notion of a directory handle
/// or of no-follow semantics, so it is its own service with per-platform implementations
/// (<c>openat</c> on Unix; held directory handles on Windows).
/// </para>
/// </remarks>
public interface IConfinedFileAccess {

	/// <summary>Opens an existing file for reading, refusing to follow a link at any path component.</summary>
	/// <param name="canonicalPath">Absolute, already-canonical and already-confined path.</param>
	/// <param name="maxBytes">
	/// Hard ceiling on how much of the file may be pulled into memory. The bound belongs HERE rather than in
	/// the caller: a caller that receives a stream and measures it afterwards has already paid for whatever
	/// the file contained, which is exactly the exhaustion the bound exists to prevent.
	/// </param>
	/// <returns>A readable stream positioned at the start of the file, never longer than <paramref name="maxBytes"/>.</returns>
	/// <exception cref="IOException">A component is a symbolic link, the file is larger than the ceiling, or it cannot be opened.</exception>
	Stream OpenRead(string canonicalPath, long maxBytes);

	/// <summary>
	/// Writes <paramref name="content"/> to a file that must NOT already exist, refusing to follow a link at
	/// any path component, and publishing the final name only once the content is complete.
	/// </summary>
	/// <param name="canonicalPath">Absolute, already-canonical and already-confined path.</param>
	/// <param name="content">Bytes to write.</param>
	/// <exception cref="IOException">A component is a symbolic link, the target exists, or the write fails.</exception>
	void WriteNew(string canonicalPath, byte[] content);
}

/// <inheritdoc cref="IConfinedFileAccess"/>
/// <remarks>Dispatches to the platform implementation; the two differ in mechanism, not in contract.</remarks>
public sealed class ConfinedFileAccess : IConfinedFileAccess {

	private readonly IConfinedFileAccess _platform = CreatePlatformAccess();

	private static IConfinedFileAccess CreatePlatformAccess() =>
		OperatingSystem.IsWindows() ? new WindowsConfinedFileAccess() : new UnixConfinedFileAccess();

	/// <inheritdoc/>
	public Stream OpenRead(string canonicalPath, long maxBytes) => _platform.OpenRead(canonicalPath, maxBytes);

	/// <inheritdoc/>
	public void WriteNew(string canonicalPath, byte[] content) => _platform.WriteNew(canonicalPath, content);

	/// <summary>The message both platform implementations use when a file is past the ceiling.</summary>
	/// <param name="observedBytes">Size seen so far.</param>
	/// <param name="maxBytes">The ceiling.</param>
	internal static string DescribeTooLarge(long observedBytes, long maxBytes) =>
		$"file is at least {observedBytes} bytes, which exceeds the {maxBytes}-byte limit.";
}
