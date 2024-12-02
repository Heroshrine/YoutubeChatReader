using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Parsing;

namespace YoutubeChatRead.FileManagement;

public static class FileManager
{
    public const string DIRECTORY_NAME = "YoutubeChatReader";
    public const string LOG_DIRECTORY_NAME = "Logs";
    public const string SETTINGS_FILE_NAME = "settings.json";
    public const string SETTINGS_DELAY = "Delay";
    public const string SETTINGS_MAX_RESULTS = "Max Results";
    public const string SETTINGS_API_KEY = "API Key";
    public const string SETTINGS_CHANNEL_NAME = "Channel Name";
    public const string INACTIVE_RETRY_DELAY = "No Livestream Retry Delay";

    public static DebugOptions debugOptions;

    public static string LogPathRelative =>
        $"{Path.DirectorySeparatorChar}{LOG_DIRECTORY_NAME}{Path.DirectorySeparatorChar}log_{DateTime.Now:yyyyMMddhhmmssffff}.txt";

    private static readonly SemaphoreSlim Semaphore = new(1, 1);

    private static FileStream? s_logFileStream;
    private static readonly string LogPath = Path.Join(DirectoryPath, LogPathRelative);

    private static string TemplateString =>
        $"A: Word1 Word2 Word3{Environment.NewLine}B: \"Word with spaces\" \"CapItaliZATion doesn't MaTter\"{Environment.NewLine}C: ... 123";

