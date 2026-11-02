import unittest

from build_vendor import _should_strip


class VendorPackagingTests(unittest.TestCase):
    def test_type_markers_are_not_shipped(self):
        self.assertTrue(_should_strip("requests/py.typed"))
        self.assertTrue(_should_strip("internetarchive/cli/py.typed"))

    def test_certificate_and_license_are_preserved(self):
        self.assertFalse(_should_strip("certifi/cacert.pem"))
        self.assertFalse(_should_strip("certifi-1.dist-info/licenses/LICENSE"))


if __name__ == "__main__":
    unittest.main()
