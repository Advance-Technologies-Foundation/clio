using System;

namespace Clio.Project
{

	#region Class: SolutionCreator

	public class SolutionProject
	{

		#region Constructors: Public

		public SolutionProject(string name, string path, Guid id, Guid uId) {
			Name = name;
			Path = path;
			Id = id.ToString().ToUpper();
			UId = uId.ToString().ToUpper();
		}

		public SolutionProject(string name, string path) : this(name, path, Guid.NewGuid(), Guid.NewGuid()) {
		}

		#endregion

		#region Properties: Public

		public string Name { get; }
		public string Path { get; }
		public string Id { get; }
		public string UId { get; }

		#endregion

	}

	#endregion

}