using System.Threading.Tasks;
using Clio.Common.Kubernetes;

namespace Clio.Common.Database
{
	/// <summary>
	/// Result of a database capability check.
	/// </summary>
	public class CapabilityCheckResult
	{
		public bool Success { get; set; }
		public string Version { get; set; }
		public string Error { get; set; }
	}

	/// <summary>
	/// Interface for checking database capabilities.
	/// </summary>
	public interface IDatabaseCapabilityChecker
	{
		/// <summary>
		/// Checks database version.
		/// </summary>
		Task<CapabilityCheckResult> CheckVersionAsync(DiscoveredDatabase database, string connectionString);
	}
}
