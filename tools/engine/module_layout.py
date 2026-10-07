"""Inspect bounded save-module framing, read-only; no text/debug strings printed."""
from pathlib import Path
import struct,sys,zlib
file=Path(sys.argv[1]).read_bytes();raw=bytearray();at=0
while at<len(file)-64:
    packed,n=struct.unpack_from('<ii',file,at);size=n if packed==-1 else packed
    if not 0<=n<=32768 or not 0<=size<=32768 or at+8+size>len(file)-64:raise ValueError('Block bounds')
    b=file[at+8:at+8+size];b=b if packed==-1 else zlib.decompress(b)
    if len(b)!=n:raise ValueError('Block length')
    raw.extend(b);at+=8+size
def nodes(start,end):
    out=[]
    while start<end:
        if end-start<6:return None
        t,n=struct.unpack_from('<HI',raw,start)
        if start+6+n>end:return None
        out.append((t,start+6,start+6+n));start+=6+n
    return out
current=nodes(4,len(raw)-1)
for tag in (0x1f4,0x1f6):
    t,s,e=next(n for n in current if n[0]==tag);current=nodes(s,e)
for t,s,e in current:
    if t not in (0x7317,0x7313,0x7302):continue
    print('Module:',hex(t),'bytes',e-s,'prefix',raw[s:s+48].hex())
    for tt,ss,ee in nodes(s,e) or []:
        print('Field:',hex(tt),'bytes',ee-ss,'prefix',raw[ss:min(ee,ss+48)].hex())
        for skip in (0,4,8,16):
            sub=nodes(ss+skip,ee)
            if sub is not None:print('Children at',skip,[(hex(st),se-sss) for st,sss,se in sub[:6]],'count',len(sub))
            if tt==0x352e and skip==4 and sub:
                for st,sss,se in sub:print('Item subfield:',hex(st),'prefix',raw[sss:min(se,sss+64)].hex())
