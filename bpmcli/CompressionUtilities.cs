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

		#endregion

		#region Methods: Public

		public static void ZipFile(string filePath, int rootDirectoryPathLength, GZipStream zipStream) {
			string relativeFilePath = filePath.Substring(rootDirectoryPathLength);
			WriteFileName(relativeFilePath.TrimStart(Path.DirectorySeparatorChar), zipStream);
			WriteFileContent(filePath, zipStream);
		}

		#endregion
	}

	#endregion

}