// Test harness only. Redirect Saved Games in this process, never the Windows profile.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shlobj.h>
#include <cstring>
#include <cwchar>

static wchar_t root[32768];
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
