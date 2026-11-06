namespace MysticMind.PostgresEmbed;

public enum Platform
{
    Windows,
    Linux,
    Darwin,
    /// <summary>musl based Linux distributions such as Alpine.</summary>
    AlpineLinux
}
