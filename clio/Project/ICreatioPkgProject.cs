namespace Clio.Project
{
	public interface ICreatioPkgProject
	{
		/// <summary>
		/// Number of Reference elements whose HintPath was rewritten by the last RefTo* call.
		/// Zero means the project's current reference style was not recognized and nothing changed,
		/// which must be reported instead of being presented as success.
		/// </summary>
		int ChangedReferencesCount { get; }

		/// <summary>
		/// Whether the last RefTo* call rewrote a reference in memory - a HintPath, or a strong-name suffix
		/// stripped from a Reference Include.
		/// </summary>
		/// <remarks>
		/// <see cref="ChangedReferencesCount"/> counts HintPath rewrites only, so a project already pointing
		/// at the requested location can still carry unsaved work. Gate a save on this, and report the count
		/// to the user.
		/// </remarks>
		bool HasPendingChanges { get; }

		/// <summary>
		/// Reference style the project carried when it was loaded, or the style set by the last
		/// RefTo* call. <see cref="RefType.Undef"/> means the style was not recognized.
		/// </summary>
		RefType CurrentRefType { get; }

		CreatioPkgProject RefToBin();

		CreatioPkgProject RefToCoreSrc();

		CreatioPkgProject RefToCustomPath(string path);

		CreatioPkgProject RefToUnitBin();

		CreatioPkgProject RefToUnitCoreSrc();

		void SaveChanges();
	}
}
