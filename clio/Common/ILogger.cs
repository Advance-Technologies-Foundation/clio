using ConsoleTables;

namespace Clio.Common
{
	public interface ILogger
	{
		
		/// <summary>
		/// Starts the logging thread.
		/// </summary>
		/// <remarks>See <see cref="ConsoleLogger"/>> for details</remarks>
		public void Start();
		
		/// <summary>
		/// Stops the logging thread.
		/// </summary>
		/// <remarks>See <see cref="ConsoleLogger"/>> for details</remarks>
		public void Stop();
		
		
		void Write(string value);
		
		/// <summary>
		/// Writes an undecorated line to the log.
		/// </summary>
		/// <remarks>See <see cref="ConsoleLogger"/> for details</remarks>
		/// <param name="value">The string value to be written to the log.</param>
		void WriteLine(string value);
		
		
		/// <summary>
		/// Writes an line to the log.
		/// </summary>
		/// <remarks>
		/// Line is decorated with [WAR] - at the beginning of the line in yellow color
		/// See <see cref="ConsoleLogger"/> for details
		/// </remarks>
		/// <param name="value">The string value to be written to the log.</param>
		void WriteWarning(string value);
		
		/// <summary>
		/// Writes an line to the log.
		/// </summary>
		/// <remarks>
		/// Line is decorated with [WAR] - at the beginning of the line in red color
		/// See <see cref="ConsoleLogger"/> for details
		/// </remarks>
		/// <param name="value">The string value to be written to the log.</param>
		void WriteError(string value);
		
		/// <summary>
		/// Writes an line to the log.
		/// </summary>
		/// <remarks>
		/// Line is decorated with [WAR] - at the beginning of the line in green color
		/// See <see cref="ConsoleLogger"/> for details
		/// </remarks>
		/// <param name="value">The string value to be written to the log.</param>
		void WriteInfo(string value);
		
		/// <summary>
		/// Prints ConsoleTable to the log.
		/// </summary>
		/// <remarks>
		/// See <see cref="ConsoleLogger"/> for details and <see cref="ConsoleTable"/> for details
		/// </remarks>
		/// <param name="table">Table to be written to the log.</param>
		void PrintTable(ConsoleTable table);
	}

}