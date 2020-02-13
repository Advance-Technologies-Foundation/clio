namespace Clio
{
	using System.Collections.Generic;
	
	public class PackageInfo
	{

		public PackageInfo(string name, IEnumerable<string> filePaths, IEnumerable<string> depends, string maintainer) {
			Name = name;
			FilePaths = filePaths;
			Depends = depends;
			Maintainer = maintainer;
		}

		public string Name { get; }
		public IEnumerable<string> FilePaths { get; }
		public IEnumerable<string> Depends { get; }
		public string Maintainer { get; }

	}
}