    public static string DirectoryPath =>
        Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), DIRECTORY_NAME);

    static FileManager()
    {
        Directory.CreateDirectory(DirectoryPath);
        Directory.CreateDirectory(Path.Join(DirectoryPath, LOG_DIRECTORY_NAME));

        CreateLogFile();
        //TODO: create settings file
    }

    public static bool FilesExist()
    {
        ReadOnlySpan<string> files = Directory.GetFiles(DirectoryPath);
        switch (files.Length)
        {
            case 1 when Path.GetFileName(files[0]) == SETTINGS_FILE_NAME:
            case 0:
                return false;
            default:
                return true;
        }
    }

    private static void CreateLogFile()
    {
        Semaphore.Wait();

        s_logFileStream?.Dispose();
        s_logFileStream = new FileStream(LogPath, FileMode.Create, FileAccess.Write, FileShare.Read);

        Semaphore.Release();
    }

    private static void CheckLogFile()
    {
        if (File.Exists(LogPath)) return;
        CreateLogFile();
    }

    public static async Task WriteLog(string log)
    {
        if (debugOptions.HasFlag(DebugOptions.NoLogging))
            return;

        log = $"[{DateTime.Now:HH:mm:ss}] {log}{Environment.NewLine}";
        await Task.Run(CheckLogFile);

        await Semaphore.WaitAsync();

        if (s_logFileStream is not { CanWrite: true })
            throw new InvalidOperationException("zipStream or fileStream is null or cannot be written to!");

        await s_logFileStream.WriteAsync(Encoding.UTF8.GetBytes(log));
        await s_logFileStream.FlushAsync();

        Semaphore.Release();
    }

    public static void CreateTemplateFile()
    {
        if (!Directory.Exists(DirectoryPath))
            Directory.CreateDirectory(DirectoryPath);

        var directoryInfo = new DirectoryInfo(DirectoryPath);
        FileInfo[] files = directoryInfo.GetFiles("NewTemplate*.txt");

        var fileName = Path.Join(DirectoryPath,
            $"NewTemplate{(files.Length == 0 ? "" : $" ({files.Length})")}.txt");
        File.WriteAllText(fileName, TemplateString);

        Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = true
        });
    }

    public static async Task<JsonDocument> LoadSettingsFile()
    {
        if (!Directory.Exists(DirectoryPath))
            Directory.CreateDirectory(DirectoryPath);

        var fileName = Path.Combine(DirectoryPath, SETTINGS_FILE_NAME);

        if (!File.Exists(fileName))
        {
            await CreateSettingsFile(fileName);
            Console.ForegroundColor = ConsoleColor.Gray;
            App.ExitProgram("Settings document created, opening... please add API key then restart the program.",
                0);
        }

        var json = await File.ReadAllTextAsync(fileName);

        using var jsonStream = new MemoryStream(Encoding.ASCII.GetBytes(json));

        return await JsonDocument.ParseAsync(jsonStream);
    }

    private static async Task CreateSettingsFile(string filePath)
    {
        var delay = JsonSerializer.SerializeToElement(ChatReader.DEFAULT_CHAT_DELAY);
        var maxResults = JsonSerializer.SerializeToElement(ChatReader.DEFAULT_MAX_RESULTS);
        var apiKey = JsonSerializer.SerializeToElement("API KEY HERE");

        using var stream = new MemoryStream();
        await using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true
        });

        writer.WriteStartObject();

        writer.WritePropertyName(SETTINGS_DELAY);
        delay.WriteTo(writer);

        writer.WritePropertyName(SETTINGS_MAX_RESULTS);
        maxResults.WriteTo(writer);

        writer.WritePropertyName(SETTINGS_API_KEY);
        apiKey.WriteTo(writer);

        writer.WriteEndObject();

        await writer.FlushAsync();

        stream.Seek(0, SeekOrigin.Begin);
        using var doc = await JsonDocument.ParseAsync(stream);

        await File.WriteAllTextAsync(filePath, doc.RootElement.GetRawText());

        Process.Start(new ProcessStartInfo
        {
            FileName = filePath,
            UseShellExecute = true
        });
    }

    public static Task ListFiles()
    {
        if (!Directory.Exists(DirectoryPath))
        {
            Directory.CreateDirectory(DirectoryPath);
            App.WriteResponse("No files were found as the directory did not yet exist (it has been created)");
            return Task.CompletedTask;
        }

        ReadOnlySpan<string> files = Directory.GetFiles(DirectoryPath);

        for (var i = 0; i < files.Length; i++)
        {
            var file = files[i];
            var name = Path.GetFileName(file);

            if (string.IsNullOrEmpty(name)) continue;

            switch (name.ToLower())
            {
                case SETTINGS_FILE_NAME:
                    App.WriteResponse($"\e[0;90m{name} (settings file)");
                    continue;
                default:
                    App.WriteResponse($"{name}");
                    break;
            }
        }

        Console.WriteLine();
        return Task.CompletedTask;
    }

    public static async Task<ReadOnlyMemory<KeywordPair>> LoadConfigFile(string relativePath)
    {
        var fileText = await File.ReadAllTextAsync(Path.Combine(DirectoryPath, relativePath));

        var lines = fileText.Split(Environment.NewLine);
        (string letter, ReadOnlyMemory<string> words)[] parsedFileLines =
            new (string, ReadOnlyMemory<string>)[lines.Length];

        for (var i = 0; i < lines.Length; i++)
        {
            ReadOnlySpan<string> split = lines[i].QuickSplit(':').Span;

            if (split.Length != 2)
                throw new FormatException("Invalid file format! Format expected is: 'A: Word1, Word2, ...'");

            parsedFileLines[i] = (split[0], split[1].QuickSplit(' ', '"'));
        }

        ReadOnlySpan<(string letter, ReadOnlyMemory<string> words)> readyKeywords = parsedFileLines.AsSpan();
        Memory<KeywordPair> result = new KeywordPair[readyKeywords.Length];
        Span<KeywordPair> resultSpan = result.Span;

        for (var i = 0; i < readyKeywords.Length; i++)
        {
            ReadOnlySpan<string> oldWords = readyKeywords[i].words.Span;
            Memory<string> usableWords = new string[oldWords.Length];
            Span<string> usableWordsSpan = usableWords.Span;

            for (var j = 0; j < oldWords.Length; j++)
            {
                usableWordsSpan[j] = oldWords[j].Replace(" ", "").Replace("\"", "").ToLower();
            }

            resultSpan[i] = new KeywordPair(readyKeywords[i].letter, usableWords);
        }

        return result;
    }

    public static void OpenDirectory() => Process.Start(new ProcessStartInfo
    {
        FileName = DirectoryPath,
        UseShellExecute = true
    });
}