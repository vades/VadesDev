using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using OpenAI;
using VadesDev.Rag.Agents;
using VadesDev.Rag.Configuration;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json")
    .AddJsonFile("appsettings.Local.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var settings = configuration.GetSection("Gemini").Get<GeminiSettings>()
               ?? throw new InvalidOperationException("Section 'Gemini' is missing in appsettings.json.");

var apiKey = configuration["Gemini:ApiKey"];

if (string.IsNullOrWhiteSpace(apiKey))
{
    throw new InvalidOperationException("Set Gemini:ApiKey in appsettings.Local.json or the Gemini__ApiKey environment variable.");
}

Console.WriteLine(settings);

var client = new OpenAIClient(
    new ApiKeyCredential(apiKey),
    new OpenAIClientOptions { Endpoint = new Uri(settings.Endpoint) });

IChatClient chatClient = client.GetChatClient(settings.ChatModel).AsIChatClient();
var agent = new RagAgentFactory().Create(chatClient);

var context = "Context:\n[1] Customer support hours are Monday through Friday, 9 AM to 5 PM CET. (source: support.md)\n\n";

foreach (var question in new[] { "What are your support hours?", "Do you sell pizza?" })
{
    var response = await agent.RunAsync($"{context}Question: {question}");
    Console.WriteLine($"? {question}\n  {response.Text}\n");
}