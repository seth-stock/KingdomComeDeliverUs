"""Read-only KCD1 RPG framing discovery. No save rewriting or personal debug fields."""
from pathlib import Path
import struct
import sys
import uuid
import zlib
import hashlib
data=Path(sys.argv[1]).read_bytes();at=0;raw=bytearray()
masked=bytearray(data);masked[-60:-44]=bytes(16)
print('Footer checksum matches:',hashlib.md5(masked).digest()==data[-60:-44])
while at<len(data)-64:
    packed,length=struct.unpack_from('<ii',data,at)
    size=length if packed==-1 else packed
    if not 0<=length<=32768 or not 0<=size<=32768 or at+8+size>len(data)-64:raise ValueError('Block bounds')
    block=data[at+8:at+8+size]
    if packed!=-1:block=zlib.decompress(block)
    if len(block)!=length:raise ValueError('Block size differs')
    raw.extend(block);at+=8+size
def children(start,end):
    result=[]
    while start<end:
        if start+6>end:raise ValueError('Incomplete TLV')
        tag,length=struct.unpack_from('<HI',raw,start)
        if start+6+length>end:raise ValueError('TLV bounds')
        result.append((tag,start+6,start+6+length));start+=6+length
    return result
print('Stream version:',struct.unpack_from('<I',raw)[0],'Tail:',raw[-16:].hex())
nodes=children(4,len(raw)-1)
for tag in (0x1f4,0x1f6,0x7317,0x3529):
    found=[n for n in nodes if n[0]==tag]
    if len(found)!=1:raise ValueError('Expected one RPG container')
    _,begin,end=found[0]
    if tag!=0x3529:nodes=children(begin,end)
count=struct.unpack_from('<I',raw,begin)[0]
print('Proxy name persisted:',b'kcdus_player_probe' in raw)
records=children(begin+4,end)
print('Declared souls:',count,'Records:',len(records))
for tag,begin,end in records:
    if tag!=0x115e:raise ValueError('Unexpected soul record')
    fields=children(begin+16,end)
    names=[n for n in fields if n[0]==0x1302]
    if not names:continue
    _,start,stop=names[0]
    label=raw[start+16:stop].split(b'\0')[0].decode('utf-8','replace')
    if label.lower() not in ('player','player_henry','henry','dude','hero'):continue
    print('Candidate:',label,'GUID:',uuid.UUID(bytes_le=bytes(raw[begin:begin+16])),'Bytes:',end-begin)
    print('Entity reference:',raw[stop-8:stop].hex())
    print('Fields:',[(hex(t),e-s) for t,s,e in fields])
    for tag,start,stop in fields:
        if tag in (0x12fb,0x12fc,0x1301):
            sub=children(start,stop)
            print('Group:',hex(tag),[(hex(t),e-s) for t,s,e in sub])
            for t,s,e in sub:
                if t in (0x927,0x92a):
                    try:
                        core=children(s,e)
                        print('Core:',[(hex(ct),ce-cs) for ct,cs,ce in core])
                        for ct,cs,ce in core:
                            if ct in (0x1385,0x138d):print('XP keys:',hex(ct),[struct.unpack_from('<I',raw,p)[0] for p in range(cs,ce-7,8)])
                            if ct in (0x1385,0x138d,0x137e):print('Trait digest:',hex(ct),hashlib.sha256(raw[cs:ce]).hexdigest())
                            if ct==0x137e:
                                pk=children(cs,ce);print('Perk entries:',len(pk),'First tags:',[(hex(pt),pe-ps) for pt,ps,pe in pk[:3]])
                                print('Other perk records:',[(hex(pt),pe-ps) for pt,ps,pe in pk if pt!=0x3d8])
                                for pt,ps,pe in pk:
                                    if pt==0x3d9:
                                        print('Secondary perk fields:',[(hex(st),se-ss) for st,ss,se in children(ps,pe)]);break
                                for pt,ps,pe in pk[:1]:
                                    print('Perk prefix:',raw[ps:ps+24].hex())
                                    try:print('Perk fields:',[(hex(st),se-ss) for st,ss,se in children(ps,pe)])
                                    except ValueError:print('Perk payload is not a TLV list')
                    except ValueError:print('Core prefix:',raw[s:s+48].hex())
