using System;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Clio.Common;

internal class OperationSystem
{
    internal static OperationSystem Current => new ();

    internal bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    internal bool HasAdminRights()
    {
        try
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new (identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
