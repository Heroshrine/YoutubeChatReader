using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace YoutubeChatRead.PythonInterOp;

public sealed partial class PythonJob : IDisposable
{
    public const int TTS_WPM = 160;

    [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [LibraryImport("kernel32.dll", EntryPoint = "SetInformationJobObject", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(IntPtr hJob, JobObjectInfoType jobObjectInfoType,
        IntPtr lpJobObjectInfo,
        uint cbJobObjectInfoLength);

    [LibraryImport("kernel32.dll", EntryPoint = "AssignProcessToJobObject", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [LibraryImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr hObbject);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint FormatMessage(
        uint dwFlags,
        IntPtr lpSource,
        uint dwMessageId,
        uint dwLanguageId,
        StringBuilder lpBuffer,
        uint nSize,
        IntPtr arguments);

    private IntPtr _jobHandle;
    private Process? _pythonProcess;

    private readonly StreamWriter _pyWriter;
    private readonly StreamReader _pyReader;

    private readonly string _pythonPathMain;

    public PythonJob(string pythonPathMain, int ttsWpm)
    {
        _pythonPathMain = pythonPathMain;

        CreateJob();

        _pyWriter = _pythonProcess!.StandardInput;
        _pyReader = _pythonProcess!.StandardOutput;

        _pyWriter.WriteLine($"WPM:{ttsWpm}");

        var response = _pyReader.ReadLine();

        if (string.IsNullOrEmpty(response))
            throw new ExternalException(
                "Python script did not signal successful start, perhaps it's executing in the wrong directory?");

        Console.WriteLine($"\e[0;34m[Python] \e[0;92m{response}{Environment.NewLine}");
    }

    private void CreateJob()
    {
        // create the job object
        _jobHandle = CreateJobObject(IntPtr.Zero, null);
        if (_jobHandle == IntPtr.Zero)
        {
            // if error, print it
            StringBuilder buffer = new(512);
            var error = (uint)Marshal.GetLastWin32Error();
            FormatMessage(0x00001000 | 0x00000200, IntPtr.Zero, error, 0,
                buffer, (uint)buffer.Capacity, IntPtr.Zero);

            throw new ExternalException("Failed to create job object.", new ExternalException($"{{{error}}} {buffer}"));
        }

        // create the job info
        var jobLimits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JobObjectLimitFlags.KillOnJobClose,
            }
        };

        var length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var jobLimitsPtr = Marshal.AllocHGlobal(length);
        Marshal.StructureToPtr(jobLimits, jobLimitsPtr, false);
        //Marshal.StructureToPtr(jobLimits, jobLimitsPtr, false);

        // set the job info
        if (!SetInformationJobObject(_jobHandle, JobObjectInfoType.JobObjectExtendedLimitinformation, jobLimitsPtr,
                (uint)length))
        {
            // if error, print it
            StringBuilder buffer = new(512);
            var error = (uint)Marshal.GetLastWin32Error();
            FormatMessage(0x00001000 | 0x00000200, IntPtr.Zero, error, 0,
                buffer, (uint)buffer.Capacity, IntPtr.Zero);

            throw new ExternalException("Failed to set information job object.",
                new ExternalException($"{{{error}}} {buffer}"));
        }


        Marshal.FreeHGlobal(jobLimitsPtr);

        // Console.WriteLine(_pythonPathMain);

        _pythonProcess = Process.Start(new ProcessStartInfo
        {
            FileName = "py",
            Arguments = _pythonPathMain,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });

        // var _alt = Process.Start(new ProcessStartInfo
        // {
        //     FileName = "py",
        //     Arguments = _pythonPathMain,
        //     UseShellExecute = true,
        // });
        //
        // _alt.WaitForExit();

        if (AssignProcessToJobObject(_jobHandle, _pythonProcess!.Handle)) return;

        _pythonProcess.Kill(true);
        throw new ExternalException("Failed to assign process to job object.");
    }

    public async Task SendCommand(string command)
    {
        Console.WriteLine($"Process Status: {_pythonProcess is { HasExited: true }}");
        await _pyWriter.WriteLineAsync(command);
    }

    public async Task<string?> ReadPythonOutput()
    {
        var result = await _pyReader.ReadLineAsync();
        if ((_pythonProcess == null || _pythonProcess.HasExited) && _pythonProcess?.ExitCode != 0)
            result = $"\e[0;91m{result}";
        return result;
    }

    public void Dispose()
    {
        _pythonProcess?.Kill(true);
        _pythonProcess?.Dispose();

        _pyWriter.Dispose();
        _pyReader.Dispose();

        CloseHandle(_jobHandle);
    }
}