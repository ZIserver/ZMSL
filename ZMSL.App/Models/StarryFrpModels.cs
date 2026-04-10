using System.Text.Json.Serialization;

namespace ZMSL.App.Models;

public class StarryFrpApiResponse<T>
{
    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("msg")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public T? Data { get; set; }

    [JsonIgnore]
    public bool IsSuccess => Status == 200;
}

public class StarryFrpUserInfo
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("verified")]
    public bool Verified { get; set; }

    [JsonPropertyName("traffic")]
    public StarryFrpTrafficInfo Traffic { get; set; } = new();

    [JsonPropertyName("proxies")]
    public int Proxies { get; set; }

    [JsonPropertyName("bandwidth")]
    public int Bandwidth { get; set; }

    [JsonPropertyName("balance")]
    public decimal Balance { get; set; }

    [JsonPropertyName("group")]
    public StarryFrpGroupInfo Group { get; set; } = new();

    [JsonPropertyName("regtime")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long RegTime { get; set; }

    [JsonPropertyName("avatar")]
    public string Avatar { get; set; } = string.Empty;
}

public class StarryFrpTrafficInfo
{
    [JsonPropertyName("total")]
    public long Total { get; set; }

    [JsonPropertyName("today_used")]
    public long TodayUsed { get; set; }
}

public class StarryFrpGroupInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("expiration")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long Expiration { get; set; }
}

public class StarryFrpNode
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("domain")]
    public string? Domain { get; set; }

    [JsonPropertyName("ip")]
    public string? Ip { get; set; }

    [JsonPropertyName("c")]
    public string IsChinaNode { get; set; } = "false";

    [JsonPropertyName("can_build_site")]
    public bool CanBuildSite { get; set; }

    [JsonPropertyName("load_status")]
    public string LoadStatus { get; set; } = string.Empty;

    public string Region => IsChinaNode == "true" ? "国内" : "国外";
    public string Bandwidth => LoadStatus;
    public bool IsOnline => !string.Equals(LoadStatus, "离线", StringComparison.Ordinal);
    public bool IsDisabled => false;
    public string AllowPort => "动态分配";
    public string AllowType => "tcp/udp/http/https/stcp";
    public int LoadPercent => LoadStatus switch
    {
        "正常" => 25,
        "拥挤" => 70,
        "满载" => 100,
        _ => 0
    };
    public string DisplayName => string.IsNullOrWhiteSpace(LoadStatus) ? Name : $"{Name} · {LoadStatus}";
}

public class StarryFrpTunnelListResponse
{
    [JsonPropertyName("online_tunnel_count")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int OnlineTunnelCount { get; set; }

    [JsonPropertyName("data")]
    public List<StarryFrpProxy> Tunnels { get; set; } = new();
}

public class StarryFrpProxy
{
    [JsonPropertyName("id")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int Id { get; set; }

    [JsonPropertyName("proxy_name")]
    public string ProxyName { get; set; } = string.Empty;

    [JsonPropertyName("remark")]
    public string Remark { get; set; } = string.Empty;

    [JsonPropertyName("proxy_type")]
    public string ProxyType { get; set; } = string.Empty;

    [JsonPropertyName("local_ip")]
    public string LocalIp { get; set; } = "127.0.0.1";

    [JsonPropertyName("local_port")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int LocalPort { get; set; }

    [JsonPropertyName("remote_port")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int? RemotePort { get; set; }

    [JsonPropertyName("domain")]
    public string Domain { get; set; } = string.Empty;

    [JsonPropertyName("use_encryption")]
    public string UseEncryption { get; set; } = "false";

    [JsonPropertyName("use_compression")]
    public string UseCompression { get; set; } = "false";

    [JsonPropertyName("locations")]
    public string Locations { get; set; } = string.Empty;

    [JsonPropertyName("host_header_rewrite")]
    public string HostHeaderRewrite { get; set; } = string.Empty;

    [JsonPropertyName("header_x_from_where")]
    public string HeaderXFromWhere { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int Status { get; set; }

    [JsonPropertyName("node")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int NodeId { get; set; }

    [JsonPropertyName("online_status")]
    public string OnlineStatus { get; set; } = string.Empty;

    [JsonPropertyName("today_traffic_in")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long TodayTrafficIn { get; set; }

    [JsonPropertyName("today_traffic_out")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long TodayTrafficOut { get; set; }

    [JsonPropertyName("cur_conns")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int CurrentConnections { get; set; }

    [JsonPropertyName("last_start_time")]
    public string LastStartTime { get; set; } = string.Empty;

    [JsonPropertyName("last_close_time")]
    public string LastCloseTime { get; set; } = string.Empty;

    public int ProxyId => Id;
    public string DisplayAddress => !string.IsNullOrWhiteSpace(Domain)
        ? Domain
        : RemotePort.HasValue ? $":{RemotePort.Value}" : "-";
}

public class StarryFrpPortsData
{
    [JsonPropertyName("available_ports")]
    public List<int> AvailablePorts { get; set; } = new();

    [JsonPropertyName("count")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int Count { get; set; }

    [JsonPropertyName("total")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int Total { get; set; }
}

public class StarryFrpTunnelConfigResponse
{
    [JsonPropertyName("tunnel_count")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int TunnelCount { get; set; }
}

public class StarryFrpCreateTunnelResponse
{
    [JsonPropertyName("tunnel_id")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int TunnelId { get; set; }

    [JsonPropertyName("proxy_name")]
    public string ProxyName { get; set; } = string.Empty;

    [JsonPropertyName("proxy_type")]
    public string ProxyType { get; set; } = string.Empty;

    [JsonPropertyName("local_ip")]
    public string LocalIp { get; set; } = string.Empty;

    [JsonPropertyName("local_port")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int LocalPort { get; set; }

    [JsonPropertyName("remote_port")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int? RemotePort { get; set; }

    [JsonPropertyName("node")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int NodeId { get; set; }
}
