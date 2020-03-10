using System;

namespace Clio.Project.NuGet
{
	
	#region Class: NugetPackageVersion

	public class NugetPackageVersion : ICloneable, IComparable
	{

		#region Constants: Public

		public const string LastVersion = "*"; 

		#endregion

		#region Constructors: Public

		public NugetPackageVersion(NugetPackageVersion nugetPackageVersion) {
			Version = nugetPackageVersion.Version;
			Suffix = nugetPackageVersion.Suffix;
		}

		public NugetPackageVersion(Version version, string versionSuffix) {
			Version = version;
			Suffix = versionSuffix;
		}

		#endregion

		#region Properties: Public

		public Version Version { get; }
		public string Suffix { get; }

		#endregion

		#region Methods: Public

		public override string ToString() {
			return $"{Version}-{Suffix}";
		}

		public object Clone() {
			return new NugetPackageVersion(this);
		}

		public override int GetHashCode() {
			var calculation = Version + Suffix;
			return calculation.GetHashCode();
		}

		public int CompareTo(Object value) {
			if (value == null) {
				return 1;
			}
			NugetPackageVersion nugetPackageVersion = value as NugetPackageVersion;
			if (nugetPackageVersion == null) {
				throw new ArgumentException(nameof(value));
			}
			return CompareTo(nugetPackageVersion);
		}

		public int CompareTo(NugetPackageVersion nugetPackageVersion) {
			return
				object.ReferenceEquals(nugetPackageVersion, this) ? 0 :
				object.ReferenceEquals(nugetPackageVersion, null) ? 1 :
				Version != nugetPackageVersion.Version ? (Version > nugetPackageVersion.Version ? 1 : -1) :
				Suffix != nugetPackageVersion.Suffix ? 
					string.Compare(Suffix, nugetPackageVersion.Suffix, StringComparison.InvariantCulture) :
				0;
		}

		public override bool Equals(Object obj) {
			return Equals(obj as NugetPackageVersion);
		}

		public bool Equals(NugetPackageVersion obj) {
			return object.ReferenceEquals(obj, this) ||
			       (!object.ReferenceEquals(obj, null) &&
			        Version == obj.Version &&
			        Suffix == obj.Suffix);
		}

		public static bool operator ==(NugetPackageVersion v1, NugetPackageVersion v2) {
			if (Object.ReferenceEquals(v1, null)) {
				return Object.ReferenceEquals(v2, null);
			}
			return v1.Equals(v2);
		}

		public static bool operator !=(NugetPackageVersion v1, NugetPackageVersion v2) {
			return !(v1 == v2);
		}

		public static bool operator <(NugetPackageVersion v1, NugetPackageVersion v2) {
			if ((Object)v1 == null)
				throw new ArgumentNullException(nameof(v1));
			return (v1.CompareTo(v2) < 0);
		}

		public static bool operator <=(NugetPackageVersion v1, NugetPackageVersion v2) {
			if ((Object)v1 == null)
				throw new ArgumentNullException(nameof(v1));
			return (v1.CompareTo(v2) <= 0);
		}

		public static bool operator >(NugetPackageVersion v1, NugetPackageVersion v2) {
			return (v2 < v1);
		}

		public static bool operator >=(NugetPackageVersion v1, NugetPackageVersion v2) {
			return (v2 <= v1);
		}

		#endregion

	}

	#endregion

}