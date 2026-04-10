using System.Text.Json.Serialization;

namespace ZMSL.App.Models;

public class MeFrpApiResponse<T>
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public T? Data { get; set; }

    [JsonIgnore]
    public bool IsSuccess => Code == 200;
}

public class MeFrpUserInfo
{
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("friendlyGroup")]
    public string FriendlyGroup { get; set; } = string.Empty;

    [JsonPropertyName("group")]
    public string Group { get; set; } = string.Empty;

    [JsonPropertyName("inBound")]
    public long InBound { get; set; }

    [JsonPropertyName("isRealname")]
    public bool IsRealname { get; set; }

    [JsonPropertyName("maxProxies")]
    public int MaxProxies { get; set; }

    [JsonPropertyName("outBound")]
    public long OutBound { get; set; }

    [JsonPropertyName("regTime")]
    public long RegTime { get; set; }

    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("todaySigned")]
    public bool TodaySigned { get; set; }

    [JsonPropertyName("traffic")]
    public long Traffic { get; set; }

    [JsonPropertyName("usedProxies")]
    public int UsedProxies { get; set; }

    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;
}

public class MeFrpProxyListData
{
    [JsonPropertyName("nodes")]
    public List<MeFrpNode> Nodes { get; set; } = new();

    [JsonPropertyName("proxies")]
    public List<MeFrpProxy> Proxies { get; set; } = new();
}

public class MeFrpNode
{
    [JsonPropertyName("nodeId")]
    public int NodeId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("hostname")]
    public string Hostname { get; set; } = string.Empty;

    [JsonPropertyName("allowGroup")]
    public string AllowGroup { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("allowPort")]
    public string AllowPort { get; set; } = string.Empty;

    [JsonPropertyName("allowType")]
    public string AllowType { get; set; } = string.Empty;

    [JsonPropertyName("region")]
    public string Region { get; set; } = string.Empty;

    [JsonPropertyName("bandwidth")]
    public string Bandwidth { get; set; } = string.Empty;

    [JsonPropertyName("isOnline")]
    public bool IsOnline { get; set; }

    [JsonPropertyName("isDisabled")]
    public bool IsDisabled { get; set; }

    [JsonPropertyName("loadPercent")]
    public int LoadPercent { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(Bandwidth) ? Name : $"{Name} · {Bandwidth}";
}

public class MeFrpProxy
{
    [JsonPropertyName("proxyId")]
    public int ProxyId { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("proxyName")]
    public string ProxyName { get; set; } = string.Empty;

    [JsonPropertyName("proxyType")]
    public string ProxyType { get; set; } = string.Empty;

    [JsonPropertyName("isBanned")]
    public bool IsBanned { get; set; }

    [JsonPropertyName("isDisabled")]
    public bool IsDisabled { get; set; }

    [JsonPropertyName("localIp")]
    public string LocalIp { get; set; } = "127.0.0.1";

    [JsonPropertyName("localPort")]
    public int LocalPort { get; set; }

    [JsonPropertyName("remotePort")]
    public int RemotePort { get; set; }

    [JsonPropertyName("nodeId")]
    public int NodeId { get; set; }

    [JsonPropertyName("runId")]
    public string RunId { get; set; } = string.Empty;

    [JsonPropertyName("isOnline")]
    public bool IsOnline { get; set; }

    [JsonPropertyName("domain")]
    public string Domain { get; set; } = string.Empty;

    [JsonPropertyName("lastStartTime")]
    public long LastStartTime { get; set; }

    [JsonPropertyName("lastCloseTime")]
    public long LastCloseTime { get; set; }

    [JsonPropertyName("clientVersion")]
    public string ClientVersion { get; set; } = string.Empty;

    [JsonPropertyName("useEncryption")]
    public bool UseEncryption { get; set; }

    [JsonPropertyName("useCompression")]
    public bool UseCompression { get; set; }

    [JsonPropertyName("transportProtocol")]
    public string TransportProtocol { get; set; } = string.Empty;

    public string DisplayAddress => string.IsNullOrWhiteSpace(Domain) ? $":{RemotePort}" : Domain;
}

public class MeFrpCreateProxyData
{
    [JsonPropertyName("currentGroup")]
    public string CurrentGroup { get; set; } = string.Empty;

    [JsonPropertyName("groups")]
    public List<MeFrpGroup> Groups { get; set; } = new();

    [JsonPropertyName("nodes")]
    public List<MeFrpNode> Nodes { get; set; } = new();
}

public class MeFrpGroup
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("friendlyName")]
    public string FriendlyName { get; set; } = string.Empty;

    [JsonPropertyName("maxProxies")]
    public int MaxProxies { get; set; }

    [JsonPropertyName("baseTraffic")]
    public long BaseTraffic { get; set; }

    [JsonPropertyName("outBound")]
    public long OutBound { get; set; }

    [JsonPropertyName("inBound")]
    public long InBound { get; set; }
}

public class MeFrpConfigData
{
    [JsonPropertyName("config")]
    public string Config { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
}
