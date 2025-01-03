using Clio.Common;
using CommandLine;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using System.Threading;

namespace Clio.Command
{
	[Verb("check-web-farm-node", Aliases = new string[] { }, HelpText = "Check web farm node configurations")]
	public class CheckWebFarmNodeConfigurationsOptions
	{
		[Value(0, MetaName = "Paths", Required = true, HelpText = "Folder Paths")]
		public string Paths
		{
			get; set;
		}

		[Option('d', "detail", Required = false, HelpText = "Short information", Default = false)]
		public bool detailMode { get; set; }
	}

	public class CheckWebFarmNodeConfigurationsCommand : Command<CheckWebFarmNodeConfigurationsOptions>
	{

		private readonly ILogger _logger;
		private readonly IFileSystem _fileSystem;

		public CheckWebFarmNodeConfigurationsCommand(ILogger logger, IFileSystem fileSystem) {
			_logger = logger;
			_fileSystem = fileSystem;
		}

		public override int Execute(CheckWebFarmNodeConfigurationsOptions options) {
			_logger.WriteLine("Check started:");
			var paths = options.Paths.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
			.Select(p => p.Trim())
			.ToList();
			if (paths.Count < 2) {
				_logger.WriteLine("At least two paths must be specified for comparison.");
				return 1;
			}
			var basePath = paths[0];
			if (!_fileSystem.ExistsDirectory(basePath)) {
				_logger.WriteLine($"Base path does not exist: {basePath}");
				return 1;
			}
			for (int i = 1; i < paths.Count; i++) {
				var comparePath = paths[i];

				if (!_fileSystem.ExistsDirectory(comparePath)) {
					_logger.WriteLine($"Comparison path does not exist: {comparePath}");
					continue;
				}
				_logger.WriteLine($"\nComparing {basePath} and {comparePath}:");
				var differences = new DirectoryComparer(_fileSystem).CompareDirectories(basePath, comparePath);

				if (differences.Count == 0) {
					_logger.WriteLine("The folders are the same.");
				} else {
					_logger.WriteLine("The folders are not the same:");
					if (!options.detailMode) {
						_logger.WriteLine($"Count: {differences.Count}");
					} else {
						foreach (var difference in differences) {
							_logger.WriteLine(difference);
						}
					}
					return 1;
				}
			}
			return 0;
		}
	}

	public class DirectoryComparer
	{
		private readonly IFileSystem _fileSystem;

		public DirectoryComparer(IFileSystem fileSystem) {
			_fileSystem = fileSystem;
		}
		public List<string> CompareDirectories(string path1, string path2) {
			var dirs1 = new ConcurrentBag<string>();
			var dirs2 = new ConcurrentBag<string>();
			var files1 = new ConcurrentBag<string>();
			var files2 = new ConcurrentBag<string>();
			if (!_fileSystem.ExistsDirectory(path1) || !_fileSystem.ExistsDirectory(path2)) {
				throw new ArgumentException("One or both paths do not exist.");
			}
			Parallel.Invoke(
				() => ProcessPath(path1, dirs1, files1),
				() => ProcessPath(path2, dirs2, files2)
			);
			files1 = new ConcurrentBag<string>(files1.Select(f => f.Substring(path1.Length).TrimStart(Path.DirectorySeparatorChar)));
			files2 = new ConcurrentBag<string>(files2.Select(f => f.Substring(path2.Length).TrimStart(Path.DirectorySeparatorChar)));
			int totalFiles1 = files1.Count, totalFiles2 = files2.Count;
			int processedFiles = 0;
			ConcurrentBag<string> commonFiles = new (files1.Intersect(files2));
			ConcurrentBag<string> differenceInCommonFiles = new();
			Parallel.ForEach(commonFiles, file => {
				var file1 = Path.Combine(path1, file);
				var file2 = Path.Combine(path2, file);
				if (!CompareFiles(file1 ,file2)) {
					differenceInCommonFiles.Add(file);
					Interlocked.Increment(ref processedFiles);
					int percentage = (int)((double)processedFiles / commonFiles.Count * 100);
					Console.WriteLine($"Progress: {processedFiles}/{commonFiles.Count} files processed ({percentage}%)");
					Console.WriteLine($"File content differs: {file}");
				}
			});
			var missingDirsInPath2 = dirs1.Except(dirs2).Select(d => $"Folder missing in {path2}: {d}");
			var missingDirsInPath1 = dirs2.Except(dirs1).Select(d => $"Folder missing in {path1}: {d}");
			var missingFilesInPath2 = files1.Except(files2)
				.Select(f => {
					Interlocked.Increment(ref processedFiles);
					int percentage = (int)((double)processedFiles / totalFiles1 * 100);
					Console.WriteLine($"Progress: {processedFiles}/{totalFiles1} files processed ({percentage}%)");
					return $"File missing in {path2}: {f}";
				});
			var missingFilesInPath1 = files2.Except(files1)
				.Select(f => {
					Interlocked.Increment(ref processedFiles);
					int percentage = (int)((double)processedFiles / totalFiles2 * 100);
					Console.WriteLine($"Progress: {processedFiles}/{totalFiles2} files processed ({percentage}%)");
					return $"File missing in {path1}: {f}";
				});
			foreach (var msg in missingDirsInPath2)
				Console.WriteLine(msg);
			foreach (var msg in missingDirsInPath1)
				Console.WriteLine(msg);
			foreach (var msg in missingFilesInPath2)
				Console.WriteLine(msg);
			foreach (var msg in missingFilesInPath1)
				Console.WriteLine(msg);
			Console.WriteLine("Files with different content:");
			foreach (var msg in differenceInCommonFiles)
				Console.WriteLine(msg);
			var allDifferences = missingDirsInPath2.Concat(missingDirsInPath1)
				.Concat(missingFilesInPath2).Concat(differenceInCommonFiles)
				.Concat(missingFilesInPath1).ToList();
			return allDifferences;
		}

		private bool CompareFiles(string v1, string v2) {
			var file1Info = _fileSystem.GetFilesInfos(v1);
			var file2Info = _fileSystem.GetFilesInfos(v2);
			if (file1Info.Length != file2Info.Length) {
				return false;
			}
			if (file1Info.Length == file2Info.Length && file1Info.LastWriteTimeUtc != file1Info.LastWriteTimeUtc) {
				return _fileSystem.CompareFiles(v1, v2);
			} else {
				return true;
			}
		}

		private void ProcessPath(string rootPath, ConcurrentBag<string> dirs, ConcurrentBag<string> files) {
			int dirCount = 0, fileCount = 0;
			var topDirs = _fileSystem.GetDirectories(rootPath, "*", SearchOption.TopDirectoryOnly);
			foreach (var dir in topDirs) {
				dirs.Add(dir.Substring(rootPath.Length).TrimStart(Path.DirectorySeparatorChar));
				dirCount++;
			}
			var topFiles = _fileSystem.GetFiles(rootPath, "*", SearchOption.TopDirectoryOnly);
			foreach (var file in topFiles) {
				files.Add(file);
				fileCount++;
			}
			Console.WriteLine($"[{rootPath}] Found: {dirCount} directories, {fileCount} files");
			Parallel.ForEach(topDirs, subDir => {
				ProcessPath(subDir, dirs, files);
			});
		}

	}

}
