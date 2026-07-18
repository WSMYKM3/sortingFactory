# Sorting Factory 自测记录

本文档记录 Step 2 Camera Pipeline 的常用命令、Unity 操作和关键参数。

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
4. 左上角应出现 `STEP 2 CAMERA PIPELINE` 面板。
5. 默认选中 `arm_1 / arm_1_camera`。
6. 使用 `<` 和 `>` 切换三台工位相机。
7. 点击 `Capture JPEG`，检查单帧截图。
8. 点击 `Start 10 FPS Stream`，开始向 Python Server 连续发送画面。
9. 面板中的 Server 状态应从 `disconnected` 变为 `connected`。
10. Frame 数字应持续增加，状态应显示服务器已经确认帧。
11. 点击 `Stop 10 FPS Stream` 停止当前相机推流。

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
{"status":"ok","camera_count":0}
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

当前共有 4 个协议测试，全部显示 `ok` 才算通过。

## 8. Step 2 关键参数

| 参数 | 当前值 | 说明 |
| --- | --- | --- |
| Camera Resolution | `1280 x 720` | Unity RenderTexture 和发送帧的尺寸 |
| Aspect Ratio | `16:9` | 与 1280 x 720 对应 |
| Robot Scale | `1.5` | 三台占位机械臂的统一缩放 |
| Workspace Size | `4.8 x 3.6 x 3.9` | 与机械臂同比放大后的工作区尺寸 |
| JPEG Quality | `85` | 图像质量与网络带宽之间的平衡 |
| Stream FPS | `10` | 每台已启动相机的目标发送帧率 |
| Camera FOV | `58` | 固定工位相机视野角度 |
| Near Clip | `0.08` | 相机最近渲染距离 |
| Far Clip | `40` | 相机最远渲染距离 |
| WebSocket URL | `ws://127.0.0.1:8000/ws/camera` | Unity 推流目标 |
| WebSocket Timeout | `2500 ms` | 连接、发送和 ACK 等待超时 |
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

## 10. 常见问题

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
