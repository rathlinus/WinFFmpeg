using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using WinFFmpeg.Core;

namespace WinFFmpeg.App;

/// <summary>
/// Makes the first process the owner of the queue. Later processes send it their
/// <see cref="LaunchRequest"/> over a named pipe and exit.
/// </summary>
internal sealed partial class SingleInstance : IDisposable
{
    private const int AnyProcess = -1;
    private const int ConnectTimeoutMilliseconds = 5000;

    // Pipes are machine-wide, so the name carries the user to keep sessions apart.
    private static readonly string s_name = $"WinFFmpeg.{Environment.UserName}";

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _stop = new();

    private SingleInstance(Mutex mutex, bool isPrimary)
    {
        _mutex = mutex;
        IsPrimary = isPrimary;
    }

    public bool IsPrimary { get; }

    public static SingleInstance Acquire()
    {
        var mutex = new Mutex(initiallyOwned: true, $@"Local\{s_name}", out bool createdNew);
        return new SingleInstance(mutex, createdNew);
    }

    /// <summary>Starts receiving requests from later instances. The callback runs on a thread-pool thread.</summary>
    public void Listen(Action<LaunchRequest> onRequest)
    {
        if (!IsPrimary)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        s_name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(_stop.Token);

                    using var reader = new StreamReader(server, Encoding.UTF8);
                    if (LaunchRequest.FromJson(await reader.ReadToEndAsync(_stop.Token)) is { } request)
                    {
                        onRequest(request);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (IOException)
                {
                    // A client went away mid-message; wait for the next one.
                }
            }
        });
    }

    /// <summary>Hands a request to the primary instance. False when it could not be reached.</summary>
    public static bool TrySend(LaunchRequest request)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", s_name, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(ConnectTimeoutMilliseconds);

            // This process was just started by the user, so it may take the foreground;
            // the long-running primary instance may not unless that right is passed on.
            AllowSetForegroundWindow(AnyProcess);

            using var writer = new StreamWriter(client, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write(request.ToJson());
            writer.Flush();
            return true;
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        if (IsPrimary)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);
}
