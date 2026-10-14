using Clio.Common;

namespace Clio.WebApplication
{
	public interface IApplication
	{
		void Restart();

		/// <summary>Restarts the Creatio application through an existing session.</summary>
		/// <param name="applicationClient">
		/// The session to send the restart request through. On a web farm, the session of a package installation
		/// is routed to the design node, so the restart reaches the node that installed the package.
		/// </param>
		/// <param name="environmentSettings">The environment to restart.</param>
		void Restart(IApplicationClient applicationClient, EnvironmentSettings environmentSettings);

		void LoadLicense(string filePath);
	}
}
