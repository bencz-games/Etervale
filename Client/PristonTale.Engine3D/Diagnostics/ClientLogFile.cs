using System.Text;

namespace PristonTale.Engine3D.Diagnostics;

// The client's console written to client.log beside the executable as well, each line stamped with the local time, since
// the game runs without a console window. The previous run's log is kept as client.previous.log. Unhandled exceptions are
// written with their stack before the process ends.
public sealed class ClientLogFile : IDisposable
{
    public const string FileName = "client.log";
    public const string PreviousFileName = "client.previous.log";

    private readonly StreamWriter _file;
    private readonly TextWriter _console;
    private readonly TextWriter _consoleError;

    private ClientLogFile(StreamWriter file)
    {
        _file = file;
        _console = Console.Out;
        _consoleError = Console.Error;
        Console.SetOut(TextWriter.Synchronized(new StampedWriter(_console, _file)));
        Console.SetError(TextWriter.Synchronized(new StampedWriter(_consoleError, _file)));
        AppDomain.CurrentDomain.UnhandledException += LogUnhandled;
        TaskScheduler.UnobservedTaskException += LogUnobserved;
    }

    // Null when the file cannot be opened (a read-only folder, a second client running): the game runs on without it.
    public static ClientLogFile Open(string directory)
    {
        try
        {
            var path = Path.Combine(directory, FileName);
            if (File.Exists(path))
            {
                File.Move(path, Path.Combine(directory, PreviousFileName), overwrite: true);
            }

            var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            return new ClientLogFile(new StreamWriter(stream, new UTF8Encoding(false)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[warn] {FileName} not written: {exception.Message}");
            return null;
        }
    }

    private static void LogUnhandled(object sender, UnhandledExceptionEventArgs args) =>
        Console.WriteLine($"[error] unhandled exception: {args.ExceptionObject}");

    private static void LogUnobserved(object sender, UnobservedTaskExceptionEventArgs args) =>
        Console.WriteLine($"[error] unobserved task exception: {args.Exception}");

    public void Dispose()
    {
        AppDomain.CurrentDomain.UnhandledException -= LogUnhandled;
        TaskScheduler.UnobservedTaskException -= LogUnobserved;
        Console.SetOut(_console);
        Console.SetError(_consoleError);
        _file.Dispose();
    }

    // Copies everything to the console and to the file, the file's lines starting with the time they began. The file is
    // flushed once a line ends (so a crash keeps every finished line), not for every character.
    private sealed class StampedWriter(TextWriter console, TextWriter file) : TextWriter
    {
        private const string StampFormat = "HH:mm:ss.fff ";
        private bool _lineStart = true;

        public override Encoding Encoding => file.Encoding;

        public override void Write(char value)
        {
            console.Write(value);
            ToFile(value);
        }

        public override void Write(string value)
        {
            console.Write(value);
            foreach (var character in value ?? string.Empty)
            {
                ToFile(character);
            }
        }

        private void ToFile(char value)
        {
            if (_lineStart && value != '\n')
            {
                Span<char> stamp = stackalloc char[StampFormat.Length];
                file.Write(DateTime.Now.TryFormat(stamp, out var written, StampFormat) ? stamp[..written] : []);
            }

            file.Write(value);
            _lineStart = value == '\n';
            if (_lineStart)
            {
                file.Flush();
            }
        }

        public override void WriteLine(string value)
        {
            Write(value);
            Write(CoreNewLine);
        }
    }
}
