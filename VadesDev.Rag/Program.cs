using Microsoft.Extensions.AI;
using VadesDev.Rag.Configuration;
using VadesDev.Rag.Providers;

var configuration = AppConfiguration.Build();
var provider = new AiProviderFactory(configuration).Create();

Console.WriteLine($"{provider.Name} MinScore={provider.MinScore}");

var vector = await provider.EmbeddingGenerator.GenerateVectorAsync("What are your support hours?");
Console.WriteLine(vector.Length);