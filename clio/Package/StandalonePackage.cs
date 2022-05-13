namespace Clio.Package
{
	using System.IO;

	public class StandalonePackage
	{
		private static string BuildStandaloneProjectPath(string packagesPath, string packageName) =>
			Path.Combine(packagesPath, packageName, "Files", $"{packageName}.csproj");

	}
}