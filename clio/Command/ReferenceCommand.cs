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

		/// <summary>
		/// Reference style a given --ReferenceType asks for, used to tell a project that is
		/// already in that style from one whose style was not recognized.
		/// </summary>
		private static RefType RequestedRefType(string referenceType) =>
			referenceType switch {
				"bin" => RefType.Bin,
				"src" => RefType.CoreSrc,
				"unit-bin" => RefType.UnitTest,
				"unit-src" => RefType.UnitTest,
				"custom" => RefType.Custom,
				_ => RefType.Undef
			};

		public override int Execute(ReferenceOptions options) {
			options.Path = options.Path ?? CurrentProj;
			if (string.IsNullOrEmpty(options.Path)) {
				throw new ArgumentNullException(nameof(options.Path));
			}
			if (_fileSystem.Directory.Exists(options.Path)) {
				//XElement.Load on a directory raises UnauthorizedAccessException, which the user
				//sees as "Access to the path ... is denied" - a permission error that is not one
				_logger.WriteError($"'{options.Path}' is a directory. "
					+ "Pass the package project file (.csproj) instead.");
				return 1;
			}
			if (!string.IsNullOrEmpty(options.RefPattern)) {
				options.ReferenceType = "custom";
			}
			ICreatioPkgProject project = _projectCreator.CreateFromFile(options.Path);
			RefType initialRefType = project.CurrentRefType;
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
				if (project.ChangedReferencesCount == 0) {
					if (initialRefType == RequestedRefType(options.ReferenceType)) {
						//Already in the requested style: running the command twice is not a failure
						_logger.WriteLine($"{options.Path} already references {options.ReferenceType}, "
							+ "nothing to change");
						return 0;
					}
					//Nothing was rewritten: the project's reference style was not recognized.
					//Saving here would strip packages.config for RefToBin and RefToCoreSrc and
					//leave references pointing at assemblies nothing restores any more.
					_logger.WriteError($"Could not recognize the reference style of {options.Path}. "
						+ "No reference was changed and the project was left unchanged.");
					return 1;
				}
				project.SaveChanges();
				_logger.WriteLine($"Changed {project.ChangedReferencesCount} references");
				_logger.WriteLine("Done");
				return 0;
			} catch (Exception e) {
				_logger.WriteError(e.Message);
				return 1;
			}
		}
	}
}
