"""Summarise a Unity/NUnit test results XML. Usage: python3 -I tools/nunit_summary.py results.xml"""
import sys, xml.etree.ElementTree as ET
root = ET.parse(sys.argv[1]).getroot()
a = root.attrib
print(f"tests: {a.get('total')}  passed: {a.get('passed')}  failed: {a.get('failed')}  skipped: {a.get('skipped')}  result: {a.get('result')}")
for tc in root.iter("test-case"):
    if tc.get("result") != "Passed":
        msg = tc.find("failure/message")
        print(f"  {tc.get('result').upper()}: {tc.get('fullname')}\n    {(msg.text or '').strip()[:400] if msg is not None else ''}")
sys.exit(0 if a.get("failed", "0") == "0" else 1)
