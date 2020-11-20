namespace Clio.Project.NuGet
{

	#region Class: LastVersionNugetPackages

	public class LastVersionNugetPackages
	{

		#region Constructors: Public

		public LastVersionNugetPackages(string name, NugetPackage last, NugetPackage stable) {
			Name = name;
			Last = last;
			Stable = stable;
		}

		#endregion

		#region Properties: Public

		public string Name { get; }
		public NugetPackage Last { get; }
		public NugetPackage Stable { get; }
		public bool LastIsStable => Last == Stable;
		public bool StableIsNotExists => Stable == null;

		#endregion

	}

	#endregion

}