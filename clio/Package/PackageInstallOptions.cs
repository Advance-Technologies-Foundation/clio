using System;

namespace Clio.Package
{

	public class PackageInstallOptions
	{

		#region Properties: Public

		public bool InstallSqlScript { get; set; } = true;
		public bool InstallPackageData { get; set; } = true;
		public bool ContinueIfError { get; set; } = true;
		public bool SkipConstraints { get; set; } = false;
		public bool SkipValidateActions { get; set; } = false;
		public bool ExecuteValidateActions { get; set; } = false;
		public bool IsForceUpdateAllColumns { get; set; } = false;

		#endregion

		#region Methods: Public


		public int CompareTo(Object value) {
			if (value == null) {
				return 1;
			}
			PackageInstallOptions packageVersion = value as PackageInstallOptions;
			if (packageVersion == null) {
				throw new ArgumentException(nameof(value));
			}
			return CompareTo(packageVersion);
		}

		public override bool Equals(Object obj) {
			return Equals(obj as PackageInstallOptions);
		}

		public bool Equals(PackageInstallOptions obj) {
			return ReferenceEquals(obj, this) ||
			       (!ReferenceEquals(obj, null) &&
			        InstallSqlScript == obj.InstallSqlScript &&
			        InstallPackageData == obj.InstallPackageData &&
			        ContinueIfError == obj.ContinueIfError &&
			        SkipConstraints == obj.SkipConstraints &&
			        SkipValidateActions == obj.SkipValidateActions &&
			        ExecuteValidateActions == obj.ExecuteValidateActions);
		}

		public static bool operator ==(PackageInstallOptions v1, PackageInstallOptions v2) =>
			v1?.Equals(v2) ?? object.ReferenceEquals(v2, null);
		

		public static bool operator !=(PackageInstallOptions v1, PackageInstallOptions v2) => 
			!(v1 == v2);

		#endregion

	}

}