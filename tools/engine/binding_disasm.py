"""Read-only binding registration inspection in a user's installed PE binary."""
from pathlib import Path
import struct
import sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_64
data = Path(sys.argv[1]).read_bytes()
pe = struct.unpack_from('<I',data,0x3c)[0]
count = struct.unpack_from('<H',data,pe+6)[0]
optional = pe+24
size = struct.unpack_from('<H',data,pe+20)[0]
base = struct.unpack_from('<Q',data,optional+24)[0]
sections = []
for i in range(count):
    at=optional+size+i*40
    name=data[at:at+8].rstrip(b'\0').decode('ascii')
    length,rva,raw_length,offset=struct.unpack_from('<IIII',data,at+8)
    sections.append((name,rva,offset,raw_length))
def va(offset):
    for _, rva, start, size in sections:
        if start<=offset<start+size: return base+rva+offset-start
    return None
decoder=Cs(CS_ARCH_X86,CS_MODE_64)
for term in sys.argv[2:]:
    if term.startswith('%'):
        target=int(term[1:],16)
        for _,rva,offset,size in sections:
            if base+rva<=target<base+rva+size:
                at=offset+target-base-rva
                address=struct.unpack_from('<Q',data,at)[0]
                print('Pointer:',hex(target),'->',hex(address))
                for _,crva,coffset,csize in sections:
                    if base+crva<=address<base+crva+csize:
                        cat=coffset+address-base-crva
                        for ins in decoder.disasm(data[cat:cat+240],address):print(hex(ins.address),ins.mnemonic,ins.op_str)
        continue
    if term.startswith('!'):
        target=int(term[1:],16)
        functions=[]
        for name,rva,offset,size in sections:
            if name=='.pdata':
                for at in range(offset,offset+size-11,12):
                    begin,end,_=struct.unpack_from('<III',data,at)
                    if begin: functions.append((base+begin,base+end))
        import bisect
        functions.sort();begins=[f[0] for f in functions]
        for name,rva,offset,size in sections:
            if name!='.text':continue
            code=data[offset:offset+size]
            for at in range(size-5):
                if code[at] not in (0xe8,0xe9):continue
                pc=base+rva+at
                if pc+5+struct.unpack_from('<i',code,at+1)[0]!=target:continue
                index=bisect.bisect_right(begins,pc)-1
                if index<0:continue
                begin,end=functions[index]
                if not begin<=pc<end:continue
                start=offset+begin-base-rva
                instructions=list(decoder.disasm(data[start:offset+at+5],begin))
                print('Call reference:',hex(pc),'function:',hex(begin))
                for ins in instructions[-16:]:print(hex(ins.address),ins.mnemonic,ins.op_str)
        continue
    if term.startswith('$'):
        target=int(term[1:],16)
        for _, rva, offset, size in sections:
            if base+rva<=target<base+rva+size:
                at=offset+target-base-rva
                print(hex(target),data[at:at+256].split(b'\0')[0].decode('utf-8','replace'))
        continue
    if term.startswith('@'):
        values=term[1:].split(':'); target=int(values[0],16); limit=int(values[1]) if len(values)>1 else 180
        for _, rva, offset, size in sections:
            if base+rva<=target<base+rva+size:
                at=offset+target-base-rva
                for ins in decoder.disasm(data[at:at+limit],target): print(hex(ins.address),ins.mnemonic,ins.op_str)
        continue
    pos=0; targets=[]
    while True:
        pos=data.find(term.encode()+b'\0',pos)
        if pos<0: break
        if va(pos): targets.append(va(pos))
        pos+=1
    print('Binding',term,'string addresses',*[hex(t) for t in targets])
    for name, rva, offset, size in sections:
        if name!='.text': continue
        text=data[offset:offset+size]
        for at in range(len(text)-7):
            if text[at:at+2] not in (b'\x48\x8d',b'\x4c\x8d') or text[at+2]&0xc7!=0x05: continue
            target=base+rva+at+7+struct.unpack_from('<i',text,at+3)[0]
            if target not in targets: continue
            print('Reference',hex(base+rva+at))
            # Beginning on a known LEA boundary avoids decoding mid-instruction.
            for ins in decoder.disasm(text[at:at+110],base+rva+at):
                print(hex(ins.address),ins.mnemonic,ins.op_str)
