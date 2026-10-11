// SPDX-License-Identifier: GPL-3.0-only
// Fixed, pointer-free disk format. SHA-256 covers bytes [0, offsetof(hash)).
#pragma once
#include <cstdint>
#include <cstddef>
#pragma pack(push,1)
struct NativeXpRecord {
    std::uint32_t magic,version,size,phase;
    std::uint64_t sequence,event;
    unsigned char boot[16],soul[16];
    std::uint64_t before,after;
    std::uint32_t kind,index,requested,actual,applied,verified,causeRva,causeLength;
    unsigned char cause[24];
    char world[64];
    std::uint32_t flags; // 1: shared quest context; 2: host; 4: received effect
    unsigned char previous[32],hash[32];
};
#pragma pack(pop)
static_assert(sizeof(NativeXpRecord)==268,"Stable native XP record size");
static_assert(offsetof(NativeXpRecord,hash)==236,"Stable hash coverage");
constexpr std::uint32_t native_xp_magic=0x3150584e; // NXP1
inline bool native_xp_record_shape(const NativeXpRecord& r) {
    if(r.magic!=native_xp_magic || r.version!=1 || r.size!=sizeof(r) ||
        (r.phase!=1 && r.phase!=2) || !r.sequence || !r.event || r.event>r.sequence ||
        r.kind>1 || r.index>(r.kind?32u:9u) || !r.requested || r.requested>128u*100000u ||
        r.applied>1 || r.verified>1 || r.flags>7 || (r.flags&3)==2)return false;
    bool ended=false;
    for(char c:r.world) {
        if(!c)ended=true;
        else if(ended || !((c>='a' && c<='z') || (c>='A' && c<='Z') ||
            (c>='0' && c<='9') || c=='-' || c=='_' || c=='.'))return false;
    }
    if((r.flags&1) && !r.world[0])return false;
    if(!((r.causeRva==0x2306d30 && r.causeLength==16) ||
        (r.causeRva==0x21c8fd0 && r.causeLength==24)))return false;
    // Canonical padding, not bytes from the original engine stack.
    unsigned n=r.causeLength==24?20:16;
    for(unsigned i=n;i<24;i++)if(r.cause[i])return false;
    if(r.phase==1 && (r.event!=r.sequence || r.actual || r.applied || r.verified || r.after))return false;
    return true;
}
