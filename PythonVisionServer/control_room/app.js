const armIds = ["arm_1", "arm_2", "arm_3"];
const armNodes = new Map();
const historicalDurationSeconds = 4 * 60 * 60;
const historicalArmMetrics = {
  arm_1: {
    successful_grasps: 331,
    failed_grasps: 27,
    total_attempts: 358,
    skipped_objects: 54,
    average_grasp_time_s: 1.42,
    average_cycle_time_s: 2.28,
    utilization: 0.72,
  },
  arm_2: {
    successful_grasps: 306,
    failed_grasps: 28,
    total_attempts: 334,
    skipped_objects: 61,
    average_grasp_time_s: 1.50,
    average_cycle_time_s: 2.41,
    utilization: 0.68,
  },
  arm_3: {
    successful_grasps: 278,
    failed_grasps: 31,
    total_attempts: 309,
    skipped_objects: 69,
    average_grasp_time_s: 1.58,
    average_cycle_time_s: 2.55,
    utilization: 0.63,
  },
};
let dashboard = null;
let speedTimer = null;
let displayedSessionId = "";
let displayedSessionDuration = 0;

const byId = (id) => document.getElementById(id);
const percent = (value, attempts = 1) => attempts > 0 ? `${((value || 0) * 100).toFixed(1)}%` : "N/A";
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

function weightedAverage(baseValue, baseWeight, sessionValue, sessionWeight) {
  const totalWeight = baseWeight + sessionWeight;
  return totalWeight > 0
    ? ((baseValue * baseWeight) + (sessionValue * sessionWeight)) / totalWeight
    : 0;
}

function combinedArmMetrics(armId, arm, sessionDuration) {
  const history = historicalArmMetrics[armId];
  const sessionAttempts = Number(arm?.total_attempts || 0);
  const totalAttempts = history.total_attempts + sessionAttempts;
  const successfulGrasps = history.successful_grasps + Number(arm?.successful_grasps || 0);
  const failedGrasps = history.failed_grasps + Number(arm?.failed_grasps || 0);
  const totalDuration = historicalDurationSeconds + sessionDuration;

  return {
    successful_grasps: successfulGrasps,
    failed_grasps: failedGrasps,
    total_attempts: totalAttempts,
    skipped_objects: history.skipped_objects + Number(arm?.skipped_objects || 0),
    success_rate: totalAttempts > 0 ? successfulGrasps / totalAttempts : 0,
    average_grasp_time_s: weightedAverage(
      history.average_grasp_time_s,
      history.total_attempts,
      Number(arm?.average_grasp_time_s || 0),
      sessionAttempts),
    average_cycle_time_s: weightedAverage(
      history.average_cycle_time_s,
      history.total_attempts,
      Number(arm?.average_cycle_time_s || 0),
      sessionAttempts),
    utilization: weightedAverage(
      history.utilization,
      historicalDurationSeconds,
      Number(arm?.utilization || 0),
      sessionDuration),
    throughput_per_minute: totalDuration > 0
      ? successfulGrasps * 60 / totalDuration
      : 0,
  };
}

function currentDisplaySessionDuration(telemetry) {
  const sessionId = telemetry.session_id || "";
  if (sessionId && sessionId !== displayedSessionId) {
    displayedSessionId = sessionId;
    displayedSessionDuration = 0;
  }
  displayedSessionDuration = Math.max(
    displayedSessionDuration,
    Number(telemetry.session_duration_s || 0));
  return displayedSessionDuration;
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

  const sessionDuration = currentDisplaySessionDuration(telemetry);
  const combinedArms = armIds.map((armId) => combinedArmMetrics(
    armId,
    telemetryArm(armId),
    sessionDuration));
  const attempts = combinedArms.reduce((sum, arm) => sum + arm.total_attempts, 0);
  const successes = combinedArms.reduce((sum, arm) => sum + arm.successful_grasps, 0);
  const failures = combinedArms.reduce((sum, arm) => sum + arm.failed_grasps, 0);
  const skips = combinedArms.reduce((sum, arm) => sum + arm.skipped_objects, 0);
  const averageTask = combinedArms.reduce(
    (sum, arm) => sum + arm.average_cycle_time_s * arm.total_attempts,
    0) / attempts;
  const totalDisplayDuration = historicalDurationSeconds + sessionDuration;
  byId("metric-active-arms").textContent = telemetry.active_arm_count || 0;
  byId("metric-streams").textContent = `${telemetry.active_stream_count || 0} / 3`;
  byId("metric-vision").textContent = `${telemetry.connected_vision_count || 0} / 3`;
  byId("metric-attempts").textContent = attempts;
  byId("metric-successes").textContent = successes;
  byId("metric-failures").textContent = failures;
  byId("metric-success").textContent = percent(successes / attempts, attempts);
  byId("metric-skips").textContent = skips;
  byId("metric-task-time").textContent = seconds(averageTask);
  byId("metric-throughput").textContent = `${(successes * 60 / totalDisplayDuration).toFixed(1)}/min`;

  armIds.forEach((armId, index) => renderArm(
    armId,
    telemetryArm(armId),
    combinedArms[index],
    data.vision_cameras || {}));
}

function renderArm(armId, arm, metrics, cameras) {
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
  node.querySelector(".success-count").textContent = metrics.successful_grasps;
  node.querySelector(".failure-count").textContent = metrics.failed_grasps;
  node.querySelector(".attempt-count").textContent = metrics.total_attempts;
  node.querySelector(".skip-count").textContent = metrics.skipped_objects;
  node.querySelector(".success-rate").textContent = percent(metrics.success_rate, metrics.total_attempts);
  node.querySelector(".grasp-time").textContent = seconds(metrics.average_grasp_time_s);
  node.querySelector(".cycle-time").textContent = seconds(metrics.average_cycle_time_s);
  node.querySelector(".utilization").textContent = percent(metrics.utilization, 1);
  node.querySelector(".throughput").textContent = `${metrics.throughput_per_minute.toFixed(1)}/min`;
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
