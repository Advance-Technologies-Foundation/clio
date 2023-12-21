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

		public void WriteError(string value){
			ConsoleColor color = Console.ForegroundColor;
			Console.ForegroundColor = ConsoleColor.Red;
			Console.Write("[ERR] - ");
			Console.ForegroundColor = color;
			Console.WriteLine(value);
		}
		
		public void WriteWarning(string value){
			ConsoleColor color = Console.ForegroundColor;
			Console.ForegroundColor = ConsoleColor.DarkYellow;
			Console.Write("[WAR] - ");
			Console.ForegroundColor = color;
			Console.WriteLine(value);
		}
		
		
		public void WriteInfo(string value){
			ConsoleColor color = Console.ForegroundColor;
			Console.ForegroundColor = ConsoleColor.Green;
			Console.Write("[INF] - ");
			Console.ForegroundColor = color;
			Console.WriteLine(value);
		}
		#endregion

	}

	#endregion

}