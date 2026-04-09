using System.Diagnostics;
using Chatbot.API.Clients;
using Chatbot.API.Configurations;
using Chatbot.API.Data;
using Chatbot.API.Services;
using Chatbot.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddScoped<IQueryNormalizationService, QueryNormalizationService>();
builder.Services.AddSingleton<IClarificationStateService, ClarificationStateService>();
builder.Services.AddSingleton<IConversationPreferenceService, ConversationPreferenceService>();
// Tool-related services
builder.Services.AddScoped<IToolDispatcher, ToolDispatcher>();
builder.Services.AddScoped<IToolDefinitionProvider, ToolDefinitionProvider>();
builder.Services.AddScoped<IPriceIntentParser, PriceIntentParser>();
builder.Services.AddScoped<IIntentParserService, IntentParserService>();
builder.Services.AddScoped<IProductRecommendationService, ProductRecommendationService>();
builder.Services.AddScoped<ICompareService, CompareService>();
builder.Services.AddScoped<IRecommendationFollowUpService, RecommendationFollowUpService>();
builder.Services.AddScoped<IRefinementService, RefinementService>();
// Main chatbot services
builder.Services.AddScoped<IChatService, ChatService>();

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

app.Run();