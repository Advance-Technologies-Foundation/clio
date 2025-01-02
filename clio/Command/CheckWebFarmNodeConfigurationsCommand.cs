using Clio.Common;
using CommandLine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;

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
			if (!_fileSystem.ExistsDirectory(path1) || !_fileSystem.ExistsDirectory(path2)) {
				throw new ArgumentException("One or both paths do not exist.");
			}
			var files1 = _fileSystem.GetFiles(path1, "*", SearchOption.AllDirectories)
			.Select(p => p.Substring(path1.Length).TrimStart(Path.DirectorySeparatorChar))
			.ToHashSet();
						var files2 = _fileSystem.GetFiles(path2, "*", SearchOption.AllDirectories)
			.Select(p => p.Substring(path2.Length).TrimStart(Path.DirectorySeparatorChar))
			.ToHashSet();
						var dirs1 = _fileSystem.GetDirectories(path1, "*", SearchOption.AllDirectories)
			.Select(p => p.Substring(path1.Length).TrimStart(Path.DirectorySeparatorChar))
			.ToHashSet();
						var dirs2 = _fileSystem.GetDirectories(path2, "*", SearchOption.AllDirectories)
			.Select(p => p.Substring(path2.Length).TrimStart(Path.DirectorySeparatorChar))
			.ToHashSet();
			var missingDirsInPath2 = dirs1.Except(dirs2).Select(d => $"Folder missing in {path2}: {d}");
			var missingDirsInPath1 = dirs2.Except(dirs1).Select(d => $"Folder missing in {path1}: {d}");
			var missingFilesInPath2 = files1.Except(files2).Select(f => $"File missing in {path2}: {f}");
			var missingFilesInPath1 = files2.Except(files1).Select(f => $"File missing in {path1}: {f}");
			var differences = new List<string>();
			differences.AddRange(missingFilesInPath2);
			differences.AddRange(missingFilesInPath1);
			differences.AddRange(missingDirsInPath2);
			differences.AddRange(missingDirsInPath1);
			var commonFiles = files1.Intersect(files2);
			foreach (var file in commonFiles) {
				var filePath1 = Path.Combine(path1, file);
				var filePath2 = Path.Combine(path2, file);
				if (!FilesAreEqual(filePath1, filePath2)) {
					differences.Add($"Files differ: {file}");
				}
			}
			return differences;
		}

		private bool FilesAreEqual(string filePath1, string filePath2) {
			var file1Bytes = _fileSystem.ReadAllBytes(filePath1);
			var file2Bytes = _fileSystem.ReadAllBytes(filePath2);
			return file1Bytes.SequenceEqual(file2Bytes);
		}
	}

}
