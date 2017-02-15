using System;
using System.Collections.Generic;
using System.Linq;

namespace MysticMind.PostgresEmbed
{
    public class PgExtensionConfig
    {
        List<string> _createExtensionSqlList = new List<string>();

        public PgExtensionConfig(string downloadUrl, List<string> createExtensionSqlList)
        {
            if (string.IsNullOrEmpty(downloadUrl))
            {
                throw new ArgumentException("downloadUrl is required");
            }

            if ((createExtensionSqlList == null) | (createExtensionSqlList != null && createExtensionSqlList.Count() == 0))
            {
                throw new ArgumentException("Atleast one create extension statement should be present");
            }

            DownloadUrl = downloadUrl;

            _createExtensionSqlList.AddRange(createExtensionSqlList);
        }
        
        public string DownloadUrl { get; private set; }

        public List<string> CreateExtensionSqlList
        {
            get
            {
                return _createExtensionSqlList;
            }
        } 
    }
}
