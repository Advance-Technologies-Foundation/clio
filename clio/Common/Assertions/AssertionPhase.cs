namespace Clio.Common.Assertions
{
	/// <summary>
	/// Defines the phase of an assertion check.
	/// </summary>
	public enum AssertionPhase
	{
		/// <summary>
		/// Kubernetes context validation phase.
		/// </summary>
		K8Context,

		/// <summary>
		/// Database discovery phase.
		/// </summary>
		DbDiscovery,

		/// <summary>
		/// Database connectivity check phase.
		/// </summary>
		DbConnect,

		/// <summary>
		/// Database capability check phase.
		/// </summary>
		DbCheck,

		/// <summary>
		/// Redis discovery phase.
		/// </summary>
		RedisDiscovery,

		/// <summary>
		/// Redis connectivity check phase.
		/// </summary>
		RedisConnect,

		/// <summary>
		/// Redis ping check phase.
		/// </summary>
		RedisPing,

		/// <summary>
		/// Filesystem path validation phase.
		/// </summary>
		FsPath,

		/// <summary>
		/// Filesystem user identity resolution phase.
		/// </summary>
		FsUser,

		/// <summary>
		/// Filesystem permission validation phase.
		/// </summary>
		FsPerm
	}
}
