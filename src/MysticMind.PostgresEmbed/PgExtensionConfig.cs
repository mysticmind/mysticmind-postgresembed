using System;
using System.Collections.Generic;
using System.Linq;

namespace MysticMind.PostgresEmbed
{
    public class PgExtensionConfig
    {
        public PgExtensionConfig(string downloadUrl, IReadOnlyCollection<string> createExtensionSqlList)
        {
            if (string.IsNullOrEmpty(downloadUrl))
            {
                throw new ArgumentException("downloadUrl is required");
            }

            if ((createExtensionSqlList == null) | (createExtensionSqlList != null && !createExtensionSqlList.Any()))
            {
                throw new ArgumentException("At least one create extension statement should be present");
            }

            DownloadUrl = downloadUrl;

            CreateExtensionSqlList.AddRange(createExtensionSqlList);
        }
        
        public string DownloadUrl { get; private set; }

        public List<string> CreateExtensionSqlList { get; } = new List<string>();
    }
}
