import json
import struct
import unittest

from protocol import FrameProtocolError, decode_frame_packet, encode_frame_packet


VALID_METADATA = {
    "protocol_version": 1,
    "robot_arm_id": "arm_1",
    "camera_id": "arm_1_camera",
    "frame_id": 42,
    "captured_at_unix_ms": 1_700_000_000_000,
    "width": 1280,
    "height": 720,
    "image_format": "jpeg",
}
FAKE_JPEG = b"\xff\xd8step-2-frame\xff\xd9"


class FrameProtocolTests(unittest.TestCase):
    def test_round_trip_preserves_identity_and_image(self) -> None:
        decoded = decode_frame_packet(encode_frame_packet(VALID_METADATA, FAKE_JPEG))

        self.assertEqual(decoded.metadata["robot_arm_id"], "arm_1")
        self.assertEqual(decoded.metadata["camera_id"], "arm_1_camera")
        self.assertEqual(decoded.metadata["width"], 1280)
        self.assertEqual(decoded.metadata["height"], 720)
        self.assertEqual(decoded.jpeg, FAKE_JPEG)

    def test_rejects_missing_camera_identity(self) -> None:
        metadata = dict(VALID_METADATA)
        del metadata["camera_id"]

        with self.assertRaisesRegex(FrameProtocolError, "camera_id"):
            decode_frame_packet(encode_frame_packet(metadata, FAKE_JPEG))

    def test_rejects_non_jpeg_payload(self) -> None:
        with self.assertRaisesRegex(FrameProtocolError, "JPEG"):
            decode_frame_packet(encode_frame_packet(VALID_METADATA, b"not-an-image"))

    def test_rejects_header_length_larger_than_packet(self) -> None:
        header = json.dumps(VALID_METADATA).encode("utf-8")
        malformed = struct.pack(">I", len(header) + 20) + header

        with self.assertRaises(FrameProtocolError):
            decode_frame_packet(malformed)


if __name__ == "__main__":
    unittest.main()
