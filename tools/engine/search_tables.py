"""Read-only local table lookup. Prints matching row metadata; extracts no game files."""
import importlib.util
from pathlib import Path
import sys
import zipfile
import xml.etree.ElementTree as ET
spec=importlib.util.spec_from_file_location('pak',Path(__file__).parents[1]/'Inspect-EngineCapabilities.py')
pak=importlib.util.module_from_spec(spec);spec.loader.exec_module(pak)
term=sys.argv[2].lower()
with zipfile.ZipFile(Path(sys.argv[1])/'Data'/'Tables.pak') as archive:
    for entry in archive.infolist():
        if not entry.filename.lower().endswith('.xml'):continue
        data=pak.read_entry(archive,entry)
        if term.encode() not in data.lower():continue
        try:tree=ET.fromstring(data)
        except ET.ParseError:continue
        for node in tree.iter():
            if any(term in str(value).lower() for value in node.attrib.values()):print(entry.filename,node.tag,node.attrib)
