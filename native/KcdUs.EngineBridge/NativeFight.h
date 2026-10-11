// SPDX-License-Identifier: GPL-3.0-only
// Real combat-action scheduler, Candidate in owned private engine sessions.
#pragma once
struct FightResult {
    int version, reason, target, targetMatched, active, canAttack, constructed, submitted, actionActive, actionSlot, mode, prepared, runningSlot;
    int requestedMatched, requestedPresent, guardRequested;
};
// Read before constructing an action too, so a refused factory request does
// not report default zeroes as if they were native combat state.
static void fight_snapshot(BYTE* combat,BYTE* soul,FightResult* out) {
    out->mode=*reinterpret_cast<int*>(soul+0xbdc);
    out->prepared=combat[0x524]?1:0;
    out->active=soul[0xbd8]?1:0;
    out->guardRequested=combat[0x565]?1:0;
    out->canAttack=virtual_function<bool(*)(void*)>(combat,0x648)(combat)?1:0;
    auto environment=*reinterpret_cast<BYTE**>(combat+0x4a8);
    out->runningSlot=environment?*reinterpret_cast<int*>(environment+0xd0):-1;
    auto target=*reinterpret_cast<BYTE**>(soul+0xca8);
    auto requested=*reinterpret_cast<BYTE**>(soul+0xca0);
    out->requestedPresent=requested?1:0;
    out->requestedMatched=!target && !requested?1:0;
    out->target=0;
    if(target && *reinterpret_cast<BYTE**>(target)==engine+0x2228be8) {
        auto targetHuman=*reinterpret_cast<BYTE**>(target+0x4a0);
        if(targetHuman) {
            out->target=*reinterpret_cast<int*>(targetHuman+0x30);
            out->requestedMatched=requested && *reinterpret_cast<BYTE**>(requested)==engine+0x22075a0
                && virtual_function<void*(*)(void*)>(requested,8)(requested)==*reinterpret_cast<void**>(targetHuman+0x38)?1:0;
        }
    }
}
static void private_fight(void* binding,void* handler,const char* command,const char* targetText,FightResult* out) {
    *out={2,1,0,0,0,0,0,0,0,-1};
    if(!action_probe_enabled || action_probe_faulted)return;
    __try {
        auto human=function<BYTE*(*)(void*,void*)>(0x10c98a0)(binding,handler);
        if(!human || *reinterpret_cast<BYTE**>(human)!=engine+0x21fc0d8) { out->reason=2;return; }
        // The normal lazy getter constructs a combat component with the human's
        // own context. No fabricated C_Player or fake action-factory object.
        auto combat=function<BYTE*(*)(void*)>(0x3a107c)(human);
        if(!combat || *reinterpret_cast<BYTE**>(combat)!=engine+0x2228be8) { out->reason=3;return; }
        auto soul=*reinterpret_cast<BYTE**>(combat+0x4b8);
        auto strategy=*reinterpret_cast<BYTE**>(combat+0x730);
        if(!soul || !strategy) { out->reason=3;return; }
        fight_snapshot(combat,soul,out);
        if(!strcmp(command,"guard") || !strcmp(command,"unguard")) {
            if(!out->prepared) { out->reason=6;return; }
            bool want=!strcmp(command,"guard");
            // Verified KCD1 setter: vtable+0x278 / 0xf4b3c4 calls 0x688d94,
            // queues the native guard action and notifies its combat soul.
            // Native caller 0xa561d0 releases scope 4. This scope is shared
            // with native AI: use only for these disposable-body probes,
            // never mistake it for an online authority/ownership lease.
            if(*reinterpret_cast<BYTE**>(*reinterpret_cast<BYTE**>(combat)+0x278)!=engine+0xf4b3c4) { out->reason=3;return; }
            virtual_function<void(*)(void*,bool,int)>(combat,0x278)(combat,want,4);
            out->guardRequested=combat[0x565]?1:0;
            if(out->guardRequested!=(want?1:0)) { out->reason=5;return; }
        } else if(!strcmp(command,"target")) {
            if(!targetText || (strlen(targetText)!=8 && strlen(targetText)!=16)) { out->reason=4;return; }
            for(size_t i=0;i<strlen(targetText);i++)if(nibble(targetText[i])<0) { out->reason=4;return; }
            auto entity=_strtoui64(targetText,nullptr,16);
            if(!entity || entity>0xffffffff) { out->reason=4;return; }
            auto targetHuman=function<BYTE*(*)(void*,unsigned)>(0x10c9854)(binding,static_cast<unsigned>(entity));
            if(!targetHuman || targetHuman==human) { out->reason=4;return; }
            auto targetCombat=function<BYTE*(*)(void*)>(0x3a107c)(targetHuman);
            if(!targetCombat || *reinterpret_cast<BYTE**>(targetCombat)!=engine+0x2228be8) { out->reason=4;return; }
            auto targetEntity=*reinterpret_cast<void**>(targetHuman+0x38);
            if(!targetEntity) { out->reason=4;return; }
            // Uses the stock target-wrapper replacement, old-target cleanup,
            // notifications and strategy registration, not soul+ca8 writes.
            // 0x302cc0 owns the requested target (soul+0xca0), using the
            // stock wrapper replacement and notifications. 0x303720 changes
            // only the current target; leaving the requested target unchanged
            // lets 0x5d9db8 restore the old target on a later update.
            function<void(*)(void*,void*)>(0x302cc0)(strategy,targetEntity);
            auto requested=*reinterpret_cast<BYTE**>(soul+0xca0);
            out->requestedPresent=requested?1:0;
            out->requestedMatched=requested && *reinterpret_cast<BYTE**>(requested)==engine+0x22075a0
                && virtual_function<void*(*)(void*)>(requested,8)(requested)==targetEntity?1:0;
            if(!out->requestedMatched) { out->reason=5;return; }
            // Private scheduling probes also require the current target now;
            // this is not an online ownership or AI-retargeting fence.
            function<void(*)(void*,void*)>(0x303720)(strategy,targetEntity);
            out->targetMatched=*reinterpret_cast<BYTE**>(soul+0xca8)==targetCombat?1:0;
            if(!out->targetMatched) { out->reason=5;return; }
        } else if(!strcmp(command,"clear")) {
            function<void(*)(void*,void*)>(0x302cc0)(strategy,nullptr);
            function<void(*)(void*,void*)>(0x303720)(strategy,nullptr);
            out->requestedPresent=*reinterpret_cast<void**>(soul+0xca0)?1:0;
            out->requestedMatched=out->requestedPresent?0:1;
            out->targetMatched=*reinterpret_cast<BYTE**>(soul+0xca8)==nullptr?1:0;
            if(!out->targetMatched || !out->requestedMatched) { out->reason=5;return; }
        } else if(!strcmp(command,"attack")) {
            auto factorySet=*reinterpret_cast<BYTE**>(combat+0x720);
            auto factory=factorySet?*reinterpret_cast<BYTE**>(factorySet+0x50):nullptr;
            if(!factory || !*reinterpret_cast<void**>(combat+0x4a8) || !*reinterpret_cast<void**>(soul+0xca8)) { out->reason=6;return; }
            BYTE* action=nullptr;
            unsigned zone=*reinterpret_cast<unsigned*>(engine+0x359c2f0);
            function<void(*)(void*,void**,unsigned,unsigned)>(0x460934)(factory,reinterpret_cast<void**>(&action),zone,0);
            if(!action) { out->reason=7;return; }
            out->constructed=*reinterpret_cast<BYTE**>(action)==engine+0x21aeca8?1:0;
            if(out->constructed) {
                out->submitted=virtual_function<bool(*)(void*,void**)>(combat,0x290)(combat,reinterpret_cast<void**>(&action))?1:0;
                out->actionActive=action[0xa8]?1:0;
                out->actionSlot=*reinterpret_cast<int*>(action+0xbc);
            }
            // Factory returns one retained local reference. The action
            // environment retains accepted actions and owns their lifecycle.
            // Keep no raw action or actor pointer between script calls.
            virtual_function<void(*)(void*)>(action,0x10)(action);
            if(!out->submitted) { out->reason=8;return; }
        } else if(strcmp(command,"inspect")) { out->reason=4;return; }
        fight_snapshot(combat,soul,out);
        out->reason=0;
    } __except(EXCEPTION_EXECUTE_HANDLER) { action_probe_faulted=true;out->reason=9;out->submitted=0; }
}
static int private_fight_binding(void* binding,void* handler,const char* command,const char* target) {
    FightResult read{};private_fight(binding,handler,command,target,&read);
    auto script=*reinterpret_cast<void**>(static_cast<BYTE*>(binding)+0x50);
    auto table=virtual_function<void*(*)(void*,bool)>(script,0x68)(script,false);
    if(!table)return virtual_function<int(*)(void*)>(handler,0x58)(handler);
    virtual_function<void(*)(void*)>(table,0x18)(table);
    auto put=function<void(*)(void*,const char*,const void*)>(0x2b6a0c);
    put(table,"version",&read.version);put(table,"reason",&read.reason);
    put(table,"target",&read.target);put(table,"targetMatched",&read.targetMatched);
    put(table,"active",&read.active);put(table,"canAttack",&read.canAttack);
    put(table,"constructed",&read.constructed);put(table,"submitted",&read.submitted);
    put(table,"actionActive",&read.actionActive);put(table,"actionSlot",&read.actionSlot);
    put(table,"mode",&read.mode);put(table,"prepared",&read.prepared);put(table,"runningSlot",&read.runningSlot);
    put(table,"requestedMatched",&read.requestedMatched);put(table,"requestedPresent",&read.requestedPresent);
    put(table,"guardRequested",&read.guardRequested);
    int result=function<int(*)(void*,void*)>(0x2b5f18)(handler,&table);
    virtual_function<void(*)(void*)>(table,0x20)(table);return result;
}
