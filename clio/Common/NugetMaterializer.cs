namespace Clio.Common;

public interface INugetMaterializer
{

	public int Materialize(string packageName);

} 

public class NugetMaterializer: INugetMaterializer
{

	public int Materialize(string packageName){
		throw new System.NotImplementedException();
	}

}