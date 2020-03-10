namespace Clio.Project.NuGet
{
	
	#region Class: NugetPackage

	public class NugetPackage
	{

		#region Constructors: Public

		public NugetPackage(string name, NugetPackageVersion version) {
			Name = name;
			Version = version;
		}

		#endregion

		#region Properties: Public

		public string Name { get; }
		public NugetPackageVersion Version { get; }

		#endregion

	}

	#endregion

}