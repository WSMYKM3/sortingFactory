"""Thread-safe runtime control and Unity telemetry state for the control room."""

from __future__ import annotations

import copy
import threading
import time
from typing import Any


ARM_IDS = ("arm_1", "arm_2", "arm_3")


class ControlRoomState:
    def __init__(self) -> None:
        self._lock = threading.Lock()
        self._revision = 0
        self._session_requested = False
        self._conveyor_running = True
        self._conveyor_speed = 0.5
        self._arms = {
            arm_id: {"arm_id": arm_id, "enabled": True, "stream_enabled": False}
            for arm_id in ARM_IDS
        }
        self._telemetry: dict[str, Any] = {}
        self._telemetry_received_at_ms = 0

    def controls(self) -> dict[str, Any]:
        with self._lock:
            return self._controls_unlocked()

    def dashboard(self, camera_stats: dict[str, dict[str, Any]]) -> dict[str, Any]:
        with self._lock:
            return {
                "server_time_unix_ms": int(time.time() * 1000),
                "controls": self._controls_unlocked(),
                "telemetry": copy.deepcopy(self._telemetry),
                "telemetry_received_at_unix_ms": self._telemetry_received_at_ms,
                "vision_cameras": copy.deepcopy(camera_stats),
            }

    def update_telemetry(self, telemetry: dict[str, Any]) -> None:
        with self._lock:
            self._telemetry = copy.deepcopy(telemetry)
            self._telemetry_received_at_ms = int(time.time() * 1000)

    def apply(self, command: dict[str, Any]) -> dict[str, Any]:
        action = str(command.get("action", "")).strip()
        with self._lock:
            if action == "start_session":
                self._session_requested = True
            elif action == "stop_session":
                self._session_requested = False
            elif action == "set_conveyor_running":
                self._conveyor_running = _required_bool(command, "value")
            elif action == "set_conveyor_speed":
                speed = float(command.get("value"))
                if speed < 0.0 or speed > 2.0:
                    raise ValueError("conveyor speed must be between 0.0 and 2.0")
                self._conveyor_speed = speed
            elif action == "set_arm_enabled":
                arm = self._required_arm(command)
                arm["enabled"] = _required_bool(command, "value")
            elif action == "set_all_arms_enabled":
                enabled = _required_bool(command, "value")
                for arm in self._arms.values():
                    arm["enabled"] = enabled
            elif action == "set_stream_enabled":
                arm = self._required_arm(command)
                arm["stream_enabled"] = _required_bool(command, "value")
            elif action == "set_all_streams_enabled":
                enabled = _required_bool(command, "value")
                for arm in self._arms.values():
                    arm["stream_enabled"] = enabled
            else:
                raise ValueError(f"unsupported control action: {action or '<empty>'}")

            self._revision += 1
            return self._controls_unlocked()

    def _required_arm(self, command: dict[str, Any]) -> dict[str, Any]:
        arm_id = str(command.get("arm_id", ""))
        if arm_id not in self._arms:
            raise ValueError(f"unknown arm_id: {arm_id or '<empty>'}")
        return self._arms[arm_id]

    def _controls_unlocked(self) -> dict[str, Any]:
        return {
            "revision": self._revision,
            "session_requested": self._session_requested,
            "conveyor_running": self._conveyor_running,
            "conveyor_speed": self._conveyor_speed,
            "arms": [copy.deepcopy(self._arms[arm_id]) for arm_id in ARM_IDS],
        }


def _required_bool(command: dict[str, Any], key: str) -> bool:
    value = command.get(key)
    if not isinstance(value, bool):
        raise ValueError(f"{key} must be a boolean")
    return value
