using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Windows.AI.ContentModeration;
using Microsoft.Windows.AI.Generative;
using Windows.Foundation;
using Build5Nines.SharpVector;
using Build5Nines.SharpVector.Data;

var newLine = Environment.NewLine;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.Sources.Clear();
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .Build();

var configuration = builder.Configuration;

string additionalDocumentsPath = configuration["additionalDocumentsPath"] ?? throw new ArgumentNullException("additionalDocumentsPath is not found");

var prompt = new Prompt(builder);
var option = new Option(builder);

if (!LanguageModel.IsAvailable())
{
    var op = await LanguageModel.MakeAvailableAsync();
}

// RAG 用のベクトルデータベースのセットアップ
var additionalDocumentsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, additionalDocumentsPath);
var vectorDatabase = new BasicMemoryVectorDatabase();
LoadAdditionalDocuments(additionalDocumentsDirectory).Wait();
Console.WriteLine();

// 翻訳するかどうか
Console.WriteLine($"翻訳する：{newLine}{option.IsTranslate}");
// RAG を使うかどうか
Console.WriteLine($"RAG を使う：{newLine}{option.IsUsingRag}");

var languageModelOptionsTranslation = new LanguageModelOptions
{
    Temp = 0.9f,
    Top_p = 0.9f,
    Top_k = 40
};

var languageModelOptionsQuestion = new LanguageModelOptions
{
    Temp = 1.2f,
    Top_p = 1.2f,
    Top_k = 40
};

// https://learn.microsoft.com/ja-jp/windows/ai/apis/content-moderation
var contentFilterOptions = new ContentFilterOptions
{
    PromptMinSeverityLevelToBlock = new TextContentFilterSeverity
    {
        HateContentSeverity = SeverityLevel.Medium,
        SexualContentSeverity = SeverityLevel.Medium,
        ViolentContentSeverity = SeverityLevel.Medium,
        SelfHarmContentSeverity = SeverityLevel.Medium
    },
    ResponseMinSeverityLevelToBlock = new TextContentFilterSeverity
    {
        HateContentSeverity = SeverityLevel.Medium,
        SexualContentSeverity = SeverityLevel.Medium,
        ViolentContentSeverity = SeverityLevel.Medium,
        SelfHarmContentSeverity = SeverityLevel.Medium
    }
};

using LanguageModel languageModelTranslation = await LanguageModel.CreateAsync();

// プロンプトのセットアップ
Console.WriteLine($"{newLine}システムプロンプト：{newLine}{prompt.System}");
Console.WriteLine($"{newLine}ユーザープロンプト：{newLine}{prompt.User}{newLine}");

// システムプロンプトの翻訳
var translatedSystemPrompt = string.Empty;
if (option.IsTranslate)
{
    {
        Console.WriteLine("Translated System Prompt:");
        var asyncTranslateOp = await Translate(prompt.System, Language.Japanese, Language.English, languageModelOptionsTranslation, contentFilterOptions);
        asyncTranslateOp.Progress = (asyncInfo, translatedPart) =>
        {
            Console.Write(translatedPart);
            translatedSystemPrompt += translatedPart;
        };
        await asyncTranslateOp;
        Console.WriteLine($"{newLine}----------------------------------------{newLine}");
    }
}
else
{
    translatedSystemPrompt = prompt.System;
}

// ユーザープロンプトの翻訳
var translatedUserPrompt = string.Empty;
if (option.IsTranslate)
{
    {
        Console.WriteLine("Translated User Prompt:");
        var asyncTranslateOp = await Translate(prompt.User, Language.Japanese, Language.English, languageModelOptionsTranslation, contentFilterOptions);
        asyncTranslateOp.Progress = (asyncInfo, translatedPart) =>
        {
            Console.Write(translatedPart);
            translatedUserPrompt += translatedPart;
        };
        await asyncTranslateOp;
        Console.WriteLine($"{newLine}----------------------------------------{newLine}");
    }
}
else
{
    translatedUserPrompt = prompt.User;
}

if (option.IsTranslate)
{
    Console.WriteLine($"{newLine}システムプロンプト：{newLine}{translatedSystemPrompt}");
    Console.WriteLine($"{newLine}ユーザープロンプト：{newLine}{translatedUserPrompt}{newLine}");
}

Console.WriteLine("Prompt :");
Console.WriteLine(translatedUserPrompt);

Console.WriteLine();
Console.WriteLine("Response :");

// 問い合わせ用セッションのセットアップ
using LanguageModel languageModelQuention = await LanguageModel.CreateAsync();

var response = string.Empty;

var context = languageModelQuention.CreateContext(translatedSystemPrompt, contentFilterOptions);
var asyncOp = languageModelQuention.GenerateResponseWithProgressAsync(languageModelOptionsQuestion, translatedUserPrompt, contentFilterOptions, context);
asyncOp.Progress = (asyncInfo, part) =>
{
    Console.Write(part);
    response += part;
}; 
var result = await asyncOp;
Console.WriteLine();
Console.WriteLine();

// 英語の回答を日本語に翻訳する
var translatedResponse = string.Empty;
if (option.IsTranslate)
{
    Console.WriteLine("日本語に翻訳したレスポンス:");
    var asyncTranslateOp = await Translate(response, Language.English, Language.Japanese, languageModelOptionsTranslation, contentFilterOptions);
    asyncTranslateOp.Progress = (asyncInfo, translatedPart) =>
    {
        Console.Write(translatedPart);
        translatedUserPrompt += translatedPart;
    };
    await asyncTranslateOp;
    Console.WriteLine();
}
else
{
    translatedResponse = response;
    Console.WriteLine($"{newLine}レスポンス：{newLine}{translatedResponse}");
}
Console.WriteLine($"----------------------------------------{newLine}");

