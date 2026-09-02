using System;
using System.Collections.Generic;
using Clio;
using Clio.Common;
using Clio.UserEnvironment;
using CommandLine;
using CommandLine.Text;

namespace Clio.Command
{
	[Verb("new-pkg", Aliases = new string[] { "init" }, HelpText = "Create a new creatio package in local file system")]
	public class NewPkgOptions
	{
		[Value(0, MetaName = "Name", Required = true, HelpText = "Name of the created instance")]
		public string Name { get; set; }

		[Option('r', "References", Required = false, HelpText = "Set references to local bin assemblies for development")]
		public string Rebase { get; set; }

		[Usage(ApplicationAlias = "clio")]
		public static IEnumerable<Example> Examples =>
			new List<Example> {
				new Example("Create new package with name 'ATF'",
					new NewPkgOptions { Name = "ATF" }
				),
				new Example("Create new package with name 'ATF' and with links on local installation creatio with file design mode",
					new NewPkgOptions { Name = "ATF", Rebase = "bin"}
				)
			};
	}

	public class NewPkgCommand : Command<NewPkgOptions>
	{
		private readonly ISettingsRepository _settingsRepository;
		private readonly Command<ReferenceOptions> _referenceCommand;
		private readonly ILogger _logger;

		public NewPkgCommand(ISettingsRepository settingsRepository, Command<ReferenceOptions> referenceCommand, ILogger logger) {
			_settingsRepository = settingsRepository;
			_referenceCommand = referenceCommand;
			_logger = logger;
		}

		public override int Execute(NewPkgOptions options) {
			var settings = _settingsRepository.GetEnvironment();
			try {
				CreatioPackage package = CreatioPackage.CreatePackage(options.Name, settings.Maintainer);
				package.Create();
				if (!string.IsNullOrEmpty(options.Rebase) && options.Rebase != "nuget") {
					int referenceResult = _referenceCommand.Execute(new ReferenceOptions {
						//The reference command loads a project file, not the package directory
						Path = package.ProjectFilePath,
						ReferenceType = options.Rebase
					});
					if (referenceResult != 0) {
						//An unsupported reference type reports the failure and returns nonzero without
						//rebasing anything. Reporting "Done" and exiting 0 here told the caller the package
						//was ready, and removing packages.config on top of that left it with a package that
						//neither restores its assemblies nor points at local ones.
						_logger.WriteError(
							$"Failed to set '{options.Rebase}' references for package '{options.Name}'.");
						return referenceResult;
					}
					package.RemovePackageConfig();
				}
				_logger.WriteInfo("Done");
				return 0;
			} catch (Exception e) {
				_logger.WriteError(e.GetReadableMessageException(Program.IsDebugMode));
				return 1;
			}
		}
	}
}
