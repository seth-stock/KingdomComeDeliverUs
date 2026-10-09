// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;

namespace KcdUs.Agent;
public static class QuestMirrorRules
{
    public static bool Valid(string code, string started, string completed, string objectives)
    {
        var quest = QuestCatalog.All.FirstOrDefault(q => q.Code == code);
        if (quest is null || quest.Tier != "open" || quest.Dlc.Length > 0 || quest.Kind is not ("main" or "side" or "activity")
            || started is not ("0" or "1") || completed is not ("0" or "1") || objectives.Length > 512) return false;
        return objectives.Length == 0 || objectives.Split(',').All(v => int.TryParse(v, NumberStyles.None,
            CultureInfo.InvariantCulture, out int id) && id > 0 && id < 1000000);
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
