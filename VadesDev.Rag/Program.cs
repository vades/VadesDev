using VadesDev.Rag.Configuration;
using VadesDev.Rag.Providers;

var provider = new AiProviderFactory(AppConfiguration.Build()).Create();

Console.WriteLine($"{provider.Name} model={provider.EmbeddingModel} MinScore={provider.MinScore}");