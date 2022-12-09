using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace Clio.Common
{
	#region Class: CompressionUtilities

	public class CompressionUtilities : ICompressionUtilities
	{

		#region Fields: Private

		private readonly IFileSystem _fileSystem;

		#endregion

		#region Constructors: Public

		public CompressionUtilities(IFileSystem fileSystem) {
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			_fileSystem = fileSystem;
		}

		#endregion

		#region Methods: Private

		private static void WriteFileName(string relativeFilePath, GZipStream zipStream) {
			char[] chars = relativeFilePath.ToCharArray();
			zipStream.Write(BitConverter.GetBytes(chars.Length), 0, sizeof(int));
			foreach (char c in chars) {
				zipStream.Write(BitConverter.GetBytes(c), 0, sizeof(char));
			}
		}

		private static void WriteFileContent(string filePath, GZipStream zipStream) {
			byte[] bytes = File.ReadAllBytes(filePath);
			zipStream.Write(BitConverter.GetBytes(bytes.Length), 0, sizeof(int));
			zipStream.Write(bytes, 0, bytes.Length);
		}

		private string ReadFileRelativePath(GZipStream zipStream) {
			var bytes = new byte[sizeof(int)];
			int readed = zipStream.Read(bytes, 0, sizeof(int));
			if (readed < sizeof(int)) {
				return null;
			}
			int fileNameLength = BitConverter.ToInt32(bytes, 0);
			bytes = new byte[sizeof(char)];
			var stringBuilder = new StringBuilder();
			for (int i = 0; i < fileNameLength; i++) {
				zipStream.Read(bytes, 0, sizeof(char));
				char c = BitConverter.ToChar(bytes, 0);
				stringBuilder.Append(c);
			}
			return _fileSystem.NormalizeFilePathByPlatform(stringBuilder.ToString());
		}

		private static void ReadFileContent(string targetFilePath, GZipStream zipStream) {
			var bytes = new byte[sizeof(int)];
			zipStream.Read(bytes, 0, sizeof(int));
			int fileContentLength = BitConverter.ToInt32(bytes, 0);
			bytes = new byte[fileContentLength];
			var buffer = new byte[fileContentLength];
			int totalRead = 0;
			while (totalRead < buffer.Length) {
				int bytesRead = zipStream.Read(buffer, totalRead, buffer.Length - totalRead);
				if (bytesRead == 0) {
					break;
				}
				totalRead += bytesRead;
			}
			string targetDirectoryPath = Path.GetDirectoryName(targetFilePath);
			if (!Directory.Exists(targetDirectoryPath)) {
				Directory.CreateDirectory(targetDirectoryPath);
			}
			using (var stream = new FileStream(targetFilePath, FileMode.Create, FileAccess.Write, FileShare.None)) {
				stream.Write(bytes, 0, fileContentLength);
			}
		}

		private static void CheckPackToGZipArgument(IEnumerable<string> files, string rootDirectoryPath, 
				string destinationPackagePath) {
			files.CheckArgumentNull(nameof(files));
			rootDirectoryPath.CheckArgumentNullOrWhiteSpace(nameof(rootDirectoryPath));
			destinationPackagePath.CheckArgumentNullOrWhiteSpace(nameof(destinationPackagePath));
		}

		private void PackToGZip(string filePath, string rootDirectoryPath, GZipStream zipStream) {
			var relativeFilePath = _fileSystem.ConvertToRelativePath(filePath, rootDirectoryPath);
			WriteFileName(relativeFilePath, zipStream);
			WriteFileContent(filePath, zipStream);
		}

		private static void CheckUnpackFromGZipArgument(string packedPackagePath, string destinationPackageDirectory) {
			packedPackagePath.CheckArgumentNullOrWhiteSpace(nameof(packedPackagePath));
			destinationPackageDirectory.CheckArgumentNullOrWhiteSpace(nameof(destinationPackageDirectory));
		}

		private bool UnpackFromGZip(string destinationDirectory, GZipStream zipStream) {
			string fileRelativePath = ReadFileRelativePath(zipStream);
			if (string.IsNullOrEmpty(fileRelativePath)) {
				return false;
			}
			ReadFileContent(Path.Combine(destinationDirectory, fileRelativePath), zipStream);
			return true;
		}

		#endregion

		#region Methods: Public


		public void PackToGZip(IEnumerable<string> files, string rootDirectoryPath, string destinationPackagePath) {
			CheckPackToGZipArgument(files, rootDirectoryPath, destinationPackagePath);
			using (Stream fileStream = 
				File.Open(destinationPackagePath, FileMode.Create, FileAccess.Write, FileShare.None)) {
				using (var zipStream = new GZipStream(fileStream, CompressionMode.Compress)) {
					foreach (string filePath in files) {
						PackToGZip(filePath, rootDirectoryPath, zipStream);
					}
				}
			}
		}

		public void UnpackFromGZip(string packedPackagePath, string destinationPackageDirectory) {
			CheckUnpackFromGZipArgument(packedPackagePath, destinationPackageDirectory);
			using (var fileStream = new FileStream(packedPackagePath, FileMode.Open, FileAccess.Read, FileShare.None)) {
				using (var zipStream = new GZipStream(fileStream, CompressionMode.Decompress, true)) {
					while(UnpackFromGZip(destinationPackageDirectory, zipStream)) {
					}
				}
			}
		}


		#endregion

	}

	#endregion

}