namespace Clio.Workspaces
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

		/// <summary>
		/// When <c>true</c>, an empty <c>&lt;Build /&gt;</c> element is emitted under the project's
		/// node in the <c>.slnx</c>. This forces the project to participate in <b>every</b> solution
		/// configuration — required for SDK-style projects (e.g. the JavaScript SDK <c>.esproj</c>)
		/// that only understand Debug/Release and would otherwise be silently skipped under custom
		/// configs such as <c>dev-n8</c>/<c>dev-nf</c>.
		/// </summary>
		public bool ForceBuild { get; init; }

		#endregion

	}

	#endregion

}