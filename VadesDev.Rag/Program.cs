using VadesDev.Rag.Documents;

var path = Path.Combine(AppContext.BaseDirectory, "Data", "knowledge.md");
var document = await new MarkdownFileReader().ReadAsync(path);

Console.WriteLine(document.Source);
Console.WriteLine(document.Text);