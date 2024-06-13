using ATF.Repository.Providers;
using ATF.Repository;
using Clio.Common;
using CommandLine;
using CreatioModel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Autofac;
using Clio.UserEnvironment;
using k8s.Models;
using YamlDotNet.Serialization;
using Clio.Command.PackageCommand;

namespace Clio.Command
{
	[Verb("clone-env", Aliases = new[] { "clone" },
		HelpText = "Clone one environment to another")]
	internal class CloneEnvironmentOptions : ShowDiffEnvironmentsOptions
	{
	}

	internal class CloneEnvironmentCommand : BaseDataContextCommand<CloneEnvironmentOptions>
	{
		private ShowDiffEnvironmentsCommand showDiffEnvironmentsCommand;
		private ApplyEnvironmentManifestCommand applyEnvironmentManifestCommand;
		private PullPkgCommand pullPkgCommand;
		private PushPackageCommand pushPackageCommand;
		private readonly IEnvironmentManager environmentManager;
		private readonly ICompressionUtilities _compressionUtilities;
		private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
		private readonly IFileSystem _fileSystem;
		private readonly ISettingsRepository settingsRepository;

		public CloneEnvironmentCommand(ShowDiffEnvironmentsCommand showDiffEnvironmentsCommand,
			ApplyEnvironmentManifestCommand applyEnvironmentManifestCommand, PullPkgCommand pullPkgCommand,
			PushPackageCommand pushPackageCommand, IEnvironmentManager environmentManager, ILogger logger,
			IDataProvider provider, ICompressionUtilities compressionUtilities,
			IWorkingDirectoriesProvider workingDirectoriesProvider, IFileSystem fileSystem,
			ISettingsRepository settingsRepository)
			: base(provider, logger) {
			this.showDiffEnvironmentsCommand = showDiffEnvironmentsCommand;
			this.applyEnvironmentManifestCommand = applyEnvironmentManifestCommand;
			this.pullPkgCommand = pullPkgCommand;
			this.environmentManager = environmentManager;
			_compressionUtilities = compressionUtilities;
			_workingDirectoriesProvider = workingDirectoriesProvider;
			_fileSystem = fileSystem;
			this.settingsRepository = settingsRepository;
			if (this.settingsRepository == null) {
				this.pushPackageCommand = pushPackageCommand;
				this.pullPkgCommand = pullPkgCommand;
				this.applyEnvironmentManifestCommand = applyEnvironmentManifestCommand;
				this.showDiffEnvironmentsCommand = showDiffEnvironmentsCommand;
			}
		}

		public override int Execute(CloneEnvironmentOptions options) {
			var useTempDirectory = string.IsNullOrEmpty(options.WorkingDirectory);
			string workingDirectoryPath = useTempDirectory
					? _workingDirectoriesProvider.CreateTempDirectory()
					: options.WorkingDirectory;
			if (this.settingsRepository != null) {
				var sourceBindingModule = new BindingsModule().Register(settingsRepository.GetEnvironment(options.Source));
				this.showDiffEnvironmentsCommand = sourceBindingModule.Resolve<ShowDiffEnvironmentsCommand>();
				this.pullPkgCommand = sourceBindingModule.Resolve<PullPkgCommand>();
				var targetBindingModule = new BindingsModule().Register(settingsRepository.GetEnvironment(options.Target));
				this.pushPackageCommand = targetBindingModule.Resolve<PushPackageCommand>();
				this.applyEnvironmentManifestCommand = targetBindingModule.Resolve<ApplyEnvironmentManifestCommand>();

			}
			try {
				options.FileName = Path.Combine(workingDirectoryPath, $"from_{options.Source}_to_{options.Target}.yaml");
				showDiffEnvironmentsCommand.Execute(options);
				var diffManifest = environmentManager.LoadEnvironmentManifestFromFile(options.FileName);
				string sourceZipPackagePath = Path.Combine(workingDirectoryPath, "SourceZipPackages");
				_fileSystem.CreateDirectory(sourceZipPackagePath);
				int number = 1;
				int packagesCount = diffManifest.Packages.Count;
				foreach(var package in diffManifest.Packages) {
					var pullPkgOptions = new PullPkgOptions() {
						Environment = options.Source
					};
					pullPkgOptions.Name = package.Name;
					pullPkgOptions.DestPath = sourceZipPackagePath;
					string progress = $"({number++} from {packagesCount})";
					_logger.WriteInfo($"Start pull package: {package.Name} {progress}");
					pullPkgCommand.Execute(pullPkgOptions);
					_logger.WriteInfo($"Done pull package: {package.Name} {progress}");
				}
				string sourceGzPackages = Path.Combine(workingDirectoryPath, "SourceGzPackages");
				number = 1;
				foreach (var package in diffManifest.Packages) {
					string packageZipPath = Path.Combine(sourceZipPackagePath, $"{package.Name}.zip");
					string progress = $"({number++} from {packagesCount})";
					_logger.WriteInfo($"Start unzip package: {package.Name} {progress}");
					_compressionUtilities.Unzip(packageZipPath, sourceGzPackages);
					_logger.WriteInfo($"Done unzip package: {package.Name} {progress}");
				}
				_fileSystem.CreateDirectory(sourceGzPackages);
				_logger.WriteInfo($"Start zip packages");
				string commonPackagesZipPath = Path.Combine(workingDirectoryPath,
					$"from_{options.Source}_to_{options.Target}.zip");
				_compressionUtilities.Zip(sourceGzPackages, commonPackagesZipPath);
				_logger.WriteInfo($"Done zip packages");
				var pushPackageOptions = new PushPkgOptions() {
					Environment = options.Target,
					Name = commonPackagesZipPath
				};
				pushPackageCommand.Execute(pushPackageOptions);
				var applyEnvironmentManifestOptions = new ApplyEnvironmentManifestOptions() {
					Environment = options.Target,
					ManifestFilePath = options.FileName
				};
				applyEnvironmentManifestCommand.Execute(applyEnvironmentManifestOptions);
			} finally {
				if (useTempDirectory) {
					_workingDirectoriesProvider.DeleteDirectoryIfExists(workingDirectoryPath);
				}
			}
			_logger.WriteInfo("Done");
			return 0;
		}

	}
}