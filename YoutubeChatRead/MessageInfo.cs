namespace YoutubeChatRead;

internal record MessageInfo(
    string? author,
    string? message,
    MessageType messageType,
    DateTime timestamp)
{
    public readonly string? author = author;
    public readonly string? message = message;
    public readonly DateTime timestamp = timestamp;
    public readonly MessageType messageType = messageType;

    public override string ToString() => $"{author}: {message}";
}