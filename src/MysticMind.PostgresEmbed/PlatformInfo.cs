using System.Runtime.InteropServices;
using OsArchitecture = System.Runtime.InteropServices.Architecture;

namespace MysticMind.PostgresEmbed;

internal static class PlatformInfo
{
    public static Platform DetectPlatform()
    {
        if (OperatingSystem.IsWindows()) return Platform.Windows;
        if (OperatingSystem.IsMacOS()) return Platform.Darwin;
        if (OperatingSystem.IsLinux()) return IsMusl() ? Platform.AlpineLinux : Platform.Linux;

        throw new UnsupportedPlatformException($"Unsupported OS platform: {RuntimeInformation.OSDescription}");
    }

    public static Architecture DetectArchitecture(Platform platform)
    {
        // zonky only publishes x64 Windows binaries; Windows on Arm runs them under emulation.
        if (platform == Platform.Windows) return Architecture.Amd64;

        return RuntimeInformation.OSArchitecture switch
        {
            OsArchitecture.X64 => Architecture.Amd64,
            OsArchitecture.Arm64 => Architecture.Arm64V8,
            var other => throw new UnsupportedPlatformException($"Unsupported CPU architecture: {other}")
        };
    }

    /// <summary>Maven artifact suffix, for example <c>darwin-arm64v8</c> or <c>linux-amd64-alpine</c>.</summary>
    public static string ArtifactSuffix(Platform platform, Architecture architecture)
    {
        var arch = architecture switch
        {
            Architecture.Amd64 => "amd64",
            Architecture.Arm64V8 => "arm64v8",
            _ => throw new UnsupportedPlatformException($"Unsupported CPU architecture: {architecture}")
        };

        return platform switch
        {
            Platform.Windows => $"windows-{arch}",
            Platform.Linux => $"linux-{arch}",
            Platform.Darwin => $"darwin-{arch}",
            Platform.AlpineLinux => $"linux-{arch}-alpine",
            _ => throw new UnsupportedPlatformException($"Unsupported OS platform: {platform}")
        };
    }

    private static bool IsMusl() =>
        RuntimeInformation.RuntimeIdentifier.Contains("musl", StringComparison.OrdinalIgnoreCase)
        || File.Exists("/etc/alpine-release");
}
