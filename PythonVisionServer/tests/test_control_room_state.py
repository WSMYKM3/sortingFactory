import unittest

from control_room_state import ControlRoomState


class ControlRoomStateTests(unittest.TestCase):
    def setUp(self) -> None:
        self.state = ControlRoomState()

    def test_session_conveyor_and_arm_controls_are_independent(self) -> None:
        self.state.apply({"action": "start_session"})
        self.state.apply({"action": "set_conveyor_running", "value": False})
        self.state.apply(
            {"action": "set_arm_enabled", "arm_id": "arm_2", "value": False}
        )

        controls = self.state.controls()

        self.assertTrue(controls["session_requested"])
        self.assertFalse(controls["conveyor_running"])
        self.assertTrue(controls["arms"][0]["enabled"])
        self.assertFalse(controls["arms"][1]["enabled"])

    def test_speed_is_validated(self) -> None:
        with self.assertRaisesRegex(ValueError, "between"):
            self.state.apply({"action": "set_conveyor_speed", "value": 3.0})

    def test_telemetry_is_included_in_dashboard(self) -> None:
        self.state.update_telemetry({"unity_status": "online", "total_attempts": 4})

        dashboard = self.state.dashboard({"arm_1_camera": {"frame_count": 12}})

        self.assertEqual(dashboard["telemetry"]["total_attempts"], 4)
        self.assertEqual(
            dashboard["vision_cameras"]["arm_1_camera"]["frame_count"],
            12,
        )


if __name__ == "__main__":
    unittest.main()
