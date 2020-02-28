namespace Clio
{
	using System.Collections.Generic;
	
	public class PackageInfo
	{

		public PackageInfo(string name, string packageVersion, string maintainer, string packagePath, 
				IEnumerable<string> filePaths, IEnumerable<PackageDependency> packageDependencies) {
			Name = name;
			PackageVersion = packageVersion;
			Maintainer = maintainer;
			PackagePath = packagePath;
			FilePaths = filePaths;
			PackageDependencies = packageDependencies;
		}

		public string Name { get; }
		public string PackageVersion { get; }
		public string Maintainer { get; }
		public string PackagePath  { get; }
		public IEnumerable<string> FilePaths { get; }
		public IEnumerable<PackageDependency> PackageDependencies { get; }

	}
}