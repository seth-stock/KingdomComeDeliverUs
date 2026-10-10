using KcdUs.Agent.Worlds;

namespace KcdUs.Tests;

public sealed class WorldRegistryConcurrencyTests
{
    [Fact]
    public async Task ReadersObserveCompleteRegistriesDuringAtomicReplacement()
    {
        string root = Path.Combine(Path.GetTempPath(), "kcdus-registry-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "worlds.json");
        try
        {
            new WorldRegistry { Active = "initial" }.Save(path);
            using var start = new ManualResetEventSlim(false);
            var reader = Task.Run(() =>
            {
                start.Wait();
                for (int i = 0; i < 200; i++)
                {
                    var observed = WorldRegistry.Load(path);
                    Assert.False(string.IsNullOrEmpty(observed.Active));
                    Assert.Empty(observed.Worlds);
                }
            });
            var writer = Task.Run(() =>
            {
                start.Wait();
                for (int i = 0; i < 100; i++) new WorldRegistry { Active = "published-" + i }.Save(path);
            });
            start.Set();await Task.WhenAll(reader, writer);
            Assert.Equal("published-99", WorldRegistry.Load(path).Active);
            Assert.Empty(Directory.GetFiles(root, "*.part"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
