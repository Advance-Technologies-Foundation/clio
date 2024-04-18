using ATF.Repository.Providers;
using Clio.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Clio.Command
{
	internal class SaveSettingsToManifestCommand:BaseDataContextCommand<SaveSettingsToManifestOptions> {
		public SaveSettingsToManifestCommand(IDataProvider provider, ILogger logger) : base(provider, logger) {
		}

		public override int Execute(SaveSettingsToManifestOptions options) {
			throw new NotImplementedException();
		}
	}

	internal class SaveSettingsToManifestOptions
	{
	}
}
