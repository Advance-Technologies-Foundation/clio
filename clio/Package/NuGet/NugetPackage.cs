using System;

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

		public override bool Equals(Object nugetPackage) {
			return Equals(nugetPackage as NugetPackage);
		}

		public bool Equals(NugetPackage nugetPackage) {
			return object.ReferenceEquals(nugetPackage, this) ||
			       (!object.ReferenceEquals(nugetPackage, null) &&
			        Name == nugetPackage.Name &&
			        Version == nugetPackage.Version);
		}

		public override int GetHashCode() {
			var calculation = $"{Name}{Version}";
			return calculation.GetHashCode();
		}

		public static bool operator ==(NugetPackage nugetPackage1, NugetPackage nugetPackage2) {
			if (Object.ReferenceEquals(nugetPackage1, null)) {
				return Object.ReferenceEquals(nugetPackage2, null);
			}
			return nugetPackage1.Equals(nugetPackage2);
		}

		public static bool operator !=(NugetPackage nugetPackage1, NugetPackage nugetPackage2) {
			if (Object.ReferenceEquals(nugetPackage1, null)) {
				return Object.ReferenceEquals(nugetPackage2, null);
			}
			return !nugetPackage1.Equals(nugetPackage2);
		}

		#endregion

	}

	#endregion

}