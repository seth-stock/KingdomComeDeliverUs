// SPDX-License-Identifier: GPL-3.0-only
// Private native restore proof. Explicitly supported timed schema only.
#pragma once
#include "TimedBuffRecord.h"
static bool owned_state_probe() {
    wchar_t enabled[8]{},root[32768]{};
    return GetEnvironmentVariableW(L"KCDUS_PROBE_STATE",enabled,_countof(enabled)) && enabled[0]==L'1' &&
        GetEnvironmentVariableW(L"KCDUS_PROBE_SAVE_ROOT",root,_countof(root)) &&
        wcsstr(root,L"\\_work\\KCDUS-engine-") && wcsstr(GetCommandLineW(),L"KCDUS-engine-");
}
static int nibble(char c) {
    if(c>='0' && c<='9')return c-'0';if(c>='a' && c<='f')return c-'a'+10;
    if(c>='A' && c<='F')return c-'A'+10;return -1;
}
static bool load_native_timed_buff(BYTE* buff,const BYTE* bytes,unsigned n) {
    double elapsed=0;
    if(!timed_buff_record(bytes,n,&elapsed))return false;
    __try {
        BuffReader reader={buff_reader_slots,bytes,n,0,false};
        using NativeAlloc=void*(*)(size_t,int*,unsigned);
        auto allocator=*reinterpret_cast<NativeAlloc*>(engine+0x3002e68);
        if(!allocator)return false;
        int accounting=0;auto chunk=static_cast<BYTE*>(allocator(0x20,&accounting,0));
        if(!chunk)return false;
        InterlockedAdd(reinterpret_cast<volatile LONG*>(engine+0x3002e90),accounting);
        InterlockedAdd(reinterpret_cast<volatile LONG*>(engine+0x3002e60),0x20);
        InterlockedIncrement(reinterpret_cast<volatile LONG*>(engine+0x3002e70));
        function<void*(*)(void*,void*)>(0xf32bdc)(chunk,&reader);
        virtual_function<void(*)(void*,void*)>(buff,0x40)(buff,chunk);
        bool closed=function<bool(*)(void*)>(0xf3368c)(chunk);
        return closed && !reader.failed && reader.position==n;
    } __except(EXCEPTION_EXECUTE_HANDLER) { private_soul_state_faulted=true;return false; }
}
static int private_restore_timed_buff(const char* uidText,const char* text) {
    if(!owned_state_probe() || private_soul_state_faulted || !uidText || !text || strlen(uidText)!=16 || strlen(text)!=108)return 0;
    for(int i=0;i<16;i++)if(nibble(uidText[i])<0)return 0;
    unsigned long long uid=_strtoui64(uidText,nullptr,16);
    if((uid>>56)!=5)return 0;
    BYTE input[54];
    for(int i=0;i<54;i++) { int a=nibble(text[i*2]),b=nibble(text[i*2+1]);if(a<0 || b<0)return 0;input[i]=static_cast<BYTE>(a*16+b); }
    double elapsed=0;if(!timed_buff_record(input,sizeof(input),&elapsed))return 0;
    __try {
        auto env=*reinterpret_cast<BYTE**>(engine+0x35ac728);
        auto registry=env?*reinterpret_cast<BYTE**>(env+0x548):nullptr;
        auto soul=registry?function<BYTE*(*)(void*,unsigned long long*)>(0x284b04)(registry+0x48,&uid):nullptr;
        if(!soul || *reinterpret_cast<unsigned long long*>(soul+0x20)!=uid)return 0;
        auto begin=*reinterpret_cast<BYTE***>(soul+0x490),end=*reinterpret_cast<BYTE***>(soul+0x498);
        size_t low=reinterpret_cast<size_t>(begin),high=reinterpret_cast<size_t>(end);
        if(high<low || (high-low)%sizeof(void*) || high-low>128*sizeof(void*))return 0;
        BYTE* selected=nullptr;
        for(unsigned i=0;i<(high-low)/sizeof(void*);i++) {
            auto buff=begin[i];if(!buff || *reinterpret_cast<BYTE**>(buff+0x88)!=soul)continue;
            auto definition=*reinterpret_cast<BYTE**>(buff+0x10);if(!definition)continue;
            char guid[64]{};function<void(*)(const void*,char*)>(0x645ea0)(definition,guid);
            if(strcmp(guid,"c37ab134-a443-433f-92a9-51ff6f08999c"))continue;
            if(selected)return 0;selected=buff;
        }
        // Prove the existing instance has the same schema before invoking its
        // native reader. No buff is added, removed, or rebuilt on uncertainty.
        BYTE current[16384];unsigned count=0;double oldElapsed=0;
        if(!selected || !serialize_native_buff(selected,current,sizeof(current),&count) ||
           !timed_buff_record(current,count,&oldElapsed))return 0;
        if(!load_native_timed_buff(selected,input,sizeof(input)))return 0;
        BYTE after[16384];unsigned afterCount=0;
        if(!serialize_native_buff(selected,after,sizeof(after),&afterCount) || afterCount!=sizeof(input) || memcmp(after,input,sizeof(input)))return 0;
        return 1;
    } __except(EXCEPTION_EXECUTE_HANDLER) { private_soul_state_faulted=true;return 0; }
}
