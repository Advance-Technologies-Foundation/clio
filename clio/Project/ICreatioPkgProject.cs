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
