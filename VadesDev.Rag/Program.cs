using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;
using VadesDev.Rag.Agents;

var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
             ?? throw new InvalidOperationException("Set the GEMINI_API_KEY environment variable.");

var client = new OpenAIClient(
    new ApiKeyCredential(apiKey),
    new OpenAIClientOptions { Endpoint = new Uri("https://generativelanguage.googleapis.com/v1beta/openai/") });

IChatClient chatClient = client.GetChatClient("gemini-2.5-flash").AsIChatClient();
var agent = new RagAgentFactory().Create(chatClient);

var context = "Context:\n[1] Customer support hours are Monday through Friday, 9 AM to 5 PM CET. (source: support.md)\n\n";

foreach (var question in new[] { "What are your support hours?", "Do you sell pizza?" })
{
    var response = await agent.RunAsync($"{context}Question: {question}");
    Console.WriteLine($"? {question}\n  {response.Text}\n");
}