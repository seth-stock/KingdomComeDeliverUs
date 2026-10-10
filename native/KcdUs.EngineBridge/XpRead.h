// SPDX-License-Identifier: GPL-3.0-only
// Private observation of native applied stat/skill XP. No grants or inferred quest cause.
#pragma once
struct AppliedXp {
    int sequence, kind, stat, modifier, applied, verified;
    unsigned requested, actual, sourceVtableRva, causeVtableRva;
    unsigned long long before, after;
    BYTE soulIdentity[16];
};
static SRWLOCK xp_read_lock=SRWLOCK_INIT;
static AppliedXp xp_read_last{};
static LONG xp_read_sequence;
using StatXpApply=unsigned long long(*)(BYTE*);
static StatXpApply stat_xp_original;
static StatXpApply skill_xp_original;
static BYTE* xp_soul(unsigned long long uid) {
    auto env=*reinterpret_cast<BYTE**>(engine+0x35ac728);
    auto registry=env?*reinterpret_cast<BYTE**>(env+0x548):nullptr;
    auto soul=registry?function<BYTE*(*)(void*,unsigned long long*)>(0x284b04)(registry+0x48,&uid):nullptr;
    return soul && *reinterpret_cast<unsigned long long*>(soul+0x20)==uid?soul:nullptr;
}
static bool xp_before(BYTE* effect,AppliedXp* read,unsigned long long* uid) {
    __try {
        read->stat=*reinterpret_cast<unsigned*>(effect+0x18);
        if(read->stat<0 || read->stat>=(read->kind?33:10))return false;
        *uid=*reinterpret_cast<unsigned long long*>(effect+0x10);
        if((*uid>>56)!=5)return false;
        auto soul=xp_soul(*uid);if(!soul)return false;
        memcpy(read->soulIdentity,soul+0x38,16);
        memcpy(&read->before,soul+(read->kind?0x540:0x4b4)+read->stat*8,8);
        read->requested=*reinterpret_cast<unsigned*>(effect+0x1c);
        read->modifier=effect[read->kind?0x38:0x34]?1:0;
        auto source=*reinterpret_cast<BYTE**>(effect+8);
        if(source) {
            auto vt=*reinterpret_cast<BYTE**>(source);
            auto nt=reinterpret_cast<IMAGE_NT_HEADERS*>(engine+reinterpret_cast<IMAGE_DOS_HEADER*>(engine)->e_lfanew);
            size_t offset=reinterpret_cast<size_t>(vt)-reinterpret_cast<size_t>(engine);
            if(offset<nt->OptionalHeader.SizeOfImage)read->sourceVtableRva=static_cast<unsigned>(offset);
            // effect+8 is C_EffectSource, not the cause. Its verified native
            // constructor owns I_Cause at +8 and its effects vector at +0x10.
            if(vt==engine+0x21beb08) {
                auto cause=*reinterpret_cast<BYTE**>(source+8);
                if(cause) {
                    auto causeVt=*reinterpret_cast<BYTE**>(cause);
                    offset=reinterpret_cast<size_t>(causeVt)-reinterpret_cast<size_t>(engine);
                    if(offset<nt->OptionalHeader.SizeOfImage)read->causeVtableRva=static_cast<unsigned>(offset);
                }
            }
        }
        return true;
    } __except(EXCEPTION_EXECUTE_HANDLER) { return false; }
}
static void xp_after(BYTE* effect,AppliedXp* read,unsigned long long uid) {
    __try {
        // Resolve again: callbacks may have unloaded/replaced the original soul.
        auto soul=xp_soul(uid);
        if(!soul || memcmp(soul+0x38,read->soulIdentity,16))return;
        memcpy(&read->after,soul+(read->kind?0x540:0x4b4)+read->stat*8,8);
        read->actual=*reinterpret_cast<unsigned*>(effect+0x20);
        read->verified=1;
    } __except(EXCEPTION_EXECUTE_HANDLER) { }
}
static unsigned long long observe_xp(BYTE* effect,int kind,StatXpApply original) {
    AppliedXp read{};read.kind=kind;unsigned long long uid=0;
    bool captured=xp_before(effect,&read,&uid);
    // Never hold a lock or alter the engine's call count/result. Exceptions
    // in the original application retain their original engine semantics.
    auto result=original(effect);
    if(captured) {
        read.applied=(result&255)?1:0;
        xp_after(effect,&read,uid);
        AcquireSRWLockExclusive(&xp_read_lock);
        // Assignment under the publication lock also preserves completion order
        // when native application is reentrant or concurrent.
        if(xp_read_sequence<LONG_MAX)++xp_read_sequence;
        read.sequence=static_cast<int>(xp_read_sequence);
        xp_read_last=read;
        ReleaseSRWLockExclusive(&xp_read_lock);
    }
    return result;
}
static unsigned long long stat_xp_observer(BYTE* effect) { return observe_xp(effect,0,stat_xp_original); }
static unsigned long long skill_xp_observer(BYTE* effect) { return observe_xp(effect,1,skill_xp_original); }
static void install_xp_slot(size_t vtable,size_t apply,StatXpApply observer,StatXpApply* original) {
    auto slot=reinterpret_cast<void**>(engine+vtable+0x18);
    auto expected=engine+apply;
    if(*slot!=expected)return;
    DWORD old;if(!VirtualProtect(slot,sizeof(void*),PAGE_READWRITE,&old))return;
    *original=reinterpret_cast<StatXpApply>(expected);
    auto previous=InterlockedCompareExchangePointer(slot,reinterpret_cast<void*>(observer),expected);
    DWORD ignored;VirtualProtect(slot,sizeof(void*),old,&ignored);
    if(previous!=expected)*original=nullptr;
}
static void install_xp_read() {
    if(!owned_state_probe())return;
    install_xp_slot(0x21c7a48,0x6ac280,stat_xp_observer,&stat_xp_original);
    install_xp_slot(0x21c7a00,0x5a40b8,skill_xp_observer,&skill_xp_original);
}
static int xp_read_binding(void* binding,void* handler) {
    if(!owned_state_probe())return virtual_function<int(*)(void*)>(handler,0x58)(handler);
    AppliedXp read{};
    AcquireSRWLockShared(&xp_read_lock);read=xp_read_last;ReleaseSRWLockShared(&xp_read_lock);
    auto script=*reinterpret_cast<void**>(static_cast<BYTE*>(binding)+0x50);
    auto table=virtual_function<void*(*)(void*,bool)>(script,0x68)(script,false);
    if(!table)return virtual_function<int(*)(void*)>(handler,0x58)(handler);
    virtual_function<void(*)(void*)>(table,0x18)(table);
    auto putInt=function<void(*)(void*,const char*,const void*)>(0x2b6a0c);
    auto putText=function<void(*)(void*,const char*,const void*)>(0x2b6a44);
    putInt(table,"sequence",&read.sequence);putInt(table,"kind",&read.kind);putInt(table,"stat",&read.stat);
    putInt(table,"modifier",&read.modifier);putInt(table,"applied",&read.applied);
    putInt(table,"verified",&read.verified);putInt(table,"sourceVtableRva",&read.sourceVtableRva);putInt(table,"causeVtableRva",&read.causeVtableRva);
    char identity[33],before[17],after[17],requested[9],actual[9];
    hex(read.soulIdentity,16,identity);hex(reinterpret_cast<BYTE*>(&read.before),8,before);
    hex(reinterpret_cast<BYTE*>(&read.after),8,after);hex(reinterpret_cast<BYTE*>(&read.requested),4,requested);
    hex(reinterpret_cast<BYTE*>(&read.actual),4,actual);
    const char* text=identity;putText(table,"soulIdentity",&text);
    text=before;putText(table,"before",&text);text=after;putText(table,"after",&text);
    text=requested;putText(table,"requestedRaw",&text);text=actual;putText(table,"actualRaw",&text);
    int result=function<int(*)(void*,void*)>(0x2b5f18)(handler,&table);
    virtual_function<void(*)(void*)>(table,0x20)(table);return result;
}
