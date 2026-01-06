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
		/// Filesystem assertions.
		/// </summary>
		Fs
	}
}
