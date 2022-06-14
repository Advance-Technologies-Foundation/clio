namespace Clio.WebApplication
{
	public interface IApplication
	{
		void Restart();

		void LoadLicense(string filePath);
	}
}