using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace ZMSL.App.Services;

/// <summary>
/// 镜像源管理服务
/// </summary>
public class MirrorSourceService
{
    public enum MirrorSource
    {
        MSL,
        ZSync
    }

    public class MirrorSourceConfig
    {
        public string Name { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string BaseUrl { get; set; } = "";
        public string Description { get; set; } = "";
    }

    private static readonly Dictionary<MirrorSource, MirrorSourceConfig> Sources = new()
    {
        {
            MirrorSource.MSL,
            new MirrorSourceConfig
            {
                Name = "MSL",
                DisplayName = "MSL 官方源",
                BaseUrl = "https://api.mslmc.cn/v3/",
                Description = "MSL 官方下载源，稳定可靠"
            }
        },
        {
            MirrorSource.ZSync,
            new MirrorSourceConfig
            {
                Name = "ZSync",
                DisplayName = "ZSync 镜像源",
                BaseUrl = "https://sync.v1.zhsdev.top/api/",
                Description = "ZSync 高速镜像源"
            }
        }
    };

    public static MirrorSourceConfig GetSourceConfig(string sourceName)
    {
        if (Enum.TryParse<MirrorSource>(sourceName, true, out var source))
        {
            return Sources[source];
        }
        return Sources[MirrorSource.MSL];
    }

    public static List<MirrorSourceConfig> GetAllSources()
    {
        return Sources.Values.ToList();
    }

    public static string GetSourceDisplayName(string sourceName)
    {
        return GetSourceConfig(sourceName).DisplayName;
    }
}
