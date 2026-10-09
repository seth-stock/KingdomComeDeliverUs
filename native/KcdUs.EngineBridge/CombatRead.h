// SPDX-License-Identifier: GPL-3.0-only
// Read-only, version-gated native combat diagnostics. No Player proxies.
#pragma once

struct CombatRead {
    int version, actorPresent, channelGuard, inputSupported, animationReady;
    unsigned actorVtableRva, animationVtableRva;
};

// The existing human:PlayAnim binding is extended with one reserved fragment
// name. Ordinary fragment calls still reach the original engine function.
// Resolves the actor on the Lua/game thread for every call; never caches an
// actor, soul, controller or entity handle across streaming or save loading.
static bool combat_read(void* binding,void* handler,CombatRead* out) {
    __try {
        *out={};out->version=1;
        auto actor=function<BYTE*(*)(void*,void*)>(0x10c98a0)(binding,handler);
        if(!actor)return true;
        out->actorPresent=1;
        auto actorVtable=*reinterpret_cast<BYTE**>(actor);
        out->actorVtableRva=static_cast<unsigned>(actorVtable-engine);
        out->inputSupported=*reinterpret_cast<void**>(actorVtable+0x460)!=engine+0x2e39b0;
        out->channelGuard=function<bool(*)(void*)>(0x370ed4)(actor)?1:0;
        // The shipped GetAnimatedCharacter path is actor+0x2f8 -> +0x20
        // (C_Human +0x628, then +0x2d0). This is NOT a combat actor.
        auto expansion=*reinterpret_cast<BYTE**>(actor+0x2f8);
        if(!expansion)return true;
        auto animation=*reinterpret_cast<BYTE**>(expansion+0x20);
        if(!animation)return true;
        auto vtable=*reinterpret_cast<BYTE**>(animation);
        out->animationVtableRva=static_cast<unsigned>(vtable-engine);
        if(vtable!=engine+0x21bb588)return true;
        auto controller=virtual_function<void*(*)(void*)>(animation,0x118)(animation);
        out->animationReady=controller?1:0;
        return true;
    } __except(EXCEPTION_EXECUTE_HANDLER) { *out={};return false; }
}

using PlayAnimBindingFn=int(*)(void*,void*,const char*,const char*);
static PlayAnimBindingFn play_anim_original;
static int play_anim_binding(void* binding,void* handler,const char* fragment,const char* tags) {
    if(!fragment || strcmp(fragment,"@kcdus/combat-read"))
        return play_anim_original(binding,handler,fragment,tags);
    CombatRead read{};
    if(!combat_read(binding,handler,&read))return virtual_function<int(*)(void*)>(handler,0x58)(handler);
    auto script=*reinterpret_cast<void**>(static_cast<BYTE*>(binding)+0x50);
    auto table=virtual_function<void*(*)(void*,bool)>(script,0x68)(script,false);
    if(!table)return virtual_function<int(*)(void*)>(handler,0x58)(handler);
    virtual_function<void(*)(void*)>(table,0x18)(table);
    auto put=function<void(*)(void*,const char*,const void*)>(0x2b6a0c);
    put(table,"version",&read.version);
    put(table,"actorPresent",&read.actorPresent);
    put(table,"channelGuard",&read.channelGuard);
    put(table,"inputSupported",&read.inputSupported);
    put(table,"animationReady",&read.animationReady);
    put(table,"actorVtableRva",&read.actorVtableRva);
    put(table,"animationVtableRva",&read.animationVtableRva);
    int result=function<int(*)(void*,void*)>(0x2b5f18)(handler,&table);
    virtual_function<void(*)(void*)>(table,0x20)(table);
    return result;
}

static void install_combat_read() {
    // C_ScriptBindHuman primary vtable, PlayAnim at +0x128, verified from
    // registration -> virtual thunk -> real binding. Exact file hash was
    // checked by initialize before this runs. Compare the original slot too.
    auto slot=reinterpret_cast<void**>(engine+0x26c5ed8+0x128);
    auto expected=engine+0x10da760;
    if(*slot!=expected)return;
    DWORD old;if(!VirtualProtect(slot,sizeof(void*),PAGE_READWRITE,&old))return;
    play_anim_original=reinterpret_cast<PlayAnimBindingFn>(expected);
    void* previous=InterlockedCompareExchangePointer(slot,reinterpret_cast<void*>(play_anim_binding),expected);
    DWORD ignored;VirtualProtect(slot,sizeof(void*),old,&ignored);
    if(previous!=expected)play_anim_original=nullptr;
}
