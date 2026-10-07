"""Read-only bone/animation name discovery; never extract or redistribute assets."""
import importlib.util,re,sys,zipfile,xml.etree.ElementTree as ET
from pathlib import Path
spec=importlib.util.spec_from_file_location('pak',Path(__file__).parents[1]/'Inspect-EngineCapabilities.py')
pak=importlib.util.module_from_spec(spec);spec.loader.exec_module(pak)
with zipfile.ZipFile(Path(sys.argv[1])/'Data'/'Characters.pak') as archive:
    for entry in archive.infolist():
        name=entry.filename.replace('\\','/').lower()
        if not name.endswith(('male.chr','male.chrparams','male.chparams')):continue
        print('Asset:',name)
        data=pak.read_entry(archive,entry)
        if name.endswith('.chrparams') and name.endswith('/male.chrparams'):
            tree=ET.fromstring(data)
            for node in tree.iter():
                if node.tag=='Animation' and any(term in str(node.attrib).lower() for term in ('walk','run','sprint','lmg','bspace')):
                    print('Animation alias:',node.attrib)
        names=set(m.decode('ascii') for m in re.findall(rb'[A-Za-z_][A-Za-z_ 0-9]{3,64}\x00',data) if re.search(rb'foot|calf|spine|thigh|walk',m,re.I))
        for value in sorted(names):print(value.rstrip('\0'))
