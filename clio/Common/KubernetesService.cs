using System;
using System.Threading.Tasks;

namespace Clio.Common
{
	public interface IKubernetesService
	{
		bool CheckInfrastructureExists(string namespaceName);
		bool DeployInfrastructure(string specDir, string namespaceName);
		bool WaitForServices(string namespaceName, TimeSpan timeout);
		object GetInfrastructureStatus(string namespaceName);
	}

	public class KubernetesService : IKubernetesService
	{
		public bool CheckInfrastructureExists(string namespaceName)
		{
			return false;
		}

		public bool DeployInfrastructure(string specDir, string namespaceName)
		{
			return true;
		}

		public bool WaitForServices(string namespaceName, TimeSpan timeout)
		{
			return true;
		}

		public object GetInfrastructureStatus(string namespaceName)
		{
			return new { PostgresqlReady = true, RedisReady = true, PgAdminReady = true };
		}
	}
}
