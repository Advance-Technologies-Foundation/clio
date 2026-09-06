namespace Clio.Package
{
	using System.Collections.Generic;

	#region Interface: IApplicationPackageListProvider
	
	public interface IApplicationPackageListProvider
	{

		#region Methods: Public

		IEnumerable<PackageInfo> GetPackages();
		IEnumerable<PackageInfo> GetPackages(string scriptData);

		/// <summary>
		/// Reads the installed packages with an explicit per-request timeout.
		/// </summary>
		/// <remarks>
		/// The unbounded overloads default to <see cref="System.Threading.Timeout.Infinite"/>, which is the
		/// wrong contract for a caller that runs this read inside an already-failing operation: an
		/// environment that accepts the connection and then stops answering would block it forever inside a
		/// diagnostic. Callers on a normal path keep the unbounded overloads and their transient re-send
		/// budget.
		/// </remarks>
		/// <param name="scriptData">Filter payload, or <c>{}</c> for no filter.</param>
		/// <param name="requestTimeoutMs">
		/// Per-request timeout in milliseconds, or <see cref="System.Threading.Timeout.Infinite"/> for no bound.
		/// </param>
		/// <returns>The installed packages.</returns>
		IEnumerable<PackageInfo> GetPackages(string scriptData, int requestTimeoutMs);

		#endregion

	}

	#endregion

}