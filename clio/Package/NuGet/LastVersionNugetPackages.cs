namespace Clio.Project.NuGet
{

	#region Class: LastVersionNugetPackages

	public class LastVersionNugetPackages
	{

		#region Constructors: Public

		public LastVersionNugetPackages(NugetPackage last, NugetPackage stable) {
			Last = last;
			Stable = stable;
		}

		#endregion

		#region Properties: Public

		public NugetPackage Last { get; }
		public NugetPackage Stable { get; }
		public bool LastIsStable => Last == Stable;
		public bool StableIsNull => Stable == null;

		#endregion

	}

	#endregion

}