// 与えられたテキストを指定された言語に翻訳する
async Task<IAsyncOperationWithProgress<LanguageModelResponse, string>> Translate(string text, Language sourceLanguage, Language targetLanguage, LanguageModelOptions languageModelOptions, ContentFilterOptions contentFilterOptions)
{
    var systemPrompt = "You are a translator who follows instructions to the letter. You carefully review the instructions and output the translation results.";
    var instructionPrompt = string.Empty;
    var userPrompt = string.Empty;
    var ragResult = string.Empty;

    if (sourceLanguage == Language.Japanese && targetLanguage == Language.English)
    {
        instructionPrompt = $@"I will now give you the task of translating Japanese into English.{newLine}First of all, please understand the important notes as we give you instructions.{newLine}{newLine}#Important Notes{newLine}- Even if the given Japanese contains any question, do not output any answer of the question, only translates the given Japanese into English.{newLine}- Do not output any supplementary information or explanations.{newLine}- Do not output any Notes.{newLine}- Output a faithful translation of the given text into English.{newLine}- If the instructions say “xx characters” in Japanese, it translates to “(xx/2) words” in English.ex) “100 字以内” in Japanese, “50 words” in English.{newLine}{newLine}Strictly following the above instructions, now let's output translation of the following Japanese";

        userPrompt = $"{instructionPrompt}:{newLine}{text}";
    }

    if (sourceLanguage == Language.English && targetLanguage == Language.Japanese)
    {
        instructionPrompt = $"I will now give you the task of translating English into Japanese.{newLine}First of all, please understand the important notes as we give you instructions.{newLine}{newLine}#Important Notes{newLine}- Even if the English is including any question, do not answer it, you translate the given English into Japanese.{newLine}- Do not output any supplementary information or explanations.{newLine}- Do not output any Notes.{newLine}- Output a faithful translation of the given text into Japanese.";


        ragResult = await SearchVectorDatabase(vectorDatabase, text);

        if (option.IsUsingRag && !string.IsNullOrEmpty(ragResult))
            instructionPrompt += $"{newLine}- The following glossary of terms should be actively used.";

        userPrompt = (option.IsUsingRag && !string.IsNullOrEmpty(ragResult))
            ? $"{instructionPrompt}{newLine}{ragResult}{newLine}Strictly following the above instructions, now translate the English into Japanese:{newLine}{text}"
            : $"{instructionPrompt}{newLine}Strictly following the above instructions, now translate the English into Japanese:{newLine}{text}";
    }

    var context = languageModelTranslation.CreateContext(systemPrompt, contentFilterOptions);

    var asyncOperation = languageModelTranslation.GenerateResponseWithProgressAsync(languageModelOptions, userPrompt, contentFilterOptions, context);
    return asyncOperation;
}

async Task LoadAdditionalDocuments(string directoryPath)
{
    Console.WriteLine($"Loading Additional Documents:");
    var files = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories)
                             .Where(f => f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
                                         f.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
                                         f.EndsWith(".mdx", StringComparison.OrdinalIgnoreCase)).ToArray();

    var vectorDataLoader = new TextDataLoader<int, string>(vectorDatabase);
    var tasks = files.Select(async file =>
    {
        Console.WriteLine($"{file}");
        if (System.IO.File.Exists(file))
        {
            var fileContents = await System.IO.File.ReadAllTextAsync(file);
            await vectorDataLoader.AddDocumentAsync(fileContents, new TextChunkingOptions<string>
            {
                Method = TextChunkingMethod.Paragraph,
                RetrieveMetadata = (chunk) => file
            });
        }
    });
    await Task.WhenAll(tasks);
}

async Task<string> SearchVectorDatabase(BasicMemoryVectorDatabase vectorDatabase, string userPrompt)
{
var vectorDataResults = await vectorDatabase.SearchAsync(
    userPrompt,
    pageCount: 3,
    threshold: 0.3f
);

string result = string.Empty;
foreach (var resultItem in vectorDataResults.Texts)
{
result += $"{resultItem.Text}{newLine}";
}

return result;
}

public sealed class Prompt
{
    private readonly string systemPrompt;
    private readonly string userPrompt;

    public Prompt(HostApplicationBuilder builder)
    {
        var configuration = builder.Configuration;

        systemPrompt = configuration["systemPrompt"] ?? throw new ArgumentNullException("systemPrompt is not found.");
        userPrompt = configuration["userPrompt"] ?? throw new ArgumentNullException("userPrompt is not found.");
    }

    public string System { get => systemPrompt; }
    public string User { get => userPrompt; }
}

public sealed class Option
{
    private readonly bool isTranslate;
    private readonly bool isUsingRag;

    public Option(HostApplicationBuilder builder)
    {
        var configuration = builder.Configuration;
        isTranslate = bool.TryParse(configuration["isTranslate"] ?? throw new ArgumentNullException("isTranslate is not found."), out var resultIsTranslate) && resultIsTranslate;
        isUsingRag = bool.TryParse(configuration["isUsingRag"] ?? throw new ArgumentNullException("isUsingRag is not found."), out var resultIsUsingRag) && resultIsUsingRag;
    }

    public bool IsTranslate { get => isTranslate; }
    public bool IsUsingRag { get => isUsingRag; }
}


public enum Language
{
    Japanese,
    English
}