using System.Collections.Generic;
using Clio.Package;
using Clio.Project.NuGet;

namespace Clio
{

	#region Class: PackageInfo

	public class PackageInfo
	{

		#region Constructors: Public

		public PackageInfo(PackageDescriptor descriptor, string packagePath,
				IEnumerable<string> filePaths, string packageDescriptorPath = null) {
			Descriptor = descriptor;
			PackagePath = packagePath;
			FilePaths = filePaths;
			PackageDescriptorPath = packageDescriptorPath ?? string.Empty;
			if (PackageVersion.TryParseVersion(descriptor.PackageVersion, out PackageVersion version)) {
				Version = version;
			}
		}

		#endregion

		#region Properties: Public

		public PackageDescriptor Descriptor { get; }
		public PackageVersion Version { get; }
		public string PackagePath  { get; }
		public IEnumerable<string> FilePaths { get; }
		public string PackageDescriptorPath { get; }

		/// <summary>
		/// Gets whether <see cref="PackageDescriptor.Type"/> and <see cref="PackageDescriptor.ProjectPath"/> were
		/// actually read. <see langword="false"/> for a package read from an environment whose <c>SysPackage</c> has no
		/// <c>Type</c>/<c>ProjectPath</c> columns, or that stores a type value clio does not know; the descriptor then
		/// holds the defaults.
		/// </summary>
		public bool IsTypeKnown { get; init; } = true;

		#endregion

	}

	#endregion

}