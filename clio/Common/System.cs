using System;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Clio.Common;

internal class OperationSystem
{

    #region Properties: Internal

    internal static OperationSystem Current
    {
        get { return new OperationSystem(); }
    }

    internal bool IsWindows
    {
        get { return RuntimeInformation.IsOSPlatform(OSPlatform.Windows); }
    }

    #endregion

    #region Methods: Internal

    internal bool HasAdminRights()
    {
        try
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    #endregion

}
