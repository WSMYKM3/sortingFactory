# Sorting Factory 已完成任务

更新日期：2026-07-18

当前进度：**Step 1、Step 2 已完成；Step 3 检测、跟踪、回传和 Unity 可视化已实现并通过管线验证。**

## Step 1：多机械臂传送带场景

### 环形主传送带

- 已将原传送带延长并构建为闭合环形路径。
- 物体能够沿 Spline 路径循环运动。
- Feeder 和主传送带上的物体统一使用 `0.5 unit/s`，由 `Phase1SceneSetup` 集中设置。
- Step 3 Runtime 面板提供 `0.10-2.00 unit/s` Slider，可在 Play Mode 实时同步修改所有传送物体速度。
- 物体在传送带上使用 Kinematic Rigidbody 和 Spline 驱动。
- 已减少物理碰撞导致的抖动、弹跳和飞出传送带问题。
- 物体进入主传送带后会保留路径位置、高度和朝向。

### 三个独立工作站

- 已创建 `Workstation_1`、`Workstation_2` 和 `Workstation_3`。
- 每个工作站拥有独立的机械臂 ID 和相机 ID。
- 每个工作站拥有独立的可视化 Workspace Volume。
- 每个工作站拥有对应的 Drop Zone。
- 已调整机械臂位置，避免绿色和黄色机械臂重叠。
- 机械臂占位模型已统一放大为原尺寸的 `1.5` 倍。
- Workspace 已同步放大为 `4.8 x 3.6 x 3.9`。
- 当前机械臂是程序生成的占位模型。
- `Phase1SceneSetup` 已支持通过 `robotArmPrefab` 替换为之后提供的 Rigging 机械臂。

当前身份对应关系：

| Workstation | Robot Arm ID | Camera ID |
| --- | --- | --- |
| Workstation 1 | `arm_1` | `arm_1_camera` |
| Workstation 2 | `arm_2` | `arm_2_camera` |
| Workstation 3 | `arm_3` | `arm_3_camera` |

### 初始分拣区域

- 已创建独立的 `InitialSortingArea`，物体不再直接生成到主传送带上。
- 已创建连接初始分拣区域和主传送带的 Feeder Belt。
- 场景中初始放置 15 个物体。
- 物体尺寸已缩小，并以随机角度和分散位置杂乱排列。
- 物体不会同时排成一条直线进入传送带。
- 每个物体拥有不同的 Release Delay，按时间间隔进入 Feeder Belt。
- 物体通过 Feeder Spline 转移到主环形传送带。
- 初始物体直接存在于编辑器场景中，不需要进入 Runtime 后才生成。

### 场景生成工具

- `Phase1SceneSetup` 可以重新生成 Step 1 和 Step 2 内容。
- Unity 菜单位置：

```text
Tools > Sorting Factory > Build Steps 1-2 Scene
```

- 场景缺少分拣区域或相机时，Editor Builder 会尝试自动重建。
- 当前生成内容已经写入 `Assets/aSimingSpace/MainScene.unity`。

## Step 2：Camera Feed 和 Vision Input Pipeline

### 固定工位相机

- 三个工作站均已创建固定的 `CameraMount_Step2`。
- 每台相机固定在对应工作站下，并朝向自己的 Workspace 中心。
- 相机不会跟随传送带物体移动。
- 相机画面能够覆盖本地传送带和 Workspace，之后可以继续放置物理 Latest Pick Line。

当前相机参数：

| 参数 | 当前值 |
| --- | --- |
| Resolution | `1280 x 720` |
| Aspect Ratio | `16:9` |
| Field of View | `58` |
| Near Clip Plane | `0.08` |
| Far Clip Plane | `40` |
| JPEG Quality | `85` |
| Target Stream Rate | `10 FPS` |

### Unity 相机调试面板

- Play Mode 左上角已添加 `STEP 2 CAMERA PIPELINE` 面板。
- 默认显示 Arm 1 相机。
- 可以使用左右按钮切换三台相机。
- 可以实时查看选中相机的 RenderTexture。
- `Capture JPEG` 可以捕获并保存单帧图片。
- `Start 10 FPS Stream` 可以开始连续推流。
- `Stop 10 FPS Stream` 可以停止当前相机推流。
- 面板显示相机身份、连接状态、已捕获帧数量和 ACK 状态。

