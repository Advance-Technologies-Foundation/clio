namespace Clio.Project.NuGet
{
	using System;
	using Clio.Common;

	#region Class: PackageVersion

	public class PackageVersion : ICloneable, IComparable
	{

		#region Constants: Public

		public const string LastVersion = "*";
		public const string Stable = "rc";

		#endregion

		#region Constructors: Public

		public PackageVersion(PackageVersion packageVersion) {
			Version = packageVersion.Version;
			Suffix = packageVersion.Suffix;
		}

		public PackageVersion(Version version, string versionSuffix) {
			Version = version;
			Suffix = versionSuffix;
		}

		#endregion

		#region Properties: Public

		public Version Version { get; }
		public string Suffix { get; }
		public bool IsStable => Suffix == Stable;
		
		#endregion

		#region Methods: Private

		private int CompareSuffix(PackageVersion packageVersion) {
			return string.IsNullOrWhiteSpace(Suffix) 
				? 1
				: string.Compare(Suffix, packageVersion.Suffix, StringComparison.InvariantCulture);
		}

		#endregion

		#region Methods: Public

		public static PackageVersion ParseVersion(string fullVersionDescription) {
			fullVersionDescription.CheckArgumentNullOrWhiteSpace(nameof(fullVersionDescription));
			fullVersionDescription = fullVersionDescription.Trim();
			int index = fullVersionDescription.IndexOf('-');
			string versionDescription = index > 0
				? fullVersionDescription.Substring(0, index)
				: fullVersionDescription;
			string suffix = index > 0
				? fullVersionDescription.Substring(index + 1, fullVersionDescription.Length - index - 1)
				: string.Empty;
			Version version = new Version(versionDescription.Trim());
			return new PackageVersion(version, suffix.Trim());
		}

		public static bool TryParseVersion(string versionDescription, out PackageVersion packageVersion) {
			try {
				packageVersion = ParseVersion(versionDescription);
				return true;
			} catch (Exception) {
				packageVersion = null;
				return false;
			}
		}

		public override string ToString() {
			return string.IsNullOrWhiteSpace(Suffix)
				? Version.ToString()
				: $"{Version}-{Suffix}";
		}

		public object Clone() {
			return new PackageVersion(this);
		}

		public override int GetHashCode() {
			var calculation = Version + Suffix;
			return calculation.GetHashCode();
		}

		public int CompareTo(Object value) {
			if (value == null) {
				return 1;
			}
			PackageVersion packageVersion = value as PackageVersion;
			if (packageVersion == null) {
				throw new ArgumentException(nameof(value));
			}
			return CompareTo(packageVersion);
		}

		public int CompareTo(PackageVersion packageVersion) {
			return
				object.ReferenceEquals(packageVersion, this) ? 0 :
				object.ReferenceEquals(packageVersion, null) ? 1 :
				Version != packageVersion.Version ? (Version > packageVersion.Version ? 1 : -1) :
				Suffix != packageVersion.Suffix ?  CompareSuffix(packageVersion) :
				0;
		}

		public override bool Equals(Object obj) {
			return Equals(obj as PackageVersion);
		}

		public bool Equals(PackageVersion obj) {
			return object.ReferenceEquals(obj, this) ||
			       (!object.ReferenceEquals(obj, null) &&
			        Version == obj.Version &&
			        Suffix == obj.Suffix);
		}

		public static bool operator ==(PackageVersion v1, PackageVersion v2) {
			if (Object.ReferenceEquals(v1, null)) {
				return Object.ReferenceEquals(v2, null);
			}
			return v1.Equals(v2);
		}

		public static bool operator !=(PackageVersion v1, PackageVersion v2) {
			return !(v1 == v2);
		}

		public static bool operator <(PackageVersion v1, PackageVersion v2) {
			if ((Object)v1 == null)
				throw new ArgumentNullException(nameof(v1));
			return (v1.CompareTo(v2) < 0);
		}

		public static bool operator <=(PackageVersion v1, PackageVersion v2) {
			if ((Object)v1 == null)
				throw new ArgumentNullException(nameof(v1));
			return (v1.CompareTo(v2) <= 0);
		}

		public static bool operator >(PackageVersion v1, PackageVersion v2) {
			return (v2 < v1);
		}

		public static bool operator >=(PackageVersion v1, PackageVersion v2) {
			return (v2 <= v1);
		}

		#endregion

	}

	#endregion

}