using System.Diagnostics;
using Chatbot.API.Clients;
using Chatbot.API.Configurations;
using Chatbot.API.Data;
using Chatbot.API.Services;
using Chatbot.API.Services.Conversation;
using Chatbot.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
  
var openAiApiKey = SharedEnvLoader.GetValue(
    "OPENAI_API_KEY",
    Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "Chatbot-dev", ".env")));

if (!string.IsNullOrWhiteSpace(openAiApiKey))
{
    builder.Configuration["OpenAI:ApiKey"] = openAiApiKey;
}

var toolApiKey = SharedEnvLoader.GetValue(
    "TOOL_API_KEY",
    Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "Chatbot-dev", ".env")));

if (!string.IsNullOrWhiteSpace(toolApiKey))
{
    builder.Configuration["ToolApi:ApiKey"] = toolApiKey;
}

var telegramBotToken = SharedEnvLoader.GetValue(
    "TELEGRAM_BOT_TOKEN",
    Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "Chatbot-dev", ".env")));

if (!string.IsNullOrWhiteSpace(telegramBotToken))
{
    builder.Configuration["Telegram:BotToken"] = telegramBotToken;
}

var telegramSecretToken = SharedEnvLoader.GetValue(
    "TELEGRAM_SECRET_TOKEN",
    Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "Chatbot-dev", ".env")));

if (!string.IsNullOrWhiteSpace(telegramSecretToken))
{
    builder.Configuration["Telegram:SecretToken"] = telegramSecretToken;
}

var telegramWebhookUrl = SharedEnvLoader.GetValue(
    "TELEGRAM_WEBHOOK_URL",
    Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "Chatbot-dev", ".env")));

if (!string.IsNullOrWhiteSpace(telegramWebhookUrl))
{
    builder.Configuration["Telegram:WebhookUrl"] = telegramWebhookUrl;
}

var telegramPublicWebBaseUrl = SharedEnvLoader.GetValue(
    "TELEGRAM_PUBLIC_WEB_BASE_URL",
    Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "Chatbot-dev", ".env")));

if (!string.IsNullOrWhiteSpace(telegramPublicWebBaseUrl))
{
    builder.Configuration["Telegram:PublicWebBaseUrl"] = telegramPublicWebBaseUrl;
}
// Database
builder.Services.AddDbContext<ChatbotDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("ChatbotConnection")));

// MVC / Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configuration validation
builder.Services.AddOptions<OpenAISettings>()
    .Bind(builder.Configuration.GetSection("OpenAI"))
    .ValidateDataAnnotations()
    .Validate(x => !string.IsNullOrWhiteSpace(x.ApiKey), "OpenAI:ApiKey is required.")
    .Validate(x => !string.IsNullOrWhiteSpace(x.Model), "OpenAI:Model is required.")
    .ValidateOnStart();

builder.Services.AddOptions<ToolApiOptions>()
    .Bind(builder.Configuration.GetSection("ToolApi"))
    .ValidateDataAnnotations()
    .Validate(x => !string.IsNullOrWhiteSpace(x.ApiKey), "ToolApi:ApiKey is required.")
    .Validate(x => !string.IsNullOrWhiteSpace(x.BaseUrl), "ToolApi:BaseUrl is required.")
    .ValidateOnStart();
builder.Services.Configure<TelegramSettings>(
    builder.Configuration.GetSection("Telegram"));
builder.Services.AddOptions<RagApiOptions>()
    .Bind(builder.Configuration.GetSection("RagApi"))
    .ValidateDataAnnotations()
    .Validate(x => !string.IsNullOrWhiteSpace(x.BaseUrl), "RagApi:BaseUrl is required.")
    .ValidateOnStart();
