import unittest
from pmcro_finetune.checker_quality import validate_checker_row
from pmcro_finetune.secret_quality import find_secrets


def row(content):
    return {"role":"checker","messages":[{"role":"system","content":"checker"},{"role":"user","content":"audit"},{"role":"assistant","content":content}]}

class QualityGateTests(unittest.TestCase):
    def test_checker_requires_status_and_evidence(self):
        self.assertTrue(validate_checker_row(row("no conclusion")))
        self.assertEqual(validate_checker_row(row("PASS. Evidence: artifact hash verified.")), [])
    def test_checker_rejects_self_mutation(self):
        findings=validate_checker_row(row("PASS. Evidence: I will fix the file."))
        self.assertTrue(any(x.code=="CHECKER_SELF_MUTATION" for x in findings))
    def test_secrets(self):
        self.assertTrue(find_secrets("api_key=abcdefghijklmnopqrstuvwxyz"))
        self.assertEqual(find_secrets("Evidence: https://github.com/example/repo/pull/1"), [])

if __name__ == '__main__': unittest.main()
