"""Binary frame protocol shared by the Step 2 vision receiver tests."""

from __future__ import annotations

import json
import struct
from dataclasses import dataclass
from typing import Any


PROTOCOL_VERSION = 1
MAX_HEADER_BYTES = 64 * 1024


class FrameProtocolError(ValueError):
    """Raised when a Unity frame packet is malformed."""


@dataclass(frozen=True)
class FramePacket:
    metadata: dict[str, Any]
    jpeg: bytes


def decode_frame_packet(packet: bytes) -> FramePacket:
    if len(packet) < 5:
        raise FrameProtocolError("packet is too short")

    (header_length,) = struct.unpack(">I", packet[:4])
    if header_length == 0 or header_length > MAX_HEADER_BYTES:
        raise FrameProtocolError("invalid metadata header length")

    image_offset = 4 + header_length
    if image_offset >= len(packet):
        raise FrameProtocolError("packet does not contain image data")

    try:
        metadata = json.loads(packet[4:image_offset].decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise FrameProtocolError("metadata is not valid UTF-8 JSON") from exc

    required_fields = {
        "protocol_version",
        "robot_arm_id",
        "camera_id",
        "frame_id",
        "captured_at_unix_ms",
        "width",
        "height",
        "image_format",
    }
    missing = required_fields.difference(metadata)
    if missing:
        raise FrameProtocolError(f"metadata is missing: {', '.join(sorted(missing))}")
    if metadata["protocol_version"] != PROTOCOL_VERSION:
        raise FrameProtocolError("unsupported protocol version")
    if metadata["image_format"] != "jpeg":
        raise FrameProtocolError("only JPEG frames are supported")
    if not metadata["robot_arm_id"] or not metadata["camera_id"]:
        raise FrameProtocolError("robot and camera identities cannot be empty")
    if metadata["width"] <= 0 or metadata["height"] <= 0:
        raise FrameProtocolError("frame dimensions must be positive")

    jpeg = packet[image_offset:]
    if len(jpeg) < 4 or not jpeg.startswith(b"\xff\xd8") or not jpeg.endswith(b"\xff\xd9"):
        raise FrameProtocolError("image payload is not a complete JPEG")

    return FramePacket(metadata=metadata, jpeg=jpeg)


def encode_frame_packet(metadata: dict[str, Any], jpeg: bytes) -> bytes:
    """Test/client helper matching Unity's big-endian packet layout."""
    header = json.dumps(metadata, separators=(",", ":")).encode("utf-8")
    return struct.pack(">I", len(header)) + header + jpeg
