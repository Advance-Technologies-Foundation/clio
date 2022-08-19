namespace Clio.Common
{
	public interface IServiceUrlBuilder
	{

		#region Properties: Public

		public string RootPath { get; }

		#endregion

		#region Methods: Public

		string Build(string serviceEndpoint);
		string Build(string serviceEndpoint, EnvironmentSettings environmentSettings);

		#endregion

	}
}