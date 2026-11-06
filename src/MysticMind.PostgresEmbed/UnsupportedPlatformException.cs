namespace MysticMind.PostgresEmbed;

public class UnsupportedPlatformException : PostgresEmbedException
{
    public UnsupportedPlatformException() : base("Unsupported OS platform")
    {
    }

    public UnsupportedPlatformException(string message) : base(message)
    {
    }
}
