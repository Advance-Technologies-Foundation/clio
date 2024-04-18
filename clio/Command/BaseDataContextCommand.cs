using ATF.Repository.Providers;
using Clio.Common;

namespace Clio.Command
{
	public abstract class BaseDataContextCommand<T>: Command<T>
	{
		#region Fields: Internal

		internal readonly IDataProvider _provider;
		internal readonly ILogger _logger;

		#endregion


		public BaseDataContextCommand(IDataProvider provider, ILogger logger) {
		}

	}
}