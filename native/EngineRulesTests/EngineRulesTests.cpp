// SPDX-License-Identifier: GPL-3.0-only
#include <cstdio>
#include <limits>
#include "../KcdUs.EngineBridge/BoundedBuffStream.h"
#include "../KcdUs.EngineBridge/TimedBuffRecord.h"
#include "../KcdUs.EngineBridge/NativeXpRecord.h"
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
    unsigned char frame[54]={1,0,48,0,0,0,0xe7,0x94,16,0,0,0};
    const unsigned char fields[]={3,0,6,0,0,0,0x8b,0xaa,0,0,0,0,2,0,8,0,0,0};
    std::memcpy(frame+28,fields,sizeof(fields));double elapsed=3.5,decoded=0;
    std::memcpy(frame+46,&elapsed,8);
    if(!check(timed_buff_record(frame,54,&decoded) && decoded==3.5))return 1;
    if(!check(!timed_buff_record(frame,53,&decoded)))return 1;
    frame[12]=1;if(!check(!timed_buff_record(frame,54,&decoded)))return 1;frame[12]=0;
    frame[30]=7;if(!check(!timed_buff_record(frame,54,&decoded)))return 1;frame[30]=6;
    elapsed=std::numeric_limits<double>::quiet_NaN();std::memcpy(frame+46,&elapsed,8);
    if(!check(!timed_buff_record(frame,54,&decoded)))return 1;
    elapsed=-1;std::memcpy(frame+46,&elapsed,8);if(!check(!timed_buff_record(frame,54,&decoded)))return 1;
    BuffReader r={buff_reader_slots,frame,54,0,false};unsigned char copy[54]{};
    if(!check(buff_reader_read(&r,copy,6) && r.position==6))return 1;
    if(!check(buff_reader_skip(&r,48) && r.position==54))return 1;
    if(!check(!buff_reader_skip(&r,1) && r.failed))return 1;
    r={buff_reader_slots,frame,54,0,false};
    if(!check(!buff_reader_read(&r,copy,55) && r.position==0))return 1;
    r={buff_reader_slots,frame,54,55,false};if(!check(!buff_reader_skip(&r,0)))return 1;
    NativeXpRecord xp{};xp.magic=native_xp_magic;xp.version=1;xp.size=sizeof(xp);xp.phase=1;
    xp.sequence=1;xp.event=1;xp.kind=1;xp.index=32;xp.requested=128;xp.causeRva=0x21c8fd0;xp.causeLength=24;
    if(!check(native_xp_record_shape(xp)))return 1;
    xp.cause[20]=1;if(!check(!native_xp_record_shape(xp)))return 1;xp.cause[20]=0;
    xp.index=33;if(!check(!native_xp_record_shape(xp)))return 1;xp.index=32;
    xp.flags=3;if(!check(!native_xp_record_shape(xp)))return 1;xp.world[0]='w';
    if(!check(native_xp_record_shape(xp)))return 1;
    xp.flags=7;if(!check(native_xp_record_shape(xp)))return 1;
    xp.actual=1;if(!check(!native_xp_record_shape(xp)))return 1;xp.actual=0;
    xp.phase=2;xp.sequence=2;xp.actual=128;xp.applied=1;xp.verified=1;
    if(!check(native_xp_record_shape(xp)))return 1;
    xp.event=3;if(!check(!native_xp_record_shape(xp)))return 1;
    std::printf("PASS %d native bounded-stream and effect-record checks\n",n);return 0;
}
