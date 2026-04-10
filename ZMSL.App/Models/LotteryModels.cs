using System;
using System.Collections.Generic;
using System.Linq;

namespace ZMSL.App.Models;

public class Lottery
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PrizeName { get; set; } = string.Empty;
    public string PrizeImageUrl { get; set; } = string.Empty;
    public int WinnerCount { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string Status { get; set; } = string.Empty; // ACTIVE, DRAWN, ENDED, PENDING
    public bool HasJoined { get; set; }
    
    // New fields
    public string? JoinCode { get; set; }
    public bool IsProtected { get; set; }
    public List<LotteryPrize>? Prizes { get; set; }
    
    // UI Helpers
    public string TimeRange => $"{StartTime:MM-dd HH:mm} - {EndTime:MM-dd HH:mm}";
    public string StatusText => Status switch
    {
        "ACTIVE" => "进行中",
        "DRAWN" => "已开奖",
        "ENDED" => "已结束",
        "PENDING" => "未开始",
        _ => Status
    };
    public string ActionButtonText => Status == "ACTIVE" 
        ? (HasJoined ? "已参与" : "查看详情") 
        : (Status == "DRAWN" ? "查看名单" : "已结束");
    
    public bool IsJoinEnabled => Status == "ACTIVE" && !HasJoined;
    public bool IsViewWinnersEnabled => Status == "DRAWN";
    
    public bool HasImage => !string.IsNullOrEmpty(PrizeImageUrl) && Uri.IsWellFormedUriString(PrizeImageUrl, UriKind.Absolute);
    public bool IsMultiPrize => Prizes != null && Prizes.Count > 0;
    
    public string DisplayPrizeName => IsMultiPrize ? "多重奖品" : PrizeName;
    public string DisplayWinnerCount => IsMultiPrize ? $"{Prizes!.Sum(p => p.Count)}" : $"{WinnerCount}";
}

public class LotteryPrize
{
    public string Name { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class LotteryWinner
{
    public long UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Prize { get; set; } = string.Empty;
}
