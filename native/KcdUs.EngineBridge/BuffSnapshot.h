// SPDX-License-Identifier: GPL-3.0-only
// Candidate read-only native buff serialization, owned private probes only.
// The engine writes its own derived-class fields; never copy object memory,
// native pointers, vtables or guessed timer fields into another character.
#pragma once
#include "BoundedBuffStream.h"
static bool private_soul_state_requested;
static bool private_soul_state_faulted;

static bool serialize_native_buff(BYTE* buff,BYTE* output,unsigned capacity,unsigned* size) {
    *size=0;void* root=nullptr;
    __try {
        BuffStream stream={buff_stream_slots,output,0,0,capacity,false};
        using NativeAlloc=void*(*)(size_t,int*,unsigned);
        auto allocator=*reinterpret_cast<NativeAlloc*>(engine+0x3002e68);
        if(!allocator)return false;
        int accounting=0;root=allocator(0x28,&accounting,0);
        if(!root)return false;
        InterlockedAdd(reinterpret_cast<volatile LONG*>(engine+0x3002e90),accounting);
        InterlockedAdd(reinterpret_cast<volatile LONG*>(engine+0x3002e60),0x28);
        InterlockedIncrement(reinterpret_cast<volatile LONG*>(engine+0x3002e70));
        // RPG chunk writer: Write +8, Seek +0x18, Tell +0x20. The native
        // close routine patches lengths and frees native-allocated chunks.
        function<void*(*)(void*,void*,unsigned short)>(0xf32c38)(root,&stream,1);
        virtual_function<void(*)(void*,void*)>(buff,0x38)(buff,root);
        bool closed=function<bool(*)(void*)>(0xf336e0)(root);root=nullptr;
        if(!closed || stream.failed || stream.position!=stream.size || stream.size<6)return false;
        *size=stream.size;return true;
    } __except(EXCEPTION_EXECUTE_HANDLER) {
        // Never attempt to close an uncertain partially constructed native
        // tree after a fault. A private process owns any diagnostic leak.
        private_soul_state_faulted=true;return false;
    }
}

static void private_soul_state(BYTE* soul,void* table) {
    if(!private_soul_state_requested || private_soul_state_faulted)return;
    private_soul_state_requested=false;
    wchar_t enabled[8]{},root[32768]{};
    if(!GetEnvironmentVariableW(L"KCDUS_PROBE_STATE",enabled,_countof(enabled)) || enabled[0]!=L'1' ||
       !GetEnvironmentVariableW(L"KCDUS_PROBE_SAVE_ROOT",root,_countof(root)) ||
       !wcsstr(root,L"\\_work\\KCDUS-engine-") || !wcsstr(GetCommandLineW(),L"KCDUS-engine-"))return;
    BYTE* bytes=nullptr;char* encoded=nullptr;
    __try {
        auto put=function<void(*)(void*,const char*,const void*)>(0x2b6a44);
        char stats[161],skills[529];hex(soul+0x4b4,80,stats);hex(soul+0x540,264,skills);
        const char* value=stats;put(table,"kcdusStatProgressRaw",&value);
        value=skills;put(table,"kcdusSkillProgressRaw",&value);
        auto begin=*reinterpret_cast<BYTE***>(soul+0x490);
        auto end=*reinterpret_cast<BYTE***>(soul+0x498);
        auto low=reinterpret_cast<size_t>(begin),high=reinterpret_cast<size_t>(end);
        if(high<low || (high-low)%sizeof(void*) || high-low>128*sizeof(void*))return;
        int count=static_cast<int>((high-low)/sizeof(void*));
        function<void(*)(void*,const char*,const void*)>(0x2b6a0c)(table,"kcdusNativeBuffCount",&count);
        bytes=static_cast<BYTE*>(HeapAlloc(GetProcessHeap(),0,16384));
        encoded=static_cast<char*>(HeapAlloc(GetProcessHeap(),0,32769));
        if(!bytes || !encoded) { if(bytes)HeapFree(GetProcessHeap(),0,bytes);if(encoded)HeapFree(GetProcessHeap(),0,encoded);return; }
        for(int i=0;i<count && !private_soul_state_faulted;i++) {
            auto buff=begin[i];char key[64];sprintf_s(key,"kcdusNativeBuff%d",i);
            if(!buff)continue;
            char detail[96];sprintf_s(detail,"vt=%llx,owner=%llx,soul=%llx",
                static_cast<unsigned long long>(*reinterpret_cast<BYTE**>(buff)-engine),
                static_cast<unsigned long long>(*reinterpret_cast<size_t*>(buff+0x88)),
                static_cast<unsigned long long>(reinterpret_cast<size_t>(soul)));
            char detailKey[64];sprintf_s(detailKey,"kcdusNativeBuffDetail%d",i);
            value=detail;put(table,detailKey,&value);
            if(*reinterpret_cast<BYTE**>(buff+0x88)!=soul)continue;
            // +0x10 is the original definition used by the native factory.
            // +0x18 is an optional override; base serialization legitimately
            // writes an empty override GUID when this pointer is null.
            auto definition=*reinterpret_cast<BYTE**>(buff+0x10);
            if(!definition)continue;
            char guid[64]{};function<void(*)(const void*,char*)>(0x645ea0)(definition,guid);
            char guidKey[64];sprintf_s(guidKey,"kcdusNativeBuffGuid%d",i);
            value=guid;put(table,guidKey,&value);
            unsigned n=0;
            if(!serialize_native_buff(buff,bytes,16384,&n))continue;
            hex(bytes,n,encoded);value=encoded;put(table,key,&value);
        }
    } __except(EXCEPTION_EXECUTE_HANDLER) { private_soul_state_faulted=true; }
    if(encoded)HeapFree(GetProcessHeap(),0,encoded);
    if(bytes)HeapFree(GetProcessHeap(),0,bytes);
}
