using System;
using System.IO;
using System.IO.Compression;

namespace cliogate.Files.cs
{

	#region Class: CompressionUtilities

	public static class CompressionUtilities
	{

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

		private static void PackToGZip(string filePath, string rootDirectoryPath, GZipStream zipStream) {
			string relativeFilePath = filePath.Substring(rootDirectoryPath.Length);
			WriteFileName(relativeFilePath.TrimStart(Path.DirectorySeparatorChar), zipStream);
			WriteFileContent(filePath, zipStream);
		}

		#endregion

		#region Methods: Public

		public static MemoryStream GetCompressedFolder(string rootDirectoryPath) {
			string[] files = Directory.GetFiles(rootDirectoryPath, "*.*", SearchOption.AllDirectories);
			byte[] compressed;
			using (var outStream = new MemoryStream()) {
				using (var zipStream = new GZipStream(outStream, CompressionMode.Compress)) {
					foreach (string filePath in files) {
						PackToGZip(filePath, rootDirectoryPath, zipStream);
					}
					zipStream.Flush();
				}
				compressed = outStream.ToArray();
			}
			return new MemoryStream(compressed);
		}

		#endregion

	}

	#endregion

}