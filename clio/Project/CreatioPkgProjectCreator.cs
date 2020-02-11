namespace Clio.Project
{
	public class CreatioPkgProjectCreator : ICreatioPkgProjectCreator
	{
		public ICreatioPkgProject CreateFromFile(string path) {
			return CreatioPkgProject.LoadFromFile(path);
		}
	}
}
