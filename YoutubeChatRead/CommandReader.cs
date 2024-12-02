using Parsing;
using YoutubeChatRead.FileManagement;

namespace YoutubeChatRead;

//TODO: should use DI smh
internal static class CommandReader
{
    public static string HelpString =>
        $"{Environment.NewLine}\e[0;92mconfig\e[0;37m"
        + $"\tcreate - creates and opens a template config file."
        + $"{Environment.NewLine}\tload \e[0;90m<\e[0;95mrelative path\e[0;90m>\e[0;37m - loads a config file at the relative path provided."
        + $"{Environment.NewLine}\tdirectory list - lists files in the directory."
        + $"{Environment.NewLine}\tdirectory open - opens the the directory."
        + Environment.NewLine
        + $"{Environment.NewLine}\e[0;92mquota\e[0;37m"
        + "\tusage - shows current quota usage (for current video ID)."
        + Environment.NewLine
        + $"{Environment.NewLine}\e[0;92myoutube\e[0;37m"
        + "\tvideo read - prints the currently used video ID."
        + Environment.NewLine + Environment.NewLine
        + "\e[0;37mExample: \e[0;92mconfig\e[0;37m load \e[0;95mTheBestConfig.txt"
        + Environment.NewLine;

    public static async Task DoAction(string? fullCommand, App app)
    {
        if (string.IsNullOrEmpty(fullCommand))
            return;

        var splitCommands = fullCommand.QuickSplit(' ').ToArray();

        if (splitCommands.Length < 1)
            return;
        if (splitCommands is [not ("help" or "?")])
        {
            App.WriteResponse($"Unknown command: {fullCommand}");
            return;
        }

        switch (splitCommands[0].ToLowerInvariant())
        {
            case "help" or "?":
                Console.WriteLine(HelpString);
                break;
            case "config" or "c":
                await GetConfigAction(splitCommands[1..], app);
                break;
            case "quota" or "q":
                GetQuotaAction(splitCommands[1..], app);
                break;
            case "youtube" or "t":
                GetYoutubeAction(splitCommands[1..], app);
                break;
            default:
                App.WriteResponse($"Unknown command: {fullCommand}");
                return;
        }
    }

    private static async Task GetConfigAction(string[] commands, App app)
    {
        switch (commands[0].ToLowerInvariant())
        {
            case "create" or "c":
                FileManager.CreateTemplateFile();
                break;
            case "load" or "l":
                if (commands.Length == 1)
                {
                    App.WriteResponse("Command needs a parameter.");
                    break;
                }

                app.SetKeywords(await FileManager.LoadConfigFile(commands[1]));
                break;
            case "directory" or "d":
                if (commands.Length == 1)
                {
                    App.WriteResponse($"Unknown command: {commands[0]}");
                    break;
                }

                await GetConfigDirectoryAction(commands[1..]);
                break;
            default:
                App.WriteResponse($"Unknown command: {commands[0]}");
                break;
        }
    }

    private static async Task GetConfigDirectoryAction(string[] commands)
    {
        switch (commands[0])
        {
            case "list" or "l":
                Console.WriteLine();
                await FileManager.ListFiles();
                break;
            case "open" or "o":
                FileManager.OpenDirectory();
                break;
        }
    }

    private static void GetQuotaAction(string[] commands, App app)
    {
        if (commands[0].Equals("usage", StringComparison.InvariantCultureIgnoreCase))
        {
            app.PrintUsage();
        }
        else
            App.WriteResponse($"Unknown command: {commands[0]}");
    }

    private static void GetYoutubeAction(string[] commands, App app)
    {
        if (!commands[0].Equals("video", StringComparison.InvariantCultureIgnoreCase) || commands.Length == 1)
        {
            App.WriteResponse($"Unknown command: {commands[0]}");
        }

        switch (commands[1].ToLowerInvariant())
        {
            case "read":
                app.PrintVideoId();
                break;
            default:
                App.WriteResponse($"Unknown command: {commands[1]}");
                break;
        }
    }
}