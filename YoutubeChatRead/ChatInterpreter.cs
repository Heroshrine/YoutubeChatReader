using YoutubeChatRead.FileManagement;

namespace YoutubeChatRead;

//TODO: should have used DI for this lol
public class ChatInterpreter : IDisposable
{
    private readonly DebugOptions _debugOptions;
    private readonly ReadOnlyMemory<KeywordPair> _keywords;

    private readonly int _longestKeywordLength;
    private DateTime _lastFound = DateTime.MinValue;

    private readonly CancellationTokenSource _cancellationTokenSource = new();

    public ChatInterpreter(DebugOptions debugOptions, ReadOnlyMemory<KeywordPair> keywords)
    {
        _debugOptions = debugOptions;
        _keywords = keywords;

        ReadOnlySpan<KeywordPair> keywordsSpan = _keywords.Span;
        foreach (var keyword in keywordsSpan)
        {
            ReadOnlySpan<string> words = keyword.words.Span;
            foreach (var word in words)
            {
                if (word.Length > _longestKeywordLength)
                    _longestKeywordLength = word.Length;
            }
        }
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
    }

    internal async Task<ReadOnlyMemory<(string, Queue<MessageInfo>)>> FindWords(FetchedMessages fetchedMessages)
    {
        ReadOnlyMemory<MessageInfo> readableOldMessages = fetchedMessages.oldMessages.Length >= _longestKeywordLength
            ? fetchedMessages.oldMessages[^_longestKeywordLength..]
            : fetchedMessages.oldMessages;

        var oldMessagesIndex = 0;
        ReadOnlySpan<MessageInfo> readableOldMessagesSpan = readableOldMessages.Span;
        for (; oldMessagesIndex < readableOldMessagesSpan.Length; oldMessagesIndex++)
        {
            if (readableOldMessagesSpan[oldMessagesIndex].timestamp > _lastFound)
                break;
        }

        readableOldMessages = oldMessagesIndex + 1 >= readableOldMessages.Length
            ? Memory<MessageInfo>.Empty
            : readableOldMessages[oldMessagesIndex..];

        Memory<MessageInfo> usingMessages = await Task.Run(() =>
        {
            Memory<MessageInfo> temp = new MessageInfo[readableOldMessages.Length + fetchedMessages.newMessages.Length];

            readableOldMessages.CopyTo(temp);
            fetchedMessages.newMessages.CopyTo(temp[readableOldMessages.Length..]);

            return temp;
        }, _cancellationTokenSource.Token);

        var tasks = new List<Task<(bool, Queue<MessageInfo>?, DateTime)>>(_keywords.Length);
        ReadOnlySpan<KeywordPair> keywordsSpan = _keywords.Span;
        for (var i = 0; i < keywordsSpan.Length; i++)
        {
            if (_cancellationTokenSource.Token.IsCancellationRequested)
                return null;

            ReadOnlyMemory<string> words = keywordsSpan[i].words;
            tasks.Add(FindWordsInMessages(words, usingMessages));
        }

        if (_cancellationTokenSource.Token.IsCancellationRequested)
            return null;

        ReadOnlySpan<(bool found, Queue<MessageInfo>? from, DateTime timestamp)>
            taskResults = await Task.WhenAll(tasks);
        keywordsSpan = _keywords.Span;
        Memory<(string, Queue<MessageInfo>?)> keys = new (string, Queue<MessageInfo>?)[taskResults.Length];
        Span<(string, Queue<MessageInfo>?)> keysSpan = keys.Span;
        for (var (i, j) = (0, 0); i < taskResults.Length; i++)
        {
            if (_cancellationTokenSource.Token.IsCancellationRequested)
                return null;

            if (!taskResults[i].found) continue;

            if (taskResults[i].timestamp > _lastFound)
                _lastFound = taskResults[i].timestamp;
            keysSpan[j++] = (keywordsSpan[i].key, taskResults[i].from);
        }

        return keys.Trim([default])!;
    }

    private async Task<(bool, Queue<MessageInfo>?, DateTime)> FindWordsInMessages(ReadOnlyMemory<string> words,
        ReadOnlyMemory<MessageInfo> usingMessages)
    {
        if (words.Length == 0 || usingMessages.Span.Length == 0)
        {
            FileManager.WriteLog($"[WARNING] Something potentially went wrong in {nameof(FindWordInMessages)}:" +
                                 $"{nameof(words)} Length: {{{words.Length}}}, {nameof(usingMessages)} Length: {{{usingMessages.Length}}}")
                .RunSynchronously();
            return (false, null, DateTime.MinValue);
        }

        List<Task<(bool found, Queue<MessageInfo>? from, DateTime timestamp)>> tasks = [];
        ReadOnlySpan<string> wordsSpan = words.Span;
        for (var i = 0; i < wordsSpan.Length; i++)
        {
            var word = wordsSpan[i];
            tasks.Add(Task.Run(() => FindWordInMessages(word, usingMessages), _cancellationTokenSource.Token));
        }

        if (_cancellationTokenSource.Token.IsCancellationRequested)
            return (false, null, DateTime.MinValue);

        (bool found, Queue<MessageInfo>? from, DateTime timestamp)[] taskResults = await Task.WhenAll(tasks);
        return taskResults.Any(result => result.found)
            ? taskResults.First(result => result.found)
            : (false, null, DateTime.MinValue);
    }

    private (bool, Queue<MessageInfo>?, DateTime) FindWordInMessages(string word,
        ReadOnlyMemory<MessageInfo> usingMessages)
    {
        ReadOnlySpan<MessageInfo> messages = usingMessages.Span;
        var index = 0;
        Queue<MessageInfo> from = new();

        foreach (var message in messages)
        {
            if (string.IsNullOrWhiteSpace(message.message))
            {
                index = 0;
                from.Clear();
                continue;
            }

            if (!(_debugOptions.HasFlag(DebugOptions.UseAnyMessage)
                  || _debugOptions.HasFlag(DebugOptions.UseFullMessages))
                && message.message.Length != 1)
            {
                index = 0;
                from.Clear();
                continue;
            }

            if (_debugOptions.HasFlag(DebugOptions.UseFullMessages) &&
                message.message.Equals(word, StringComparison.OrdinalIgnoreCase))
                return (true, new Queue<MessageInfo>([message]), message.timestamp);

            if (char.ToLowerInvariant(message.message[0]).Equals(char.ToLowerInvariant(word[index])))
            {
                index++;
                from.Enqueue(message);

                if (index >= word.Length)
                    return (true, from, message.timestamp);
            }
            else
            {
                index = 0;
                from.Clear();
            }
        }

        return (false, null, DateTime.MinValue);
    }
}