### 单帧截图

- Unity 使用 `AsyncGPUReadback` 读取相机 RenderTexture。
- 原始 RGBA 数据会转换为 JPEG。
- 单帧截图使用相机 ID 和时间戳作为文件名。
- 当前截图保存目录：

```text
/Users/simon/Documents/WsmFiles/SortingFactoryScreenshots
```

- 手动截图不会自动删除，需要按需要自行清理。

### Unity 到 Python 的 WebSocket 协议

- Unity 使用 WebSocket 向外部 Python Server 发送 JPEG。
- 当前连接地址：

```text
ws://127.0.0.1:8000/ws/camera
```

- 每个二进制消息包含：
  - 4 bytes Big-Endian JSON Header Length。
  - UTF-8 JSON Metadata。
  - JPEG Image Bytes。
- Metadata 已包含：
  - `protocol_version`
  - `robot_arm_id`
  - `camera_id`
  - `frame_id`
  - `captured_at_unix_ms`
  - `width`
  - `height`
  - `image_format`
- WebSocket 连接、发送和 ACK 等待超时当前为 `2500 ms`。
- ACK 可以关联到正确的 Arm ID、Camera ID 和 Frame ID。

### Python Vision Receiver

- 已创建 `PythonVisionServer`。
- 已创建项目本地 Python 虚拟环境 `.venv`。
- 已安装 FastAPI 和 Uvicorn。
- Server 可以接收并验证 Unity WebSocket 帧。
- Server 会向 Unity 返回 JSON ACK。
- Server 会分别记录每台相机的接收统计信息。
- Server 不会在内存中保留历史图片列表。
- 每台相机只保留一个 latest JPEG，新帧会覆盖旧帧。

当前 HTTP 接口：

```text
GET http://127.0.0.1:8000/health
GET http://127.0.0.1:8000/stats
GET http://127.0.0.1:8000/latest/arm_1_camera
GET http://127.0.0.1:8000/latest/arm_2_camera
GET http://127.0.0.1:8000/latest/arm_3_camera
```

latest frame 保存位置：

```text
PythonVisionServer/received_frames/arm_n_camera_latest.jpg
```

`received_frames` 已加入 `.gitignore`。

## 已完成验证

- Unity Runtime 脚本编译通过。
- Unity Editor 脚本编译通过。
- Python 文件语法检查通过。
- Frame Protocol、ROI 和过滤的 8 个单元测试全部通过。
- 已执行真实 WebSocket 往返测试。
- Python 正确收到 `arm_1 / arm_1_camera / frame_id` Metadata。
- Unity 可以收到 Python ACK。
- `/stats` 可以按 Camera ID 分别显示接收结果。
- 手动 JPEG 截图已经成功生成。

## Step 3：YOLO 实时检测与 ByteTrack 跟踪

- Python 已加载 COCO 预训练 `yolo26n.pt`。
- Python 会先裁剪 Workspace ROI，再以 `640 x 640` 推理并映射回完整画面。
- Detector confidence floor 为 `0.10`，ByteTrack 高置信度/新建轨迹阈值为 `0.45`，IoU 为 `0.70`。
- 当前主动检测类别限制为 `bottle / cup / banana / apple / orange`，减少传送带纹理误检。
- Apple Silicon Server 实测选择 `mps`，CPU 作为 fallback。
- 每台 Camera ID 使用独立的 ByteTrack 状态，避免三台相机的 track ID 互相污染。
- Python 返回 class、confidence、归一化 bounding box、track ID 和 tracking status。
- 弱检测可以维持已有 Track；1-3 帧漏检会返回最多 `300 ms` 的淡化 predicted 轨迹。
- Unity 协议已升级到 v2，并校验 Arm ID、Camera ID 和 Frame ID。
- Unity 每帧将 Workspace Collider 投影为归一化 ROI，Python 忽略 ROI 外结果。
- 已过滤覆盖超过 70% 画面的明显异常检测框。
- Unity 面板已显示 ROI、bounding box、类别、置信度和 persistent track ID。
- Unity 在服务器帧之间平滑 bbox，并区分真实 `tracked` 与淡色 `PRED` 结果。
- 主面板显示实际 Server FPS、tracked/predicted 数量和 inference latency。
- Unity 面板顶部只汇总三台相机的 `OFF / LIVE` 推流状态。
- 独立 `LIVE DETECTIONS` 面板按 Arm 显示当前 track ID、类别和置信度，并支持滚动。
- 已添加单独启动当前 Arm 和同时启动/停止三台 Camera Stream 的控制按钮。
- WebSocket 首次推理超时已由 `2500 ms` 调整到 `15000 ms`。
- `/health` 会显示模型、tracker、device 和推理参数。
- `/classes` 会返回 COCO 的全部 80 个类别。

