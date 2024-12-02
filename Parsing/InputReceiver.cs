namespace Parsing;

public class InputReceiver
{
    public static InputReceiver Instance => s_instance ??= new InputReceiver();
    private static InputReceiver? s_instance;
    private InputReceiver() { }

    public async Task<string?> Start()
    {
        PrintIndicator();
        return await Task.Run(GetInput);
    }

//TODO: make input fancier
    private string? GetInput()
    {
        return Console.ReadLine();
    }

    public static void PrintIndicator() => Console.Write("\e[0;37m>\e[0m ");
}