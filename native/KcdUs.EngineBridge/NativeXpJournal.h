// SPDX-License-Identifier: GPL-3.0-only
// Persist native quest XP observations before and after the original call.
// Intent without result is uncertain, never permission to apply XP again.
#pragma once
#include "NativeXpRecord.h"
static SRWLOCK native_xp_journal_lock=SRWLOCK_INIT;
static HANDLE native_xp_file=INVALID_HANDLE_VALUE;
static BCRYPT_ALG_HANDLE native_xp_algorithm;
static BYTE native_xp_boot[16],native_xp_previous[32];
static unsigned long long native_xp_sequence;
static bool native_xp_open_attempted,native_xp_failed;
static wchar_t native_xp_path[32768];
static char native_xp_world[64];
static unsigned native_xp_flags;
static thread_local unsigned native_xp_replay_depth;

static bool native_xp_directory(const wchar_t* path) {
    if(CreateDirectoryW(path,nullptr))return true;
    if(GetLastError()!=ERROR_ALREADY_EXISTS)return false;
    DWORD attributes=GetFileAttributesW(path);
    return attributes!=INVALID_FILE_ATTRIBUTES && (attributes&FILE_ATTRIBUTE_DIRECTORY)!=0;
}
// Called under the journal lock, outside any native engine call.
static bool native_xp_open() {
    if(native_xp_open_attempted)return native_xp_file!=INVALID_HANDLE_VALUE && !native_xp_failed;
    native_xp_open_attempted=true;
    wchar_t root[32768]{},folder[32768]{};
    if(GetEnvironmentVariableW(L"KCDUS_PROBE_SAVE_ROOT",root,_countof(root)) &&
        wcsstr(root,L"\\_work\\KCDUS-engine-") && wcsstr(GetCommandLineW(),L"KCDUS-engine-")) {
        if(swprintf_s(folder,L"%s\\native-effects",root)<0 || !native_xp_directory(folder))return false;
    } else {
        DWORD n=GetEnvironmentVariableW(L"LOCALAPPDATA",root,_countof(root));
        if(!n || n>=_countof(root) || swprintf_s(folder,L"%s\\KcdUs",root)<0 || !native_xp_directory(folder))return false;
        if(swprintf_s(folder,L"%s\\KcdUs\\native-effects",root)<0 || !native_xp_directory(folder))return false;
    }
    if(BCryptGenRandom(nullptr,native_xp_boot,sizeof(native_xp_boot),BCRYPT_USE_SYSTEM_PREFERRED_RNG)<0 ||
        BCryptOpenAlgorithmProvider(&native_xp_algorithm,BCRYPT_SHA256_ALGORITHM,nullptr,0)<0)return false;
    char boot[33];hex(native_xp_boot,sizeof(native_xp_boot),boot);
    if(swprintf_s(native_xp_path,L"%s\\%hs.nxp",folder,boot)<0)return false;
    native_xp_file=CreateFileW(native_xp_path,GENERIC_READ|GENERIC_WRITE,FILE_SHARE_READ,nullptr,
        CREATE_NEW,FILE_ATTRIBUTE_NORMAL|FILE_FLAG_WRITE_THROUGH,nullptr);
    return native_xp_file!=INVALID_HANDLE_VALUE;
}
static bool native_xp_hash(const NativeXpRecord& record,BYTE* result) {
    BCRYPT_HASH_HANDLE hash=nullptr;
    bool ok=BCryptCreateHash(native_xp_algorithm,&hash,nullptr,0,nullptr,0,0)>=0;
    if(ok)ok=BCryptHashData(hash,reinterpret_cast<PUCHAR>(const_cast<NativeXpRecord*>(&record)),offsetof(NativeXpRecord,hash),0)>=0;
    if(ok)ok=BCryptFinishHash(hash,result,32,0)>=0;
    if(hash)BCryptDestroyHash(hash);return ok;
}
static unsigned long long native_xp_append(const AppliedXp& read,unsigned phase,unsigned long long event) {
    if(!read.requested || (read.causeVtableRva!=0x2306d30 && read.causeVtableRva!=0x21c8fd0))return 0;
    AcquireSRWLockExclusive(&native_xp_journal_lock);
    NativeXpRecord r{};
    bool ok=native_xp_open() && !native_xp_failed && native_xp_sequence<1000000;
    if(ok) {
        r.magic=native_xp_magic;r.version=1;r.size=sizeof(r);r.phase=phase;
        r.sequence=native_xp_sequence+1;r.event=phase==1?r.sequence:event;
        memcpy(r.boot,native_xp_boot,16);memcpy(r.soul,read.soulIdentity,16);
        r.before=read.before;r.after=phase==2?read.after:0;
        r.kind=static_cast<unsigned>(read.kind);r.index=static_cast<unsigned>(read.stat);r.requested=read.requested;
        r.actual=phase==2?read.actual:0;r.applied=phase==2?static_cast<unsigned>(read.applied):0;
        r.verified=phase==2?static_cast<unsigned>(read.verified):0;
        r.causeRva=read.causeVtableRva;r.causeLength=static_cast<unsigned>(read.causeDataLength);
        memcpy(r.cause,read.causeData,r.causeLength==24?20:16);
        memcpy(r.world,read.world,64);r.flags=read.contextFlags;
        memcpy(r.previous,native_xp_previous,32);
        ok=native_xp_record_shape(r) && native_xp_hash(r,r.hash);
    }
    if(ok) {
        LARGE_INTEGER at;at.QuadPart=static_cast<LONGLONG>(native_xp_sequence*sizeof(r));
        LARGE_INTEGER size{};DWORD written=0;
        ok=GetFileSizeEx(native_xp_file,&size) && size.QuadPart==at.QuadPart &&
            SetFilePointerEx(native_xp_file,at,nullptr,FILE_BEGIN) &&
            WriteFile(native_xp_file,&r,sizeof(r),&written,nullptr) && written==sizeof(r) && FlushFileBuffers(native_xp_file);
    }
    if(ok) { native_xp_sequence=r.sequence;memcpy(native_xp_previous,r.hash,32); }
    else native_xp_failed=true;
    ReleaseSRWLockExclusive(&native_xp_journal_lock);
    return ok?r.event:0;
}
static int native_xp_context_binding(void* handler,const char* world,const char* mode) {
    unsigned flags=0;char context[64]{};
    if(world && mode && *world && strlen(world)<=64 && (!strcmp(mode,"host") || !strcmp(mode,"guest"))) {
        bool valid=true;
        for(const char* p=world;*p;p++)if(!((*p>='a' && *p<='z') || (*p>='A' && *p<='Z') ||
            (*p>='0' && *p<='9') || *p=='-' || *p=='_' || *p=='.'))valid=false;
        if(valid) { memcpy(context,world,strlen(world));flags=!strcmp(mode,"host")?3:1; }
    }
    AcquireSRWLockExclusive(&native_xp_journal_lock);
    memcpy(native_xp_world,context,64);native_xp_flags=flags;
    ReleaseSRWLockExclusive(&native_xp_journal_lock);
    return virtual_function<int(*)(void*)>(handler,0x58)(handler);
}
static int native_xp_journal_binding(void* binding,void* handler) {
    wchar_t path[32768]{};BYTE boot[16]{};int healthy=0;unsigned long long sequence=0;
    AcquireSRWLockShared(&native_xp_journal_lock);
    healthy=native_xp_open_attempted && native_xp_file!=INVALID_HANDLE_VALUE && !native_xp_failed?1:0;
    wcscpy_s(path,native_xp_path);memcpy(boot,native_xp_boot,16);sequence=native_xp_sequence;
    ReleaseSRWLockShared(&native_xp_journal_lock);
    auto script=*reinterpret_cast<void**>(static_cast<BYTE*>(binding)+0x50);
    auto table=virtual_function<void*(*)(void*,bool)>(script,0x68)(script,false);
    if(!table)return virtual_function<int(*)(void*)>(handler,0x58)(handler);
    virtual_function<void(*)(void*)>(table,0x18)(table);
    auto put=function<void(*)(void*,const char*,const void*)>(0x2b6a44);
    char bootText[33],seq[32],utf8[65536]{};hex(boot,16,bootText);sprintf_s(seq,"%llu",sequence);
    WideCharToMultiByte(CP_UTF8,WC_ERR_INVALID_CHARS,path,-1,utf8,sizeof(utf8),nullptr,nullptr);
    const char* value=utf8;put(table,"path",&value);value=bootText;put(table,"boot",&value);value=seq;put(table,"sequence",&value);
    function<void(*)(void*,const char*,const void*)>(0x2b6a0c)(table,"healthy",&healthy);
    int result=function<int(*)(void*,void*)>(0x2b5f18)(handler,&table);
    virtual_function<void(*)(void*)>(table,0x20)(table);return result;
}
// The live mod consumes completed records from disk, independent of the
// diagnostic 64-row ring. Cursor counts physical records, including intents.
static int native_xp_queue_binding(void* binding,void* handler,const char* afterText) {
    char* end=nullptr;unsigned long after=afterText?strtoul(afterText,&end,10):0;
    if(!afterText || !*afterText || !end || *end || after>1000000)after=0;
    NativeXpRecord rows[16]{};int n=0,last=0,cursor=static_cast<int>(after),healthy=1,available=1;BYTE boot[16]{};
    AcquireSRWLockExclusive(&native_xp_journal_lock);
    last=static_cast<int>(native_xp_sequence);memcpy(boot,native_xp_boot,16);
    healthy=native_xp_failed?0:1;
    if(healthy && native_xp_file!=INVALID_HANDLE_VALUE && after<=native_xp_sequence) {
        LARGE_INTEGER at;at.QuadPart=static_cast<LONGLONG>(after*sizeof(NativeXpRecord));
        if(!SetFilePointerEx(native_xp_file,at,nullptr,FILE_BEGIN))healthy=0;
        for(unsigned scanned=0;healthy && cursor<last && n<16 && scanned<64;scanned++) {
            NativeXpRecord r{};DWORD read=0;BYTE digest[32]{};
            if(!ReadFile(native_xp_file,&r,sizeof(r),&read,nullptr) || read!=sizeof(r) ||
                !native_xp_record_shape(r) || r.sequence!=static_cast<unsigned long long>(cursor)+1 ||
                memcmp(r.boot,boot,16) || !native_xp_hash(r,digest) || memcmp(r.hash,digest,32)) { healthy=0;break; }
            cursor=static_cast<int>(r.sequence);
            if(r.phase==2)rows[n++]=r;
        }
    }
    if(!healthy)native_xp_failed=true;
    ReleaseSRWLockExclusive(&native_xp_journal_lock);
    auto script=*reinterpret_cast<void**>(static_cast<BYTE*>(binding)+0x50);
    auto table=virtual_function<void*(*)(void*,bool)>(script,0x68)(script,false);
    if(!table)return virtual_function<int(*)(void*)>(handler,0x58)(handler);
    virtual_function<void(*)(void*)>(table,0x18)(table);
    auto putInt=function<void(*)(void*,const char*,const void*)>(0x2b6a0c);
    auto putText=function<void(*)(void*,const char*,const void*)>(0x2b6a44);
    putInt(table,"n",&n);putInt(table,"last",&last);putInt(table,"cursor",&cursor);
    putInt(table,"healthy",&healthy);putInt(table,"available",&available);
    char bootText[33];hex(boot,16,bootText);const char* value=bootText;putText(table,"boot",&value);
    for(int i=0;i<n;i++) {
        char identity[33],cause[49],row[300],key[8];hex(rows[i].soul,16,identity);hex(rows[i].cause,rows[i].causeLength,cause);
        sprintf_s(row,"%llu,%u,%u,%u,%u,%u,%u,%x,%s,%s,%.*s,%u",rows[i].event,rows[i].kind,rows[i].index,
            rows[i].requested,rows[i].actual,rows[i].applied,rows[i].verified,rows[i].causeRva,identity,cause,64,rows[i].world,rows[i].flags);
        sprintf_s(key,"e%d",i+1);value=row;putText(table,key,&value);
    }
    int result=function<int(*)(void*,void*)>(0x2b5f18)(handler,&table);
    virtual_function<void(*)(void*)>(table,0x20)(table);return result;
}
