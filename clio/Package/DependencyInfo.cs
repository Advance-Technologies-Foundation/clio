namespace Clio
{

	public struct DependencyInfo
	{

		public DependencyInfo(string name, string packageVersion) {
			Name = name;
			PackageVersion = packageVersion;
		}

		public string Name { get; }
		public string PackageVersion { get; }

		public bool Equals(DependencyInfo dependencyInfo) {
			return Equals(dependencyInfo, this);
		}

		public override bool Equals(object obj) {
			if (obj == null || GetType() != obj.GetType()) {
				return false;
			}
			var dependencyInfo = (DependencyInfo) obj;
			return dependencyInfo.Name == Name && 
				dependencyInfo.PackageVersion == PackageVersion;
			
		}

		public override int GetHashCode() {
			var calculation = Name + PackageVersion;
			return calculation.GetHashCode();
		}

		public static bool operator ==(DependencyInfo c1, DependencyInfo c2) {
			return c1.Equals(c2);
		}

		public static bool operator !=(DependencyInfo c1, DependencyInfo c2) {
			return !c1.Equals(c2);
		}

	}

}