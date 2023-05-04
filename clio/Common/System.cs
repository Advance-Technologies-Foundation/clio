using System;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Clio.Common
{
	internal class OperationSystem
	{

		internal static OperationSystem Current {
			get {
				return new OperationSystem();
			}
		}

		internal bool IsWindows {
			get {
				return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
			}
		}

		internal bool HasAdminRights() {
			try {
				WindowsIdentity identity = WindowsIdentity.GetCurrent();
				WindowsPrincipal principal = new WindowsPrincipal(identity);
				return principal.IsInRole(WindowsBuiltInRole.Administrator);
			} catch (UnauthorizedAccessException) {
				return false;
			}
		}

	}
}
