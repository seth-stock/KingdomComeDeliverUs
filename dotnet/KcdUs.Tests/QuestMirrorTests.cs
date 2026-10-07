using KcdUs.Agent;

namespace KcdUs.Tests;
public sealed class QuestMirrorTests
{
    private static LuaMod Fixture()
    {
        var lua = new LuaMod(); lua.InWorld();
        lua.Do("KCDUS.QuestMirror.catalogue.test_shared={code='test_shared',tier='open',dlc='',kind='side',objectives={[11]='goal'}}; " +
               "__started=false; __done=false; __starts=0; __completes=0; " +
               "QuestSystem.IsQuestStarted=function() return __started end; QuestSystem.IsQuestCompleted=function() return false end; " +
               "QuestSystem.IsObjectiveStarted=function() return __started end; QuestSystem.IsObjectiveCompleted=function() return __done end; " +
               "QuestSystem.ActivateQuest=function() __started=true; __starts=__starts+1 end; " +
               "QuestSystem.CompleteObjective=function(q,o,message) assert(q=='test_shared' and o=='goal' and message==false); __done=true; __completes=__completes+1 end");
        return lua;
    }
    [Fact] public void ExplicitCandidateModeAndMatchingEpochAreRequired()
    {
        var lua = Fixture();
        lua.Send("1~QMAPPLY|epoch|test_shared|1|0|11");
        Assert.Equal(0, lua.Num("__starts"));
        lua.Send("2~QMODE|candidate|epoch");
        lua.Send("3~QMAPPLY|other|test_shared|1|0|11");
        Assert.Equal(0, lua.Num("__starts"));
        lua.Send("4~QMAPPLY|epoch|test_shared|1|0|11");
        Assert.Equal(1, lua.Num("__starts")); Assert.True(lua.Bool("__done"));
        lua.Send("5~QMAPPLY|epoch|test_shared|1|0|11");
        Assert.Equal(1, lua.Num("__completes"));
    }
    [Fact] public void DlcRailsUnknownObjectivesAndInertBindingsCannotClaimSuccess()
    {
        var lua = Fixture(); lua.Send("1~QMODE|candidate|epoch");
        lua.Do("KCDUS.QuestMirror.catalogue.test_shared.dlc='NewHomes'");
        lua.Send("2~QMAPPLY|epoch|test_shared|1|0|11"); Assert.Equal(0, lua.Num("__starts"));
        lua.Do("KCDUS.QuestMirror.catalogue.test_shared.dlc=''; KCDUS.QuestMirror.catalogue.test_shared.tier='rails'");
        lua.Send("3~QMAPPLY|epoch|test_shared|1|0|11"); Assert.Equal(0, lua.Num("__starts"));
        lua.Do("KCDUS.QuestMirror.catalogue.test_shared.tier='open'");
        lua.Send("4~QMAPPLY|epoch|test_shared|1|0|999"); Assert.Equal(0, lua.Num("__starts"));
        lua.Do("QuestSystem.CompleteObjective=function() end");
        lua.Send("5~QMAPPLY|epoch|test_shared|1|0|11");
        Assert.Contains(lua.Lines("KCDUS|QMAPPLIED"), l => l.EndsWith("objective-refused"));
        Assert.False(lua.Bool("__done"));
    }
    [Fact] public void CatalogRulesRejectLocalSystemDlcAndMalformedPayloads()
    {
        var q = QuestCatalog.All.First(q => q.Tier == "open" && q.Dlc.Length == 0 && q.Kind == "side");
        Assert.True(QuestMirrorRules.Valid(q.Code, "1", "0", "11,12"));
        Assert.False(QuestMirrorRules.Valid(q.Code, "1", "0", "11|lua"));
        Assert.False(QuestMirrorRules.Valid(q.Code, "yes", "0", ""));
        Assert.False(QuestMirrorRules.Valid("q_tutorials", "1", "0", "11"));
        Assert.False(QuestMirrorRules.Valid("made_up", "1", "0", "11"));
        foreach (var excluded in QuestCatalog.All.Where(q => q.Dlc.Length > 0 || q.Tier != "open"))
            Assert.False(QuestMirrorRules.Valid(excluded.Code, "1", "0", "11"));
    }
}
