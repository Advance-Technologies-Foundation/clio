using System;
using System.IO;
using Clio.Common;
using Clio.Package;
using Clio.Project.NuGet;
using CommandLine;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command.PackageCommand
{

	#region Class: SetPackageVersionOptions

	[Verb("set-pkg-version", Aliases = ["spv"], HelpText = "Set package version")]
	public class SetPackageVersionOptions
	{
		[Value(0, MetaName = "PackagePath", Required = true, HelpText = "Package path")]
		public string PackagePath { get; set; }

		[Option('v', "package-version", Required = false, HelpText = "Package version")]
		public string PackageVersion { get; set; }

		[Option("PackageVersion", Required = false, Hidden = true, HelpText = "Alias for --package-version")]
		public string PackageVersionAlias {
			get => PackageVersion;
			set { if (!string.IsNullOrEmpty(value)) PackageVersion = value; }
		}
	}

	#endregion

	#region Class: SetPackageVersionCommand

	public class SetPackageVersionCommand : Command<SetPackageVersionOptions>
	{

		#region Fields: Public

		protected readonly IJsonConverter _jsonConverter;
		private readonly IFileSystem _fileSystem;
		private readonly ILogger _logger;

		#endregion

		#region Constructors: Public

		public SetPackageVersionCommand(IJsonConverter jsonConverter, IFileSystem fileSystem, ILogger logger) {
			jsonConverter.CheckArgumentNull(nameof(jsonConverter));
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			logger.CheckArgumentNull(nameof(logger));
			_jsonConverter = jsonConverter;
			_fileSystem = fileSystem;
			_logger = logger;
		}

		#endregion

		#region Methods: Public

		/// <summary>
		/// Writes <paramref name="options"/>'s version into the package descriptor, moving
		/// <c>ModifiedOnUtc</c> with it.
		/// </summary>
		/// <param name="options">Parsed command options.</param>
		/// <returns><c>0</c> on success; <c>1</c> when no usable version was supplied.</returns>
		/// <remarks>
		/// The two descriptor fields are written TOGETHER because that is the descriptor's editing contract:
		/// Creatio rewrites the <c>SysPackage</c> row only when <c>ModifiedOnUtc</c> changes, so a version
		/// without a fresh timestamp installs and silently leaves the recorded version behind. This command
		/// exists to make that pairing automatic — which is exactly why it must refuse an unusable version
		/// instead of writing one: doing otherwise moves the timestamp while erasing the version, breaking the
		/// contract this command is here to keep, and the descriptor would then claim to have changed while
		/// carrying no version at all.
		/// </remarks>
		public override int Execute(SetPackageVersionOptions options) {
			if (string.IsNullOrWhiteSpace(options.PackageVersion)) {
				_logger.WriteError(
					"--package-version is required. Without it the descriptor's version would be erased while "
					+ "ModifiedOnUtc still moves, leaving a package that claims to have changed but carries no "
					+ "version — and Creatio would record that as the installed version.");
				return 1;
			}
			// PackageVersion.TryParseVersion, NOT Version.TryParse: this field is READ everywhere else through
			// the former (PackageInfo, NuGetManager), which models the X.Y.Z.W-suffix pre-release form. Using the
			// stricter parser here made the writer refuse values its own readers accept - 1.0.0.0-rc among them.
			if (!PackageVersion.TryParseVersion(options.PackageVersion, out PackageVersion parsed)) {
				_logger.WriteError(
					$"'{options.PackageVersion}' is not a valid package version. Expected four numeric parts, "
					+ "optionally followed by '-<suffix>', for example 1.2.3.4 or 1.2.3.4-rc.");
				return 1;
			}
			// Four parts REQUIRED, and this is not pedantry: System.Version gives a three-part string a Revision
			// of -1, so RequiredPackageChecker.IsCompatible then compares such a version as LOWER than every
			// four-part floor. Writing 2.0.0 into a descriptor therefore makes lock-package, unlock-package and
			// sql refuse permanently against an environment carrying a NEWER package than the floor they demand.
			// See the remarks on BundledPackages.ProcessBuilderVersion.
			if (parsed.Version.Revision < 0) {
				_logger.WriteError(
					$"'{options.PackageVersion}' has fewer than four parts. Creatio compares recorded versions "
					+ "through System.Version, which treats a missing part as -1, so a shorter version sorts BELOW "
					+ "every four-part requirement floor and would refuse commands forever. Write "
					+ $"'{parsed.Version.Major}.{parsed.Version.Minor}.{Math.Max(parsed.Version.Build, 0)}."
					+ $"{Math.Max(parsed.Version.Revision, 0)}' instead.");
				return 1;
			}
			string packageDescriptorPath = _fileSystem.Path.Combine(options.PackagePath, CreatioPackage.DescriptorName);
			try {
				var dto = _jsonConverter.DeserializeObjectFromFile<PackageDescriptorDto>(packageDescriptorPath);
				// The CANONICAL form, not the raw argument: Version's components accept surrounding whitespace
				// and a leading '+', so " 1.0.0.0 " or "+1.0.0.0" would otherwise reach the descriptor verbatim,
				// land in SysPackage.Version, and break every string comparison against it while every Version
				// comparison still agreed.
				dto.Descriptor.PackageVersion = parsed.ToString();
				dto.Descriptor.ModifiedOnUtc = PackageDescriptor.ConvertToModifiedOnUtc(DateTime.Now);
				_jsonConverter.SerializeObjectToFile(dto, packageDescriptorPath);
			}
			catch (FileNotFoundException) {
				throw new Exception($"Package descriptor not found by path: '{packageDescriptorPath}'");
			}
			return 0;
		}

		#endregion


	}

	#endregion

}
