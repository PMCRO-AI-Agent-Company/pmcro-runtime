import tempfile,unittest
from pathlib import Path
from pmcro_finetune.manifest import build_manifest,validate_manifest
class ManifestTests(unittest.TestCase):
 def test_manifest_matches_artifact(self):
  with tempfile.TemporaryDirectory() as d:
   p=Path(d)/'data.jsonl';p.write_text('{"id":1}\n{"id":2}\n',encoding='utf-8')
   m=build_manifest(p,'test',['trail:test'],{'schema':'1.0','checker':'1.0','secret':'1.0','disposition':'1.0','dedup':'1.0'},{'train':1,'validation':1,'test':0})
   self.assertEqual(validate_manifest(m,p),[])
 def test_tampering_is_detected(self):
  with tempfile.TemporaryDirectory() as d:
   p=Path(d)/'data.jsonl';p.write_text('{"id":1}\n',encoding='utf-8')
   m=build_manifest(p,'test',['trail:test'],{'schema':'1.0','checker':'1.0','secret':'1.0','disposition':'1.0','dedup':'1.0'},{'train':1,'validation':0,'test':0})
   p.write_text('{"id":2}\n',encoding='utf-8')
   self.assertIn('content_sha256 mismatch',validate_manifest(m,p))
if __name__=='__main__':unittest.main()
