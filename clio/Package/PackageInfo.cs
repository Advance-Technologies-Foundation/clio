namespace Clio
{
	using System.Collections.Generic;
	
	public class PackageInfo
	{

		public PackageInfo(string name, string packageVersion, string maintainer, string packagePath, 
				IEnumerable<string> filePaths, IEnumerable<DependencyInfo> depends) {
			Name = name;
			PackageVersion = packageVersion;
			Maintainer = maintainer;
			PackagePath = packagePath;
			FilePaths = filePaths;
			Depends = depends;
		}

		public string Name { get; }
		public string PackageVersion { get; }
		public string Maintainer { get; }
		public string PackagePath  { get; }
		public IEnumerable<string> FilePaths { get; }
		public IEnumerable<DependencyInfo> Depends { get; }

	}
}