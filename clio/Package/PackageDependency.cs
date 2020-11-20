using Clio.Project.NuGet;

namespace Clio
{

	#region Struct: NugetPackageFullName

	public struct PackageDependency
	{

		#region Constructors: Public

		public PackageDependency(string name, string packageVersion, string uid = null) {
			Name = name;
			PackageVersion = packageVersion;
			UId = uid ?? string.Empty;
		}

		#endregion

		#region Properties: Public

		public string UId { get; set; }
		public string PackageVersion { get; set; }
		public string Name { get; set; }

		#endregion

		#region Methods: Public

		public static bool operator ==(PackageDependency packageDependency1, PackageDependency packageDependency2) {
			return packageDependency1.Equals(packageDependency2);
		}

		public static bool operator !=(PackageDependency packageDependency1, PackageDependency packageDependency2) {
			return !packageDependency1.Equals(packageDependency2);
		}

		public bool Equals(PackageDependency packageDependency) {
			return Equals(packageDependency, this);
		}

		public override bool Equals(object obj) {
			if (obj == null || GetType() != obj.GetType()) {
				return false;
			}
			var dependencyInfo = (PackageDependency) obj;
			return dependencyInfo.Name == Name && 
				dependencyInfo.PackageVersion == PackageVersion &&
				dependencyInfo.UId == UId;
			
		}

		public override int GetHashCode() {
			return ToString().GetHashCode();
		}

		public override string ToString() {
			return $"{Name}:{PackageVersion}(UId='{UId}')";
		}

		#endregion

	}

	#endregion

}