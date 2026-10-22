using VadesDev.Rag.Models;

namespace VadesDev.Rag.Knowledge;

/// <summary>
/// Dummy knowledge base: returns a few hardcoded chunks instead of reading real documents.
/// </summary>
public class DummyKnowledgeBase
{
    /// <summary>
    /// Returns all chunks of the knowledge base.
    /// </summary>
    public DocumentChunk[] GetChunks()
    {
        return
        [
            new("faq-1", "refund-policy.md", "Our refund window is strictly 30 days from the purchase date."),
            new("faq-2", "support.md",       "Customer support hours are Monday through Friday, 9 AM to 5 PM CET."),
            new("faq-3", "account.md",       "To reset your password click 'Forgot Password' on the login screen and check your email inbox."),
            new("faq-4", "shipping.md",      "Standard shipping takes 3 to 5 business days within the EU."),
            new("faq-5", "security.md",      "We support two-factor authentication using an authenticator app.")
        ];
    }
}