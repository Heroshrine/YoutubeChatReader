using System.Text.Json;
using YoutubeChatRead;
using YoutubeChatRead.FileManagement;

int delay;
int maxResults;
string? apiKey;
try
{
    var jsonDocument = await FileManager.LoadSettingsFile();
    delay = jsonDocument.RootElement.GetProperty(FileManager.SETTINGS_DELAY).GetInt32();
    maxResults = jsonDocument.RootElement.GetProperty(FileManager.SETTINGS_MAX_RESULTS).GetInt32();
    apiKey = jsonDocument.RootElement.GetProperty(FileManager.SETTINGS_API_KEY).GetString();
    if (apiKey == null)
    {
        await App.WriteErrorAndLog("No api key specified in settings document. Please check your settings file.");
        App.ExitProgram("", 0);
    }
}
catch (JsonException jsonExcept)
{
    await App.WriteErrorAndLog("JSON document is corrupt, creating a new one. Please restart the program...");
    Console.WriteLine();
    await App.WriteExceptionAndLog(jsonExcept);
    throw;
}

var debugOptions = (from arg in args where arg.StartsWith('-') select arg.Replace("-", "").ToLower()).Aggregate(
    DebugOptions.None, (current, replaced) => current | replaced switch
    {
        "useanymessage" or "a" => DebugOptions.UseAnyMessage,
        "useallcharacters" or "c" => DebugOptions.UseFullMessages,
        "nologging" or "l" => DebugOptions.NoLogging,
        _ => DebugOptions.None
    });

try
{
    var app = new App(delay, maxResults, apiKey, debugOptions);

    if (!FileManager.FilesExist())
    {
        FileManager.CreateTemplateFile();
        Console.Write("\e[0;37mPress any key once you've set up the config file.");
        Console.ReadKey(true);
        Console.WriteLine();
    }

    await FileManager.ListFiles();

    string? input;
    do
    {
        Console.Write("\e[0mPlease load a config file by typing its name: \e[0;94m");
        input = Console.ReadLine();
    } while (string.IsNullOrWhiteSpace(input));

    app.SetKeywords(await FileManager.LoadConfigFile(input));
    await app.GetVideoId();

    await App.WriteLoadSuccess(delay, maxResults);

    await app.Start();
}
catch (Exception e)
{
    try
    {
        await App.WriteExceptionAndLog(e);
    }
    catch (Exception exc)
    {
        App.WriteException(exc);
        throw;
    }
}