// Memory
builder.Services.AddScoped<IConversationMemoryService, ConversationMemoryService>();
builder.Services.AddScoped<IConversationHistoryService, ConversationHistoryService>();
builder.Services.AddScoped<IConversationStateService, ConversationStateService>();
builder.Services.AddScoped<IQueryNormalizationService, QueryNormalizationService>();
builder.Services.AddSingleton<IClarificationStateService, ClarificationStateService>();
builder.Services.AddSingleton<IConversationPreferenceService, ConversationPreferenceService>();
// Tool-related services
builder.Services.AddScoped<IToolDispatcher, ToolDispatcher>();
builder.Services.AddScoped<IToolDefinitionProvider, ToolDefinitionProvider>();
builder.Services.AddScoped<IPriceIntentParser, PriceIntentParser>();
builder.Services.AddScoped<IIntentParserService, IntentParserService>();
builder.Services.AddScoped<IChatFlowRouter, ChatFlowRouter>();
builder.Services.AddScoped<IProductRecommendationService, ProductRecommendationService>();
builder.Services.AddScoped<ICompareService, CompareService>();
builder.Services.AddScoped<IRecommendationFollowUpService, RecommendationFollowUpService>();
builder.Services.AddScoped<IRefinementService, RefinementService>();
builder.Services.AddScoped<IChatFlowRouter, ChatFlowRouter>();
builder.Services.AddScoped<IProductLookupFlowService, ProductLookupFlowService>();
builder.Services.AddScoped<IProductSearchFlowService, ProductSearchFlowService>();
builder.Services.AddScoped<IInputTextSanitizer, InputTextSanitizer>();
builder.Services.AddScoped<IConversationContextResolver, ConversationContextResolver>();
builder.Services.AddScoped<IConversationPolicyService, ConversationPolicyService>();
builder.Services.AddScoped<IFlowDecisionService, FlowDecisionService>();
builder.Services.AddScoped<ITurnContextBuilder, TurnContextBuilder>();
builder.Services.AddScoped<IUtteranceGuardService, UtteranceGuardService>();
builder.Services.AddScoped<IContextualIntentClassifierService, ContextualIntentClassifierService>();
builder.Services.AddScoped<IRecommendationClarificationService, RecommendationClarificationService>();
builder.Services.AddScoped<IChatFlowOrchestrator, ChatFlowOrchestrator>();
builder.Services.AddScoped<IOrderLookupFlowService, OrderLookupFlowService>();
builder.Services.AddScoped<IRecommendationFlowService, RecommendationFlowService>();
builder.Services.AddScoped<IRecommendationLLMService, RecommendationLLMService>();
builder.Services.AddScoped<IReplyStyleService, ReplyStyleService>();
builder.Services.AddScoped<IReplyRewriteService, ReplyRewriteService>();
builder.Services.AddScoped<IRecommendationScoringService, RecommendationScoringService>();
builder.Services.AddScoped<IServiceInfoFlowService, ServiceInfoFlowService>();

builder.Services.Configure<ReplyRewriteOptions>(
    builder.Configuration.GetSection("ReplyRewrite"));
// Main chatbot services
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddScoped<ILLMIntentUnderstandingService, LLMIntentUnderstandingService>();
builder.Services.AddHttpClient<ISemanticParserService, SemanticParserService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddHttpClient<IOpenAIService, OpenAIService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(45);
});

builder.Services.AddHttpClient<IWebBanXeMayToolClient, WebBanXeMayToolClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddHttpClient<ITelegramService, TelegramService>();
builder.Services.AddHttpClient<IRagService, PythonRagService>((sp, client) =>
{
    var options = sp.GetRequiredService<
        Microsoft.Extensions.Options.IOptions<RagApiOptions>>().Value;

    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});
var app = builder.Build();

// Swagger
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Request timing middleware
app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    var stopwatch = Stopwatch.StartNew();

    try
    {
        await next();
    }
    finally
    {
        stopwatch.Stop();

        logger.LogInformation(
            "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs} ms",
            context.Request.Method,
            context.Request.Path,
            context.Response.StatusCode,
            stopwatch.ElapsedMilliseconds);
    }
});

app.UseHttpsRedirection();
app.UseAuthorization();

app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var telegramSettings = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<TelegramSettings>>().Value;
    var telegramService = scope.ServiceProvider.GetRequiredService<ITelegramService>();

    if (!string.IsNullOrWhiteSpace(telegramSettings.BotToken)
        && !string.IsNullOrWhiteSpace(telegramSettings.SecretToken)
        && !string.IsNullOrWhiteSpace(telegramSettings.WebhookUrl))
    {
        try
        {
            await telegramService.SetWebhookAsync(telegramSettings.WebhookUrl, telegramSettings.SecretToken);
            logger.LogInformation("Telegram webhook registered: {WebhookUrl}", telegramSettings.WebhookUrl);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to register Telegram webhook automatically.");
        }
    }
}

app.Run();

static class SharedEnvLoader
{
    public static string? GetValue(string key, string envFilePath)
    {
        var systemValue = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrWhiteSpace(systemValue))
        {
            return systemValue.Trim();
        }

        if (!File.Exists(envFilePath))
        {
            return null;
        }

        foreach (var rawLine in File.ReadAllLines(envFilePath))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var currentKey = line[..separatorIndex].Trim();
            if (!string.Equals(currentKey, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = line[(separatorIndex + 1)..].Trim().Trim('"');
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }
}
