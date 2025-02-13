using Microsoft.Windows.AI.Generative;

if (!LanguageModel.IsAvailable())
{
    var op = await LanguageModel.MakeAvailableAsync();
}

using LanguageModel languageModel = await LanguageModel.CreateAsync();

string prompt = "Tell me about Japan in 150 words or less.";

Console.WriteLine("Prompt :");
Console.WriteLine(prompt);

var result = await languageModel.GenerateResponseAsync(prompt);

Console.WriteLine();
Console.WriteLine("Response :");
Console.WriteLine(result.Response);