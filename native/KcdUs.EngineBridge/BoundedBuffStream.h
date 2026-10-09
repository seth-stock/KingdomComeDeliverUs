// SPDX-License-Identifier: GPL-3.0-only
// Memory-only stream for the exact engine's RPG serializer ABI.
#pragma once
#include <cstring>
struct BuffStream {
    void** vtable;
    unsigned char* data;
    unsigned size, position, capacity;
    bool failed;
};
static bool buff_stream_write(BuffStream* s,const void* bytes,unsigned count) {
    if(!s || s->failed || s->position>s->capacity || s->size>s->capacity ||
       count>s->capacity-s->position || (count && (!bytes || !s->data))) {
        if(s)s->failed=true;return false;
    }
    if(count)std::memcpy(s->data+s->position,bytes,count);
    s->position+=count;if(s->size<s->position)s->size=s->position;return true;
}
static bool buff_stream_seek(BuffStream* s,unsigned long long position) {
    if(!s || s->failed || s->size>s->capacity || position>s->size) { if(s)s->failed=true;return false; }
    s->position=static_cast<unsigned>(position);return true;
}
static unsigned long long buff_stream_tell(BuffStream* s) { return s->position; }
static void* buff_stream_slots[]={nullptr,reinterpret_cast<void*>(buff_stream_write),nullptr,
    reinterpret_cast<void*>(buff_stream_seek),reinterpret_cast<void*>(buff_stream_tell)};
