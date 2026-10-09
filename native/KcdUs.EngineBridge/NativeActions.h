// SPDX-License-Identifier: GPL-3.0-only
// Candidate native action floor for ordinary NPC actors. Never call Player
// PlayAnim/StopAnim: their +0xaf0 storage is outside a 0x9f0 NPC allocation.
#pragma once

struct ActionResult { int version, submitted, reason, fragment, references, status, elapsedMs, activeScopes, requestedScopes, maxPriority; };
static bool action_probe_enabled;
static bool action_probe_faulted;
static void* probe_action;
static void* probe_actor;

static bool queue_native_action(void* binding,void* handler,const char* fragment,const char* tags,ActionResult* result) {
    *result={1,0,1,-1,0,0,0};
    if(!action_probe_enabled || action_probe_faulted)return false;
    if(!fragment || !tags || strlen(fragment)>64 || strlen(tags)>240)return false;
    // Paired animations and interactive actions require another actor's native
    // synchronization context. They are deliberately outside this entry.
    if(strcmp(fragment,"MotionIdle") && strcmp(fragment,"MotionMovement") && strcmp(fragment,"CombatIdle") &&
       strcmp(fragment,"CombatAttack") && strcmp(fragment,"CombatPreBlock") && strcmp(fragment,"CombatHit"))return false;
    void* owned=nullptr;
    __try {
        CombatRead read{};
        if(!combat_read(binding,handler,&read) || read.actorVtableRva!=0x21fc0d8 || !read.animationReady) { result->reason=2;return false; }
        auto actor=function<BYTE*(*)(void*,void*)>(0x10c98a0)(binding,handler);
        if(probe_action) { result->reason=9;return false; } // one bounded diagnostic lease
        auto expansion=*reinterpret_cast<BYTE**>(actor+0x2f8);
        auto animated=*reinterpret_cast<void**>(expansion+0x20);
        auto controller=virtual_function<BYTE*(*)(void*)>(animated,0x118)(animated);
        if(!controller || *reinterpret_cast<BYTE**>(controller)!=engine+0x2229848) { result->reason=3;return false; }
        auto context=virtual_function<BYTE*(*)(void*)>(controller,0xb8)(controller);
        if(!context || !*reinterpret_cast<void**>(context)) { result->reason=3;return false; }
        auto definition=*reinterpret_cast<BYTE**>(context);
        auto names=*reinterpret_cast<void**>(definition+0x18);
        unsigned crc=function<unsigned(*)(const char*)>(0x2d3114)(fragment);
        int id=function<int(*)(void*,unsigned)>(0x4790dc)(names,crc);
        result->fragment=id;
        if(id<0) { result->reason=4;return false; }
        unsigned localTags[3]{};
        auto tagDefinition=virtual_function<void*(*)(void*,int)>(controller,0x90)(controller,id);
        char input[241];strcpy_s(input,tags);char* next=nullptr;
        for(char* token=strtok_s(input,"+",&next);token;token=strtok_s(nullptr,"+",&next)) {
            if(!tagDefinition) { result->reason=5;return false; }
            int tag=function<int(*)(void*,unsigned)>(0x4790dc)(tagDefinition,function<unsigned(*)(const char*)>(0x2d3114)(token));
            if(tag<0) { result->reason=5;return false; }
            function<void(*)(void*,void*,int,bool)>(0x47928c)(tagDefinition,localTags,tag,true);
        }
        // The game's own allocator and native TAction<SAnimationContext>
        // constructor/destructor share the same heap. The controller retains
        // the queued action. No smart pointer is stored in an NPC/Player field.
        using NativeAlloc=void*(*)(size_t,int*,unsigned);
        auto allocator=*reinterpret_cast<NativeAlloc*>(engine+0x3002e68);
        if(!allocator) { result->reason=6;return false; }
        int accounted=0;
        auto action=static_cast<BYTE*>(allocator(0x70,&accounted,0));
        if(!action) { result->reason=6;return false; }
        InterlockedAdd(reinterpret_cast<volatile LONG*>(engine+0x3002e90),accounted);
        InterlockedAdd(reinterpret_cast<volatile LONG*>(engine+0x3002e60),0x70);
        InterlockedIncrement(reinterpret_cast<volatile LONG*>(engine+0x3002e70));
        // +0x18 is the forced scope mask (not a user token). Zero lets the
        // controller definition select this fragment's scopes. The Player
        // scripting routine forces all scopes, which is inappropriate for an
        // NPC with independently occupied motion/weapon scopes.
        function<void*(*)(void*,int,int,void*,unsigned,int)>(0x2c3ebc)(action,5,id,localTags,0,0);
        *reinterpret_cast<BYTE**>(action)=engine+0x22c4e28;
        function<void*(*)(void**,void*)>(0x21eb18)(&owned,action);
        virtual_function<void(*)(void*,void*,float)>(controller,0x98)(controller,action,0.8f);
        result->references=*reinterpret_cast<int*>(action+0x50);
        result->status=*reinterpret_cast<int*>(action+0x28);
        result->submitted=result->references>=2?1:0;
        result->reason=result->submitted?0:7;
        // Private diagnostic lease: inspect status without dereferencing a
        // freed action after the controller finishes. Explicitly released by
        // the probe before removing the body; normal profiles cannot arm it.
        probe_action=owned;probe_actor=actor;owned=nullptr;
        return result->submitted!=0;
    } __except(EXCEPTION_EXECUTE_HANDLER) {
        // An uncertain native ownership transition is never retried. Do not
        // free a possibly transferred action after an exception.
        action_probe_faulted=true;result->submitted=0;result->reason=8;return false;
    }
}

