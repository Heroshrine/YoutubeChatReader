using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using Parsing;
using YoutubeChatRead.PythonInterOp;

namespace YoutubeChatRead;

using FileManagement;

internal class App
{
    public const string VERSION = "0.2.0";
    private readonly List<Task<ReadOnlyMemory<(string, string, Queue<MessageInfo>)>>?> _processTasks = [];
    private Task<(FetchedMessages?, string exitMessage, int exitCode)>? _readTask;
    private Task<string?>? _inputTask;
    private Task<string?> _pythonResponseTask;

    private readonly string _apiKey;
    private readonly int _delay;
    private readonly int _maxResults;

    private readonly PythonJob _pythonJob;

    private readonly DebugOptions _debugOptions;
    private ChatInterpreter? _chatInterpreter;
    private ChatReader? _chatReader;

    private CancellationTokenSource _readStopSource = new();

    public App(int delay, int maxResults, string apiKey, DebugOptions debugOptions, string pythonPathExe,
        string pythonPathMain, int pyTtsWpm)
    {
        _delay = delay;
        _maxResults = maxResults;
        _apiKey = apiKey;
        _debugOptions = debugOptions;

        _pythonJob = new PythonJob(pythonPathExe, pythonPathMain, pyTtsWpm);
        _pythonResponseTask = _pythonJob.ReadPythonOutput();
    }

    public async Task Start()
    {
        _inputTask = InputReceiver.Instance.Start();
        _readTask ??= _chatReader?.Start();

        while (true)
        {
            if (_pythonResponseTask.IsCompleted)
            {
                var result = await _pythonResponseTask;
                Console.WriteLine($"\e[0;34m[PYTHON]\e[0;37m {result}");
                InputReceiver.PrintIndicator();
                _pythonResponseTask = _pythonJob.ReadPythonOutput();
            }

            if (_inputTask.IsCompleted)
            {
                try
                {
                    await CommandReader.DoAction(await _inputTask, this);
                }
                catch (Exception e)
                {
                    await WriteExceptionAndLog(e);
                }

                _inputTask = null;
            }


            await EndOfLoopOps();

            _readTask ??= _chatReader?.Start();
            _inputTask ??= InputReceiver.Instance.Start();

            if (_processTasks.Any(t => t is null))
                WriteError("Null process task?");
            // if (_readTask is null)
            //     WriteError("Null read task?");

            var allTasks = new List<Task>(_processTasks!)
            {
                _readTask!,
                _inputTask
            };
            // await Task.WhenAny(allTasks);
        }
    }

    private async Task EndOfLoopOps()
    {
        if (_readTask is { IsCompleted: true })
        {
            var (fetched, exitMessage, exitCode) = await _readTask;
            _readTask = null;

            if (exitCode != 0)
                ExitProgram($"Exit code was {exitCode}, message: {exitMessage}", exitCode);

            if (fetched is not null)
                _processTasks.Add(_chatInterpreter!.FindWords(fetched));
        }

        var workDone = false;
        for (var i = 0; i < _processTasks.Count; i++)
        {
            if (_processTasks[i] is null)
            {
                _processTasks.RemoveAt(i);
                i--;
                continue;
            }

            if (!_processTasks[i]!.IsCompleted) continue;

            ReadOnlyMemory<(string keypress, string, Queue<MessageInfo> messages)> result = await _processTasks[i]!;

            foreach ((var key, var speach, Queue<MessageInfo> messages) in result.ToArray())
            {
                await _pythonJob.SendCommand($"KEY:{key}:{speach}");
                await PrintKeyword(key, messages);
                workDone = true;
            }

            _processTasks.RemoveAt(i);
            i--;
        }

        if (_inputTask is not null && !_inputTask.IsCompleted && workDone)
            InputReceiver.PrintIndicator();
    }

    #region Writing

    public static async Task WriteAndLog(string message)
    {
        Console.WriteLine(message);
        try
        {
            await FileManager.WriteLog(message);
        }
        catch (Exception e)
        {
            // don't try to write to file if there was an exception while writing to the file
            WriteException(e);
        }
    }

    public static async Task WriteResponseAndLog(string response)
    {
        WriteResponse(response);
        try
        {
            await FileManager.WriteLog(response);
        }
        catch (Exception e)
        {
            // don't try to write to file if there was an exception while writing to the file
            WriteException(e);
        }
    }

    public static void WriteResponse(string response) => Console.WriteLine($"\e[0;37m{response}");

