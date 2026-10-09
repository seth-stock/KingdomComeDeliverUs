// SPDX-License-Identifier: GPL-3.0-only
namespace KcdUs.Tests;
public class RewardReadbackTests
{
    private const string Coin="5ef63059-322e-4e1b-abe8-926e100c770e";
    private static LuaMod World()
    {
        var m=new LuaMod(); m.InWorld(); m.Send("1~LOOTMODE|1|w1|0"); return m;
    }
    private static double Amount(LuaMod m) => m.Num("player.inventory:GetCountOfClass('"+Coin+"')");
    [Fact] public void NativePaymentBaselinePrecedesTheMutationAndLateReadyDoesNotReplaceIt()
    {
        var m=World(); m.Do("KCDUS.Rewards.beginApply('q_test')");
        m.Do("player.inventory:AddItem(ItemManager.CreateItem('"+Coin+"',1,50))");
        m.Do("KCDUS.Rewards.appliedHere('q_test')");
        m.Send("2~QRGIVE|q_test|12345678|"+Coin+":150:1");
        Assert.Equal(150,Amount(m)); // old post-apply baseline incorrectly gave 200
    }
    [Fact] public void ReorderedRewardWaitsForReadbackAndNativeInertCreationIsNotSuccess()
    {
        var m=World(); m.Send("2~QRGIVE|q_test|12345678|"+Coin+":150:1");
        Assert.Equal(0,Amount(m));
        m.Do("KCDUS.Rewards.beginApply('q_test'); KCDUS.Rewards.appliedHere('q_test'); player.inventory.AddItem=function() end");
        m.Advance(0.6);
        Assert.Empty(m.Lines("KCDUS|QRGIVEN|")); Assert.Single(m.Lines("KCDUS|QRUNVERIFIED|"));
        m.Send("3~QRGIVE|q_test|12345678|"+Coin+":150:1");
        Assert.Equal(0,Amount(m)); Assert.Single(m.Lines("KCDUS|QRUNVERIFIED|"));
    }
    [Fact] public void InvalidOrDuplicateRowsNeverPartiallyPay()
    {
        var m=World(); m.Do("KCDUS.Rewards.appliedHere('q_test')");
        m.Send("2~QRGIVE|q_test|12345678|"+Coin+":150:1;garbage");
        m.Send("3~QRGIVE|q_test|12345678|"+Coin+":150:1;"+Coin+":150:1");
        Assert.Equal(0,Amount(m)); Assert.Empty(m.Lines("KCDUS|QRGIVEN|"));
    }
}
