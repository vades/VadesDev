namespace VadesDev.Rag.Cli;

/// <summary>
/// Runs the startup work (create the table, ingest the knowledge folder) and turns the expected failures
/// (AI server not running, database not running, knowledge folder missing) into one readable line
/// instead of a stack trace. Real bugs are not caught.
/// </summary>
public static class StartupGuard
{
    /// <summary>
    /// Runs the startup work. Returns true if it finished, and false after printing one line for an expected failure.
    /// </summary>
    public static async Task<bool> RunAsync(Func<Task> startup)
    {
        try
        {
            await startup();
            return true;
        }
        catch (Exception exception) when (ProviderErrors.IsProviderError(exception))
        {
            return Fail("AI server", ProviderErrors.Describe(exception));
        }
        catch (Exception exception) when (DatabaseErrors.IsDatabaseError(exception))
        {
            return Fail("database", DatabaseErrors.Describe(exception));
        }
        catch (DirectoryNotFoundException exception)
        {
            return Fail("knowledge folder", exception.Message);
        }
    }

    /// <summary>
    /// Prints one failure line and returns false, so the catch blocks stay one line each.
    /// </summary>
    private static bool Fail(string area, string message)
    {
        Console.WriteLine($"! Startup failed ({area}): {message}");
        return false;
    }
}