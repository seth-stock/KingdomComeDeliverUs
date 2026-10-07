// Test harness only. Redirect Saved Games in this process, never the Windows profile.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shlobj.h>
#include <cstring>
#include <cwchar>

static wchar_t root[32768];
static HANDLE faultLog = INVALID_HANDLE_VALUE;
static LONG faultCount;
// Optional, private item-layout experiment. No item or actor state is changed.
static BYTE* itemModule;
static int (*getItemOriginal)(void*,void*,unsigned long long);
static LONG itemReads;
static int inspect_item(void* binding,void* handler,unsigned long long uid) {
    if (InterlockedIncrement(&itemReads)<=1024) {
        __try {
            auto manager=*reinterpret_cast<BYTE**>(static_cast<BYTE*>(binding)+0x60);
            auto lookup=reinterpret_cast<BYTE*(*)(void*,unsigned long long*)>(itemModule+0x454638);
            auto item=lookup(manager+0x18,&uid);
            if(item) {
                wchar_t path[32768]; swprintf_s(path,L"%s\\items-native.txt",root);
                HANDLE file=CreateFileW(path,FILE_APPEND_DATA,FILE_SHARE_READ|FILE_SHARE_WRITE,nullptr,OPEN_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
                if(file!=INVALID_HANDLE_VALUE) {
                    char line[512];int n=sprintf_s(line,"%llu ",uid);
                    for(unsigned i=0;i<0x70;++i)n+=sprintf_s(line+n,sizeof(line)-n,"%02x",item[i]);
                    n+=sprintf_s(line+n,sizeof(line)-n,"\r\n");DWORD wrote;
                    WriteFile(file,line,static_cast<DWORD>(n),&wrote,nullptr);CloseHandle(file);
                }
            }
        } __except(EXCEPTION_EXECUTE_HANDLER) { }
    }
    return getItemOriginal(binding,handler,uid);
}
static void inspect_items(HMODULE module) {
    wchar_t enabled[8];
    if(!GetEnvironmentVariableW(L"KCDUS_PROBE_ITEMS",enabled,_countof(enabled)) || enabled[0]!=L'1' || itemModule) return;
    wchar_t name[MAX_PATH];GetModuleFileNameW(module,name,_countof(name));
    auto leaf=wcsrchr(name,L'\\');if(!leaf || _wcsicmp(leaf+1,L"WHGame.dll"))return;
    auto base=reinterpret_cast<BYTE*>(module);
    // Full file SHA is gated by the Python harness before this process starts.
    const BYTE signature[]={0x48,0x89,0x5c,0x24,0x10,0x48,0x89,0x74,0x24,0x20,0x55,0x57,0x41,0x56,0x48,0x8b,0xec};
    auto entry=base+0x10c9a98;
    if(memcmp(entry,signature,sizeof(signature)))return;
    auto trampoline=static_cast<BYTE*>(VirtualAlloc(nullptr,64,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE));if(!trampoline)return;
    memcpy(trampoline,signature,sizeof(signature));
    const BYTE jump[]={0xff,0x25,0,0,0,0};memcpy(trampoline+sizeof(signature),jump,sizeof(jump));
    *reinterpret_cast<void**>(trampoline+sizeof(signature)+sizeof(jump))=entry+sizeof(signature);
    DWORD old;if(!VirtualProtect(trampoline,64,PAGE_EXECUTE_READ,&old))return;
    itemModule=base;getItemOriginal=reinterpret_cast<decltype(getItemOriginal)>(trampoline);
    if(!VirtualProtect(entry,sizeof(signature),PAGE_EXECUTE_READWRITE,&old))return;
    memcpy(entry,jump,sizeof(jump));*reinterpret_cast<void**>(entry+sizeof(jump))=reinterpret_cast<void*>(inspect_item);
    memset(entry+14,0x90,3);DWORD ignored;VirtualProtect(entry,sizeof(signature),old,&ignored);
    FlushInstructionCache(GetCurrentProcess(),entry,sizeof(signature));
}
static void fault_line(const char* line) {
    if (faultLog == INVALID_HANDLE_VALUE) return;
    DWORD wrote;
    WriteFile(faultLog, line, static_cast<DWORD>(strlen(line)), &wrote, nullptr);
}
static void fault_address(DWORD64 address) {
    HMODULE module = nullptr;
    char name[MAX_PATH] = {};
    GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCSTR>(address), &module);
    if (module) GetModuleFileNameA(module, name, MAX_PATH);
    const char* base = strrchr(name, '\\');
    char line[512];
    sprintf_s(line, "frame %s+%llx\r\n", base ? base + 1 : name, address - reinterpret_cast<DWORD64>(module));
    fault_line(line);
}
static bool leaf_return(CONTEXT* context) {
    __try {
        context->Rip = *reinterpret_cast<DWORD64*>(context->Rsp);
        context->Rsp += 8;
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
static LONG CALLBACK diagnose_fault(EXCEPTION_POINTERS* fault) {
    if (fault->ExceptionRecord->ExceptionCode != EXCEPTION_ACCESS_VIOLATION) return EXCEPTION_CONTINUE_SEARCH;
    if (InterlockedIncrement(&faultCount) > 5) return EXCEPTION_CONTINUE_SEARCH;
    char line[512]; auto record = fault->ExceptionRecord;
    sprintf_s(line, "exception %lx operation=%llu address=%llx\r\n", record->ExceptionCode,
        static_cast<unsigned long long>(record->ExceptionInformation[0]),
        static_cast<unsigned long long>(record->ExceptionInformation[1])); fault_line(line);
    auto c = *fault->ContextRecord;
    sprintf_s(line, "registers rax=%llx rcx=%llx rdx=%llx rbx=%llx rsi=%llx rdi=%llx r8=%llx r9=%llx\r\n",
        c.Rax,c.Rcx,c.Rdx,c.Rbx,c.Rsi,c.Rdi,c.R8,c.R9); fault_line(line);
    for (unsigned i = 0; i < 24 && c.Rip; ++i) {
        fault_address(c.Rip);
        DWORD64 base; auto entry = RtlLookupFunctionEntry(c.Rip, &base, nullptr);
        if (!entry) { if (!leaf_return(&c)) break; }
        else {
            PVOID handler; DWORD64 establisher;
            RtlVirtualUnwind(UNW_FLAG_NHANDLER, base, c.Rip, entry, &c, &handler, &establisher, nullptr);
        }
    }
    FlushFileBuffers(faultLog);
    return EXCEPTION_CONTINUE_SEARCH;
}
static decltype(&SHGetKnownFolderPath) original;
static HRESULT WINAPI isolated(REFKNOWNFOLDERID id, DWORD flags, HANDLE token, PWSTR* out);
static FARPROC WINAPI proc_isolated(HMODULE module, LPCSTR name) {
    FARPROC result = GetProcAddress(module, name);
    if (reinterpret_cast<ULONG_PTR>(name) > 0xffff && !strcmp(name, "SHGetKnownFolderPath") && result == reinterpret_cast<FARPROC>(original))
        return reinterpret_cast<FARPROC>(isolated);
    return result;
}
static unsigned patch(HMODULE module);
static void trace(const char* kind, unsigned value) {
    wchar_t path[32768];
    if (swprintf_s(path, L"%s\\isolation-trace.txt", root) < 0) return;
    HANDLE file = CreateFileW(path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return;
    char line[128]; int n = sprintf_s(line, "%s:%u\r\n", kind, value); DWORD wrote;
    if (n > 0) WriteFile(file, line, static_cast<DWORD>(n), &wrote, nullptr);
    CloseHandle(file);
}
static HRESULT WINAPI folder_w(HWND window, int id, HANDLE token, DWORD flags, LPWSTR out) {
    trace("folder-w", static_cast<unsigned>(id));
    HRESULT result = SHGetFolderPathW(window, id, token, flags, out);
    if (SUCCEEDED(result) && (id & 0xff) == CSIDL_PROFILE) {
        if (wcscpy_s(out, MAX_PATH, root)) return E_FAIL;
    }
    return result;
}
static HRESULT WINAPI folder_a(HWND window, int id, HANDLE token, DWORD flags, LPSTR out) {
    trace("folder-a", static_cast<unsigned>(id));
    HRESULT result = SHGetFolderPathA(window, id, token, flags, out);
    if (SUCCEEDED(result) && (id & 0xff) == CSIDL_PROFILE) {
        if (!WideCharToMultiByte(CP_ACP, WC_NO_BEST_FIT_CHARS, root, -1, out, MAX_PATH, nullptr, nullptr)) return E_FAIL;
    }
    return result;
}
static HMODULE WINAPI load_isolated(LPCSTR name) {
    HMODULE module = LoadLibraryA(name);
    trace(name, patch(module));
    if(module)inspect_items(module);
    return module;
}
static HRESULT WINAPI isolated(REFKNOWNFOLDERID id, DWORD flags, HANDLE token, PWSTR* out) {
    if (IsEqualGUID(id, FOLDERID_SavedGames)) {
        trace("known-saved-games", 1);
        if (!out) return E_POINTER;
        size_t bytes = (wcslen(root) + 1) * sizeof(wchar_t);
        *out = static_cast<PWSTR>(CoTaskMemAlloc(bytes));
        if (!*out) return E_OUTOFMEMORY;
        memcpy(*out, root, bytes);
        return S_OK;
    }
    return original(id, flags, token, out);
}

static unsigned patch(HMODULE module) {
    if (!module) return 0;
    auto base = reinterpret_cast<BYTE*>(module);
    auto dos = reinterpret_cast<IMAGE_DOS_HEADER*>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) return 0;
    auto nt = reinterpret_cast<IMAGE_NT_HEADERS*>(base + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE) return 0;
    auto dir = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    if (!dir.VirtualAddress) return 0;
    unsigned count = 0;
    for (auto desc = reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(base + dir.VirtualAddress); desc->Name; ++desc) {
        if (!desc->OriginalFirstThunk) continue;
        auto names = reinterpret_cast<IMAGE_THUNK_DATA*>(base + desc->OriginalFirstThunk);
        auto slots = reinterpret_cast<IMAGE_THUNK_DATA*>(base + desc->FirstThunk);
        for (; names->u1.AddressOfData; ++names, ++slots) {
            if (IMAGE_SNAP_BY_ORDINAL(names->u1.Ordinal)) continue;
            auto name = reinterpret_cast<IMAGE_IMPORT_BY_NAME*>(base + names->u1.AddressOfData);
            PVOID replacement = nullptr;
            if (!strcmp(reinterpret_cast<char*>(name->Name), "SHGetKnownFolderPath") && slots->u1.Function == reinterpret_cast<ULONG_PTR>(original))
                replacement = reinterpret_cast<PVOID>(isolated);
            // The executable dynamically loads WHGame, so redirect before its entry point runs.
            if (!strcmp(reinterpret_cast<char*>(name->Name), "LoadLibraryA") && slots->u1.Function == reinterpret_cast<ULONG_PTR>(&LoadLibraryA))
                replacement = reinterpret_cast<PVOID>(load_isolated);
            if (!strcmp(reinterpret_cast<char*>(name->Name), "SHGetFolderPathW")) replacement = reinterpret_cast<PVOID>(folder_w);
            if (!strcmp(reinterpret_cast<char*>(name->Name), "SHGetFolderPathA")) replacement = reinterpret_cast<PVOID>(folder_a);
            if (!strcmp(reinterpret_cast<char*>(name->Name), "GetProcAddress") && slots->u1.Function == reinterpret_cast<ULONG_PTR>(&GetProcAddress)) replacement = reinterpret_cast<PVOID>(proc_isolated);
            if (!replacement) continue;
            DWORD old;
            if (!VirtualProtect(&slots->u1.Function, sizeof(ULONG_PTR), PAGE_READWRITE, &old)) continue;
            InterlockedExchangePointer(reinterpret_cast<PVOID volatile*>(&slots->u1.Function), replacement);
            DWORD ignored;
            VirtualProtect(&slots->u1.Function, sizeof(ULONG_PTR), old, &ignored);
            ++count;
        }
    }
    return count;
}

BOOL WINAPI DllMain(HINSTANCE self, DWORD reason, LPVOID) {
    if (reason != DLL_PROCESS_ATTACH) return TRUE;
    DisableThreadLibraryCalls(self);
    auto len = GetEnvironmentVariableW(L"KCDUS_PROBE_SAVE_ROOT", root, _countof(root));
    if (!len || len >= _countof(root) || root[1] != L':' || root[2] != L'\\') return FALSE;
    wchar_t faultPath[32768];
    if (swprintf_s(faultPath, L"%s\\engine-faults.txt", root) >= 0)
        faultLog = CreateFileW(faultPath, GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
    AddVectoredExceptionHandler(1, diagnose_fault);
    original = reinterpret_cast<decltype(original)>(GetProcAddress(GetModuleHandleW(L"shell32.dll"), "SHGetKnownFolderPath"));
    if (!original) return FALSE;
    unsigned count = patch(GetModuleHandleW(nullptr)) + patch(GetModuleHandleW(L"WHGame.dll"));
    if (!count) return FALSE;
    wchar_t marker[32768];
    if (swprintf_s(marker, L"%s\\isolation-installed.txt", root) < 0) return FALSE;
    HANDLE file = CreateFileW(marker, GENERIC_WRITE, 0, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return FALSE;
    DWORD wrote;
    WriteFile(file, &count, sizeof(count), &wrote, nullptr);
    CloseHandle(file);
    return wrote == sizeof(count);
}
