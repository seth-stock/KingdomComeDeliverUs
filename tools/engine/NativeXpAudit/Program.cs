// SPDX-License-Identifier: GPL-3.0-only
using KcdUs.Agent;
using System.Text.Json;
if(args.Length!=1)throw new ArgumentException("Pass one native XP journal file.");
using var stream=new FileStream(args[0],FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
var reader=new NativeQuestXpLog();var count=0;
while(stream.Length-reader.Offset>=NativeQuestXpLog.RecordSize) {
    foreach(var xp in reader.Read(stream,256)) { Console.WriteLine(JsonSerializer.Serialize(xp));count++; }
}
Console.WriteLine($"Verified effects={count} pending={reader.Pending} trailing={stream.Length-reader.Offset}");
return reader.Pending==0 && stream.Length==reader.Offset?0:2;
