// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;

namespace KcdUs.Agent;
public static class QuestMirrorRules
{
    public static bool ValidNativeXp(string key,string kind,string index,string raw,string cause,string data)
    {
        if(key.Length is <35 or >41 || key[0]!='n' || key[33]!='.' || !key[1..33].All(char.IsAsciiHexDigitLower) ||
            !int.TryParse(key[34..],NumberStyles.None,CultureInfo.InvariantCulture,out int seq) || seq is <1 or >1000000)return false;
        if(kind is not("0" or "1") || !int.TryParse(index,NumberStyles.None,CultureInfo.InvariantCulture,out int i) || i<0 || i>(kind=="0"?9:32) ||
            !int.TryParse(raw,NumberStyles.None,CultureInfo.InvariantCulture,out int r) || r is <1 or >128*100000)return false;
        return (cause=="2306d30" && data.Length==32 || cause=="21c8fd0" && data.Length==48 && data[40..]=="00000000") && data.All(char.IsAsciiHexDigitLower);
    }
    public static bool Valid(string code, string started, string completed, string objectives)
    {
        var quest = QuestCatalog.All.FirstOrDefault(q => q.Code == code);
        if (quest is null || quest.Tier != "open" || quest.Dlc.Length > 0 || quest.Kind is not ("main" or "side" or "activity")
            || started is not ("0" or "1") || completed is not ("0" or "1") || objectives.Length > 512) return false;
        return objectives.Length == 0 || objectives.Split(',').All(v => int.TryParse(v, NumberStyles.None,
            CultureInfo.InvariantCulture, out int id) && id > 0 && id < 1000000);
    }

    /// <summary>A quest-caused native XP application (rewards.lua / XpRead.h): key x + 6 hex + sequence, kind 0 stat (0-9) or 1 skill (0-32), raw 1..128*100000.</summary>
    public static bool ValidXp(string key, string kind, string index, string raw)
    {
        if (key.Length is < 8 or > 24 || key[0] != 'x' || !key[1..7].All(char.IsAsciiHexDigitLower) || !key[7..].All(char.IsAsciiDigit)) return false;
        if (kind is not ("0" or "1") || !int.TryParse(index, NumberStyles.None, CultureInfo.InvariantCulture, out int i) || i < 0 || i > (kind == "0" ? 9 : 32)) return false;
        return long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out long r) && r is >= 1 and <= 128L * 100000;
    }

    /// <summary>A quest step's reward (rewards.lua): a mirrored quest, an 8-hex key and up to 12 entries class:count:health.</summary>
    public static bool ValidReward(string code, string key, string items)
    {
        var quest = QuestCatalog.All.FirstOrDefault(q => q.Code == code);
        if (quest is null || quest.Tier != "open" || quest.Dlc.Length > 0 || key.Length != 8 || !key.All(char.IsAsciiHexDigitLower) || items.Length is 0 or > 900) return false;
        var parts = items.Split(';');
        if (parts.Length > 12) return false;
        foreach (var p in parts)
        {
            var v = p.Split(':');
            if (v.Length != 3 || !OutcomesRules.ValidClass(v[0]) || !OutcomesRules.Count(v[1], out _) || !OutcomesRules.Number(v[2], 0, 1, out _)) return false;
        }
        return true;
    }
}
