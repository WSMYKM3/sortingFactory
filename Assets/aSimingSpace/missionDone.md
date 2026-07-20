# Sorting Factory 已完成任务

更新日期：2026-07-19

当前进度：**Step 1-Step 5 已实现；Step 6-Step 8 已联合完成代码实现和编译验证，等待退出当前 Play Mode 后进行三机械臂端到端运行验收。**

## Step 1：多机械臂传送带场景

### 环形主传送带

- 已将原传送带延长并构建为闭合环形路径。
- 物体能够沿 Spline 路径循环运动。
- Feeder 和主传送带上的物体统一使用 `0.5 unit/s`，由 `Phase1SceneSetup` 集中设置。
- Runtime 面板提供 `0.10-2.00 unit/s` Slider，可在 Play Mode 实时同步修改所有传送物体速度。
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
- 当前机械臂是程序生成的 SO-101 结构占位模型。
- 三台机械臂均已升级为 `shoulder_pan -> shoulder_lift -> elbow_flex -> wrist_flex -> wrist_roll -> gripper` 六通道层级。
- 每台机械臂均带有 `So101RobotArmRig`，保存关节、夹爪、抓取点和物体跟随点引用。
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
- 场景中初始放置 15 个单面图片检测盒。
- 检测类别为 `apple / banana / bottle / cup / orange`，每个盒子只在朝向相机的一面显示图片。
- 图片 Quad 与盒体保持间距，盒体尺寸与图片比例已经调整，避免重叠生成和明显穿插。
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
- 相机画面能够覆盖本地传送带和 Workspace，并用于投影物理 Latest Pick Line。

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

当前素材限制：COCO 模型通过检测盒正面的写实图片识别 `apple / banana / bottle / cup / orange`；当图片背向相机、尺寸过小或被遮挡时，检测可靠性仍会下降。后续换成真实 3D 物体时可能需要更写实的模型或自定义 YOLO 权重。

详细启动命令和自测步骤见：

```text
Assets/aSimingSpace/selfTest.md
```

## Step 4：Latest Pick Line 和抓取时间判断

- 三个工作站均根据主传送带 Spline、Workspace 和传送带速度计算物理 Latest Pick Line。
- Latest Pick Line 会投影到对应相机画面，不是只存在于 UI 中的任意线条。
- 每个持续跟踪目标会计算剩余可抓取时间、是否越过最晚抓取线以及 `Execute / Skip` 决策。
- 当前预设抓取时间为 `1.25 s`，安全余量为 `0.25 s`，最低决策时间合计 `1.50 s`。
- 只有当前检测置信度达到 `45%` 的目标才允许进入 `Execute`；低置信度目标继续等待，不能触发抓取。
- 目标在开始抓取前越过 Latest Pick Line 时会被跳过；已经锁定并开始的任务不会因越线而中止。
- 检测结果支持确认、短时 coasting、丢失和跳过状态，减少单帧漏检造成的决策跳变。
- Runtime 调试面板能够显示 Latest Pick Line、目标状态、剩余时间、所需时间和决策原因。

## Step 5：目标锁定和本地抓取循环

- 满足条件的目标会被锁定，机械臂进入 Busy/Securing 状态并暂时忽略其他目标。
- 目标由检测框和图片 Quad 确认；执行动画时使用 Quad 所属的 `DetectionLabeledBox` 根对象作为临时可移动载体。
- 机械臂接近根对象中心，并把整个父级绑定到夹爪后再执行放置；无法解析或绑定父级时不能把该次动作记录为成功。
- 抓取后的物体绑定到机械臂末端 `ObjectHoldPoint`，移动期间暂停原传送带驱动和刚体碰撞。
- 当前可靠原型采用短距离放置动作后将已抓取物体稳定放入对应 Drop Zone，再让机械臂回到初始姿态。
- 三个 Drop Zone 已放大并增加约束碰撞体，降低物体落在区域外的概率。
- 抓取状态会向决策控制器报告成功、失败和循环完成，完成回零后才重新接收目标。
- 三台场景机械臂已安全升级为 SO-101 六通道层级，升级过程未重建相机、传送带或 Drop Zone。
- Unity Runtime 与 Editor 程序集已通过编译；升级后的 SO-101 抓取循环仍需进行一次最终 Play Mode 回归确认。

## Step 6：真实失败恢复和下游重新检测

- 不注入随机或人工 `simulated_grasp_failure`，只处理正常运行中真实发生的失败。
- 已统一处理图片父级无法解析、目标消失、IK 无法到达、夹取未附着和放置失败。
- 失败后会释放 Camera Target Lock、物理物体占用和夹爪状态，并让机械臂回零恢复 `Idle`。
- 已夹起但失败的物体会解绑，并重新投影到主传送带最近位置继续运动。
- 当前工位会把失败 Evaluation 标记为终态，不使用旧位置再次创建同一任务。
- 下游机械臂继续使用自己的 Camera、ByteTrack 和本地 Logical Track 重新检测。
- 没有添加全局 `simulation_object_id`，也不会跨工位传递物体位置或 Track ID。

## Step 7：多机械臂并行保护和监测

