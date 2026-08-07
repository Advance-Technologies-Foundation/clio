using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Clio.Common
{
	#region Class: CompressionUtilities

	public class CompressionUtilities : ICompressionUtilities
	{

		#region Fields: Private

		private readonly IFileSystem _fileSystem;
		private readonly IZipFile _zipFile;

		#endregion

		#region Constructors: Public

		public CompressionUtilities(IFileSystem fileSystem, IZipFile zipFile)
		{
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			_fileSystem = fileSystem;
			_zipFile = zipFile;
		}

		#endregion

		#region Methods: Private

		
		
		private static void WriteFileName(string relativeFilePath, GZipStream zipStream)
		{
			char[] chars = relativeFilePath.ToCharArray();
			zipStream.Write(BitConverter.GetBytes(chars.Length), 0, sizeof(int));
			foreach (char c in chars)
			{
				zipStream.Write(BitConverter.GetBytes(c), 0, sizeof(char));
			}
		}

		private void WriteFileContent(string filePath, GZipStream zipStream)
		{
			
			byte[] bytes = _fileSystem.ReadAllBytes(filePath);
			zipStream.Write(BitConverter.GetBytes(bytes.Length), 0, sizeof(int));
			zipStream.Write(bytes, 0, bytes.Length);
		}

		private static int SafeReadGZipStream(Stream stream, byte[] bytes)
		{
			int totalRead = 0;
			while (totalRead < bytes.Length)
			{
				var charBytesRead = stream.Read(bytes, totalRead, bytes.Length - totalRead);
				if (charBytesRead == 0)
				{
					break;
				}
				totalRead += charBytesRead;
			}
			return totalRead;
		}

		// Returns the empty string for BOTH a clean end of archive and an incredible name length. The two
		// are indistinguishable to a caller by design: the container has no terminator, so "the next
		// length prefix decodes as zero" IS how the walk ends, and a length that cannot fit in what is
		// left is corruption that must stop the walk just as firmly.
		//
		// The bound matters. Without it a garbage prefix (a truncated download, a half-written copy) drives
		// the read loop up to int.MaxValue times appending to a StringBuilder — gigabytes of allocation and
		// a long stall instead of a refusal — and this method is on the untrusted path: `extract-pkg-zip`
		// unpacks whatever archive a user was handed, and Downloader unpacks whatever the remote instance
		// returned. The content walk has always had this bound; the name walk did not.
		//
		// One asymmetry this introduced, deliberately left: WriteFileName emits each char through
		// BitConverter, i.e. in HOST byte order, while Encoding.Unicode here is fixed UTF-16LE. The previous
		// per-char BitConverter.ToChar loop was symmetric with the writer on either endianness. Theoretical
		// on every host .NET actually runs on, and the bulk read is what makes the bound expressible at all.
		private string ReadFileRelativePath(MemoryStream zipStream)
		{
			var bytes = new byte[sizeof(int)];
			if (SafeReadGZipStream(zipStream, bytes) != bytes.Length)
			{
				return string.Empty;
			}
			int fileNameLength = BitConverter.ToInt32(bytes, 0);
			// long arithmetic: fileNameLength * sizeof(char) overflows int for a length above 2^30.
			if (fileNameLength < 0
				|| (long)fileNameLength * sizeof(char) > zipStream.Length - zipStream.Position)
			{
				return string.Empty;
			}
			var nameBytes = new byte[fileNameLength * sizeof(char)];
			if (SafeReadGZipStream(zipStream, nameBytes) != nameBytes.Length)
			{
				return string.Empty;
			}
			return Encoding.Unicode.GetString(nameBytes).Replace('\\', Path.DirectorySeparatorChar);
		}

		private void ReadFileContent(string targetFilePath, Stream zipStream)
		{
			var bytes = new byte[sizeof(int)];

			int totalRead = 0;
			int bytesRead;
			while ((bytesRead = zipStream.Read(bytes, totalRead, bytes.Length - totalRead)) > 0)
			{
				totalRead += bytesRead;
			}
			int fileContentLength = BitConverter.ToInt32(bytes, 0);
			bytes = new byte[fileContentLength];
			totalRead = 0;
			while (totalRead < bytes.Length)
			{
				bytesRead = zipStream.Read(bytes, totalRead, bytes.Length - totalRead);
				if (bytesRead == 0)
				{
					break;
				}
				totalRead += bytesRead;
			}
			string targetDirectoryPath = Path.GetDirectoryName(targetFilePath);
			if (!_fileSystem.ExistsDirectory(targetDirectoryPath))
			{
				_fileSystem.CreateDirectory(targetDirectoryPath);
			}
			using (var stream = _fileSystem.FileOpenStream(targetFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				stream.Write(bytes, 0, fileContentLength);
			}
		}

		private static void CheckPackToGZipArgument(IEnumerable<string> files, string rootDirectoryPath,
				string destinationPackagePath)
		{
			files.CheckArgumentNull(nameof(files));
			rootDirectoryPath.CheckArgumentNullOrWhiteSpace(nameof(rootDirectoryPath));
			destinationPackagePath.CheckArgumentNullOrWhiteSpace(nameof(destinationPackagePath));
		}

		private void PackToGZip(string filePath, string rootDirectoryPath, GZipStream zipStream)
		{
			var relativeFilePath = _fileSystem.ConvertToRelativePath(filePath, rootDirectoryPath);
			WriteFileName(relativeFilePath, zipStream);
			WriteFileContent(filePath, zipStream);
		}

		private static void CheckUnpackFromGZipArgument(string packedPackagePath, string destinationPackageDirectory)
		{
			packedPackagePath.CheckArgumentNullOrWhiteSpace(nameof(packedPackagePath));
			destinationPackageDirectory.CheckArgumentNullOrWhiteSpace(nameof(destinationPackageDirectory));
		}

		private bool UnpackFromGZip(string destinationDirectory, MemoryStream zipStream)
		{
			string fileRelativePath = ReadFileRelativePath(zipStream);
			if (string.IsNullOrEmpty(fileRelativePath)) {
				return false;
			}
			ReadFileContent(Path.Combine(destinationDirectory, fileRelativePath), zipStream);
			return true;
		}

		// Reads the length-prefixed content block the stream is positioned at, WITHOUT writing it anywhere.
		// Answers false when the block is truncated or its declared length is not credible, which is the only
		// signal a caller gets that the archive is corrupt: entries carry no checksum and no terminator, so a
		// bad length would otherwise be read as a valid (huge) allocation request. A Try-pattern rather than a
		// nullable return because a zero-length entry is a legitimate result and must stay distinguishable
		// from a failed read.
		private static bool TryReadFileContent(MemoryStream zipStream, out byte[] content)
		{
			content = null;
			var lengthBytes = new byte[sizeof(int)];
			if (SafeReadGZipStream(zipStream, lengthBytes) != lengthBytes.Length) {
				return false;
			}
			int fileContentLength = BitConverter.ToInt32(lengthBytes, 0);
			// Bound by what is actually left rather than by a magic number: the archive is already fully
			// decompressed in memory, so "longer than the remainder" is exactly the impossible case.
			if (fileContentLength < 0 || fileContentLength > zipStream.Length - zipStream.Position) {
				return false;
			}
			var buffer = new byte[fileContentLength];
			if (SafeReadGZipStream(zipStream, buffer) != fileContentLength) {
				return false;
			}
			content = buffer;
			return true;
		}

		// Advances past the content block the stream is positioned at. Same credibility bound as the reader
		// above — a bad length here would seek past the end and turn every later entry into garbage.
		private static bool SkipFileContent(MemoryStream zipStream)
		{
			var lengthBytes = new byte[sizeof(int)];
			if (SafeReadGZipStream(zipStream, lengthBytes) != lengthBytes.Length) {
				return false;
			}
			int fileContentLength = BitConverter.ToInt32(lengthBytes, 0);
			if (fileContentLength < 0 || fileContentLength > zipStream.Length - zipStream.Position) {
				return false;
			}
			zipStream.Seek(fileContentLength, SeekOrigin.Current);
			return true;
		}

		// The archive stores whatever separator the packing host used, so compare on a normalized form.
		private static string NormalizeEntryPath(string entryPath) =>
			entryPath.Replace('\\', '/').Trim('/');

		#endregion

		#region Methods: Public


		public void PackToGZip(IEnumerable<string> files, string rootDirectoryPath, string destinationPackagePath)
		{
			CheckPackToGZipArgument(files, rootDirectoryPath, destinationPackagePath);
			using Stream fileStream =
				_fileSystem.FileOpenStream(destinationPackagePath, FileMode.Create, FileAccess.Write, FileShare.None);
			using var zipStream = new GZipStream(fileStream, CompressionMode.Compress);
			foreach (string filePath in files) {
				PackToGZip(filePath, rootDirectoryPath, zipStream);
			}
		}

		public void UnpackFromGZip(string packedPackagePath, string destinationPackageDirectory){
			CheckUnpackFromGZipArgument(packedPackagePath, destinationPackageDirectory);
			using var fileStream = _fileSystem.FileOpenStream(packedPackagePath, FileMode.Open, FileAccess.Read, FileShare.None);
			using var zipStream = new GZipStream(fileStream, CompressionMode.Decompress, true);
			var newStream = new MemoryStream();
			zipStream.CopyTo(newStream);
			newStream.Seek(0, SeekOrigin.Begin);
			while (UnpackFromGZip(destinationPackageDirectory, newStream)) { }
		}

		public bool TryReadFileFromGZip(string packedPackagePath, string relativeFilePath, out byte[] content) {
			packedPackagePath.CheckArgumentNullOrWhiteSpace(nameof(packedPackagePath));
			relativeFilePath.CheckArgumentNullOrWhiteSpace(nameof(relativeFilePath));
			content = null;
			string wanted = NormalizeEntryPath(relativeFilePath);
			using var fileStream =
				_fileSystem.FileOpenStream(packedPackagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
			using var zipStream = new GZipStream(fileStream, CompressionMode.Decompress, true);
			using var newStream = new MemoryStream();
			zipStream.CopyTo(newStream);
			newStream.Seek(0, SeekOrigin.Begin);
			while (true) {
				string entryPath = ReadFileRelativePath(newStream);
				if (string.IsNullOrEmpty(entryPath)) {
					// End of the walk. It is a CLEAN end only if the stream is actually exhausted; bytes left
					// over mean the name prefix was incredible, i.e. the container is corrupt. Distinguishing
					// them matters to the caller, which otherwise reports a truncated archive as one that
					// simply has no such entry — same remedy, wrong diagnosis.
					if (newStream.Position < newStream.Length) {
						throw new InvalidDataException(
							$"'{packedPackagePath}' is not a readable package archive: the entry starting at "
							+ $"offset {newStream.Position} declares an impossible name length.");
					}
					return false;
				}
				if (!string.Equals(NormalizeEntryPath(entryPath), wanted, StringComparison.OrdinalIgnoreCase)) {
					if (!SkipFileContent(newStream)) {
						throw new InvalidDataException(
							$"'{packedPackagePath}' is not a readable package archive: entry '{entryPath}' "
							+ "declares a content length that does not fit in the remaining bytes.");
					}
					continue;
				}
				if (!TryReadFileContent(newStream, out content)) {
					throw new InvalidDataException(
						$"'{packedPackagePath}' is not a readable package archive: entry '{entryPath}' is "
						+ "truncated.");
				}
				return true;
			}
		}

		public IReadOnlyList<string> ListGZipEntryNames(string packedPackagePath) {
			packedPackagePath.CheckArgumentNullOrWhiteSpace(nameof(packedPackagePath));
			var names = new List<string>();
			using var fileStream =
				_fileSystem.FileOpenStream(packedPackagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
			using var zipStream = new GZipStream(fileStream, CompressionMode.Decompress, true);
			using var newStream = new MemoryStream();
			zipStream.CopyTo(newStream);
			newStream.Seek(0, SeekOrigin.Begin);
			while (true) {
				string entryPath = ReadFileRelativePath(newStream);
				if (string.IsNullOrEmpty(entryPath)) {
					// Same discriminator as ReadFileFromGZip: a clean end leaves nothing behind, so bytes
					// remaining mean the name prefix was incredible and the container is corrupt.
					if (newStream.Position < newStream.Length) {
						throw new InvalidDataException(
							$"'{packedPackagePath}' is not a readable package archive: the entry starting at "
							+ $"offset {newStream.Position} declares an impossible name length.");
					}
					return names;
				}
				names.Add(NormalizeEntryPath(entryPath));
				if (!SkipFileContent(newStream)) {
					throw new InvalidDataException(
						$"'{packedPackagePath}' is not a readable package archive: entry '{entryPath}' "
						+ "declares a content length that does not fit in the remaining bytes.");
				}
			}
		}

		public void Unzip(string zipFilePath, string destinationDirectory) {
			_zipFile.ExtractToDirectory(zipFilePath, destinationDirectory);
		}

		public void Zip(string directoryPath, string zipFilePath) {
			_zipFile.CreateFromDirectory(directoryPath, zipFilePath);
		}

		#endregion

	}

	#endregion

	public interface IZipFile
	{

		void ExtractToDirectory(string sourceArchiveFileName, string destinationDirectoryName);

		void CreateFromDirectory(string sourceDirectoryName, string destinationArchiveFileName);

	}
	
	public class ZipFileWrapper : IZipFile
	{

		public void ExtractToDirectory(string sourceArchiveFileName, string destinationDirectoryName) {
			ZipFile.ExtractToDirectory(sourceArchiveFileName, destinationDirectoryName);
		}

		public void CreateFromDirectory(string sourceDirectoryName, string destinationArchiveFileName) {
			
			ZipFile.CreateFromDirectory(sourceDirectoryName, destinationArchiveFileName);
		}

	}
	
}
