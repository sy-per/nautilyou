const API_BASE = import.meta.env.VITE_API_URL || "http://localhost:4100/api";

async function request(path, options) {
  const res = await fetch(`${API_BASE}${path}`, {
    headers: { "Content-Type": "application/json" },
    ...options,
  });
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new Error(body.error || `Erreur API ${res.status}`);
  }
  if (res.status === 204) return null;
  return res.json();
}

export function fetchChildren() {
  return request("/children").then((r) => r.children);
}

export function createChild(name, avatarColor) {
  return request("/children", {
    method: "POST",
    body: JSON.stringify({ name, avatarColor }),
  }).then((r) => r.child);
}

export function updateChild(childId, { name, avatarColor }) {
  return request(`/children/${childId}`, {
    method: "PUT",
    body: JSON.stringify({ name, avatarColor }),
  }).then((r) => r.child);
}

export function deleteChild(childId) {
  return request(`/children/${childId}`, { method: "DELETE" });
}

export function generatePairingCode(childId, deviceName) {
  return request(`/children/${childId}/pairing-code`, {
    method: "POST",
    body: JSON.stringify({ deviceName }),
  });
}

export function fetchDeviceApps(deviceId) {
  return request(`/devices/${deviceId}/apps`).then((r) => r.apps);
}

export function fetchChildApps(childId) {
  return request(`/children/${childId}/apps`).then((r) => r.apps);
}

export function fetchDevice(deviceId) {
  return request(`/devices/${deviceId}`).then((r) => r.device);
}

export function postBonus(deviceId, minutes) {
  return request(`/devices/${deviceId}/bonus`, {
    method: "POST",
    body: JSON.stringify({ minutes }),
  }).then((r) => r.device);
}

export function postLock(deviceId) {
  return request(`/devices/${deviceId}/lock`, { method: "POST" });
}

export function deleteDevice(deviceId) {
  return request(`/devices/${deviceId}`, { method: "DELETE" });
}

export function updateDeviceOverrides(deviceId, overrides) {
  return request(`/devices/${deviceId}/overrides`, {
    method: "PUT",
    body: JSON.stringify({ overrides }),
  }).then((r) => r.device);
}

export function updateChildConfig(childId, defaultConfig) {
  return request(`/children/${childId}/config`, {
    method: "PUT",
    body: JSON.stringify({ defaultConfig }),
  }).then((r) => r.child);
}

export function fetchRequests(status) {
  const qs = status ? `?status=${status}` : "";
  return request(`/requests${qs}`).then((r) => r.requests);
}

export function approveRequest(id) {
  return request(`/requests/${id}/approve`, { method: "POST" }).then((r) => r.request);
}

export function denyRequest(id) {
  return request(`/requests/${id}/deny`, { method: "POST" }).then((r) => r.request);
}
