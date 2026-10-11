// SPDX-License-Identifier: GPL-3.0-only
// Exact KCD1 native quest XP construction. The event owns its cause and effects;
// use the game's constructors/factories and release its retained reference.
#pragma once

struct QuestXpRequest {
    unsigned long long soul;
    unsigned kind, index, amount, causeRva;
    BYTE cause[24];
    unsigned causeLength;
    char world[65];
};
struct QuestXpResult {
    int version, reason, applied, verified, kind, index;
    unsigned amount, actual, causeRva;
    unsigned long long before, after;
};
static bool quest_xp_faulted;

static bool quest_xp_hex(const char* s,unsigned n,BYTE* out) {
    if(!s || strlen(s)!=n*2)return false;
    for(unsigned i=0;i<n;i++) {
        int a=nibble(s[i*2]),b=nibble(s[i*2+1]);
        if(a<0 || b<0)return false;
        out[i]=static_cast<BYTE>((a<<4)|b);
    }
    return true;
}
static bool quest_xp_number(const char* s,unsigned maximum,unsigned* out) {
    if(!s || !*s)return false;
    unsigned value=0;
    for(;*s;s++) {
        if(*s<'0' || *s>'9')return false;
        unsigned digit=static_cast<unsigned>(*s-'0');
        if(digit>maximum || value>(maximum-digit)/10)return false;
        value=value*10+digit;
    }
    *out=value;return true;
}
// tags: kind,index,rawAmount,causeRvaHex,causeDataHex. No pointers arrive over Lua.
static bool quest_xp_request(const char* soulText,const char* text,QuestXpRequest* out) {
    *out={};BYTE uidBytes[8];
    if(!quest_xp_hex(soulText,8,uidBytes) || !text || strlen(text)>320)return false;
    out->soul=_strtoui64(soulText,nullptr,16);
    if((out->soul>>56)!=5)return false;
    char copy[321];strcpy_s(copy,text);char* fields[7]={copy};unsigned n=1;
    for(char* p=copy;*p;p++)if(*p==',') {
        if(n==7)return false;*p=0;fields[n++]=p+1;
    }
    if((n!=5 && n!=7) || !quest_xp_number(fields[0],1,&out->kind) ||
       !quest_xp_number(fields[1],out->kind?32u:9u,&out->index) ||
       !quest_xp_number(fields[2],128u*100000u,&out->amount) || !out->amount)return false;
    if(!strcmp(fields[3],"2306d30")) { out->causeRva=0x2306d30;out->causeLength=16; }
    else if(!strcmp(fields[3],"21c8fd0")) { out->causeRva=0x21c8fd0;out->causeLength=24; }
    else return false;
    if(!quest_xp_hex(fields[4],out->causeLength,out->cause))return false;
    if(n==7) {
        unsigned keyLength=static_cast<unsigned>(strlen(fields[5]));
        if(keyLength<35 || keyLength>51 || fields[5][0]!='n' || fields[5][33]!='.')return false;
        for(unsigned i=1;i<33;i++)if(nibble(fields[5][i])<0)return false;
        unsigned sequence=0;if(!quest_xp_number(fields[5]+34,1000000,&sequence) || !sequence)return false;
        unsigned length=static_cast<unsigned>(strlen(fields[6]));if(!length || length>64)return false;
        for(unsigned i=0;i<length;i++)if(!((fields[6][i]>='a' && fields[6][i]<='z') ||
            (fields[6][i]>='A' && fields[6][i]<='Z') || (fields[6][i]>='0' && fields[6][i]<='9') ||
            fields[6][i]=='-' || fields[6][i]=='_' || fields[6][i]=='.'))return false;
        strcpy_s(out->world,fields[6]);
    }
    // S_Quest*Data begins with the rewarded soul's WUID. Native quest code
    // supplies quest id, objective id and repeat counter, never pointers.
    // Objective data has four trailing padding bytes; they are not identity.
    memcpy(out->cause,&out->soul,8);
    if(out->causeLength==24)memset(out->cause+20,0,4);
    return true;
}
static void quest_xp_apply(void* binding,void* handler,const QuestXpRequest* request,QuestXpResult* out) {
    *out={1,1,0,0,static_cast<int>(request->kind),static_cast<int>(request->index),request->amount,0,request->causeRva,0,0};
    bool context=false;
    AcquireSRWLockShared(&native_xp_journal_lock);
    context=native_xp_flags==1 && request->world[0] &&
        strlen(request->world)<=64 && !memcmp(request->world,native_xp_world,strlen(request->world)) &&
        (strlen(request->world)==64 || native_xp_world[strlen(request->world)]==0);
    ReleaseSRWLockShared(&native_xp_journal_lock);
    // Production calls require a connected guest context in the same world.
    // Five-field constructor probes remain limited to our disposable profiles.
    if((!context && !owned_state_probe()) || quest_xp_faulted)return;
    AcquireSRWLockExclusive(&native_xp_journal_lock);
    bool journalReady=native_xp_open() && !native_xp_failed;
    ReleaseSRWLockExclusive(&native_xp_journal_lock);
    if(!journalReady) { out->reason=8;return; }
    BYTE* event=nullptr;bool replaying=false;
    __try {
        auto actor=function<BYTE*(*)(void*,void*)>(0x10c98a0)(binding,handler);
        if(!actor || *reinterpret_cast<unsigned*>(actor+0x30)!=0x7777) { out->reason=2;return; }
        unsigned long long localUid=0;
        function<void*(*)(void*,void*)>(0x39f320)(actor,&localUid);
        if(localUid!=request->soul) { out->reason=2;return; }
        auto soul=xp_soul(request->soul);
        if(!soul) { out->reason=3;return; }
        BYTE identity[16];memcpy(identity,soul+0x38,16);
        unsigned offset=(request->kind?0x540u:0x4b4u)+request->index*8u;
        memcpy(&out->before,soul+offset,8);
        auto modules=function<BYTE*(*)()>(0x430aa4)();
        auto questModule=modules?*reinterpret_cast<BYTE**>(modules+0xe0):nullptr;
        auto manager=questModule?*reinterpret_cast<BYTE**>(questModule+0x88):nullptr;
        if(!manager || *reinterpret_cast<BYTE**>(manager)!=engine+0x26e5be8) { out->reason=7;return; }
        unsigned questId=0;memcpy(&questId,request->cause+8,4);
        auto quest=function<BYTE*(*)(void*,unsigned)>(0x5d4c44)(*reinterpret_cast<void**>(manager+0x20),questId);
        if(!quest || *reinterpret_cast<unsigned*>(quest+0x8c)!=questId) { out->reason=7;return; }
        if(request->causeLength==24) {
            unsigned objective=0,repeat=0;memcpy(&objective,request->cause+12,4);memcpy(&repeat,request->cause+16,4);
            void* node=nullptr;
            function<void*(*)(void*,void**,const unsigned*)>(0x20a7f0)(quest+0xd8,&node,&objective);
            if(!node || node==*reinterpret_cast<void**>(quest+0xd8) ||
                *reinterpret_cast<unsigned*>(quest+0xd4)!=repeat) { out->reason=7;return; }
        }
        // Use the same RPGQuest factory as QuestManager. It constructs C_Event,
        // the typed cause, and C_SoulStatsLogEffect through native allocators.
        auto env=*reinterpret_cast<BYTE**>(engine+0x35ac728);
        auto factory=env?*reinterpret_cast<BYTE**>(env+0x568):nullptr;
        auto events=env?*reinterpret_cast<BYTE**>(env+0x578):nullptr;
        if(!factory || !events || *reinterpret_cast<BYTE**>(factory)!=engine+0x26d9540 ||
            *reinterpret_cast<BYTE**>(events)!=engine+0x26d3410) { out->reason=4;return; }
        virtual_function<void*(*)(void*,void**,const void*)>(factory,request->causeLength==16?8:0)
            (factory,reinterpret_cast<void**>(&event),request->cause);
        if(!event) { out->reason=4;return; }
        if(*reinterpret_cast<BYTE**>(event)!=engine+0x21beb08) { out->reason=4;quest_xp_faulted=true;return; }
        auto effect=function<BYTE*(*)(void*,void*,unsigned long long,unsigned,const unsigned*)>(
            request->kind?0x5a4274:0x5a4450)(nullptr,event,request->soul,request->index,&request->amount);
        if(!effect || *reinterpret_cast<BYTE**>(effect)!=engine+(request->kind?0x21c7a00:0x21c7a48)) { out->reason=4; }
        else {
            // The game's event dispatcher applies all effects and queues the
            // retained event for native logging/observers. Do not bypass it.
            ++native_xp_replay_depth;replaying=true;
            auto result=virtual_function<bool(*)(void*,void*)>(events,0)(events,event);
            --native_xp_replay_depth;replaying=false;
            out->applied=result?1:0;
            out->actual=*reinterpret_cast<unsigned*>(effect+0x20);
            soul=xp_soul(request->soul);
            if(soul && !memcmp(identity,soul+0x38,16)) {
                memcpy(&out->after,soul+offset,8);out->verified=1;
            }
            out->reason=out->applied && out->verified?0:5;
        }
        // Release outside the call, with no retained actor/effect/cause pointers.
        virtual_function<int(*)(void*)>(event,0x20)(event);event=nullptr;
    } __except(EXCEPTION_EXECUTE_HANDLER) {
        if(replaying)--native_xp_replay_depth;
        quest_xp_faulted=true;out->reason=9;
    }
    // A native fault leaves an uncertain object graph in the owned process;
    // never dereference it in a speculative cleanup path.
}
static int quest_xp_binding(void* binding,void* handler,const char* uid,const char* text) {
    QuestXpRequest request{};QuestXpResult r{1,6};
    if(quest_xp_request(uid,text,&request))quest_xp_apply(binding,handler,&request,&r);
    auto script=*reinterpret_cast<void**>(static_cast<BYTE*>(binding)+0x50);
    auto table=virtual_function<void*(*)(void*,bool)>(script,0x68)(script,false);
    if(!table)return virtual_function<int(*)(void*)>(handler,0x58)(handler);
    virtual_function<void(*)(void*)>(table,0x18)(table);
    auto put=function<void(*)(void*,const char*,const void*)>(0x2b6a0c);
    put(table,"version",&r.version);put(table,"reason",&r.reason);put(table,"applied",&r.applied);put(table,"verified",&r.verified);
    put(table,"kind",&r.kind);put(table,"index",&r.index);put(table,"amount",&r.amount);put(table,"actual",&r.actual);put(table,"causeRva",&r.causeRva);
    char before[17],after[17];hex(reinterpret_cast<BYTE*>(&r.before),8,before);hex(reinterpret_cast<BYTE*>(&r.after),8,after);
    const char* value=before;auto putText=function<void(*)(void*,const char*,const void*)>(0x2b6a44);putText(table,"before",&value);
    value=after;putText(table,"after",&value);
    int result=function<int(*)(void*,void*)>(0x2b5f18)(handler,&table);
    virtual_function<void(*)(void*)>(table,0x20)(table);return result;
}
