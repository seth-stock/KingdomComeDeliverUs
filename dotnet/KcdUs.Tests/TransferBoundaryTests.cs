using KcdUs.Agent.Worlds;
using Xunit;

namespace KcdUs.Tests;
public sealed class TransferBoundaryTests
{
    [Fact] public void WrongSizedAndConflictingChunksCannotReplaceAcceptedData()
    {
        byte[] original = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();
        var receiver = new WorldTransfer.Receiver(WorldTransfer.MakeOffer("t", "world", original));
        Assert.False(receiver.Add(0, Convert.ToBase64String(new byte[101])));
        Assert.False(receiver.Add(0, new string('A', 1_000_000)));
        Assert.True(receiver.Add(0, Convert.ToBase64String(original)));
        Assert.True(receiver.Add(0, Convert.ToBase64String(original)));
        Assert.False(receiver.Add(0, Convert.ToBase64String(new byte[100])));
        Assert.Equal(original, receiver.Assemble());
    }
    [Fact] public void DeclaredShortBlockCannotInflateMoreDataThanItsBound()
    {
        byte[] save = WorldTests.FakeSave(WorldTests.Desc(1000000001, 1));
        BitConverter.GetBytes(1).CopyTo(save, 4);
        Assert.False(SaveInfo.Validate(save, out _));
        Assert.Null(SaveInfo.Head(save));
    }
    [Theory] [InlineData("NaN")] [InlineData("Infinity")] [InlineData("-1")]
    public void NonfiniteOrNegativePlaytimeIsNotAWorldStamp(string hours)
    {
        Assert.Null(WorldStamp.Decode(["world", hours, "1000000001"], 0));
    }
}
