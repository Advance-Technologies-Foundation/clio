using System.Collections.Generic;
using System.IO;

namespace Clio
{

	#region Class: PackageInfo

	public class PackageInfo
	{

		#region Constructors: Public

		public PackageInfo(string name, string version, string maintainer, string uid, string packagePath, 
				IEnumerable<string> filePaths, IEnumerable<PackageDependency> packageDependencies) {
			Name = name;
			Version = version;
			Maintainer = maintainer;
			PackagePath = packagePath;
			UId = uid;
			FilePaths = filePaths;
			PackageDependencies = packageDependencies;
		}

		#endregion

		#region Properties: Public

		public string Name { get; }
		public string Version { get; }
		public string Maintainer { get; }
		public string UId { get; }
		public string PackagePath  { get; }
		public IEnumerable<string> FilePaths { get; }
		public IEnumerable<PackageDependency> PackageDependencies { get; }
		public string PackageDescriptorPath => Path.Combine(PackagePath, "descriptor.json");
	
		#endregion

	}

	#endregion

}