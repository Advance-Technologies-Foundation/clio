using System;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Clio.Common
{
	/// <summary>
	/// Provides operating system and privilege information for command execution decisions.
	/// </summary>
	public interface IOperationSystem {
		/// <summary>
		/// Gets a value indicating whether the current operating system is Windows.
		/// </summary>
		bool IsWindows { get; }

		/// <summary>
		/// Gets a value indicating whether the current operating system is macOS.
		/// </summary>
		bool IsMacOS { get; }

		/// <summary>
		/// Determines whether the current process has administrator rights.
		/// </summary>
		/// <returns><c>true</c> when the current process runs with administrator privileges; otherwise, <c>false</c>.</returns>
		bool HasAdminRights();
	}

	internal class OperationSystem
		: IOperationSystem
	{

		internal static OperationSystem Current {
			get {
				return new OperationSystem();
			}
		}

		public bool IsWindows {
			get {
				return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
			}
		}

		public bool IsMacOS {
			get {
				return RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
			}
		}

		public bool HasAdminRights() {
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
