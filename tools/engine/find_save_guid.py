"""Locate a known runtime item class in save sections, read-only."""
import runpy,sys,uuid
saved=sys.argv[1];guid=uuid.UUID(sys.argv[2]).bytes_le
sys.argv=[sys.argv[0],saved]
ns=runpy.run_path(str(__import__('pathlib').Path(__file__).with_name('module_layout.py')))
raw=ns['raw'];nodes=ns['nodes'];root=nodes(4,len(raw)-1)
at=0
while True:
    at=raw.find(guid,at)
    if at<0:break
    current=root;path=[]
    while current:
        node=next((n for n in current if n[1]<=at<n[2]),None)
        if not node:break
        t,s,e=node;path.append(hex(t));current=nodes(s,e)
    print('GUID offset:',at,'path','/'.join(path),'prefix',raw[max(0,at-12):at+24].hex());at+=1
