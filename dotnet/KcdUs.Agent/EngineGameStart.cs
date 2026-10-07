// SPDX-License-Identifier: GPL-3.0-only
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
namespace KcdUs.Agent;

/// <summary>Own a suspended game child, install our startup adapter, then resume.
/// Never attaches to an existing process or changes game binaries on disk.</summary>
public static class EngineGameStart
{
    public const string SupportedHash="CF9F6DC384EDCF35C20647A912745DDB8ADB5BA65953E329C24E89DD9C4381AA";
    public static bool Supported(string file)
    {
        using var stream=File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream))==SupportedHash;
    }
    public static int Start(string gameDir,string bridgeDll) => StartWithModules(gameDir,new[]{bridgeDll});
    /// <summary>Ordered startup modules; also used by the private profile harness.
    /// This starts a new child only; the public launcher supplies our adapter.</summary>
    public static int StartWithModules(string gameDir,IReadOnlyList<string> startupModules,bool developerMode=false)
    {
        if(!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            throw new PlatformNotSupportedException("The equipment adapter currently requires 64-bit Windows.");
        string root=Path.GetFullPath(gameDir),exe=Path.Combine(root,"Bin","Win64","KingdomCome.exe");
        var modules=startupModules.Select(Path.GetFullPath).ToArray();
        if(modules.Length is <1 or >2)throw new ArgumentException("One or two startup modules are required.");
        if(!File.Exists(exe) || !Supported(Path.Combine(root,"Bin","Win64","WHGame.dll")))
            throw new InvalidOperationException("This equipment adapter supports the verified retail KCD1 1.9.8 engine only.");
        foreach(var dll in modules)if(!File.Exists(dll))throw new FileNotFoundException("Rebuild/reinstall the co-op engine adapter.",dll);
        if(Process.GetProcessesByName("KingdomCome").Length!=0)
            throw new InvalidOperationException("Close Kingdom Come before starting it from this launcher.");
        if(Process.GetProcessesByName("steam").Length==0)
            throw new InvalidOperationException("Open Steam before starting the game.");
        // Resolve a Steam-library junction before launching. The engine discovers
        // mods from its executable path; a synthetic alias can hide them.
        using(var image=File.OpenRead(exe))
        {
            var canonical=new StringBuilder(32768);
            if(GetFinalPathNameByHandleW(image.SafeFileHandle,canonical,(uint)canonical.Capacity,0)==0)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            exe=canonical.ToString();if(exe.StartsWith(@"\\?\"))exe=exe[4..];
        }
        var startup=new StartupInfo { Cb=Marshal.SizeOf<StartupInfo>() };
        if(!CreateProcessW(exe,new StringBuilder('"'+exe+"\" -root \""+root+'"'+(developerMode?" -devmode":"")),IntPtr.Zero,IntPtr.Zero,false,4,IntPtr.Zero,root,ref startup,out var process))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        IntPtr memory=IntPtr.Zero,thread=IntPtr.Zero;bool resumed=false;
        try
        {
            // Let Windows initialize the child's loader without running the game's
            // primary thread. Its module list is unavailable before this step.
            var exitThread=NativeLibrary.GetExport(NativeLibrary.Load("ntdll.dll"),"RtlExitUserThread");
            var expected=new byte[16];Marshal.Copy(exitThread,expected,0,expected.Length);
            var observed=new byte[16];
            if(!ReadProcessMemory(process.Process,exitThread,observed,16,out var read) || read!=16 || !expected.SequenceEqual(observed))
                throw new InvalidOperationException("The owned child's initial system loader could not be verified.");
            thread=CreateRemoteThread(process.Process,IntPtr.Zero,0,exitThread,IntPtr.Zero,0,out _);
            if(thread==IntPtr.Zero || WaitForSingleObject(thread,10000)!=0)
                throw new InvalidOperationException("The owned child's system loader did not initialize.");
            CloseHandle(thread);thread=IntPtr.Zero;
            // Resolve the actual module owning LoadLibraryW, including forwarded exports.
            var local=NativeLibrary.GetExport(NativeLibrary.Load("kernel32.dll"),"LoadLibraryW");
            if(!GetModuleHandleExW(6,local,out var owner))throw new Win32Exception(Marshal.GetLastWin32Error());
            var ownerPath=new StringBuilder(32768);if(GetModuleFileNameW(owner,ownerPath,ownerPath.Capacity)==0)throw new Win32Exception(Marshal.GetLastWin32Error());
            using var child=Process.GetProcessById(process.Pid);
            ProcessModule? remote=child.Modules.Cast<ProcessModule>().FirstOrDefault(m=>string.Equals(m.ModuleName,Path.GetFileName(ownerPath.ToString()),StringComparison.OrdinalIgnoreCase));
            if(remote==null)throw new InvalidOperationException("The owned child's system loader module was not found.");
            IntPtr loader=remote.BaseAddress+(int)(local.ToInt64()-owner.ToInt64());
            foreach(var bridgeDll in modules)
            {
            var bytes=Encoding.Unicode.GetBytes(bridgeDll+'\0');
            memory=VirtualAllocEx(process.Process,IntPtr.Zero,(nuint)bytes.Length,0x3000,4);
            if(memory==IntPtr.Zero || !WriteProcessMemory(process.Process,memory,bytes,(nuint)bytes.Length,out var written) || written!=(nuint)bytes.Length)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            thread=CreateRemoteThread(process.Process,IntPtr.Zero,0,loader,memory,0,out _);
            if(thread==IntPtr.Zero || WaitForSingleObject(thread,10000)!=0)
                throw new InvalidOperationException("The adapter did not initialize in the suspended child.");
            child.Refresh();
            if(!child.Modules.Cast<ProcessModule>().Any(m=>string.Equals(m.FileName,bridgeDll,StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("The adapter refused startup; the owned child will be stopped.");
            CloseHandle(thread);thread=IntPtr.Zero;
            VirtualFreeEx(process.Process,memory,0,0x8000);memory=IntPtr.Zero;
            }
            if(ResumeThread(process.Thread)==uint.MaxValue)throw new Win32Exception(Marshal.GetLastWin32Error());
            resumed=true;return process.Pid;
        }
        finally
        {
            if(!resumed)TerminateProcess(process.Process,1);
            // On timeout the worker may still reference memory; terminating our child
            // releases it. Never free that buffer while a loader thread is running.
            if(resumed && memory!=IntPtr.Zero)VirtualFreeEx(process.Process,memory,0,0x8000);
            if(thread!=IntPtr.Zero)CloseHandle(thread);
            CloseHandle(process.Thread);CloseHandle(process.Process);
        }
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct StartupInfo
    {
        public int Cb;public IntPtr Reserved,Desktop,Title;public uint X,Y,XSize,YSize,XCount,YCount,Fill,Flags;
        public ushort Show,ReservedBytes;public IntPtr Reserved2,Input,Output,Error;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { public IntPtr Process,Thread;public int Pid,Tid; }
    [DllImport("kernel32",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool CreateProcessW(string app,StringBuilder command,IntPtr security,IntPtr threadSecurity,bool inherit,uint flags,IntPtr environment,string cwd,ref StartupInfo startup,out ProcessInfo process);
    [DllImport("kernel32",SetLastError=true)] private static extern IntPtr VirtualAllocEx(IntPtr process,IntPtr address,nuint size,uint allocation,uint protect);
    [DllImport("kernel32",SetLastError=true)] private static extern bool WriteProcessMemory(IntPtr process,IntPtr address,byte[] bytes,nuint size,out nuint written);
    [DllImport("kernel32",SetLastError=true)] private static extern bool ReadProcessMemory(IntPtr process,IntPtr address,byte[] bytes,nuint size,out nuint read);
    [DllImport("kernel32",SetLastError=true)] private static extern IntPtr CreateRemoteThread(IntPtr process,IntPtr security,nuint size,IntPtr function,IntPtr parameter,uint flags,out uint tid);
    [DllImport("kernel32")] private static extern uint WaitForSingleObject(IntPtr handle,uint ms);
    [DllImport("kernel32",SetLastError=true)] private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32")] private static extern bool TerminateProcess(IntPtr process,uint code);
    [DllImport("kernel32")] private static extern bool VirtualFreeEx(IntPtr process,IntPtr address,nuint size,uint free);
    [DllImport("kernel32")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool GetModuleHandleExW(uint flags,IntPtr address,out IntPtr module);
    [DllImport("kernel32",CharSet=CharSet.Unicode,SetLastError=true)] private static extern int GetModuleFileNameW(IntPtr module,StringBuilder path,int size);
    [DllImport("kernel32",CharSet=CharSet.Unicode,SetLastError=true)] private static extern uint GetFinalPathNameByHandleW(Microsoft.Win32.SafeHandles.SafeFileHandle file,StringBuilder path,uint size,uint flags);
}
