using System;
using System.Collections.Generic;

namespace MysticMind.PostgresEmbed;

public class PgExtensionConfig
{
    public PgExtensionConfig(string downloadUrl)
    {
        if (string.IsNullOrEmpty(downloadUrl))
        {
            throw new ArgumentException("downloadUrl is required");
        }

        DownloadUrl = downloadUrl;
    }
        
    public string DownloadUrl { get; private set; }

    public List<string> CreateExtensionSqlList { get; } = new List<string>();
}