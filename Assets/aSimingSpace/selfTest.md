# Sorting Factory 自测记录

本文档记录 Step 2 Camera Pipeline 和 Step 3 YOLO Tracking 的常用命令、Unity 操作和关键参数。

## 1. Python Server 首次安装

在 macOS Terminal 中进入项目的 Python Server 文件夹：

```bash
cd /Users/simon/Documents/UnityProjects/sortingFactory/PythonVisionServer
```

创建虚拟环境并安装依赖：

```bash
python3 -m venv .venv
source .venv/bin/activate
python3 -m pip install -r requirements.txt
```

项目中已经创建过 `.venv` 时，不需要重复安装。

## 2. 每次启动 Python Server

```bash
cd /Users/simon/Documents/UnityProjects/sortingFactory/PythonVisionServer
source .venv/bin/activate
python server.py
```

启动成功时 Terminal 会显示：

```text
Uvicorn running on http://127.0.0.1:8000
```

运行期间不要关闭这个 Terminal。停止 Server 使用：

```text
Control + C
```

## 3. 检查和终止被占用的 8000 端口

检查哪个进程正在监听端口：

```bash
lsof -nP -iTCP:8000 -sTCP:LISTEN
```

输出中的 `PID` 是进程编号。正常终止对应进程：

```bash
kill <PID>
```

例如 PID 为 `12345`：

```bash
kill 12345
```

再次运行 `lsof`，没有输出表示端口已经释放。

## 4. Unity Camera Pipeline 自测

1. 打开 `Assets/aSimingSpace/MainScene.unity`。
2. 确认 Python Server 已经运行。
3. 进入 Unity Play Mode。
4. 左上角应出现 `STEP 3 YOLO + BYTETRACK` 面板。
5. 顶部 `A1 / A2 / A3` 按钮只显示三台工位的 `OFF / LIVE` 状态。
6. 点击对应状态按钮可切换相机预览；右侧 `LIVE DETECTIONS` 独立面板按 Arm 显示 track ID、类别和置信度。
7. 点击 `Capture JPEG`，检查单帧截图。
8. 点击 `Start Arm n` 只启动当前相机；点击 `Start All Streams` 同时启动三台相机。
9. 面板中的 Server 状态应从 `disconnected` 变为 `connected`。
10. Frame 数字应持续增加，并显示模型名、检测数量和推理耗时。
11. 识别到物体后，应显示 bounding box、类别、置信度和 `#track_id`。
12. 绿色边框是由 Workspace Collider 投影得到的检测 ROI，ROI 外物体会被忽略。
13. 点击 `Stop Arm n` 停止当前相机，或点击 `Stop All Streams` 停止三台相机。

如果场景中没有相机或调试面板，先退出 Play Mode，然后执行：

```text
Unity menu: Tools > Sorting Factory > Build Steps 1-2 Scene
```

## 5. 截图保存位置

单帧截图保存在：

```text
/Users/simon/Documents/WsmFiles/SortingFactoryScreenshots
```

文件名示例：

```text
arm_1_camera_20260718_105011_525.jpg
```

文件名包含相机 ID 和截图时间。第一次截图时 Unity 会自动创建文件夹。

## 6. Server 检查命令

检查 Server 是否运行：

```bash
curl http://127.0.0.1:8000/health
```

预期结果：

```json
{"status":"ok","model":"yolo26n.pt","tracker":"ByteTrack","device":"mps"}
```

查看模型支持的全部类别：

```bash
curl http://127.0.0.1:8000/classes
```

查看每台相机接收状态：

```bash
curl http://127.0.0.1:8000/stats
```

在 Unity 开始推流后，结果中应出现 `arm_1_camera`、分辨率、帧数量和最后帧编号。

在浏览器查看最新画面：

```text
http://127.0.0.1:8000/latest/arm_1_camera
http://127.0.0.1:8000/latest/arm_2_camera
http://127.0.0.1:8000/latest/arm_3_camera
```

相机还没有发送过画面时，latest 接口返回 `404` 是正常情况。

## 7. 协议单元测试

```bash
cd /Users/simon/Documents/UnityProjects/sortingFactory
PYTHONPATH=PythonVisionServer PythonVisionServer/.venv/bin/python \
  -m unittest discover -s PythonVisionServer/tests -v
```

当前共有 10 个协议、ROI、坐标映射、过滤和连续性测试，全部显示 `ok` 才算通过。

## 8. Step 2 关键参数

| 参数 | 当前值 | 说明 |
| --- | --- | --- |
| Camera Resolution | `1280 x 720` | Unity RenderTexture 和发送帧的尺寸 |
| Aspect Ratio | `16:9` | 与 1280 x 720 对应 |
| Robot Scale | `1.5` | 三台占位机械臂的统一缩放 |
| Workspace Size | `4.8 x 3.6 x 3.9` | 与机械臂同比放大后的工作区尺寸 |
| JPEG Quality | `85` | 图像质量与网络带宽之间的平衡 |
| Stream FPS | `10` | 每台已启动相机的目标发送帧率 |
| Conveyor Object Speed | `0.5 unit/s` | Feeder 和主环上物体的统一移动速度 |
| Camera FOV | `46` | 收紧后的固定工位相机视野角度 |
| Near Clip | `0.08` | 相机最近渲染距离 |
| Far Clip | `40` | 相机最远渲染距离 |
| WebSocket URL | `ws://127.0.0.1:8000/ws/camera` | Unity 推流目标 |
| WebSocket Timeout | `15000 ms` | 包含首次 YOLO/MPS warm-up 的 ACK 等待超时 |
| Capture Folder | `/Users/simon/Documents/WsmFiles/SortingFactoryScreenshots` | 单帧 JPEG 保存目录 |
| Arm 1 Identity | `arm_1 / arm_1_camera` | Python 返回结果时必须保留 |
| Arm 2 Identity | `arm_2 / arm_2_camera` | Python 返回结果时必须保留 |
| Arm 3 Identity | `arm_3 / arm_3_camera` | Python 返回结果时必须保留 |

