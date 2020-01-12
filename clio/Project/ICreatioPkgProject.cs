namespace Clio.Project
{
	public interface ICreatioPkgProject
	{
		CreatioPkgProject RefToBin();

		CreatioPkgProject RefToCoreSrc();

		CreatioPkgProject RefToCustomPath(string path);

		CreatioPkgProject RefToUnitBin();

		CreatioPkgProject RefToUnitCoreSrc();

		void SaveChanges();
	}
}
