import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parents[1]))

from secrandom_integration import DrawChannel, DrawOptions


class ProtocolShapeTests(unittest.TestCase):
    def test_default_options_are_omitted(self):
        self.assertIsNone(DrawOptions().to_params())

    def test_overrides_are_sparse(self):
        self.assertEqual({"count": 2}, DrawOptions(count=2).to_params())

    def test_channel_names_match_wire_methods(self):
        self.assertEqual("draw.rollCall.trigger", DrawChannel.ROLL_CALL.method)
        self.assertEqual("draw.quick.trigger", DrawChannel.QUICK.method)
        self.assertEqual("draw.lottery.trigger", DrawChannel.LOTTERY.method)

    def test_selection_options_are_flattened(self):
        from secrandom_integration.client import AutomationClient

        client = AutomationClient()
        client.read_roster = lambda *args, **kwargs: {
            "roster_kind": "students",
            "lists": [{"members": [
                {"name": "A", "group": "red", "gender": "F"},
                {"name": "B", "group": "red", "gender": "M"},
            ]}],
        }
        options = client.roster_selection_options("students")
        self.assertEqual(["red"], options["groups"])
        self.assertEqual(["F", "M"], options["genders"])


if __name__ == "__main__":
    unittest.main()
