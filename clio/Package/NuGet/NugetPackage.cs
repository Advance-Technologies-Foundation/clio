namespace Clio.Project.NuGet
{
	
	#region Class: NugetPackage

	public class NugetPackage
	{

		#region Constructors: Public

		public NugetPackage(string name, PackageVersion version) {
			Name = name;
			Version = version;
		}

		#endregion

		#region Properties: Public

		public string Name { get; }
		public PackageVersion Version { get; }

		#endregion

		#region Methods: Public

		public bool Equals(NugetPackage packageDependency) {
			return Equals(packageDependency, this);
		}

		public override bool Equals(object obj) {
			if (obj == null || GetType() != obj.GetType()) {
				return false;
			}
			var nugetPackage = (NugetPackage) obj;
			return nugetPackage.Name == Name && 
			       nugetPackage.Version == Version;
		}

		public override int GetHashCode() {
			var calculation = $"{Name}{Version}";
			return calculation.GetHashCode();
		}

		public static bool operator ==(NugetPackage nugetPackage1, NugetPackage nugetPackage2) {
			return nugetPackage1.Equals(nugetPackage2);
		}

		public static bool operator !=(NugetPackage nugetPackage1, NugetPackage nugetPackage2) {
			return !nugetPackage1.Equals(nugetPackage2);
		}

		#endregion

	}

	#endregion

}