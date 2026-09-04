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
					if (initialRefType == RefType.Undef) {
						//Nothing was rewritten AND the style was never recognized. Saving here would strip
						//packages.config for RefToBin and RefToCoreSrc and leave references pointing at
						//assemblies nothing restores any more.
						//Diagnosed from the recognition signal itself, not from the change count: a
						//recognized project can legitimately need no write - `custom` never yields
						//RefType.Custom from detection, and unit-bin/unit-src match nothing on a non-UnitTest
						//project - and reporting those as unrecognized contradicts what ref-to.md promises
						//about exit 1.
						_logger.WriteError($"Could not recognize the reference style of {options.Path}. "
							+ "No reference was changed and the project was left unchanged.");
						return 1;
					}
					if (!project.HasPendingChanges) {
						//Recognized and no reference needed rewriting: running the command twice, or on a
						//project no rule of this reference type matches, is not a failure.
						_logger.WriteLine($"{options.Path} already references {options.ReferenceType}, "
							+ "nothing to change");
						return 0;
					}
					//No HintPath moved, but something else was rewritten - a strong-name suffix stripped
					//from a Reference Include. Returning here would compute it in memory and throw it away,
					//which is what the reference-count gate used to do.
					project.SaveChanges();
					_logger.WriteLine($"{options.Path} already references {options.ReferenceType}; "
						+ "normalized the remaining reference metadata");
					_logger.WriteLine("Done");
					return 0;
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
