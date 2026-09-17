using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace TokenMonitor.Providers.Cli;

public sealed class ConPtySession : IAsyncDisposable
{
    private readonly IntPtr _pseudoConsole;
    private readonly IntPtr _attributeList;
    private readonly IntPtr _jobHandle;
    private readonly IntPtr _processHandle;
    private readonly IntPtr _threadHandle;
    private readonly FileStream _inputWriter;
    private readonly FileStream _outputReader;
    private readonly Task _pumpTask;
    private readonly StringBuilder _buffer = new();
    private readonly object _bufferLock = new();
    private readonly CancellationTokenSource _pumpCts = new();

    private long _lastOutputTicks = DateTime.UtcNow.Ticks;
    private bool _disposed;

    private ConPtySession(
        IntPtr pseudoConsole,
        IntPtr attributeList,
        IntPtr jobHandle,
        IntPtr processHandle,
        IntPtr threadHandle,
        FileStream inputWriter,
        FileStream outputReader)
    {
        _pseudoConsole = pseudoConsole;
        _attributeList = attributeList;
        _jobHandle = jobHandle;
        _processHandle = processHandle;
        _threadHandle = threadHandle;
        _inputWriter = inputWriter;
        _outputReader = outputReader;
        _pumpTask = Task.Run(() => PumpOutputAsync(_pumpCts.Token));
    }

