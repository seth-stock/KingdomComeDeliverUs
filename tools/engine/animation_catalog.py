"""Read fragment/tag names from installed assets, without extracting game assets."""
import importlib.util
from pathlib import Path
import sys
import zipfile
import xml.etree.ElementTree as ET
spec=importlib.util.spec_from_file_location('pak',Path(__file__).parents[1]/'Inspect-EngineCapabilities.py')
pak=importlib.util.module_from_spec(spec);spec.loader.exec_module(pak)
for path in (Path(sys.argv[1])/'Data').glob('*.pak'):
    with zipfile.ZipFile(path) as archive:
        for entry in archive.infolist():
            name=entry.filename.replace('\\','/').lower()
            if 'relaxed_walk' in name and name.endswith('.caf') and not any(x in name for x in ('turn','strafe','back','crouch')):
                print('Walk clip:',name)
            if any(term in name for term in ('3d_relaxed_walk_turn_strafe','3d_relaxed_run_turn_strafe','2d_sprint_turn','2d_relaxed_walk','2d_relaxed_run')):
                print('Blend space:',name)
                try:root=ET.fromstring(pak.read_entry(archive,entry))
                except ET.ParseError:continue
                for node in root.iter():
                    if node.tag in ('Example','Param'):print('Sample:',node.attrib)
            if 'kcd_male_' not in name or not name.endswith(('.xml','.adb')): continue
            if not any(term in name for term in ('controllerdefs','database','tags','fragments')): continue
            print('Asset:',name)
            try: root=ET.fromstring(pak.read_entry(archive,entry))
            except ET.ParseError: continue
            for child in root:
                print('Section:',child.tag,child.attrib)
                if child.tag in ('FragmentIDs','Tags','Fragments','TagDefinition','FragmentList'):
                    for element in list(child)[:100]:
                        print('Entry:',element.tag,element.attrib)
                        if element.tag in ('MotionMovement','MotionIdle'):
                            candidates=[f for f in element if set(f.attrib.get('Tags','').split('+')) <= {'walk','run','sprint','prime','forward',''}]
                            for fragment in candidates[:12]:
                                print('Variant:',fragment.attrib)
                                for anim in fragment.iter('Animation'):print('Clip:',anim.attrib)
                        if element.tag=='Group':
                            for tag in element: print('Tag:',tag.attrib)
