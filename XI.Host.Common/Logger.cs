using System;
//using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace XI.Host.Common
{
    public static class Logger
    {
        [Flags]
        public enum LogVerbosity : byte
        {
            None = 0,
            Errors = 1,
            Warnings = 2,
            Information = 4,
            All = 7
        }

        [Flags]
        public enum LogType : byte
        {
            None = 0,
            Console = 1,
            File = 2,
            //System = 4,
        }

        private static readonly Assembly entryAssembly = Assembly.GetEntryAssembly();

        private static readonly char[] INFO_CHARS = "INFO: ".ToCharArray();
        private static readonly char[] WARNING_CHARS = "WARNING: ".ToCharArray();
        private static readonly char[] ERROR_CHARS = "ERROR: ".ToCharArray();
        private static readonly char[] DIV_CHARS = " : ".ToCharArray();
        private static FileStream fileStream;

        public static readonly string SOURCE = entryAssembly != null ? entryAssembly.GetName().Name : Assembly.GetExecutingAssembly().GetName().Name;
        public static readonly string VERSION = entryAssembly != null ? entryAssembly.GetName().Version.ToString() : Assembly.GetExecutingAssembly().GetName().Version.ToString();

        public static LogVerbosity Verbosity { get; set; }
        public static LogType Location { get; set; }

        static Logger()
        {
            Verbosity = LogVerbosity.All;
            if (Enum.TryParse(Global.Config["LogVerbosity"], out LogVerbosity logVerbosity))
            {
                Verbosity = logVerbosity;
            }

            Location = LogType.Console;
            if (Enum.TryParse(Global.Config["LogType"], out LogType logType))
            {
                Location = logType;
            }
        }

        // https://docs.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/passing-parameters
        // "To pass by reference with the intent of avoiding copying but not changing the value, use the 'in' modifier."
        private static StringBuilder Build(in MethodBase mb, ReadOnlySpan<char> caller, ReadOnlySpan<char> message)
        {
            var stringBuilder = new StringBuilder();

            stringBuilder.Append(mb.ReflectedType.ToString());
            stringBuilder.Append('.');
            stringBuilder.Append(caller);
            stringBuilder.Append(DIV_CHARS);
            stringBuilder.Append(message);
            stringBuilder.Append(Environment.NewLine);

            return stringBuilder;
        }

        private static void Log(ReadOnlySpan<char> verbosity, in MethodBase mb, ReadOnlySpan<char> caller, ReadOnlySpan<char> message)
        {
            try
            {
                fileStream ??= new FileStream(SOURCE + ".log", FileMode.OpenOrCreate, FileAccess.Write);

                //fileStream.Write(verbosity);
                fileStream.Write(Build(mb, caller, message).Insert(0, verbosity));
            }
            catch (IOException)
            {
                // swallow disk full, all others let go
            }
            catch (ObjectDisposedException)
            {
                // swallow disposed, all others let go
            }
        }

        public static void Information(ReadOnlySpan<char> message, in MethodBase mb, [CallerMemberName] string caller = null)
        {
            if (Verbosity.HasFlag(LogVerbosity.Information))
            {
                switch (Location)
                {
                    case LogType.Console:
                        Console.Write(INFO_CHARS);
                        Console.Write(Build(mb, caller, message));
                        break;
                    case LogType.File:
                        Log(INFO_CHARS, mb, caller, message);
                        break;
                    // Not supported in NET Standard 2.x
                    //case LogType.System:
                    //    EventLog.WriteEntry(SOURCE, Build(mb, caller, message), EventLogEntryType.Information);
                    //    break;
                }
            }
        }

        public static void Warning(ReadOnlySpan<char> message, in MethodBase mb, [CallerMemberName] string caller = null)
        {
            if (Verbosity.HasFlag(LogVerbosity.Warnings))
            {
                switch (Location)
                {
                    case LogType.Console:
                        Console.Write(WARNING_CHARS);
                        Console.Write(Build(mb, caller, message));
                        break;
                    case LogType.File:
                        Log(WARNING_CHARS, mb, caller, message);
                        break;
                    // Not supported in NET Standard 2.x
                    //case LogType.System:
                    //    EventLog.WriteEntry(SOURCE, Build(mb, caller, message), EventLogEntryType.Warning);
                    //    break;
                }
            }
        }

        public static void Warning(in Exception ex, in MethodBase mb, [CallerMemberName] string caller = null)
        {
            if (Verbosity.HasFlag(LogVerbosity.Warnings))
            {
                switch (Location)
                {
                    case LogType.Console:
                        Console.Write(WARNING_CHARS);
                        Console.Write(Build(mb, caller, ex.ToLog()));
                        break;
                    case LogType.File:
                        Log(WARNING_CHARS, mb, caller, ex.ToLog());
                        break;
                    // Not supported in NET Standard 2.x
                    //case LogType.System:
                    //    EventLog.WriteEntry(SOURCE, Build(mb, caller, string.Concat(ex.Message, Environment.NewLine, ex.StackTrace)), EventLogEntryType.Warning);
                    //    break;
                }
            }
        }

        public static void Error(in Exception ex, in MethodBase mb, [CallerMemberName] string caller = null)
        {
            if (Verbosity.HasFlag(LogVerbosity.Errors))
            {
                switch (Location)
                {
                    case LogType.Console:
                        Console.Write(ERROR_CHARS);
                        Console.Write(Build(mb, caller, ex.ToLog()));
                        break;
                    case LogType.File:
                        Log(ERROR_CHARS, mb, caller, ex.ToLog());
                        break;
                    // Not supported in NET Standard 2.x
                    //case LogType.System:
                    //    EventLog.WriteEntry(SOURCE, Build(mb, caller, string.Concat(ex.Message, Environment.NewLine, ex.StackTrace)), EventLogEntryType.Error);
                    //    break;
                }
            }
        }

        public static void Close()
        {
            if (fileStream != null)
            {
                // Do it all explicitly rather than implicitly to ensure .NET changes don't break it.
                fileStream.Flush(true);
                fileStream.Close();
                fileStream.Dispose();
            }
        }
    }
}
