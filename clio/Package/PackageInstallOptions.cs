using System;

namespace Clio.Package;

public class PackageInstallOptions
{
    public bool InstallSqlScript { get; set; } = true;

    public bool InstallPackageData { get; set; } = true;

    public bool ContinueIfError { get; set; } = true;

    public bool SkipConstraints { get; set; } = false;

    public bool SkipValidateActions { get; set; } = false;

    public bool ExecuteValidateActions { get; set; } = false;

    public bool IsForceUpdateAllColumns { get; set; } = false;

    public int CompareTo(object value)
    {
        if (value == null)
        {
            return 1;
        }

        PackageInstallOptions packageVersion = value as PackageInstallOptions ?? throw new ArgumentException(nameof(value));
        return CompareTo(packageVersion);
    }

    public override bool Equals(object obj) => Equals(obj as PackageInstallOptions);

    public bool Equals(PackageInstallOptions obj) =>
        ReferenceEquals(obj, this) ||
        (!ReferenceEquals(obj, null) &&
         InstallSqlScript == obj.InstallSqlScript &&
         InstallPackageData == obj.InstallPackageData &&
         ContinueIfError == obj.ContinueIfError &&
         SkipConstraints == obj.SkipConstraints &&
         SkipValidateActions == obj.SkipValidateActions &&
         ExecuteValidateActions == obj.ExecuteValidateActions);

    public static bool operator ==(PackageInstallOptions v1, PackageInstallOptions v2) =>
        v1?.Equals(v2) ?? ReferenceEquals(v2, null);

    public static bool operator !=(PackageInstallOptions v1, PackageInstallOptions v2) =>
        !(v1 == v2);
}
