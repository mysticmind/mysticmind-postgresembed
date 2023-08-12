using System;
using System.Runtime.Serialization;

namespace MysticMind.PostgresEmbed;

public class PostgresEmbedException : Exception
{
    public PostgresEmbedException()
    {
    }

    protected PostgresEmbedException(SerializationInfo info, StreamingContext context): base(info, context)
    {
    }

    public PostgresEmbedException(string message): base(message)
    {
    }

    public PostgresEmbedException(string message, Exception innerException): base(message, innerException)
    {
    }
}