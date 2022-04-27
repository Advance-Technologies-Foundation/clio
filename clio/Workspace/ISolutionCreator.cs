namespace Clio.Workspace
{
	using System.Collections.Generic;

	#region Interface: ISolutionCreator

	public interface ISolutionCreator
	{

		#region Methods: Public

		void Create(string solutionPath, IEnumerable<SolutionProject> solutionProjects);

		#endregion

	}

	#endregion

}