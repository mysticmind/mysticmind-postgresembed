using System;
using System.Runtime.Serialization;

namespace MysticMind.PostgresEmbed;

public class PostgresEmbedException : Exception
{
    public PostgresEmbedException()
    {
    }

    public PostgresEmbedException(string message): base(message)
    {
    }

    public PostgresEmbedException(string message, Exception innerException): base(message, innerException)
    {
    }
}