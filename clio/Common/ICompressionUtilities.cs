using System.Collections.Generic;
using System.IO.Compression;

namespace Clio.Common
{
	public interface ICompressionUtilities
	{
		void PackToGZip(IEnumerable<string> files, string rootDirectoryPath, string destinationPackagePath);
		void UnpackFromGZip(string packedPackagePath, string destinationPackageDirectoryPath);

		/// <summary>
		/// Reads the contents of a single entry out of a gz-packed package, without writing anything to disk.
		/// </summary>
		/// <param name="packedPackagePath">Path to the packed package.</param>
		/// <param name="relativeFilePath">
		/// Entry path relative to the package root, as stored in the archive. Matched case-insensitively and
		/// independently of directory-separator flavour, since the archive stores whatever separator the host
		/// that packed it used.
		/// </param>
		/// <param name="content">The entry's bytes when found; otherwise <c>null</c>.</param>
		/// <returns>
		/// <c>true</c> when the entry was found. <c>false</c> means the archive was read to its end without
		/// holding such an entry — ABSENT and nothing else, because a corrupt container throws instead. A
		/// caller can therefore tell "no such file" from "this archive is damaged" and say the right thing
		/// about it.
		/// </returns>
		/// <exception cref="System.IO.InvalidDataException">
		/// Thrown when the container is unreadable: an entry declares a name or content length that does not
		/// fit in the bytes remaining, or the wanted entry is truncated.
		/// </exception>
		/// <remarks>
		/// Exists so a caller that needs one small file — a descriptor, say — does not have to unpack a whole
		/// package to a temporary directory to read it.
		/// <para>
		/// A Try-pattern rather than a nullable <c>byte[]</c> return, because an EMPTY entry is a legitimate
		/// result: returning a zero-length array for "not found" would make the two indistinguishable, and
		/// that distinction is the point of the contract above.
		/// </para>
		/// <para>
		/// The archive is decompressed into memory in FULL before any entry is inspected (as
		/// <see cref="UnpackFromGZip"/> already does), so the caller must be willing to hold the whole
		/// decompressed package. There is no cap: this is not a streaming reader, and it must not be pointed
		/// at an archive whose size is not already trusted.
		/// </para>
		/// </remarks>
		bool TryReadFileFromGZip(string packedPackagePath, string relativeFilePath, out byte[] content);

		/// <summary>
		/// Lists the entry paths a gz-packed package contains, without writing anything to disk.
		/// </summary>
		/// <param name="packedPackagePath">Path to the packed package.</param>
		/// <returns>
		/// Every entry path, relative to the package root, with directory separators normalized to
		/// <c>/</c> so a caller's expectations do not depend on which host packed the archive or which host
		/// is reading it.
		/// </returns>
		/// <exception cref="System.IO.InvalidDataException">
		/// Thrown when the container is unreadable — same conditions as <see cref="TryReadFileFromGZip"/>.
		/// </exception>
		/// <remarks>
		/// Exists because entry names are the one thing a caller CANNOT check by searching the decompressed
		/// bytes: they are stored UTF-16LE, so an ASCII/UTF-8 probe never matches a path, and any assertion
		/// phrased against one is silently vacuous. Measured on the shipped process-builder archive: a text
		/// scan finds zero hits for <c>SafeText.cs</c>, which is a real entry.
		/// </remarks>
		IReadOnlyList<string> ListGZipEntryNames(string packedPackagePath);

		void Unzip(string zipFilePath, string destinationDirectory);

		void Zip(string directoryPath, string zipFilePath);

	}
}