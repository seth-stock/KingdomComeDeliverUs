// SPDX-License-Identifier: GPL-3.0-only
// Explicit schema for the observed no-modifier WorldTimeTimedBuff record.
// This is not an arbitrary native deserializer input validator.
#pragma once
#include <cmath>
#include <cstring>
static bool timed_buff_record(const unsigned char* bytes,unsigned size,double* elapsed) {
    if(!bytes || !elapsed || size!=54)return false;
    const unsigned char prefix[]={1,0,48,0,0,0,0xe7,0x94,16,0,0,0};
    const unsigned char middle[]={3,0,6,0,0,0,0x8b,0xaa,0,0,0,0,2,0,8,0,0,0};
    if(std::memcmp(bytes,prefix,sizeof(prefix)) || std::memcmp(bytes+28,middle,sizeof(middle)))return false;
    for(unsigned i=12;i<28;i++)if(bytes[i])return false; // nonempty overrides require another schema
    std::memcpy(elapsed,bytes+46,8);
    return std::isfinite(*elapsed) && *elapsed>=0 && *elapsed<=315360000.0;
}
struct BuffReader {
    void** vtable;
    const unsigned char* data;
    unsigned size,position;
    bool failed;
};
static bool buff_reader_read(BuffReader* r,void* target,unsigned count) {
    if(!r || r->failed || r->position>r->size || count>r->size-r->position || (count && (!target || !r->data))) {
        if(r)r->failed=true;return false;
    }
    if(count)std::memcpy(target,r->data+r->position,count);
    r->position+=count;return true;
}
static bool buff_reader_skip(BuffReader* r,unsigned count) {
    if(!r || r->failed || r->position>r->size || count>r->size-r->position) { if(r)r->failed=true;return false; }
    r->position+=count;return true;
}
static void* buff_reader_slots[]={nullptr,reinterpret_cast<void*>(buff_reader_read),reinterpret_cast<void*>(buff_reader_skip)};
