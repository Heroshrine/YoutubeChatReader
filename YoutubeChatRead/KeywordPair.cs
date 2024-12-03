using System.Text;

namespace YoutubeChatRead;

public record KeywordPair(string key, ReadOnlyMemory<string> words, string speach)
{
    public readonly string key = key;
    public readonly ReadOnlyMemory<string> words = words;
    public readonly string speach = speach;

    public override string ToString()
    {
        var sb = new StringBuilder($"{{{key}}}:");

        ReadOnlySpan<string> wordsSpan = words.Span;

        for (var i = 0; i < wordsSpan.Length; i++)
        {
            sb.Append($" {wordsSpan[i]}");
            if (i != wordsSpan.Length - 1)
                sb.Append(',');
        }

        return sb.ToString();
    }
}