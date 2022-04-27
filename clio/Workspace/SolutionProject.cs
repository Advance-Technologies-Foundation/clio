namespace Clio.Workspace
{
	using System;

	#region Class: SolutionProject

	public class SolutionProject
	{

		#region Constructors: Public

		public SolutionProject(string name, string path, Guid id, Guid uId) {
			Name = name;
			Path = path;
			Id = id;
			UId = uId;
		}

		public SolutionProject(string name, string path) : this(name, path, Guid.NewGuid(), Guid.NewGuid()) {
		}

		#endregion

		#region Properties: Public

		public string Name { get; }
		public string Path { get; }
		public Guid Id { get; }
		public Guid UId { get; }

		#endregion

	}

	#endregion

}