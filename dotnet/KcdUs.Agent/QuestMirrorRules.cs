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
}
