using System;
using System.Linq;

namespace Clio.Project.NuGet;

public class CreatioSdk : ICreatioSdk
{
    private readonly Version[] _versions =
    [
        new("8.0.3.1759"), new("8.0.2.2425"), new("8.0.1.1993"), new("8.0.0.5484"), new("7.18.5.1500"),
        new("7.18.4.1534"), new("7.18.4.1532"), new("7.18.3.1241"), new("7.18.3.1238"), new("7.18.2.1236"),
        new("7.18.2.1235"), new("7.18.1.2800"), new("7.18.0.1353"), new("7.17.4.2265"), new("7.17.3.1379"),
        new("7.17.3.1378"), new("7.17.3.1377"), new("7.17.3.1376"), new("7.17.2.1728"), new("7.17.2.1725"),
        new("7.17.1.1363"), new("7.17.0.2148"), new("7.17.0.2147"), new("7.16.4.1731"), new("7.16.3.1473"),
        new("7.16.3.1472"), new("7.16.2.1600"), new("7.16.2.1599"), new("7.16.1.2142"), new("7.16.1.2140"),
        new("7.16.1.2135"), new("7.16.0.4462"), new("7.16.0.4461"), new("7.16.0.4449"), new("7.15.4.3060"),
        new("7.15.4.3055"), new("7.15.3.1650"), new("7.15.3.1649"), new("7.15.2.501")
    ];

    public Version LastVersion => _versions[0];

    public Version FindLatestSdkVersion(Version applicationVersion) => _versions
        .LastOrDefault(sdkVersion => sdkVersion >= applicationVersion) ?? LastVersion;
}
