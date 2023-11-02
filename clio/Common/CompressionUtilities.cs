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

		public CompressionUtilities(IFileSystem fileSystem)
		{
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			_fileSystem = fileSystem;
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

		private static void WriteFileContent(string filePath, GZipStream zipStream)
		{
			byte[] bytes = File.ReadAllBytes(filePath);
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

		private string ReadFileRelativePath(MemoryStream zipStream)
		{
			var bytes = new byte[sizeof(int)];
			SafeReadGZipStream(zipStream, bytes);
			int fileNameLength = BitConverter.ToInt32(bytes, 0);
			bytes = new byte[sizeof(char)];
			var sb = new StringBuilder();
			for (int i = 0; i < fileNameLength; i++)
			{
				SafeReadGZipStream(zipStream, bytes);
				char character = BitConverter.ToChar(bytes, 0);
				sb.Append(character);
			}
			string filePath = sb.ToString();
			filePath = filePath.Replace('\\', Path.DirectorySeparatorChar);
			return filePath;
		}

		private static void ReadFileContent(string targetFilePath, Stream zipStream)
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
			if (!Directory.Exists(targetDirectoryPath))
			{
				Directory.CreateDirectory(targetDirectoryPath);
			}
			using (var stream = new FileStream(targetFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
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
			if (string.IsNullOrEmpty(fileRelativePath))
			{
				return false;
			}
			ReadFileContent(Path.Combine(destinationDirectory, fileRelativePath), zipStream);
			return true;
		}

		#endregion

		#region Methods: Public


		public void PackToGZip(IEnumerable<string> files, string rootDirectoryPath, string destinationPackagePath)
		{
			CheckPackToGZipArgument(files, rootDirectoryPath, destinationPackagePath);
			using (Stream fileStream =
				File.Open(destinationPackagePath, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				using (var zipStream = new GZipStream(fileStream, CompressionMode.Compress))
				{
					foreach (string filePath in files)
					{
						PackToGZip(filePath, rootDirectoryPath, zipStream);
					}
				}
			}
		}

		public void UnpackFromGZip(string packedPackagePath, string destinationPackageDirectory)
		{
			CheckUnpackFromGZipArgument(packedPackagePath, destinationPackageDirectory);
			using (var fileStream = new FileStream(packedPackagePath, FileMode.Open, FileAccess.Read, FileShare.None))
			{
				using (var zipStream = new GZipStream(fileStream, CompressionMode.Decompress, true))
				{
					var newStream = new MemoryStream();
					zipStream.CopyTo(newStream);

					newStream.Seek(0, SeekOrigin.Begin);

					while (UnpackFromGZip(destinationPackageDirectory, newStream))
					{
					}
				}
			}
		}


		#endregion

	}

	#endregion

}