namespace YoutubeChatRead;

internal record FetchedMessages(
    ReadOnlyMemory<MessageInfo> allMessages,
    ReadOnlyMemory<MessageInfo> newMessages,
    ReadOnlyMemory<MessageInfo> oldMessages)
{
    public readonly ReadOnlyMemory<MessageInfo> allMessages = allMessages;
    public readonly ReadOnlyMemory<MessageInfo> oldMessages = oldMessages;
    public readonly ReadOnlyMemory<MessageInfo> newMessages = newMessages;
}