const armIds = ["arm_1", "arm_2", "arm_3"];
const armNodes = new Map();
let dashboard = null;
let speedTimer = null;

const byId = (id) => document.getElementById(id);
const percent = (value, attempts = 1) => attempts > 0 ? `${Math.round((value || 0) * 100)}%` : "N/A";
const seconds = (value) => `${Number(value || 0).toFixed(2)}s`;
const elapsed = (value) => {
  const total = Math.max(0, Math.floor(value || 0));
  return `${String(Math.floor(total / 60)).padStart(2, "0")}:${String(total % 60).padStart(2, "0")}`;
};

function buildArmPanels() {
  const grid = byId("arm-grid");
  const template = byId("arm-template");
  armIds.forEach((armId, index) => {
    const node = template.content.firstElementChild.cloneNode(true);
    node.dataset.armId = armId;
    node.querySelector("h2").textContent = `ARM ${index + 1}`;
    const image = node.querySelector("img");
    image.addEventListener("load", () => {
      image.style.display = "block";
      node.querySelector(".camera-empty").style.display = "none";
    });
    node.querySelector(".arm-enable").addEventListener("click", () => {
      const arm = telemetryArm(armId);
      sendControl("set_arm_enabled", { arm_id: armId, value: !(arm?.enabled ?? true) });
    });
    node.querySelector(".stream-toggle").addEventListener("click", () => {
      const arm = telemetryArm(armId);
      sendControl("set_stream_enabled", { arm_id: armId, value: !(arm?.streaming ?? false) });
    });
    grid.appendChild(node);
    armNodes.set(armId, node);
  });
}

async function sendControl(action, fields = {}) {
  try {
    await fetch("/api/control", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ action, ...fields }),
    });
    await refresh();
  } catch (_) {
    setUnityOnline(false);
  }
}

function telemetryArm(armId) {
  return dashboard?.telemetry?.arms?.find((arm) => arm.arm_id === armId);
}

function setUnityOnline(online) {
  byId("unity-dot").classList.toggle("online", online);
  byId("unity-status").textContent = online ? "UNITY ONLINE" : "UNITY OFFLINE";
}

function render(data) {
  dashboard = data;
  const controls = data.controls || {};
  const telemetry = data.telemetry || {};
  const telemetryAge = Date.now() - Number(data.telemetry_received_at_unix_ms || 0);
  const unityOnline = telemetry.unity_status === "online" && telemetryAge < 3000;
  setUnityOnline(unityOnline);

  const state = telemetry.session_state || (controls.session_requested ? "WAITING" : "INACTIVE");
  byId("session-state").textContent = state.toUpperCase();
  byId("session-time").textContent = elapsed(telemetry.session_duration_s);
  byId("start-session").disabled = !!controls.session_requested;
  byId("stop-session").disabled = !controls.session_requested;

  const running = controls.conveyor_running !== false;
  byId("conveyor-toggle").textContent = running ? "Pause" : "Run";
  const speed = Number(controls.conveyor_speed ?? 0.5);
  if (document.activeElement !== byId("conveyor-speed")) byId("conveyor-speed").value = speed;
  byId("conveyor-speed-value").textContent = `${speed.toFixed(2)} u/s`;

  const attempts = Number(telemetry.total_attempts || 0);
  byId("metric-active-arms").textContent = telemetry.active_arm_count || 0;
  byId("metric-streams").textContent = `${telemetry.active_stream_count || 0} / 3`;
  byId("metric-vision").textContent = `${telemetry.connected_vision_count || 0} / 3`;
  byId("metric-attempts").textContent = attempts;
  byId("metric-successes").textContent = telemetry.successful_grasps || 0;
  byId("metric-failures").textContent = telemetry.failed_grasps || 0;
  byId("metric-success").textContent = percent(telemetry.success_rate, attempts);
  byId("metric-skips").textContent = telemetry.skipped_objects || 0;
  byId("metric-task-time").textContent = seconds(telemetry.average_task_duration_s);
  byId("metric-throughput").textContent = `${Number(telemetry.throughput_per_minute || 0).toFixed(1)}/min`;

  armIds.forEach((armId) => renderArm(armId, telemetryArm(armId), data.vision_cameras || {}));
}

