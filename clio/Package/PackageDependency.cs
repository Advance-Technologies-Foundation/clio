namespace Clio
{

	public struct PackageDependency
	{

		public PackageDependency(string name, string packageVersion) {
			Name = name;
			PackageVersion = packageVersion;
		}

		public string Name { get; }
		public string PackageVersion { get; }

		public bool Equals(PackageDependency packageDependency) {
			return Equals(packageDependency, this);
		}

		public override bool Equals(object obj) {
			if (obj == null || GetType() != obj.GetType()) {
				return false;
			}
			var dependencyInfo = (PackageDependency) obj;
			return dependencyInfo.Name == Name && 
				dependencyInfo.PackageVersion == PackageVersion;
			
		}

		public override int GetHashCode() {
			var calculation = Name + PackageVersion;
			return calculation.GetHashCode();
		}

		public static bool operator ==(PackageDependency packageDependency1, PackageDependency packageDependency2) {
			return packageDependency1.Equals(packageDependency2);
		}

		public static bool operator !=(PackageDependency packageDependency1, PackageDependency packageDependency2) {
			return !packageDependency1.Equals(packageDependency2);
		}

	}

}