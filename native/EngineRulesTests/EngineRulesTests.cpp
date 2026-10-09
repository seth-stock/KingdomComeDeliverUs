// SPDX-License-Identifier: GPL-3.0-only
#include <cstdio>
#include <limits>
#include "../KcdUs.EngineBridge/BoundedBuffStream.h"
int main() {
    int n=0;
    auto check=[&n](bool value){if(!value){std::printf("FAIL %d\n",n+1);return false;}++n;return true;};
    unsigned char bytes[16]={},input[]={1,0,9,0,0,0,42,43,44};
    BuffStream s={buff_stream_slots,bytes,0,0,sizeof(bytes),false};
    if(!check(buff_stream_write(&s,input,sizeof(input))))return 1;
    if(!check(buff_stream_tell(&s)==9 && s.size==9))return 1;
    // Native close seeks back to patch a chunk length, then restores the end.
    if(!check(buff_stream_seek(&s,2)))return 1;
    unsigned char length[]={3,0,0,0};
    if(!check(buff_stream_write(&s,length,4) && bytes[2]==3 && bytes[6]==42))return 1;
    if(!check(buff_stream_seek(&s,9) && s.size==9))return 1;
    if(!check(!buff_stream_seek(&s,10) && s.failed && s.position==9))return 1;
    if(!check(!buff_stream_write(&s,input,1) && bytes[9]==0))return 1;
    s={buff_stream_slots,bytes,0,0,16,false};
    if(!check(!buff_stream_write(&s,input,std::numeric_limits<unsigned>::max()) && s.position==0))return 1;
    s={buff_stream_slots,bytes,0,17,16,false};
    if(!check(!buff_stream_write(&s,input,1)))return 1; // subtraction must not underflow
    s={buff_stream_slots,bytes,17,0,16,false};
    if(!check(!buff_stream_seek(&s,0)))return 1;
    s={buff_stream_slots,nullptr,0,0,16,false};
    if(!check(!buff_stream_write(&s,input,1)))return 1;
    s={buff_stream_slots,bytes,0,0,16,false};
    if(!check(buff_stream_write(&s,nullptr,0) && !s.failed && s.size==0))return 1;
    if(!check(!buff_stream_write(&s,nullptr,1) && s.failed))return 1;
    if(!check(!buff_stream_write(nullptr,input,1) && !buff_stream_seek(nullptr,0)))return 1;
    std::printf("PASS %d native bounded-stream checks\n",n);return 0;
}