Step 3 实测结果：

- YOLO26n 权重 SHA-256 校验通过。
- Python 真实 JPEG 推理通过。
- 完整 WebSocket v2 往返通过。
- 连续两帧的 ByteTrack ID 均保持为 `1`。
- MPS warm-up 后两次实测推理约为 `17.6 ms` 和 `8.4 ms`。
- Unity Runtime C# 使用项目当前 Roslyn response file 编译通过。

当前素材限制：场景中的 Bottle 是 COCO 类别，但简单几何占位模型不保证可靠识别；COCO 没有通用 Can 和 Box 类别。要稳定识别这三类，需要换成写实物体模型或训练自定义 YOLO 权重。

详细启动命令和自测步骤见：

```text
Assets/aSimingSpace/selfTest.md
```

## 主要实现文件

| 文件 | 作用 |
| --- | --- |
| `Phase1SceneSetup.cs` | 生成工作站、分拣区域、初始物体和相机 |
| `PrototypeVisualFactory.cs` | 创建占位机械臂、物体和场景可视化 |
| `RobotWorkstation.cs` | 保存工作站引用和 Arm/Camera 身份 |
| `ClosedLoopConveyorMover.cs` | 检测进入主传送带的 Rigidbody |
| `SplineConveyorObject.cs` | 稳定驱动物体沿环形 Spline 运动 |
| `SortingAreaFeedObject.cs` | 将初始物体按延迟送入 Feeder 和主传送带 |
| `WorkstationCameraController.cs` | 相机预览、截图、JPEG 和连续推流 |
| `VisionFrameProtocol.cs` | Unity WebSocket 帧协议和 ACK 接收 |
| `Step2CameraDebugPanel.cs` | Step 2 Runtime 调试面板 |
| `PythonVisionServer/server.py` | FastAPI WebSocket 和 HTTP Server |
| `PythonVisionServer/protocol.py` | Python 帧协议解析和验证 |
| `PythonVisionServer/tests/test_protocol.py` | Protocol 单元测试 |
| `PythonVisionServer/vision_service.py` | YOLO26n、ROI 过滤和分相机 ByteTrack |
| `PythonVisionServer/tests/test_vision_service.py` | ROI 和检测框过滤测试 |

## 当前尚未完成

以下内容属于后续步骤，当前实现中还没有：

- 当前三个占位物体类别的自定义 YOLO 数据集和训练权重。
- 使用实际移动 Bottle 模型进行完整场景识别验收。
- 物理 Latest Pick Line 及其相机画面显示。
- Remaining Pickable Time 计算。
- Execute 或 Skip 决策。
- 机械臂目标锁定和 Busy/Idle 状态机。
- Rigging 机械臂的 IK、抓取和放置动作。
- 抓取失败处理和下游重新检测验证。
- 多机械臂并行抓取验证。
- 每台机械臂的数据记录和全局监控。

当前 Python Server 已是 **Step 3 YOLO + ByteTrack 推理服务**。

## 下一阶段

下一步从 `MainMission.md` 的 **Step 4: Determine Whether There Is Enough Time to Pick** 开始：

```text
Tracked Object
-> Latest Pick Line
-> Remaining Pickable Time
-> Execute or Skip
```
