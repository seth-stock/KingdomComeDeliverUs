// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using KcdUs.Agent;
using KcdUs.Agent.Worlds;
namespace KcdUs.Tests;
public class WorldLoadRecoveryTests
{
    [Theory]
    [InlineData("OriginalBackedUp", false)]
    [InlineData("ReplacementInstalled", true)]
    public async Task RecoveryDistinguishesCanceledPreparationFromAnInstalledUnacknowledgedWorld(string phase,bool installed)
    {
        string root=Path.Combine(Path.GetTempPath(),"kcdus-load-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new SaveStore(Path.Combine(root,"saves"),Path.Combine(root,"backups"));
            Directory.CreateDirectory(store.PlaylineDir(4));
            var old=WorldTests.FakeSave(WorldTests.Desc(1_790_000_100,3));
            var next=WorldTests.FakeSave(WorldTests.Desc(1_790_000_200,10));
            File.WriteAllBytes(Path.Combine(store.PlaylineDir(4),"old.whs"),old);
            var registry=new WorldRegistry { PendingLoad=new("world","World",4,Convert.ToHexString(SHA256.HashData(next)).ToLowerInvariant(),"",1,"transfer",10,1_790_000_200,"Prepared") };
            string path=Path.Combine(root,"worlds.json");registry.Save(path);
            Assert.Throws<IOException>(()=>new SaveInstallTransaction(store.Root,store.BackupRoot,p=>{if(p==phase)throw new IOException("interrupted");}).Install(4,"world",next,true));
            var game=new FakeGame();using var session=new Session(new SessionOptions(),game,new NullRelay(),()=>0);
            var coordinator=new WorldCoordinator(new AgentConfig(),game,()=>session,store,registry,path,_=>{},_=>{});
            try
            {
                Assert.Equal("",registry.Active);
                Assert.False(game.Has("LOAD|"));
                if(installed)
                { Assert.Equal("Installed",registry.PendingLoad!.Phase);Assert.Equal(next,File.ReadAllBytes(Path.Combine(store.PlaylineDir(4),"world.whs"))); }
                else
                { Assert.Null(registry.PendingLoad);Assert.Equal(old,File.ReadAllBytes(Path.Combine(store.PlaylineDir(4),"old.whs"))); }
            }
            finally { coordinator.Dispose();await coordinator.Completion; }
        }
        finally { if(Directory.Exists(root))Directory.Delete(root,true); }
    }
    [Fact]
    public void CorruptPendingLoadCannotSelectAnOutOfRangePlayline()
    {
        string dir=Path.Combine(Path.GetTempPath(),"kcdus-load-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try
        {
            string path=Path.Combine(dir,"worlds.json");
            new WorldRegistry { PendingLoad=new("w","W",5,new string('a',64),"",1,"x",10,1_790_000_200,"Installed") }.Save(path);
            Assert.Throws<InvalidDataException>(()=>WorldRegistry.Load(path));
        }
        finally { Directory.Delete(dir,true); }
    }
}