## 9. 参数调整注意事项

- 调高 FPS 会增加 JPEG 编码开销、CPU 使用率和网络带宽。
- 调高 JPEG Quality 会增加单帧数据大小。
- 修改分辨率后，需要同时确认模型输入缩放和 bounding box 坐标转换。
- `robot_arm_id` 和 `camera_id` 是后续 YOLO、Tracking 和机械臂关联的关键字段，不应重复。
- 三台相机可以分别启动或停止推流。调试时建议先只验证 Arm 1。
- Camera Mount 必须固定在工位下，不要在运行时跟随传送带物体移动。
- Step 3 返回检测结果时，必须使用收到帧中的 `robot_arm_id`、`camera_id` 和 `frame_id`。
- Play Mode 中可使用 Step 3 面板底部的 `CONVEYOR OBJECT SPEED` Slider，在 `0.10-2.00 unit/s` 范围实时调整所有 Feeder 和主环物体速度。

## 10. Step 3 模型与可检测物体

当前模型为 COCO 预训练 `YOLO26n`，跟踪器为定制 `sorting_bytetrack.yaml`。Python 会先裁剪 Workspace ROI，再缩放到 `640 x 640` 推理，并将 bbox 映射回完整画面。Detector confidence floor 为 `0.10`，新建 Track 和高置信度关联阈值为 `0.45`，IoU 为 `0.70`。

当前分拣线只启用 `bottle / cup / banana / apple / orange` 五类。模型仍包含下面全部 80 个 COCO 类别，但其他类别不会返回给 Unity，以避免传送带箭头和机械臂被误认成 umbrella 等无关物体。

`0.10-0.45` 的弱检测可以维持已有 ByteTrack ID，但不会轻易创建新 Track。短暂漏检最多保留 `300 ms`，返回 `tracking_status: predicted`；Unity 使用淡色 `PRED` 框显示，并在渲染帧之间平滑 bbox。主面板的 `FPS / T / P / ms` 分别表示服务器实际帧率、真实 Track 数、预测 Track 数和推理耗时。

COCO 的 80 个类别：

```text
person, bicycle, car, motorcycle, airplane, bus, train, truck, boat,
traffic light, fire hydrant, stop sign, parking meter, bench, bird, cat,
dog, horse, sheep, cow, elephant, bear, zebra, giraffe, backpack, umbrella,
handbag, tie, suitcase, frisbee, skis, snowboard, sports ball, kite,
baseball bat, baseball glove, skateboard, surfboard, tennis racket, bottle,
wine glass, cup, fork, knife, spoon, bowl, banana, apple, sandwich, orange,
broccoli, carrot, hot dog, pizza, donut, cake, chair, couch, potted plant,
bed, dining table, toilet, tv, laptop, mouse, remote, keyboard, cell phone,
microwave, oven, toaster, sink, refrigerator, book, clock, vase, scissors,
teddy bear, hair drier, toothbrush
```

当前场景对应关系：

| Unity 原型 | COCO 是否有对应类别 | 说明 |
| --- | --- | --- |
| Bottle | 有：`bottle` | 真实或高质量瓶子模型更可靠 |
| Can | 没有通用 `can` | 可能误认成 cup/bottle，也可能完全不识别 |
| Box | 没有通用 `box` | COCO 没有 cardboard box/package 类别 |

程序生成的简单 Cylinder/Cube 不保证能被真实图像训练的 COCO 模型识别。要稳定区分 `Bottle / Can / Box`，需要换成写实模型，或后续训练自定义 YOLO 权重。

首次启动若 `yolo26n.pt` 不存在，Ultralytics 会联网下载。模型文件已加入 `.gitignore`。Apple Silicon 正常会使用 `mps`；需要排查问题时可以强制 CPU：

```bash
VISION_DEVICE=cpu python server.py
```

## 11. 常见问题

### Unity 显示 Server disconnected

1. 检查运行 Server 的 Terminal 是否仍然打开。
2. 运行 `curl http://127.0.0.1:8000/health`。
3. 检查 Unity 相机上的 Server URL 是否为 `ws://127.0.0.1:8000/ws/camera`。
4. 停止推流后重新点击 `Start 10 FPS Stream`。

### 图片没有保存

1. 确认点击的是 `Capture JPEG`，连续推流不会把每一帧保存到截图目录。
2. 检查 `/Users/simon/Documents/WsmFiles/SortingFactoryScreenshots`。
3. 查看面板底部是否显示 `Saved arm_n_camera_...jpg`。

### latest 图片没有更新

1. 确认 Python Server 正在运行。
2. 在 Unity 中点击 `Start 10 FPS Stream`。
3. 使用 `/stats` 确认 `frame_count` 正在增加。
4. 刷新对应相机的 `/latest/arm_n_camera` 页面。

### 有框但类别明显不对

1. 先确认物体属于上面的 COCO 80 类。
2. 确认相机能清楚看到完整物体，而不是只看到 Workspace 或传送带。
3. 使用真实或写实 3D 模型；纯色 Cylinder/Cube 很容易误判。
4. 对项目特有的罐子、包装盒和工业零件，需要训练自定义数据集。
