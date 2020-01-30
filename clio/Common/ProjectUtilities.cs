using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Clio.Common
{
	public class ProjectUtilities : IProjectUtilities
	{
		internal static void CopyDir(string source, string dest) {
			if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(dest))
				return;
			Directory.CreateDirectory(dest);
			foreach (string fn in Directory.GetFiles(source)) {
				File.Copy(fn, Path.Combine(dest, Path.GetFileName(fn)), true);
			}
			foreach (string dirFn in Directory.GetDirectories(source)) {
				CopyDir(dirFn, Path.Combine(dest, Path.GetFileName(dirFn)));
			}
		}

		internal static void CopyProjectElement(string sourcePath, string destinationPath, string name) {
			string fromAssembliesPath = Path.Combine(sourcePath, name);
			if (Directory.Exists(fromAssembliesPath)) {
				string toAssembliesPath = Path.Combine(destinationPath, name);
				CopyDir(fromAssembliesPath, toAssembliesPath);
			}
		}

		private static string CreateTempPath(string sourcePath) {
			var directoryInfo = new DirectoryInfo(sourcePath);
			string tempPath = Path.Combine(Path.GetTempPath(), directoryInfo.Name);
			return tempPath;
		}

		private static void CopyProjectFiles(string sourcePath, string destinationPath) {
			if (Directory.Exists(destinationPath)) {
				Directory.Delete(destinationPath, true);
			}
			Directory.CreateDirectory(destinationPath);
			CopyProjectElement(sourcePath, destinationPath, "Assemblies");
			CopyProjectElement(sourcePath, destinationPath, "Bin");
			CopyProjectElement(sourcePath, destinationPath, "Data");
			CopyProjectElement(sourcePath, destinationPath, "Files");
			CopyProjectElement(sourcePath, destinationPath, "Resources");
			CopyProjectElement(sourcePath, destinationPath, "Schemas");
			CopyProjectElement(sourcePath, destinationPath, "SqlScripts");
			File.Copy(Path.Combine(sourcePath, "descriptor.json"), Path.Combine(destinationPath, "descriptor.json"));
		}

		public void CompressProject(string sourcePath, string destinationPath, bool skipPdb) {
			if (File.Exists(destinationPath)) {
				File.Delete(destinationPath);
			}
			string tempPath = CreateTempPath(sourcePath);
			CopyProjectFiles(sourcePath, tempPath);

			var files = Directory.GetFiles(tempPath, "*.*", SearchOption.AllDirectories)
				.Where(name => !name.EndsWith(".pdb") || !skipPdb);
			int directoryPathLength = tempPath.Length;
			using (Stream fileStream =
				File.Open(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None)) {
				using (var zipStream = new GZipStream(fileStream, CompressionMode.Compress)) {
					foreach (string filePath in files) {
						CompressionUtilities.ZipFile(filePath, directoryPathLength, zipStream);
					}
				}
			}
			Directory.Delete(tempPath, true);
		}

		public void CompressProjects(string sourcePath, string destinationPath, IEnumerable<string> names, bool skipPdb) {
			throw new NotImplementedException();
		}
	}
}