- 新增 `ConveyorPickClaim`，物理物体只保存临时 Owner Arm ID，不承担任务分配。
- 两台机械臂尝试处理同一物体时，只有第一台能够获得占用，另一台结束本地任务并安全回零。
- 每台机械臂仍独立检测、判断、锁定和执行，没有中央抓取任务队列。
- 新增只读 `MultiRobotOperationMonitor`，统计当前活跃机械臂、峰值并发数和被阻止的物体占用冲突。
- 监测到两台以上机械臂同时工作时会在 Console 输出验证日志。

## Step 8：SO-101 CSV 数据记录

- 每次进入 Play Mode 自动创建统一 Run ID，三台机械臂分别写入 `arm_1.csv`、`arm_2.csv` 和 `arm_3.csv`。
- CSV 根目录为 `/Users/simon/Documents/WsmFiles/SortingFactoryScreenshots/csvdata`。
- 当前只保存 CSV，不保存相机图片或视频。
- 以 `10 Hz` 记录 `frame` 行，并在每次机械臂回零后写入 `episode` 汇总行。
- `unity_time_s` 保留整次运行的连续时间；`episode_time_s` 在每次抓取周期首次记录时从 `0` 开始，Idle 行留空。
- Observation 和 Action 均使用 `shoulder_pan / shoulder_lift / elbow_flex / wrist_flex / wrist_roll / gripper` 固定顺序。
- 同时记录 Arm/Camera ID、时间戳、相机帧号、工作流状态、检测和 Track 信息、世界位置、时间判断、Execute/Skip、结果、失败原因及循环耗时。
- 当前占位机械臂是运动学驱动，因此 Observation 与该帧 Action 可能相同；字段已经分离，之后可直接接真实 SO-101 状态反馈。

## 主要实现文件

| 文件 | 作用 |
| --- | --- |
| `Phase1SceneSetup.cs` | 生成工作站、分拣区域、初始物体和相机 |
| `PrototypeVisualFactory.cs` | 创建占位机械臂、物体和场景可视化 |
| `So101RobotArmRig.cs` | 保存 SO-101 六通道关节、夹爪和抓取点引用 |
| `RobotWorkstation.cs` | 保存工作站引用和 Arm/Camera 身份 |
| `ClosedLoopConveyorMover.cs` | 检测进入主传送带的 Rigidbody |
| `SplineConveyorObject.cs` | 稳定驱动物体沿环形 Spline 运动 |
| `SortingAreaFeedObject.cs` | 将初始物体按延迟送入 Feeder 和主传送带 |
| `WorkstationCameraController.cs` | 相机预览、截图、JPEG 和连续推流 |
| `VisionFrameProtocol.cs` | Unity WebSocket 帧协议和 ACK 接收 |
| `Step2CameraDebugPanel.cs` | Step 2 Runtime 调试面板 |
| `WorkstationPickDecisionController.cs` | Latest Pick Line、时间判断、目标锁定和机械臂状态 |
| `PrototypeRobotArmIKController.cs` | SO-101 占位机械臂 IK、夹取、放置和回零 |
| `DetectionLabeledBox.cs` | 构建单面 YOLO 图片检测盒 |
| `ConveyorPickClaim.cs` | 防止多台机械臂同时处理同一物理物体 |
| `MultiRobotOperationMonitor.cs` | 统计三机械臂活跃数、并发峰值和占用冲突 |
| `So101CsvRecorder.cs` | 每台机械臂的 SO-101 frame/episode CSV 记录 |
| `PythonVisionServer/server.py` | FastAPI WebSocket 和 HTTP Server |
| `PythonVisionServer/protocol.py` | Python 帧协议解析和验证 |
| `PythonVisionServer/tests/test_protocol.py` | Protocol 单元测试 |
| `PythonVisionServer/vision_service.py` | YOLO26n、ROI 过滤和分相机 ByteTrack |
| `PythonVisionServer/tests/test_vision_service.py` | ROI 和检测框过滤测试 |

## 当前尚未完成

以下内容尚未完成：

- 用真实 3D 水果和容器模型替换当前图片检测盒，并评估是否需要自定义 YOLO 权重。
- 升级后的 SO-101 占位机械臂完整抓取循环运行回归。
- 正常运行中真实失败的安全恢复和下游重新检测运行验证。
- 三台 Camera Stream 同时开启时的并行抓取运行验收。
- 检查实际生成的三份 SO-101 CSV 内容和 episode 汇总行。
- Step 9 localhost 控制室。
- Step 10 Session Controls。

随机或人工注入的 `simulated_grasp_failure` 已从当前任务范围移除，不再实现失败概率、随机失败开关或相关 UI。真实运行失败仍需按 Step 6 安全处理。

## 下一阶段

退出当前 Play Mode 并重新进入后，按 `selfTest.md` 的 Step 6-8 联合自测流程进行验收。验收通过后进入 **Step 9: Build a Localhost Control Room**。

```text
Three local camera streams
-> Independent detection and grasp cycles
-> Real failure recovery and downstream re-detection
-> Per-arm SO-101 CSV records
```
