using System;

namespace Clio.Utilities;

public interface IOSPlatformChecker
{
    bool IsWindowsEnvironment { get; }
}

public class OSPlatformChecker : IOSPlatformChecker
{
    public bool IsWindowsEnvironment => GetIsWindowsEnvironment();

    public static bool GetIsWindowsEnvironment() =>
        Environment.OSVersion.Platform switch
        {
            PlatformID.Win32NT or PlatformID.Win32S or PlatformID.Win32Windows or PlatformID.WinCE => true,
            _ => false
        };
}
