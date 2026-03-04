namespace Clio.Common.Assertions
{
	/// <summary>
	/// Defines the scope of an assertion operation.
	/// </summary>
	public enum AssertionScope
	{
		/// <summary>
		/// Kubernetes resource assertions.
		/// </summary>
		K8,

		/// <summary>
		/// Local infrastructure assertions.
		/// </summary>
		Local,

		/// <summary>
		/// Filesystem assertions.
		/// </summary>
		Fs
	}
}
