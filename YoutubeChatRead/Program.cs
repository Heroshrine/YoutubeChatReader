﻿using System.Runtime.InteropServices;
using System.Text.Json;
using YoutubeChatRead;
using YoutubeChatRead.FileManagement;

var delay = int.MaxValue;
var maxResults = 0;
string? apiKey = null;
var ttpWpm = 200;
try
{
    var jsonDocument = await FileManager.LoadSettingsFile();
    delay = jsonDocument.RootElement.GetProperty(FileManager.SETTINGS_DELAY).GetInt32();
    maxResults = jsonDocument.RootElement.GetProperty(FileManager.SETTINGS_MAX_RESULTS).GetInt32();
    apiKey = jsonDocument.RootElement.GetProperty(FileManager.SETTINGS_API_KEY).GetString();
    ttpWpm = jsonDocument.RootElement.GetProperty(FileManager.TTS_READ_SPEED).GetInt32();
    if (apiKey == null)
    {
        await App.WriteErrorAndLog("No api key specified in settings document. Please check your settings file.");
        App.ExitProgram("", 0);
    }
}
catch (JsonException jsonExcept)
{
    await App.WriteErrorAndLog("JSON document is corrupt, creating a new one. Please restart the program...");
    await FileManager.ResetSettings();
    await App.WriteExceptionAndLog(jsonExcept);
    App.ExitProgram("", -1);
}
catch (KeyNotFoundException keyNotFoundExcept)
{
    await App.WriteErrorAndLog("JSON document is corrupt, creating a new one. Please restart the program...");
    await FileManager.ResetSettings();
    await App.WriteExceptionAndLog(keyNotFoundExcept);
    App.ExitProgram("", -1);
}

var debugOptions = DebugOptions.None;
var pythonPathExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"YoutubeChatRead\Python\python.exe");
var pythonPathMain = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"YoutubeChatRead\Python\main.py");
foreach (var arg in args)
{
    if (arg.StartsWith('-'))
    {
        var replaced = arg.Replace("-", "").ToLower();

        if (replaced.Contains("ppath=") || replaced.Contains("p="))
        {
            replaced = replaced.Replace("ppath=", "");
            replaced = replaced.Replace("p=", "");

            pythonPathExe = Path.Combine(replaced, "python.exe");
            pythonPathMain = Path.Combine(replaced, "main.py");

            continue;
        }

        debugOptions |= replaced switch
        {
            "useanymessage" or "a" => DebugOptions.UseAnyMessage,
            "useallcharacters" or "c" => DebugOptions.UseFullMessages,
            "nologging" or "l" => DebugOptions.NoLogging,
            "nopython" or "n" => DebugOptions.NoPython,
            _ => DebugOptions.None
        };
    }
}

try
{
    var app = new App(delay, maxResults, apiKey, debugOptions, pythonPathExe, pythonPathMain, ttpWpm);

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
catch (ExternalException e)
{
    await App.WriteWarningAndLog(
        $"Encountered error starting pythong script.{Environment.NewLine}\tEnsure that python was added to PATH when installing.{Environment.NewLine}\tEnsure that pyttsx3 is installed with <pip install pyttsx3>.{Environment.NewLine}\tEnsure that pyautogui is installed with <pip install pyautogui>.");
    try
    {
        await App.WriteExceptionAndLog(e);
    }
    catch (Exception exc)
    {
        App.WriteException(exc);
        App.ExitProgram("", -1);
    }

    App.ExitProgram("", -1);
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
        App.ExitProgram("", -1);
    }

    App.ExitProgram("", -1);
}