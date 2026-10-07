// SPDX-License-Identifier: GPL-3.0-only
// Original code. Retail KCD1 1.9.8 adapter; no game code or assets included.
// Installed during suspended startup, before WHGame/script initialization.
// Unsupported engine files remain untouched. Do not attach to a running game.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <bcrypt.h>
#include <cstring>
#include <cstdlib>

static BYTE* engine;
template<class T> static T function(size_t rva) { return reinterpret_cast<T>(engine+rva); }
template<class T> static T virtual_function(void* object,size_t slot) {
    return reinterpret_cast<T>((*reinterpret_cast<BYTE***>(object))[slot/sizeof(void*)]);
}
static bool supported(HMODULE module) {
    wchar_t path[32768];if(!GetModuleFileNameW(module,path,_countof(path)))return false;
    HANDLE file=CreateFileW(path,GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);
    if(file==INVALID_HANDLE_VALUE)return false;
    BCRYPT_ALG_HANDLE algorithm=nullptr;BCRYPT_HASH_HANDLE hash=nullptr;
    bool ok=BCryptOpenAlgorithmProvider(&algorithm,BCRYPT_SHA256_ALGORITHM,nullptr,0)>=0;
    if(ok)ok=BCryptCreateHash(algorithm,&hash,nullptr,0,nullptr,0,0)>=0;
    BYTE block[32768],digest[32];DWORD n;
    while(ok) {
        if(!ReadFile(file,block,sizeof(block),&n,nullptr)){ok=false;break;}
        if(!n)break;
        ok=BCryptHashData(hash,block,n,0)>=0;
    }
    if(ok)ok=BCryptFinishHash(hash,digest,sizeof(digest),0)>=0;
    if(hash)BCryptDestroyHash(hash);if(algorithm)BCryptCloseAlgorithmProvider(algorithm,0);CloseHandle(file);
    const BYTE expected[]={0xcf,0x9f,0x6d,0xc3,0x84,0xed,0xcf,0x35,0xc2,0x06,0x47,0xa9,0x12,0x74,0x5d,0xdb,
        0x8a,0xdb,0x5b,0xa6,0x59,0x53,0xe3,0x29,0xc2,0x4e,0x89,0xdd,0x9c,0x43,0x81,0xaa};
    return ok && !memcmp(digest,expected,sizeof(expected));
}
static void hex(const BYTE* source,size_t length,char* output) {
    const char* digits="0123456789abcdef";
    for(size_t i=0;i<length;i++){output[2*i]=digits[source[i]>>4];output[2*i+1]=digits[source[i]&15];}
    output[2*length]=0;
}
static int soul_info(void* binding,void* handler,unsigned long long uid) {
    // Explicit extension: GetItem accepts a type-5 soul WUID for read-only
    // identity lookup. Type-2 item behavior remains unchanged.
    auto env=*reinterpret_cast<BYTE**>(engine+0x35ac728);
    auto registry=env?*reinterpret_cast<BYTE**>(env+0x548):nullptr;
    auto soul=registry?function<BYTE*(*)(void*,unsigned long long*)>(0x284b04)(registry+0x48,&uid):nullptr;
    if(!soul || *reinterpret_cast<unsigned long long*>(soul+0x20)!=uid)
        return virtual_function<int(*)(void*)>(handler,0x58)(handler);
    auto script=*reinterpret_cast<void**>(static_cast<BYTE*>(binding)+0x50);
    auto table=virtual_function<void*(*)(void*,bool)>(script,0x68)(script,false);
    if(!table)return virtual_function<int(*)(void*)>(handler,0x58)(handler);
    virtual_function<void(*)(void*)>(table,0x18)(table);
    char persistent[33];hex(soul+0x38,16,persistent);const char* text=persistent;
    function<void(*)(void*,const char*,const void*)>(0x2b6a44)(table,"kcdusPersistent",&text);
    int version=1;function<void(*)(void*,const char*,const void*)>(0x2b6a0c)(table,"kcdusSoul",&version);
    int result=function<int(*)(void*,void*)>(0x2b5f18)(handler,&table);
    virtual_function<void(*)(void*)>(table,0x20)(table);return result;
}
static int item_info(void* binding,void* handler,unsigned long long uid) {
    if((uid>>56)==5)return soul_info(binding,handler,uid);
    auto manager=*reinterpret_cast<BYTE**>(static_cast<BYTE*>(binding)+0x60);
    auto item=function<BYTE*(*)(void*,unsigned long long*)>(0x454638)(manager+0x18,&uid);
    if(!item)return virtual_function<int(*)(void*)>(handler,0x58)(handler);
    auto script=*reinterpret_cast<void**>(static_cast<BYTE*>(binding)+0x50);
    auto table=virtual_function<void*(*)(void*,bool)>(script,0x68)(script,false);
    if(!table)return virtual_function<int(*)(void*)>(handler,0x58)(handler);
    virtual_function<void(*)(void*)>(table,0x18)(table);
    auto put_handle=function<void(*)(void*,const char*,const void*)>(0x2b8bcc);
    auto put_text=function<void(*)(void*,const char*,const void*)>(0x2b6a44);
    auto put_float=function<void(*)(void*,const char*,const void*)>(0x2b69d8);
    auto put_int=function<void(*)(void*,const char*,const void*)>(0x2b6a0c);
    auto put_entity=function<void(*)(void*,const char*,const void*)>(0x10b35d0);
    put_handle(table,"id",item+8);
    char guid[64]={};function<void(*)(const void*,char*)>(0x645ea0)(item+0x28,guid);
    const char* text=guid;put_text(table,"class",&text);
    put_float(table,"health",item+0x3c);put_int(table,"amount",item+0x38);
    put_entity(table,"entity",item+0x60);
    char persistent[33];hex(item+0x10,16,persistent);const char* instance=persistent;
    put_text(table,"kcdusPersistent",&instance);
    int equipped=item[0x48]&1,version=1;
    put_int(table,"kcdusEquipped",&equipped);put_int(table,"kcdusBridge",&version);
    int result=function<int(*)(void*,void*)>(0x2b5f18)(handler,&table);
    virtual_function<void(*)(void*)>(table,0x20)(table);
    return result;
}
static void initialize(HMODULE module) {
    if(engine || !module)return;
    wchar_t name[MAX_PATH];if(!GetModuleFileNameW(module,name,_countof(name)))return;
    auto leaf=wcsrchr(name,L'\\');if(!leaf || _wcsicmp(leaf+1,L"WHGame.dll") || !supported(module))return;
    auto base=reinterpret_cast<BYTE*>(module);auto entry=base+0x10c9a98;
    const BYTE signature[]={0x48,0x89,0x5c,0x24,0x10,0x48,0x89,0x74,0x24,0x20,0x55,0x57,0x41,0x56,0x48,0x8b,0xec};
    if(memcmp(entry,signature,sizeof(signature)))return;
    // The game DLL has just loaded; script initialization has not run yet.
    DWORD old;if(!VirtualProtect(entry,sizeof(signature),PAGE_EXECUTE_READWRITE,&old))return;
    HMODULE pinned=nullptr;
    if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_PIN,
        reinterpret_cast<LPCWSTR>(item_info),&pinned)) {
        DWORD ignored;VirtualProtect(entry,sizeof(signature),old,&ignored);return;
    }
    engine=base;
    const BYTE jump[]={0xff,0x25,0,0,0,0};memcpy(entry,jump,sizeof(jump));
    *reinterpret_cast<void**>(entry+sizeof(jump))=reinterpret_cast<void*>(item_info);
    memset(entry+14,0x90,3);DWORD ignored;VirtualProtect(entry,sizeof(signature),old,&ignored);
    FlushInstructionCache(GetCurrentProcess(),entry,sizeof(signature));
}
static decltype(&LoadLibraryA) previous_loader;
static HMODULE WINAPI load_game(LPCSTR name) {
    HMODULE module=previous_loader(name);initialize(module);return module;
}
static bool startup() {
    // A late attachment is refused; the launcher must own a suspended child.
    if(GetModuleHandleW(L"WHGame.dll"))return false;
    auto base=reinterpret_cast<BYTE*>(GetModuleHandleW(nullptr));
    auto dos=reinterpret_cast<IMAGE_DOS_HEADER*>(base);
    if(dos->e_magic!=IMAGE_DOS_SIGNATURE)return false;
    auto nt=reinterpret_cast<IMAGE_NT_HEADERS*>(base+dos->e_lfanew);
    auto dir=nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];if(!dir.VirtualAddress)return false;
    for(auto desc=reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(base+dir.VirtualAddress);desc->Name;++desc) {
        if(!desc->OriginalFirstThunk)continue;
        auto names=reinterpret_cast<IMAGE_THUNK_DATA*>(base+desc->OriginalFirstThunk);
        auto slots=reinterpret_cast<IMAGE_THUNK_DATA*>(base+desc->FirstThunk);
        for(;names->u1.AddressOfData;++names,++slots) {
            if(IMAGE_SNAP_BY_ORDINAL(names->u1.Ordinal))continue;
            auto symbol=reinterpret_cast<IMAGE_IMPORT_BY_NAME*>(base+names->u1.AddressOfData);
            if(strcmp(reinterpret_cast<char*>(symbol->Name),"LoadLibraryA"))continue;
            DWORD old;if(!VirtualProtect(&slots->u1.Function,sizeof(void*),PAGE_READWRITE,&old))return false;
            previous_loader=reinterpret_cast<decltype(previous_loader)>(slots->u1.Function);
            InterlockedExchangePointer(reinterpret_cast<void* volatile*>(&slots->u1.Function),reinterpret_cast<void*>(load_game));
            DWORD ignored;VirtualProtect(&slots->u1.Function,sizeof(void*),old,&ignored);return true;
        }
    }
    return false;
}
BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,LPVOID) {
    if(reason==DLL_PROCESS_ATTACH){DisableThreadLibraryCalls(instance);return startup()?TRUE:FALSE;}
    return TRUE;
}
