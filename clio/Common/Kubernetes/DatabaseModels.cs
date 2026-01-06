namespace Clio.Common.Kubernetes
{
	/// <summary>
	/// Represents a database engine type.
	/// </summary>
	public enum DatabaseEngine
	{
		Postgres,
		Mssql
	}

	/// <summary>
	/// Represents a discovered database instance.
	/// </summary>
	public class DiscoveredDatabase
	{
		public DatabaseEngine Engine { get; set; }
		public string Name { get; set; }
		public string PodName { get; set; }
		public string ServiceName { get; set; }
		public int Port { get; set; }
		public string Host { get; set; }
		public bool IsReady { get; set; }
	}
}
