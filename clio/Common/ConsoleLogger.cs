using System;

namespace Clio.Common
{

	#region Class: ConsoleLogger

	public class ConsoleLogger : ILogger
	{

		#region Methods: Public

		public void WriteLine(string value) {
			Console.WriteLine(value);
		}

		#endregion

	}

	#endregion

}