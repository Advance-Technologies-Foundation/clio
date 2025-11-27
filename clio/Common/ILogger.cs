using System.Collections.Generic;
using ConsoleTables;
using FluentValidation.Results;

namespace Clio.Common
{
	public interface ILogger
	{
		/// <summary>
		/// Starts the logging process.
		/// </summary>
		/// <param name="logFilePath">The path to the log file. If not provided, a default path is used.</param>
		public void Start(string logFilePath = "");

		/// <summary>
		/// <see langword="public"/> method to set the <see cref="CreatioLogStreamer"/> object.
		/// </summary>
		/// <param name="creatioLogStreamer"></param>
		public void SetCreatioLogStreamer(ILogStreamer creatioLogStreamer);

		/// <summary>
		/// Starts the logging process with a stream.
		/// </summary>
		public void StartWithStream();

		/// <summary>
		/// Stops the logging thread.
		/// </summary>
		/// <remarks>See <see cref="ConsoleLogger"/>> for details</remarks>
		public void Stop();
		
		
		void Write(string value);
		
		/// <summary>
		/// Write a empty line to the log.
		/// </summary>
		void WriteLine();

		/// <summary>
		/// Writes an undecorated line to the log.
		/// </summary>
		/// <remarks>See <see cref="ConsoleLogger"/> for details</remarks>
		/// <param name="value">The string value to be written to the log.</param>
		void WriteLine(string value);
		
		
		/// <summary>
		/// Writes a line to the log.
		/// </summary>
		/// <remarks>
		/// Line is decorated with [WAR] - at the beginning of the line in yellow color
		/// See <see cref="ConsoleLogger"/> for details
		/// </remarks>
		/// <param name="value">The string value to be written to the log.</param>
		void WriteWarning(string value);
		
		/// <summary>
		/// Writes a line to the log.
		/// </summary>
		/// <remarks>
		/// Line is decorated with [WAR] - at the beginning of the line in red color
		/// See <see cref="ConsoleLogger"/> for details
		/// </remarks>
		/// <param name="value">The string value to be written to the log.</param>
		void WriteError(string value);
		
		/// <summary>
		/// Writes a line to the log.
		/// </summary>
		/// <remarks>
		/// Line is decorated with [WAR] - at the beginning of the line in green color
		/// See <see cref="ConsoleLogger"/> for details
		/// </remarks>
		/// <param name="value">The string value to be written to the log.</param>
		void WriteInfo(string value);
		
		/// <summary>
		/// Writes a line to the log.
		/// </summary>
		/// <remarks>
		/// Line is decorated with [DBG] - at the beginning of the line in DarkYellow color
		/// See <see cref="ConsoleLogger"/> for details
		/// </remarks>
		/// <param name="value">The string value to be written to the log.</param>
		void WriteDebug(string value);
		
		/// <summary>
		/// Prints ConsoleTable to the log.
		/// </summary>
		/// <remarks>
		/// See <see cref="ConsoleLogger"/> for details and <see cref="ConsoleTable"/> for details
		/// </remarks>
		/// <param name="table">Table to be written to the log.</param>
		void PrintTable(ConsoleTable table);
		
		
		/// <summary>
		/// Prints a collection of validation errors to the log.
		/// </summary>
		/// <remarks>
		/// Each error in the collection is printed as a separate entry in the log.
		/// This method is useful for displaying the details of validation failures to the user.
		/// </remarks>
		/// <param name="errors">The collection of <see cref="ValidationFailure"/> objects to be printed.</param>
		void PrintValidationFailureErrors(IEnumerable<ValidationFailure> errors);
	}

}
