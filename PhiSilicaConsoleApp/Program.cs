using Microsoft.Windows.AI.Generative;
using Windows.Foundation;

if (!LanguageModel.IsAvailable())
{
    var op = await LanguageModel.MakeAvailableAsync();
}

using LanguageModel languageModel = await LanguageModel.CreateAsync();

//string prompt = "Tell me about Japan in 150 words or less.";
string prompt = "「ファイナルファンタジー7」の主人公の名前とその生い立ちを最大300字以内で教えてください。";

Console.WriteLine("Prompt :");
Console.WriteLine(prompt);

Console.WriteLine();
Console.WriteLine("Response :");

AsyncOperationProgressHandler<LanguageModelResponse, string>
progressHandler = (asyncInfo, delta) =>
{
    Console.Write(delta);
};

var asyncOp = languageModel.GenerateResponseWithProgressAsync(prompt);
asyncOp.Progress = progressHandler;

var result = await asyncOp;
Console.WriteLine();