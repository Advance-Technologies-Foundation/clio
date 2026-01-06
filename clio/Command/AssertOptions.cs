using CommandLine;

namespace Clio.Command
{
	[Verb("assert", HelpText = "Validates infrastructure and filesystem resources")]
	public class AssertOptions : BaseCommandOptions
	{
		[Value(0, MetaName = "scope", Required = true, HelpText = "Assertion scope: k8 (Kubernetes) or fs (Filesystem)")]
		public string Scope { get; set; }

		// Kubernetes Database Options
		[Option("db", Required = false, HelpText = "Database engines to assert (comma-separated): postgres, mssql")]
		public string DatabaseEngines { get; set; }

		[Option("db-min", Required = false, Default = 1, HelpText = "Minimum number of database engines required")]
		public int DatabaseMinimum { get; set; }

		[Option("db-connect", Required = false, HelpText = "Validate database connectivity")]
		public bool DatabaseConnect { get; set; }

		[Option("db-check", Required = false, HelpText = "Database capability check (e.g., 'version')")]
		public string DatabaseCheck { get; set; }

		// Kubernetes Redis Options
		[Option("redis", Required = false, HelpText = "Assert Redis presence")]
		public bool Redis { get; set; }

		[Option("redis-connect", Required = false, HelpText = "Validate Redis connectivity")]
		public bool RedisConnect { get; set; }

		[Option("redis-ping", Required = false, HelpText = "Execute Redis PING command")]
		public bool RedisPing { get; set; }

		// Kubernetes Context Options
		[Option("context", Required = false, HelpText = "Expected Kubernetes context name")]
		public string Context { get; set; }

		[Option("context-regex", Required = false, HelpText = "Regex pattern for Kubernetes context name")]
		public string ContextRegex { get; set; }

		[Option("cluster", Required = false, HelpText = "Expected Kubernetes cluster name")]
		public string Cluster { get; set; }

		[Option("namespace", Required = false, HelpText = "Expected Kubernetes namespace")]
		public string Namespace { get; set; }

		// Filesystem Options
		[Option("path", Required = false, HelpText = "Filesystem path to validate")]
		public string Path { get; set; }

		[Option("user", Required = false, HelpText = "Windows user identity to validate")]
		public string User { get; set; }

		[Option("perm", Required = false, HelpText = "Required permission level: read, write, modify, full")]
		public string Permission { get; set; }
	}
}
