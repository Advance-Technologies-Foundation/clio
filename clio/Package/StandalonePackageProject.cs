namespace Clio.Package
{

	#region Class: StandalonePackageProject

	public class StandalonePackageProject
	{

		#region Constructors: Public

		public StandalonePackageProject(string packageName, string path) {
			PackageName = packageName;
			Path = path;
		}

		#endregion

		#region Properties: Public

		public string PackageName { get; }
		public string Path { get; }

		#endregion

	}

	#endregion

}