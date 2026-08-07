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
		/// <returns>
		/// The entry's bytes, or <c>null</c> when the archive was read to its end without holding such an
		/// entry. <c>null</c> means ABSENT and nothing else — a corrupt container throws instead, so a caller
		/// can tell "no such file" from "this archive is damaged" and say the right thing about it.
		/// </returns>
		/// <exception cref="System.IO.InvalidDataException">
		/// Thrown when the container is unreadable: an entry declares a name or content length that does not
		/// fit in the bytes remaining, or the wanted entry is truncated.
		/// </exception>
		/// <remarks>
		/// Exists so a caller that needs one small file — a descriptor, say — does not have to unpack a whole
		/// package to a temporary directory to read it.
		/// <para>
		/// The archive is decompressed into memory in FULL before any entry is inspected (as
		/// <see cref="UnpackFromGZip"/> already does), so the caller must be willing to hold the whole
		/// decompressed package. There is no cap: this is not a streaming reader, and it must not be pointed
		/// at an archive whose size is not already trusted.
		/// </para>
		/// </remarks>
		byte[] ReadFileFromGZip(string packedPackagePath, string relativeFilePath);

		void Unzip(string zipFilePath, string destinationDirectory);

		void Zip(string directoryPath, string zipFilePath);

	}
}