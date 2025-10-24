namespace Clio.Common;

public interface IConsole {
	bool KeyAvailable { get; }
	System.ConsoleKeyInfo ReadKey();
}

internal class SystemConsoleAdapter : IConsole {
	public bool KeyAvailable => System.Console.KeyAvailable;
	public System.ConsoleKeyInfo ReadKey() => System.Console.ReadKey();
}

