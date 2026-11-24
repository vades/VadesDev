using System.Net.Sockets;
using Npgsql;

namespace VadesDev.Rag.Cli;

/// <summary>
/// Recognizes errors that mean "the database is not usable" (not running, wrong login, wrong database name,
/// still starting up) and describes each one in a single readable line.
/// SQL mistakes are not recognized on purpose, so real bugs still crash with a stack trace.
/// </summary>
public static class DatabaseErrors
{
    private const string WrongLogin = "28P01";        // invalid password
    private const string LoginRejected = "28000";     // invalid authorization
    private const string UnknownDatabase = "3D000";   // database does not exist
    private const string StartingUp = "57P03";        // cannot connect now

    /// <summary>
    /// True for connection-level problems: no connection, timeout, rejected login, unknown database, server starting up.
    /// </summary>
    public static bool IsDatabaseError(Exception exception)
    {
        return exception switch
        {
            PostgresException e => e.SqlState is WrongLogin or LoginRejected or UnknownDatabase or StartingUp,
            NpgsqlException => true,
            SocketException => true,
            _ => false
        };
    }

    /// <summary>
    /// Returns one short message for a database error, with a hint what to check. Never contains the connection string.
    /// </summary>
    public static string Describe(Exception exception)
    {
        return exception switch
        {
            PostgresException { SqlState: WrongLogin or LoginRejected } =>
                "Database login failed. Check the user and password in ConnectionStrings:Rag. " +
                "The password is stored in the Docker volume at the first start, so changing POSTGRES_PASSWORD in .env later has no effect.",
            PostgresException { SqlState: UnknownDatabase } =>
                "The database does not exist. Check Database in ConnectionStrings:Rag and POSTGRES_DB in .env. " +
                "The init script only runs on the first start with an empty volume.",
            PostgresException { SqlState: StartingUp } =>
                "The database is still starting up. Wait until 'docker compose ps' shows healthy, then try again.",
            NpgsqlException { InnerException: TimeoutException } =>
                "The database did not answer in time. Check that the container is healthy (docker compose ps) and that Host and Port are correct.",
            _ =>
                "Cannot reach the database. Check that Docker Desktop is running, that the container is healthy (docker compose ps), " +
                "and that Host and Port in ConnectionStrings:Rag are correct."
        };
    }
}