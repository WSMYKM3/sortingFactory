import unittest

from vision_service import (
    NormalizedRoi,
    TrackContinuityCache,
    center_is_inside_roi,
    detection_is_valid_for_workspace,
    pixel_crop_for_roi,
    remap_box_from_crop,
    roi_from_metadata,
)


class VisionServiceTests(unittest.TestCase):
    def test_roi_is_read_from_frame_metadata(self) -> None:
        roi = roi_from_metadata(
            {
                "roi_x_min": 0.1,
                "roi_y_min": 0.2,
                "roi_x_max": 0.9,
                "roi_y_max": 0.8,
            }
        )

        self.assertEqual(roi, NormalizedRoi(0.1, 0.2, 0.9, 0.8))

    def test_detection_center_must_be_inside_workspace_roi(self) -> None:
        roi = NormalizedRoi(0.2, 0.25, 0.8, 0.75)

        self.assertTrue(center_is_inside_roi(0.5, 0.5, roi))
        self.assertTrue(center_is_inside_roi(0.2, 0.25, roi))
        self.assertFalse(center_is_inside_roi(0.1, 0.5, roi))
        self.assertFalse(center_is_inside_roi(0.5, 0.9, roi))

    def test_implausibly_large_detection_is_filtered(self) -> None:
        roi = NormalizedRoi(0.0, 0.0, 1.0, 1.0)

        self.assertTrue(detection_is_valid_for_workspace(0.5, 0.5, 0.2, 0.3, roi))
        self.assertFalse(detection_is_valid_for_workspace(0.5, 0.5, 0.9, 0.9, roi))

    def test_crop_box_is_remapped_to_full_frame_coordinates(self) -> None:
        roi = NormalizedRoi(0.2, 0.1, 0.8, 0.9)
        crop = pixel_crop_for_roi(1000, 500, roi)

        remapped = remap_box_from_crop(0.5, 0.5, 0.5, 0.25, crop)

        self.assertEqual((crop.x_min, crop.y_min, crop.x_max, crop.y_max), (200, 50, 800, 450))
        self.assertAlmostEqual(remapped[0], 0.5)
        self.assertAlmostEqual(remapped[1], 0.5)
        self.assertAlmostEqual(remapped[2], 0.3)
        self.assertAlmostEqual(remapped[3], 0.2)

    def test_missing_track_is_predicted_briefly_then_expires(self) -> None:
        roi = NormalizedRoi(0.0, 0.0, 1.0, 1.0)
        cache = TrackContinuityCache(grace_seconds=0.3)
        first = self._detection(center_x=0.4)
        second = self._detection(center_x=0.5)

        cache.update([first], roi, now=1.0)
        cache.update([second], roi, now=1.1)
        predicted = cache.update([], roi, now=1.2)
        expired = cache.update([], roi, now=1.5)

        self.assertEqual(len(predicted), 1)
        self.assertEqual(predicted[0]["tracking_status"], "predicted")
        self.assertAlmostEqual(predicted[0]["bbox_center_x"], 0.6)
        self.assertGreater(predicted[0]["prediction_age_ms"], 0)
        self.assertEqual(expired, [])

    @staticmethod
    def _detection(center_x: float) -> dict:
        return {
            "track_id": 7,
            "class_id": 47,
            "class_name": "apple",
            "confidence": 0.8,
            "bbox_center_x": center_x,
            "bbox_center_y": 0.5,
            "bbox_width": 0.1,
            "bbox_height": 0.1,
            "tracking_status": "tracked",
        }


if __name__ == "__main__":
    unittest.main()
