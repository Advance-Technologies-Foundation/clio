using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace bpmcli
{
	#region Class: CompressionUtilities

	/// <summary>
	/// Предоставляет методы работы по упаковке (сжатию) и распаковке данных.
	/// </summary>
	[ComVisible(true)]
	public class CompressionUtilities
	{

		#region Methods: Private

		private static void WriteFileName(string relativeFilePath, GZipStream zipStream) {
			char[] chars = relativeFilePath.ToCharArray();
			zipStream.Write(BitConverter.GetBytes(chars.Length), 0, sizeof(int));
			foreach (char c in chars)
			{
				zipStream.Write(BitConverter.GetBytes(c), 0, sizeof(char));
			}
		}

		private static void WriteFileContent(string filePath, GZipStream zipStream) {
			byte[] bytes = File.ReadAllBytes(filePath);
			zipStream.Write(BitConverter.GetBytes(bytes.Length), 0, sizeof(int));
			zipStream.Write(bytes, 0, bytes.Length);
		}

		private static string ReadFileName(GZipStream zipStream) {
			var bytes = new byte[sizeof(int)];
			int readed = zipStream.Read(bytes, 0, sizeof(int));
			if (readed < sizeof(int)) {
				return null;
			}
			int fileNameLength = BitConverter.ToInt32(bytes, 0);
			bytes = new byte[sizeof(char)];
			var sb = new StringBuilder();
			for (int i = 0; i < fileNameLength; i++) {
				zipStream.Read(bytes, 0, sizeof(char));
				char c = BitConverter.ToChar(bytes, 0);
				sb.Append(c);
			}
			return sb.ToString();
		}

		private static void ReadFileContent(string targetFilePath, GZipStream zipStream) {
			var bytes = new byte[sizeof(int)];
			zipStream.Read(bytes, 0, sizeof(int));
			int fileContentLength = BitConverter.ToInt32(bytes, 0);
			bytes = new byte[fileContentLength];
			zipStream.Read(bytes, 0, bytes.Length);
			string targetDirectoryPath = Path.GetDirectoryName(targetFilePath);
			if (!Directory.Exists(targetDirectoryPath)) {
				Directory.CreateDirectory(targetDirectoryPath);
			}
			using (var stream = new FileStream(targetFilePath, FileMode.Create, FileAccess.Write, FileShare.None)) {
				stream.Write(bytes, 0, fileContentLength);
			}
		}

		#endregion

		#region Methods: Public

		public static void ZipFile(string filePath, int rootDirectoryPathLength, GZipStream zipStream) {
			string relativeFilePath = filePath.Substring(rootDirectoryPathLength);
			WriteFileName(relativeFilePath.TrimStart(Path.DirectorySeparatorChar), zipStream);
			WriteFileContent(filePath, zipStream);
		}

		public static bool UnzipFile(string targetDirectoryPath, GZipStream zipStream) {
			string fileName = ReadFileName(zipStream);
			if (string.IsNullOrEmpty(fileName)) {
				return false;
			}
			ReadFileContent(Path.Combine(targetDirectoryPath, fileName), zipStream);
			return true;
		}

		#endregion
	}

	#endregion

}