    public static async Task WriteWarningAndLog(string warning)
    {
        WriteWarning(warning);
        try
        {
            await FileManager.WriteLog($"[WARNING] {warning}");
        }
        catch (Exception e)
        {
            // don't try to write to file if there was an exception while writing to the file
            WriteException(e);
        }
    }

    public static void WriteWarning(string warning) => Console.WriteLine($"\e[0;93m[WARNING] {warning}");


    public static async Task WriteErrorAndLog(string error)
    {
        WriteError(error);
        try
        {
            await FileManager.WriteLog($"[ERROR] {error}");
        }
        catch (Exception e)
        {
            // don't try to write to file if there was an exception while writing to the file
            WriteException(e);
        }
    }

    public static void WriteError(string error) => Console.WriteLine($"\e[0;31m[ERROR] {error}");

    public static async Task WriteExceptionAndLog(Exception e)
    {
        WriteException(e);
        try
        {
            await FileManager.WriteLog($"[EXCEPTION] {e}");
        }
        catch (Exception exc)
        {
            // don't try to write to file if there was an exception while writing to the file
            WriteException(exc);
        }
    }

    public static void WriteException(Exception e) => Console.WriteLine($"\e[0;31m[EXCEPTION] {e}");

    #endregion

    [DoesNotReturn]
    public static void ExitProgram(string? message, int exitCode, ConsoleColor messageColor = ConsoleColor.DarkCyan)
    {
        Console.ForegroundColor = messageColor;
        if (message is not null)
            Console.WriteLine(message);

        Console.WriteLine("\e[0;90mPress any key to exit...");
        Console.ReadKey(true);

        Environment.ExitCode = exitCode;
        Environment.Exit(exitCode);
    }

    public static async Task WriteLoadSuccess(int delay, int maxResults)
    {
        Console.WriteLine(
            $"\e[0;37mYoutube Chat Reader \e[0;95mv{VERSION} \e[0;90m{{fetch delay {delay} : max results {maxResults}}}");
        Console.WriteLine("\e[0;37mType \e[0;92mhelp\e[0;37m or \e[0;92m?\e[0;37m for help.");
        try
        {
            await FileManager.WriteLog(
                $"Youtube Chat Reader v{VERSION} {{fetch delay {delay} : max results {maxResults}}}");
        }
        catch (Exception exc)
        {
            // don't try to write to file if there was an exception while writing to the file
            WriteException(exc);
        }
    }

    public void SetKeywords(ReadOnlyMemory<KeywordPair> loadedKeywordPairs)
    {
        _chatInterpreter?.Dispose();
        _chatInterpreter = new ChatInterpreter(_debugOptions, loadedKeywordPairs);
    }

    public void PrintUsage()
    {
        if (_chatReader is null)
        {
            WriteResponse("Not reading chat messages, can't print quota usage of session.");
            return;
        }

        var color = _chatReader.AquiredCost switch
        {
            < 3500 => "\e[0;94m",
            <= 3500 => "\e[0;93m",
            < 10000 => "\e[0;91m",
            _ => "\e[5;91m",
        };

        Console.WriteLine($"{color}{_chatReader.AquiredCost}");
    }

    public async Task GetVideoId()
    {
        Console.Write("\e[0;37mEnter video ID or URL: \e[0;94m");
        var input = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(input))
            await GetVideoId();

        _readStopSource = new CancellationTokenSource();
        _chatReader = new ChatReader(_delay, _apiKey, ChatReader.FormatVideoId(input!), _maxResults,
            _readStopSource.Token);
        await _chatReader.Initialize();
        _readTask = _chatReader.Start();
    }

    public void PrintVideoId() => _chatReader?.PrintId();

    private async Task PrintKeyword(string keyPress, Queue<MessageInfo> messagesForWord)
    {
        await WriteAndLog($"\e[0;37mFound Keyword! Pressing\e[0;90m {{\e[0;92m{keyPress}\e[0;90m}}\e[0;37m from:");
        while (messagesForWord.TryDequeue(out var message))
        {
            await WriteAndLog(message.message!.Length == 1
                ? $"[{message.timestamp:HH:mm:ss}] {message.author}: \e[0;92m{message.message!}"
                : $"[{message.timestamp:HH:mm:ss}] {message.author}: \e[0;92m{message.message![0]}\e[0;37m{message.message![1..]}");
        }

        Console.WriteLine();
    }
}