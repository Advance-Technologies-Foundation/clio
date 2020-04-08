using System;
using System.IO;
using Clio.Common;
using Clio.Package;
using CommandLine;

namespace Clio.Command.PackageCommand
{

	#region Class: SetPackageVersionOptions

	[Verb("set-pkg-version", Aliases = new string[] { "pkgversion" }, HelpText = "Set package version")]
	public class SetPackageVersionOptions
	{
		[Value(0, MetaName = "PackagePath", Required = true, HelpText = "Package path")]
		public string PackagePath { get; set; }

		[Option('v', "PackageVersion", Required = true, HelpText = "Package version")]
		public string PackageVersion { get; set; }
	}

	#endregion

	#region Class: SetPackageVersionCommand

	public class SetPackageVersionCommand : Command<SetPackageVersionOptions>
	{

		#region Fields: Public

		protected readonly IJsonConverter _jsonConverter;

		#endregion

		#region Constructors: Public

		public SetPackageVersionCommand(IJsonConverter jsonConverter) {
			jsonConverter.CheckArgumentNull(nameof(jsonConverter));
			_jsonConverter = jsonConverter;
		}

		#endregion

		#region Methods: Public

		public override int Execute(SetPackageVersionOptions options) {
			string packageDescriptorPath = Path.Combine(options.PackagePath, CreatioPackage.DescriptorName);
			try {
				var dto = _jsonConverter.DeserializeObjectFromFile<PackageDescriptorDto>(packageDescriptorPath);
				dto.Descriptor.PackageVersion = options.PackageVersion;
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