function renderArm(armId, arm, cameras) {
  const node = armNodes.get(armId);
  if (!node) return;
  const cameraId = arm?.camera_id || `${armId}_camera`;
  const cameraStats = cameras[cameraId];
  const enabled = arm?.enabled ?? true;
  const streaming = arm?.streaming ?? false;
  const online = arm?.vision_connected ?? false;
  node.querySelector(".arm-dot").classList.toggle("online", enabled && !arm?.disable_pending);
  node.querySelector(".arm-enable").textContent = arm?.disable_pending ? "Disabling" : (enabled ? "Disable" : "Enable");
  node.querySelector(".stream-toggle").textContent = streaming ? "Stop Stream" : "Start Stream";
  node.querySelector(".camera-badge").textContent = `${streaming ? "LIVE" : "OFF"} · ${online ? "VISION ONLINE" : "VISION OFFLINE"}`;
  node.querySelector(".workflow").textContent = arm?.workflow_state || (enabled ? "IDLE" : "DISABLED");
  node.querySelector(".motion").textContent = arm?.motion_state || "";
  node.querySelector(".target").textContent = arm?.current_target || "No Target";
  node.querySelector(".decision").textContent = arm?.decision || "—";
  node.querySelector(".failure").textContent = arm?.last_failure_reason || "—";
  node.querySelector(".success-count").textContent = arm?.successful_grasps || 0;
  node.querySelector(".failure-count").textContent = arm?.failed_grasps || 0;
  node.querySelector(".attempt-count").textContent = arm?.total_attempts || 0;
  node.querySelector(".skip-count").textContent = arm?.skipped_objects || 0;
  node.querySelector(".success-rate").textContent = percent(arm?.success_rate, arm?.total_attempts || 0);
  node.querySelector(".grasp-time").textContent = seconds(arm?.average_grasp_time_s);
  node.querySelector(".cycle-time").textContent = seconds(arm?.average_cycle_time_s);
  node.querySelector(".utilization").textContent = percent(arm?.utilization, 1);
  node.querySelector(".throughput").textContent = `${Number(arm?.throughput_per_minute || 0).toFixed(1)}/min`;
  if (streaming && cameraStats) {
    node.querySelector("img").src = `/latest/${cameraId}?t=${Date.now()}`;
  }
}

async function refresh() {
  try {
    const response = await fetch("/api/control-room", { cache: "no-store" });
    if (!response.ok) throw new Error("dashboard unavailable");
    render(await response.json());
  } catch (_) {
    setUnityOnline(false);
  }
}

byId("start-session").addEventListener("click", () => sendControl("start_session"));
byId("stop-session").addEventListener("click", () => sendControl("stop_session"));
byId("conveyor-toggle").addEventListener("click", () => sendControl("set_conveyor_running", { value: !(dashboard?.controls?.conveyor_running !== false) }));
byId("enable-arms").addEventListener("click", () => sendControl("set_all_arms_enabled", { value: true }));
byId("disable-arms").addEventListener("click", () => sendControl("set_all_arms_enabled", { value: false }));
byId("start-streams").addEventListener("click", () => sendControl("set_all_streams_enabled", { value: true }));
byId("stop-streams").addEventListener("click", () => sendControl("set_all_streams_enabled", { value: false }));
byId("conveyor-speed").addEventListener("input", (event) => {
  const value = Number(event.target.value);
  byId("conveyor-speed-value").textContent = `${value.toFixed(2)} u/s`;
  clearTimeout(speedTimer);
  speedTimer = setTimeout(() => sendControl("set_conveyor_speed", { value }), 180);
});

buildArmPanels();
refresh();
setInterval(refresh, 750);
