using System.Collections.Generic;
using System.IO;
using Clio.Package;

namespace Clio
{

	#region Class: PackageInfo

	public class PackageInfo
	{

		#region Constructors: Public

		public PackageInfo(PackageDescriptorDto.DescriptorDto descriptor, string packagePath, 
				IEnumerable<string> filePaths) {
			Descriptor = descriptor;
			PackagePath = packagePath;
			FilePaths = filePaths;
		}

		#endregion

		#region Properties: Public

		public PackageDescriptorDto.DescriptorDto Descriptor { get; }
		public string PackagePath  { get; }
		public IEnumerable<string> FilePaths { get; }
		public string PackageDescriptorPath => Path.Combine(PackagePath, "descriptor.json");
	
		#endregion

	}

	#endregion

}