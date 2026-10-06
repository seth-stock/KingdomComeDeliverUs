using Xunit;
namespace KcdUs.Tests;
public sealed class CapabilityTests
{
    [Fact] public void CandidatePresenceNeverClaimsRuntimeProofOrCallsSetters()
    {
        var m = new LuaMod();
        m.Do("mutations=0; player.soul.SetStatLevel=function() mutations=mutations+1 end");
        m.Do("KCDUS.capabilities()");
        Assert.Contains(m.Lines("KCDUS|CAPABILITY|"), l => l.Contains("soul.SetStatLevel|unproven|candidate-present"));
        Assert.Equal(0, m.Num("mutations"));
        Assert.All(m.Lines("KCDUS|CAPABILITY|"), l => Assert.Contains("|unproven|", l));
    }
    [Fact] public void MissingPlayerIsHandledWithoutMutatingOrCrashing()
    {
        var m = new LuaMod(); m.Do("player=nil; KCDUS.capabilities()");
        Assert.Contains("KCDUS|CAPABILITY|player|unproven|not-loaded", m.Log());
    }
}
