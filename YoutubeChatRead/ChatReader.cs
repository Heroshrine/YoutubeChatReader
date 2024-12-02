using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using YoutubeChatRead.FileManagement;

namespace YoutubeChatRead;

internal sealed partial class ChatReader(
    int desiredDelay,
    string apiKey,
    string videoId,
    int maxResults,
    CancellationToken cancellationToken)
    : IDisposable
{
    public const int DEFAULT_MAX_RESULTS = 120;
    public const int DEFAULT_CHAT_DELAY = 6500;
    public const int DEFAULT_INACTIVE_RETRY = 20000;

    private readonly string _apiKey = apiKey;
    private readonly string _videoId = videoId;
    private readonly int _desiredDelay = desiredDelay;
    private readonly int _maxResults = maxResults;

    private readonly CancellationToken _cancellationToken = cancellationToken;
    private readonly HttpClient _client = new();

    private string LiveMsgsApiUrl =>
        $"https://www.googleapis.com/youtube/v3/liveChat/messages?liveChatId={_liveChatId}&part=snippet,authorDetails&maxResults={_maxResults}&key={_apiKey}";

    private string VideoApiUrl =>
        $"https://www.googleapis.com/youtube/v3/videos?part=liveStreamingDetails&id={_videoId}&key={_apiKey}";

    public int AquiredCost { get; private set; }

    private int _currentDelay;
    private string? _liveChatId;
    private List<MessageInfo> _previous = [];


    public async Task Initialize()
    {
        if (string.IsNullOrWhiteSpace(_liveChatId))
        {
            await SetupLiveChatId(_videoId);
            _currentDelay = _desiredDelay;
            await FileManager.WriteLog("Initialized ChatReader");
        }
        else
            await App.WriteWarningAndLog("ChatReader instance already initialized");
    }

    public async Task<(FetchedMessages?, string exitMessage, int exitCode)> Start()
    {
        Debug.Assert(!string.IsNullOrWhiteSpace(_liveChatId), "Must call Initialize() before calling Start()!");

        await Task.Delay(_currentDelay, _cancellationToken);

        (List<MessageInfo> messages, var requiredDelay) = await FetchMessages();
        AquiredCost += 5;
        await FileManager.WriteLog($"Fetched chat messages, current reader cost: {AquiredCost}");

        // check if should return early
        if (await Task.Run(() => messages.Any(m => m.messageType is MessageType.Exit), _cancellationToken))
            return (default, "Stream Ended", 1);
        if (await Task.Run(() => messages.Any(m => m.messageType is MessageType.Error), _cancellationToken))
            return (default, "MessageType.Error received", -1);

        // seperate messages
        IEnumerable<MessageInfo> current = await Task.Run(() => messages.Except(_previous), _cancellationToken);
        MessageInfo[] currentArray = current as MessageInfo[] ?? current.ToArray();
        IEnumerable<MessageInfo> old = await Task.Run(() => messages.Intersect(_previous), _cancellationToken);
        MessageInfo[] oldArray = old as MessageInfo[] ?? old.ToArray();

        if (currentArray.Length > 0)
            await FileManager.WriteLog(
                $"New sampled: {currentArray.Length} | Total sampled: {messages.Count} | Old sampled: {oldArray.Length}");
        if (currentArray.Length >= _maxResults - 5)
            await App.WriteWarningAndLog(
                $"Possible missed messages! New messages was length of {currentArray.Length}! Maximum is {_maxResults}");

        _previous = messages;
        _currentDelay = requiredDelay;

        return (new FetchedMessages(messages.ToArray(), currentArray, oldArray),
            $"fetched {messages.Count} messages", 0);
    }

    private async Task<(List<MessageInfo> messages, int requiredDelay)> FetchMessages()
    {
        var response = await _client.GetAsync(LiveMsgsApiUrl, _cancellationToken);
        response.EnsureSuccessStatusCode();


        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(_cancellationToken),
            cancellationToken: _cancellationToken);
        var items = json.RootElement.GetProperty("items");

        if (items.GetArrayLength() <= 0)
            return ([new MessageInfo(null, null, MessageType.Unsupported, default)], _desiredDelay);

        var messages = new List<MessageInfo>(_maxResults);

        foreach (var item in items.EnumerateArray())
        {
            _cancellationToken.ThrowIfCancellationRequested();

            var snippet = item.GetProperty("snippet");
            var messageType = snippet.GetProperty("type").GetString() switch
            {
                "chatEndedEvent" => MessageType.Exit,
                "textMessageEvent" => MessageType.Text,
                "superChatEvent" => MessageType.SuperChat,
                _ => MessageType.Unsupported
            };

            // ReSharper disable once ConvertIfStatementToSwitchStatement
            if (messageType is MessageType.Exit)
                return ([new MessageInfo(null, null, MessageType.Exit, default)], int.MaxValue);
            if (messageType is MessageType.Unsupported)
                continue;

            var author = item.GetProperty("authorDetails").GetProperty("displayName").GetString();
            var message = snippet.GetProperty("displayMessage").GetString();
            var publishTime = snippet.GetProperty("publishedAt").GetString();
            if (string.IsNullOrEmpty(author) || string.IsNullOrEmpty(message) || string.IsNullOrEmpty(publishTime))
                continue;

            var timecode = DateTime.Parse(publishTime, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            messages.Add(new MessageInfo(author, message, messageType, timecode));
        }

        var requestedDelay = json.RootElement.GetProperty("pollingIntervalMillis").GetUInt32();
        return messages.Count <= 0
            ? ([new MessageInfo(null, null, MessageType.Error, default)], int.MaxValue)
            : (messages, Math.Max(_desiredDelay, (int)requestedDelay));
    }

    // sets _liveChatId to the correct ID for API calls.
    private async Task SetupLiveChatId(string videoId)
    {
        var response = await _client.GetAsync(VideoApiUrl, _cancellationToken);
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(_cancellationToken));
        if (!json.RootElement.TryGetProperty("items", out var items))
            throw new InvalidOperationException(
                "Unable to get items property from videoID, but the videoID was correct.");

        if (items.GetArrayLength() <= 0)
            throw new InvalidOperationException(
                $"Items length was 0. Was the correct ID used? {{{videoId}}}");

        if (!items[0].TryGetProperty("liveStreamingDetails", out var details))
            throw new InvalidOperationException("The video ID provided is not a live stream!");

        if (!details.TryGetProperty("activeLiveChatId", out var chatIdElement))
            throw new InvalidOperationException("The video ID provided points to an inactive livestream!");

        var chatIdString = chatIdElement.GetString();
        if (string.IsNullOrEmpty(chatIdString))
            throw new InvalidOperationException("Active LiveChatId is null or empty?");

        _liveChatId = chatIdString;
        AquiredCost++;

        await FileManager.WriteLog($"Retreived LiveChatID: {_liveChatId}, current reader cost: {AquiredCost}");
    }

    public void PrintId()
    {
        App.WriteResponse($"Video ID: {_videoId}, Live Chat ID: {_liveChatId}");
    }

    public void Dispose()
    {
        _client.CancelPendingRequests();
        _client.Dispose();
    }

    public static string FormatVideoId(string input)
    {
        if (string.IsNullOrEmpty(input))
            return "";

        var match = VideoIdRegex().Match(input);

        return match.Success
            ? match.Groups[1].Length > 0
                ? match.Groups[1].Value
                : match.Groups[2].Value
            : input;
    }

    [GeneratedRegex("\\?v=([^\"]*)|live/([^\"]*)")]
    private static partial Regex VideoIdRegex();
}