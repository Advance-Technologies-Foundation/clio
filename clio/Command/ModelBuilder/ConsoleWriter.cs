using System;


namespace Clio.Command.ModelBuilder
{
    public static class ConsoleWriter
    {
        public static void WriteMessage(MessageType type, string message)
        {
            switch (type)
            {
                case MessageType.OK:
                    OkMessage(message);
                    break;
                case MessageType.Error:
                    ErrorMessage(message);
                    break;
                case MessageType.Warning:
                    break;
                case MessageType.Info:
                    InfoMessage(message);
                    break;
                case MessageType.Detail:
                    DetailMessage(message);
                    break;
                case MessageType.Try:
                    TryMessage(message);
                    break;



                default:
                    break;
            }
        }

        private static void OkMessage(string message)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("[OK]");
            Console.ResetColor();
            Console.Write($"\t\t{message}");
            Console.WriteLine();
            Console.ResetColor();
        }
        private static void ErrorMessage(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("[ERROR]");
            Console.ResetColor();
            Console.Write($"\t\t{message}");
            Console.WriteLine();
            Console.ResetColor();
        }
        private static void TryMessage(string message)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("[TRY]");
            Console.ResetColor();
            Console.Write($"\t\t{message}");
            Console.WriteLine();
            Console.ResetColor();
        }
        private static void InfoMessage(string message)
        {
            Console.ResetColor();
            Console.Write("[INFO]");
            Console.Write($"\t\t{message}");
            Console.WriteLine();
            Console.ResetColor();
        }
        private static void DetailMessage(string message)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("[DETAIL]");
            Console.ResetColor();
            Console.Write($"\t\t\t{message}");
            Console.ResetColor();
        }

        public enum MessageType {
            OK,
            Error,
            Warning,
            Info,
            Detail,
            Try
        }
    }
}
