namespace Clio.Common
{
	public interface ILogger
	{
		void Write(string value);
		void WriteLine(string value);
		
		void WriteWarning(string value);
		void WriteError(string value);

	}

}