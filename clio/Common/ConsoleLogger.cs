using System;

namespace Clio.Common
{

	#region Class: ConsoleLogger

	public class ConsoleLogger : ILogger
	{
		public void Write(string value) {
			Console.Write(value);
		}

		#region Methods: Public

		public void WriteLine(string value) {
			Console.WriteLine(value);
		}

		#endregion

	}

	#endregion

}