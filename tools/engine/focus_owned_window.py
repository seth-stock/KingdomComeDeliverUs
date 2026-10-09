"""Focus only the verified private engine window; never send input to another process."""
import ctypes
from ctypes import wintypes as w
import subprocess
import engine_session

def focus():
    session=engine_session.verified()
    user=ctypes.WinDLL('user32',use_last_error=True)
    kernel=ctypes.WinDLL('kernel32',use_last_error=True)
    user.GetForegroundWindow.restype=w.HWND
    user.GetWindowThreadProcessId.argtypes=[w.HWND,ctypes.POINTER(w.DWORD)]
    user.ShowWindow.argtypes=[w.HWND,ctypes.c_int]
    user.BringWindowToTop.argtypes=[w.HWND]
    user.SetForegroundWindow.argtypes=[w.HWND]
    user.AttachThreadInput.argtypes=[w.DWORD,w.DWORD,w.BOOL]
    handle=int(subprocess.check_output(['powershell','-NoProfile','-Command',
        f'(Get-Process -Id {int(session["pid"])}).MainWindowHandle'],text=True).strip())
    pid=w.DWORD()
    target_thread=user.GetWindowThreadProcessId(handle,ctypes.byref(pid))
    if not handle or pid.value!=session['pid']: raise RuntimeError('No verified owned window')
    foreground_thread=user.GetWindowThreadProcessId(user.GetForegroundWindow(),None)
    thread=kernel.GetCurrentThreadId()
    attached=[]
    try:
        for other in set((target_thread,foreground_thread)):
            if other and other!=thread and user.AttachThreadInput(thread,other,True): attached.append(other)
        user.ShowWindow(handle,9)
        user.BringWindowToTop(handle)
        user.SetForegroundWindow(handle)
    finally:
        for other in attached: user.AttachThreadInput(thread,other,False)
    user.GetWindowThreadProcessId(user.GetForegroundWindow(),ctypes.byref(pid))
    if pid.value!=session['pid']: raise RuntimeError('Focus refused; no input sent')
    print('Focused owned private engine PID',session['pid'])

if __name__=='__main__': focus()
