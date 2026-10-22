using System.Text;
using VadesDev.Rag.Models;

namespace VadesDev.Rag.Prompting;

/// <summary>
/// Builds the grounded prompt: numbered sources from the hits, followed by the question.
/// </summary>
public class GroundedPromptBuilder
{
    /// <summary>
    /// Returns the prompt text: "Context:", one numbered line per hit, then "Question: ...".
    /// </summary>
    public string Build(string question, List<SearchHit> hits)
    {
        var prompt = new StringBuilder().AppendLine("Context:");

        for (int n = 0; n < hits.Count; n++)
        {
            prompt.AppendLine($"[{n + 1}] {hits[n].Chunk.Text} (source: {hits[n].Chunk.Source})");
        }

        prompt.AppendLine().AppendLine($"Question: {question}");

        return prompt.ToString();
    }
}