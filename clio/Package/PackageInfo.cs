using System.Collections.Generic;
using System.IO;
using Clio.Package;
using Clio.Project.NuGet;

namespace Clio
{

	#region Class: PackageInfo

	public class PackageInfo
	{

		#region Constructors: Public

		public PackageInfo(PackageDescriptor descriptor, string packagePath, 
				IEnumerable<string> filePaths) {
			Descriptor = descriptor;
			PackagePath = packagePath;
			FilePaths = filePaths;
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
		public string PackageDescriptorPath => Path.Combine(PackagePath, "descriptor.json");
	
		#endregion

	}

	#endregion

}