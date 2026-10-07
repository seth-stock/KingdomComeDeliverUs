// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
namespace KcdUs.Agent;
public static class OutfitSnapshot
{
    public static bool Valid(string text)
    {
        if(text.Length>1600)return false;
        var fields=text.Split(';');if(fields[0]!="1" || fields.Length>33)return false;
        var seen=new HashSet<Guid>();
        foreach(var field in fields.Skip(1))
        {
            var f=field.Split(',');
            if(f.Length!=2 || !Guid.TryParseExact(f[0],"D",out var id) || !seen.Add(id)
                || f[1].Any(c=>c!='.' && (c<'0' || c>'9'))
                || !double.TryParse(f[1],NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var health)
                || !double.IsFinite(health) || health<0 || health>1)return false;
        }
        return true;
    }
}