    public static ConPtySession Start(string commandLine, string workingDirectory, short columns = 120, short rows = 40)
    {
        if (!ConPtyNativeMethods.CreatePipe(out var inputReadSide, out var inputWriteSide, IntPtr.Zero, 0))
        {
            ThrowLastWin32Error("Failed to create input pipe.");
        }

        if (!ConPtyNativeMethods.CreatePipe(out var outputReadSide, out var outputWriteSide, IntPtr.Zero, 0))
        {
            ThrowLastWin32Error("Failed to create output pipe.");
        }

        var size = new ConPtyNativeMethods.COORD { X = columns, Y = rows };
        var hr = ConPtyNativeMethods.CreatePseudoConsole(size, inputReadSide, outputWriteSide, 0, out var pseudoConsole);
        if (hr != 0)
        {
            inputReadSide.Dispose();
            inputWriteSide.Dispose();
            outputReadSide.Dispose();
            outputWriteSide.Dispose();
            throw new Win32Exception(hr, "CreatePseudoConsole failed.");
        }

        // ConPTY duplicates these handles internally; our copies must be closed or output will never reach EOF.
        inputReadSide.Dispose();
        outputWriteSide.Dispose();

        var attributeList = IntPtr.Zero;
        IntPtr processHandle = IntPtr.Zero;
        IntPtr threadHandle = IntPtr.Zero;
        IntPtr jobHandle = IntPtr.Zero;

        try
        {
            var size2 = IntPtr.Zero;
            ConPtyNativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size2);
            attributeList = Marshal.AllocHGlobal(size2);
            if (!ConPtyNativeMethods.InitializeProcThreadAttributeList(attributeList, 1, 0, ref size2))
            {
                ThrowLastWin32Error("InitializeProcThreadAttributeList failed.");
            }

            if (!ConPtyNativeMethods.UpdateProcThreadAttribute(
                    attributeList,
                    0,
                    (IntPtr)ConPtyNativeMethods.PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                    pseudoConsole,
                    (IntPtr)IntPtr.Size,
                    IntPtr.Zero,
                    IntPtr.Zero))
            {
                ThrowLastWin32Error("UpdateProcThreadAttribute failed.");
            }

            var startupInfo = new ConPtyNativeMethods.STARTUPINFOEX
            {
                StartupInfo = new ConPtyNativeMethods.STARTUPINFO(),
                lpAttributeList = attributeList,
            };
            startupInfo.StartupInfo.cb = Marshal.SizeOf<ConPtyNativeMethods.STARTUPINFOEX>();
            startupInfo.StartupInfo.dwFlags = ConPtyNativeMethods.STARTF_USESTDHANDLES;

            var commandLineBuffer = new StringBuilder(commandLine);

            if (!ConPtyNativeMethods.CreateProcessW(
                    null,
                    commandLineBuffer,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    ConPtyNativeMethods.EXTENDED_STARTUPINFO_PRESENT,
                    IntPtr.Zero,
                    workingDirectory,
                    ref startupInfo,
                    out var processInfo))
            {
                ThrowLastWin32Error("CreateProcessW failed.");
            }

            processHandle = processInfo.hProcess;
            threadHandle = processInfo.hThread;

            jobHandle = ConPtyNativeMethods.CreateJobObjectW(IntPtr.Zero, null);
            if (jobHandle == IntPtr.Zero)
            {
                ThrowLastWin32Error("CreateJobObjectW failed.");
            }

            var limitInfo = new ConPtyNativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new ConPtyNativeMethods.JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = ConPtyNativeMethods.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
                },
            };
            var limitInfoSize = (uint)Marshal.SizeOf<ConPtyNativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            if (!ConPtyNativeMethods.SetInformationJobObject(jobHandle, ConPtyNativeMethods.JobObjectExtendedLimitInformation, ref limitInfo, limitInfoSize))
            {
                ThrowLastWin32Error("SetInformationJobObject failed.");
            }

            if (!ConPtyNativeMethods.AssignProcessToJobObject(jobHandle, processHandle))
            {
                ThrowLastWin32Error("AssignProcessToJobObject failed.");
            }

            var inputWriter = new FileStream(inputWriteSide, FileAccess.Write);
            var outputReader = new FileStream(outputReadSide, FileAccess.Read);

            return new ConPtySession(pseudoConsole, attributeList, jobHandle, processHandle, threadHandle, inputWriter, outputReader);
        }
        catch
        {
            if (jobHandle != IntPtr.Zero)
            {
                ConPtyNativeMethods.CloseHandle(jobHandle);
            }

            if (threadHandle != IntPtr.Zero)
            {
                ConPtyNativeMethods.CloseHandle(threadHandle);
            }

            if (processHandle != IntPtr.Zero)
            {
                ConPtyNativeMethods.TerminateProcess(processHandle, 1);
                ConPtyNativeMethods.CloseHandle(processHandle);
            }

            if (attributeList != IntPtr.Zero)
            {
                ConPtyNativeMethods.DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }

            ConPtyNativeMethods.ClosePseudoConsole(pseudoConsole);
            inputWriteSide.Dispose();
            outputReadSide.Dispose();
            throw;
        }
    }

    public string Snapshot()
    {
        lock (_bufferLock)
        {
            return _buffer.ToString();
        }
    }

    public async Task WriteAsync(string text, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await _inputWriter.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await _inputWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task WaitForIdleAsync(TimeSpan quietPeriod, TimeSpan hardTimeout, CancellationToken cancellationToken)
    {
        var start = DateTime.UtcNow;
        var deadline = start + hardTimeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var now = DateTime.UtcNow;
            if (now >= deadline)
            {
                return;
            }

            var lastOutput = new DateTime(Interlocked.Read(ref _lastOutputTicks), DateTimeKind.Utc);
            // Measure the quiet period from this call, not from output that arrived before it: otherwise a caller
            // that writes input and then waits returns instantly, before the process has had a chance to react.
            var reference = lastOutput > start ? lastOutput : start;
            var quietFor = now - reference;
            if (quietFor >= quietPeriod)
            {
                return;
            }

            var remainingQuiet = quietPeriod - quietFor;
            var remainingHard = deadline - now;
            var delay = remainingQuiet < remainingHard ? remainingQuiet : remainingHard;
            if (delay < TimeSpan.FromMilliseconds(25))
            {
                delay = TimeSpan.FromMilliseconds(25);
            }

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Kill()
    {
        if (_jobHandle != IntPtr.Zero)
        {
            ConPtyNativeMethods.TerminateJobObject(_jobHandle, 1);
        }
    }

    private async Task PumpOutputAsync(CancellationToken cancellationToken)
    {
        var decoder = Encoding.UTF8.GetDecoder();
        var readBuffer = new byte[4096];
        var charBuffer = new char[4096];

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int read;
                try
                {
                    read = await _outputReader.ReadAsync(readBuffer, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (IOException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                if (read <= 0)
                {
                    break;
                }

                var charCount = decoder.GetChars(readBuffer, 0, read, charBuffer, 0);
                if (charCount > 0)
                {
                    lock (_bufferLock)
                    {
                        _buffer.Append(charBuffer, 0, charCount);
                    }

                    Interlocked.Exchange(ref _lastOutputTicks, DateTime.UtcNow.Ticks);
                }
            }
        }
        catch
        {
            // The pump stops silently on unexpected stream errors; callers observe output via Snapshot().
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Kill();

        _pumpCts.Cancel();
        try
        {
            await _pumpTask.ConfigureAwait(false);
        }
        catch
        {
            // Ignore pump shutdown errors.
        }

        _pumpCts.Dispose();

        _inputWriter.Dispose();
        _outputReader.Dispose();

        if (_pseudoConsole != IntPtr.Zero)
        {
            ConPtyNativeMethods.ClosePseudoConsole(_pseudoConsole);
        }

        if (_attributeList != IntPtr.Zero)
        {
            ConPtyNativeMethods.DeleteProcThreadAttributeList(_attributeList);
            Marshal.FreeHGlobal(_attributeList);
        }

        if (_threadHandle != IntPtr.Zero)
        {
            ConPtyNativeMethods.CloseHandle(_threadHandle);
        }

        if (_processHandle != IntPtr.Zero)
        {
            ConPtyNativeMethods.CloseHandle(_processHandle);
        }

        if (_jobHandle != IntPtr.Zero)
        {
            ConPtyNativeMethods.CloseHandle(_jobHandle);
        }
    }

    private static void ThrowLastWin32Error(string message)
    {
        throw new Win32Exception(Marshal.GetLastWin32Error(), message);
    }
}
