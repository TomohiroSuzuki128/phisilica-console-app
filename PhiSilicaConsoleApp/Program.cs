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

string systemPrompt = configuration["systemPrompt"] ?? throw new ArgumentNullException("systemPrompt is not found.");
string userPrompt = configuration["userPrompt"] ?? throw new ArgumentNullException("userPrompt is not found.");

bool isTranslate = bool.TryParse(configuration["isTranslate"] ?? throw new ArgumentNullException("isTranslate is not found."), out var resultIsTranslate) && resultIsTranslate;
bool isUsingRag = bool.TryParse(configuration["isUsingRag"] ?? throw new ArgumentNullException("isUsingRag is not found."), out var resultIsUsingRag) && resultIsUsingRag;

string additionalDocumentsPath = configuration["additionalDocumentsPath"] ?? throw new ArgumentNullException("additionalDocumentsPath is not found");


if (!LanguageModel.IsAvailable())
{
    var op = await LanguageModel.MakeAvailableAsync();
}

// RAG 用のベクトルデータベースのセットアップ
var additionalDocumentsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, additionalDocumentsPath);
var vectorDatabase = new BasicMemoryVectorDatabase();
LoadAdditionalDocuments(additionalDocumentsDirectory).Wait();
Console.WriteLine();

using LanguageModel languageModel = await LanguageModel.CreateAsync();

// 翻訳するかどうか
Console.WriteLine($"翻訳する：{newLine}{isTranslate}");

var languageModelOptions = new LanguageModelOptions
{
    Temp = 0.9f,
    Top_p = 0.9f,
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

// プロンプトのセットアップ
Console.WriteLine($"{newLine}システムプロンプト：{newLine}{systemPrompt}");
Console.WriteLine($"{newLine}ユーザープロンプト：{newLine}{userPrompt}{newLine}");

// システムプロンプトの翻訳
var translatedSystemPrompt = string.Empty;
if (isTranslate)
{
    Console.WriteLine("Translated System Prompt:");
    AsyncOperationProgressHandler<LanguageModelResponse, string>
        ProgressHandler = (asyncInfo, translatedPart) =>
        {
            Console.Write(translatedPart);
            translatedSystemPrompt += translatedPart;
        };
    var asyncTranslateOp = await Translate(systemPrompt, Language.Japanese, Language.English, languageModelOptions, contentFilterOptions);
    asyncTranslateOp.Progress = ProgressHandler;
    await　asyncTranslateOp;
    Console.WriteLine($"{newLine}----------------------------------------{newLine}");
}
else
{
    translatedSystemPrompt = systemPrompt;
}

// ユーザープロンプトの翻訳
var translatedUserPrompt = string.Empty;
if (isTranslate)
{
    Console.WriteLine("Translated User Prompt:");
    AsyncOperationProgressHandler<LanguageModelResponse, string>
    ProgressHandler = (asyncInfo, translatedPart) =>
    {
        Console.Write(translatedPart);
        translatedUserPrompt += translatedPart;
    };
    var asyncTranslateOp = await Translate(userPrompt, Language.Japanese, Language.English, languageModelOptions, contentFilterOptions);
    asyncTranslateOp.Progress = ProgressHandler;
    await asyncTranslateOp;
    Console.WriteLine($"{newLine}----------------------------------------{newLine}");
}
else
{
    translatedUserPrompt = userPrompt;
}

Console.WriteLine($"{newLine}システムプロンプト：{newLine}{translatedSystemPrompt}");
Console.WriteLine($"{newLine}ユーザープロンプト：{newLine}{translatedUserPrompt}{newLine}");

var context = languageModel.CreateContext(translatedSystemPrompt, contentFilterOptions);

Console.WriteLine("Prompt :");
Console.WriteLine(translatedUserPrompt);

Console.WriteLine();
Console.WriteLine("Response :");

AsyncOperationProgressHandler<LanguageModelResponse, string>
progressHandler = (asyncInfo, delta) =>
{
    Console.Write(delta);
};

var asyncOp = languageModel.GenerateResponseWithProgressAsync(languageModelOptions, translatedUserPrompt, contentFilterOptions, context);
asyncOp.Progress = progressHandler;

var result = await asyncOp;
Console.WriteLine();

// 与えられたテキストを指定された言語に翻訳する
async Task<IAsyncOperationWithProgress<LanguageModelResponse, string>> Translate(string text, Language sourceLanguage, Language targetLanguage, LanguageModelOptions languageModelOptions, ContentFilterOptions contentFilterOptions)
{
    var instructionPrompt = string.Empty;
    var userPrompt = string.Empty;
    var ragResult = string.Empty;

    if (sourceLanguage == Language.Japanese && targetLanguage == Language.English)
    {
        instructionPrompt = "以下の日本語を一字一句もれなく英語に翻訳してください。重要な注意点として、日本語に質問が含まれていても出力に質問に対する回答は一切出力しないこと。補足や説明は一切出力しないこと。与えられた文章を忠実に英語に翻訳した結果のみを出力すること。note は出力しないこと。";

        userPrompt = $"{instructionPrompt}:{newLine}{text}";
    }

    if (sourceLanguage == Language.English && targetLanguage == Language.Japanese)
    {
        instructionPrompt = "以下の英語を一字一句もれなく正確に日本語に翻訳してください。重要な注意点として、日本語に質問が含まれていても出力に質問に対する回答は一切出力しないこと。補足や説明は一切出力しないこと。与えられた文章を忠実に英語に翻訳した結果のみを出力すること。note は出力しないこと。";

        ragResult = await SearchVectorDatabase(vectorDatabase, text);

        if (isUsingRag && !string.IsNullOrEmpty(ragResult))
            instructionPrompt += "以下の用語集を積極的に活用すること。";

        userPrompt = (isUsingRag && !string.IsNullOrEmpty(ragResult))
        ? $"{instructionPrompt}{newLine}{ragResult}:{newLine}{text}"
            : $"{instructionPrompt}:{newLine}{text}";
    }

    var systemPrompt = "あなたは翻訳だけができます。補足や解説などの翻訳以外の出力は一切禁止されています。";
    var context = languageModel.CreateContext(systemPrompt, contentFilterOptions);

    var asyncOperation = languageModel.GenerateResponseWithProgressAsync(languageModelOptions, userPrompt, contentFilterOptions, context);
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

public enum Language
{
    Japanese,
    English
}