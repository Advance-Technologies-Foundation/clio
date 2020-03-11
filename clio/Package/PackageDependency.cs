namespace Clio
{

	public struct PackageDependency
	{

		public PackageDependency(string name, string packageVersion, string uid = null) {
			Name = name;
			PackageVersion = packageVersion;
			UId = uid ?? string.Empty;
		}

		public string UId { get; }
		public string PackageVersion { get; }
		public string Name { get; }

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
			var calculation = $"{Name}{PackageVersion}{UId}";
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