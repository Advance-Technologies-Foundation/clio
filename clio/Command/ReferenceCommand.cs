using System;
using System.IO;
using System.Linq;
using Clio.Common;
using Clio.Project;
using CommandLine;
using IAbstractionsFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command
{
	[Verb("ref-to", HelpText = "Change creatio package project core paths", Hidden = true)]
	public class ReferenceOptions
	{
		[Option('r', "ReferencePattern", Required = false, HelpText = "Pattern for reference path",
			Default = null)]
		public string RefPattern { get; set; }

		[Option('p', "Path", Required = false, HelpText = "Path to the project file",
			Default = null)]
		public string Path { get; set; }

		[Value(0, MetaName = "ReferenceType", Required = false, HelpText = "Indicates what the project will refer to." +
			" Can be 'bin' or 'src'", Default = "src")]
		public string ReferenceType { get; set; }

	}

	public class ReferenceCommand : Command<ReferenceOptions>
	{
		private readonly ICreatioPkgProjectCreator _projectCreator;
		private readonly ILogger _logger;
		private readonly IAbstractionsFileSystem _fileSystem;

		public ReferenceCommand(ICreatioPkgProjectCreator projectCreator, ILogger logger)
			: this(projectCreator, logger, new System.IO.Abstractions.FileSystem()) {
		}

		public ReferenceCommand(ICreatioPkgProjectCreator projectCreator, ILogger logger, IAbstractionsFileSystem fileSystem) {
			_projectCreator = projectCreator;
			_logger = logger;
			_fileSystem = fileSystem;
		}

		private string CurrentProj =>
			_fileSystem.DirectoryInfo.New(Environment.CurrentDirectory).GetFiles("*.csproj").FirstOrDefault()?.FullName;

		public override int Execute(ReferenceOptions options) {
			options.Path = options.Path ?? CurrentProj;
			if (string.IsNullOrEmpty(options.Path)) {
				throw new ArgumentNullException(nameof(options.Path));
			}
			if (!string.IsNullOrEmpty(options.RefPattern)) {
				options.ReferenceType = "custom";
			}
			ICreatioPkgProject project = _projectCreator.CreateFromFile(options.Path);
			try {
				switch (options.ReferenceType) {
					case "bin":
						project = project.RefToBin();
						break;
					case "src":
						project = project.RefToCoreSrc();
						break;
					case "custom":
						project = project.RefToCustomPath(options.RefPattern);
						break;
					case "unit-bin":
						project = project.RefToUnitBin();
						break;
					case "unit-src":
						project = project.RefToUnitCoreSrc();
						break;
					default:
						throw new NotSupportedException($"You use not supported option type {options.ReferenceType}");
				}
				project.SaveChanges();
				_logger.WriteLine("Done");
				return 0;
			} catch (Exception e) {
				_logger.WriteError(e.Message);
				return 1;
			}
		}
	}
}
