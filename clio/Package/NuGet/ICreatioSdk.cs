using System;

namespace Clio.Project.NuGet;

public interface ICreatioSdk
{
    public Version LastVersion { get; }

    Version FindLatestSdkVersion(Version applicationVersion);
}