static int native_action_binding(void* binding,void* handler,const char* fragment,const char* tags) {
    ActionResult result{};
    if(fragment && (!strcmp(fragment,"inspect") || !strcmp(fragment,"release"))) {
        result.version=1;result.reason=1;
        __try {
            auto actor=function<void*(*)(void*,void*)>(0x10c98a0)(binding,handler);
            if(action_probe_enabled && probe_action && actor==probe_actor) {
                auto action=static_cast<BYTE*>(probe_action);
                result.submitted=1;result.reason=0;
                result.fragment=*reinterpret_cast<int*>(action+0x38);
                result.references=*reinterpret_cast<int*>(action+0x50);
                result.status=*reinterpret_cast<int*>(action+0x28);
                result.elapsedMs=static_cast<int>(*reinterpret_cast<float*>(action+0x10)*1000);
                auto expansion=*reinterpret_cast<BYTE**>(static_cast<BYTE*>(actor)+0x2f8);
                auto animated=*reinterpret_cast<void**>(expansion+0x20);
                auto controller=virtual_function<BYTE*(*)(void*)>(animated,0x118)(animated);
                if(controller && *reinterpret_cast<BYTE**>(controller)==engine+0x2229848) {
                    result.activeScopes=*reinterpret_cast<int*>(controller+0x38);
                    unsigned count=*reinterpret_cast<unsigned*>(controller+0x28);
                    auto scopes=*reinterpret_cast<BYTE**>(controller+0x30);
                    if(count<=32 && scopes) for(unsigned i=0;i<count;i++) {
                        auto owner=*reinterpret_cast<BYTE**>(scopes+i*0x118+0x100);
                        if(!owner)owner=*reinterpret_cast<BYTE**>(scopes+i*0x118+0xf8);
                        if(owner && *reinterpret_cast<int*>(owner+0x24)>result.maxPriority)
                            result.maxPriority=*reinterpret_cast<int*>(owner+0x24);
                    }
                    unsigned fragmentTags[3];memcpy(fragmentTags,action+0x3c,sizeof(fragmentTags));
                    result.requestedScopes=function<unsigned(*)(void*,int,void*,int)>(0x221010)
                        (controller,result.fragment,fragmentTags,*reinterpret_cast<int*>(action+0x20));
                }
                if(!strcmp(fragment,"release")) {
                    function<void(*)(void*)>(0xf541a0)(action);
                    function<void(*)(void*)>(0x222688)(action);
                    probe_action=nullptr;probe_actor=nullptr;
                }
            }
        } __except(EXCEPTION_EXECUTE_HANDLER) { action_probe_faulted=true;result.reason=8;result.submitted=0; }
    } else queue_native_action(binding,handler,fragment,tags,&result);
    auto script=*reinterpret_cast<void**>(static_cast<BYTE*>(binding)+0x50);
    auto table=virtual_function<void*(*)(void*,bool)>(script,0x68)(script,false);
    if(!table)return virtual_function<int(*)(void*)>(handler,0x58)(handler);
    virtual_function<void(*)(void*)>(table,0x18)(table);
    auto put=function<void(*)(void*,const char*,const void*)>(0x2b6a0c);
    put(table,"version",&result.version);put(table,"submitted",&result.submitted);
    put(table,"reason",&result.reason);put(table,"fragment",&result.fragment);put(table,"references",&result.references);
    put(table,"status",&result.status);put(table,"elapsedMs",&result.elapsedMs);
    put(table,"activeScopes",&result.activeScopes);put(table,"requestedScopes",&result.requestedScopes);
    put(table,"maxPriority",&result.maxPriority);
    int count=function<int(*)(void*,void*)>(0x2b5f18)(handler,&table);
    virtual_function<void(*)(void*)>(table,0x20)(table);return count;
}

static void configure_action_probe() {
    wchar_t enabled[8]{};wchar_t root[32768]{};
    // Research builds must be launched through the owned private profile.
    // No runtime UI enables this unproven path in a normal player's profile.
    action_probe_enabled=GetEnvironmentVariableW(L"KCDUS_PROBE_ACTIONS",enabled,_countof(enabled))>0 && enabled[0]==L'1' &&
        GetEnvironmentVariableW(L"KCDUS_PROBE_SAVE_ROOT",root,_countof(root))>0 && wcsstr(root,L"\\_work\\KCDUS-engine-") &&
        wcsstr(GetCommandLineW(),L"KCDUS-engine-");
}
