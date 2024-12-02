namespace YoutubeChatRead;

[Flags]
public enum DebugOptions : short
{
    None = 0,
    UseAnyMessage = 1,
    UseFullMessages = 2,
    NoLogging = 4,
    All = ~0
}