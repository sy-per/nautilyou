import { useEffect, useState } from "react";
import {
  fetchChildren,
  fetchDevice,
  fetchDeviceApps,
  fetchChildApps,
  postBonus,
  postLock,
  updateDeviceOverrides,
  updateChildConfig,
  createChild,
  updateChild,
  deleteChild,
  deleteDevice,
  fetchRequests,
  approveRequest,
  denyRequest,
} from "./api";
import ConfigModal from "./ConfigModal";
import ChildModal from "./ChildModal";
import AddDeviceModal from "./AddDeviceModal";
import AlertsModal from "./AlertsModal";
import Toggle from "./Toggle";
import "./App.css";

const ALL_DEVICES = "__all__";

function initials(name) {
  return name.slice(0, 2).toUpperCase();
}

function minutesToLabel(min) {
  const h = Math.floor(min / 60);
  const m = min % 60;
  if (h === 0) return `${m}min`;
  if (m === 0) return `${h}h`;
  return `${h}h${String(m).padStart(2, "0")}`;
}

// L'entrée "Tous les appareils" du sélecteur se comporte comme un appareil virtuel dont la
// config est directement la config par défaut de l'enfant (pas d'overrides, pas d'activité réelle).
function syntheticAllDevicesDetail(child) {
  return {
    id: ALL_DEVICES,
    config: child.defaultConfig,
    overrides: {},
    hasOverride: { web: false, time: false, apps: false },
    bonusMinutesToday: 0,
    activity: {
      usedMinutesToday: 0,
      topSites: [],
      topApps: [],
      blockedAppsCount: child.defaultConfig.apps.blockedApps.length,
    },
  };
}

function TopBar({ children, selectedChildId, onSelectChild, alertsCount, onOpenAlerts, onAddChild }) {
  return (
    <div className="topbar">
      <button className="avatar-btn" title="Alertes" onClick={onOpenAlerts}>
        <div className="avatar avatar-alert">
          🔔
          {alertsCount > 0 && <span className="badge">{alertsCount}</span>}
        </div>
        <span>Alertes</span>
      </button>

      {children.map((c) => (
        <button
          key={c.id}
          className={`avatar-btn ${selectedChildId === c.id ? "active" : ""}`}
          onClick={() => onSelectChild(c.id)}
        >
          <div className="avatar" style={{ background: c.avatarColor }}>
            {initials(c.name)}
            {c.status !== "none" && (
              <span
                className={`status-dot ${c.status === "green" ? "status-dot-green" : "status-dot-red"}`}
                title={c.status === "green" ? "Temps restant, dans la plage horaire" : "Pas de temps restant ou hors plage horaire"}
              />
            )}
          </div>
          <span>{c.name}</span>
        </button>
      ))}

      <button className="avatar-btn" onClick={onAddChild}>
        <div className="avatar avatar-add">+</div>
        <span>Ajouter enfant</span>
      </button>
    </div>
  );
}

function BonusTimeButton({ onAddBonus, disabled }) {
  const [open, setOpen] = useState(false);
  const [custom, setCustom] = useState("");
  const options = [15, 30, 60];

  const submitCustom = () => {
    const min = Number(custom);
    if (min > 0) {
      onAddBonus(min);
      setCustom("");
      setOpen(false);
    }
  };

  return (
    <div className="bonus-wrap">
      <button className="pill-btn" disabled={disabled} onClick={() => setOpen((o) => !o)}>
        🎁 Bonus temps
      </button>
      {open && (
        <div className="bonus-popover">
          <div className="bonus-presets">
            {options.map((min) => (
              <button
                key={min}
                className="bonus-option"
                onClick={() => {
                  onAddBonus(min);
                  setOpen(false);
                }}
              >
                + {minutesToLabel(min)}
              </button>
            ))}
          </div>
          <div className="bonus-custom-row">
            <input
              type="number"
              min="1"
              className="text-input bonus-custom-input"
              placeholder="min"
              value={custom}
              onChange={(e) => setCustom(e.target.value)}
              onKeyDown={(e) => e.key === "Enter" && submitCustom()}
            />
            <button className="mini-add-btn" onClick={submitCustom}>
              Ajouter
            </button>
          </div>
        </div>
      )}
    </div>
  );
}

function QuickActions({ onAddBonus, onLock, lockStatus, onOpenProfile, onAddDevice, instantActionsDisabled }) {
  return (
    <div className="quick-actions">
      <div className="quick-actions-left">
        <button className="pill-btn" disabled={instantActionsDisabled} onClick={onLock}>
          🔒 Verrouillage instantané
        </button>
        {lockStatus && <span className="status-msg">{lockStatus}</span>}
        <BonusTimeButton onAddBonus={onAddBonus} disabled={instantActionsDisabled} />
      </div>
      <div className="quick-actions-right">
        <button className="link-btn" onClick={onAddDevice}>
          🖥️ Ajouter un appareil
        </button>
        <button className="link-btn" onClick={onOpenProfile}>
          👤 Voir le profil
        </button>
      </div>
    </div>
  );
}

function DeviceSelector({ devices, selectedDeviceId, onSelect }) {
  return (
    <select className="device-select" value={selectedDeviceId} onChange={(e) => onSelect(e.target.value)}>
      <option value={ALL_DEVICES}>⚙ Tous les appareils (réglages par défaut)</option>
      {devices.map((d) => (
        <option key={d.id} value={d.id}>
          {d.name} {d.online ? "● en ligne" : "○ hors ligne"}
        </option>
      ))}
    </select>
  );
}

function DeleteDeviceButton({ deviceName, onConfirm }) {
  const [open, setOpen] = useState(false);
  return (
    <div className="bonus-wrap">
      <button className="gear-btn danger-icon-btn" title="Supprimer cet appareil" onClick={() => setOpen((o) => !o)}>
        🗑️
      </button>
      {open && (
        <div className="bonus-popover danger-confirm">
          <p className="muted">
            Supprimer <strong>{deviceName}</strong> ? Il faudra le réappairer pour le superviser à nouveau.
          </p>
          <div className="danger-confirm-actions">
            <button type="button" className="link-btn" onClick={() => setOpen(false)}>
              Annuler
            </button>
            <button
              type="button"
              className="pill-btn danger"
              onClick={() => {
                setOpen(false);
                onConfirm();
              }}
            >
              Confirmer la suppression
            </button>
          </div>
        </div>
      )}
    </div>
  );
}

function SectionCard({ icon, title, on, onToggle, overridden, onConfigure, children }) {
  return (
    <div className="card">
      <div className="card-header">
        <div className="card-title">
          <span className="card-icon">{icon}</span>
          <span>{title}</span>
          {overridden && <span className="chip">override appareil</span>}
        </div>
        <div className="card-header-actions">
          <button className="gear-btn" title="Configurer" onClick={onConfigure}>
            ⚙
          </button>
          <Toggle checked={on} onChange={onToggle} />
        </div>
      </div>
      <div className="card-body">{children}</div>
    </div>
  );
}

function WebCard({ config, activity, overridden, onToggle, onConfigure }) {
  return (
    <SectionCard
      icon="🌐"
      title="WEB"
      on={config.web.supervisionOn}
      onToggle={onToggle}
      overridden={overridden}
      onConfigure={onConfigure}
    >
      <p className="muted">
        Mode : <strong>{config.web.whitelistMode ? "Liste blanche (restrictif)" : "Liste noire"}</strong>
      </p>
      <p className="muted">Sites les plus consultés</p>
      <div className="tag-row">
        {activity.topSites.length === 0 && <span className="empty">Aucune donnée</span>}
        {activity.topSites.map(([site, count]) => (
          <span className="tag" key={site} title="Durée approximative de présence aujourd'hui">
            {site} ~{count} min
          </span>
        ))}
      </div>
    </SectionCard>
  );
}

function TimeCard({ config, activity, overridden, onToggle, onConfigure, bonusMinutes }) {
  const quotaEnabled = config.time.quotaEnabled !== false;
  const windowEnabled = config.time.windowEnabled !== false;
  const used = activity.usedMinutesToday;
  const quota = config.time.dailyQuotaMinutes + bonusMinutes;
  const pct = quotaEnabled ? Math.min(100, Math.round((used / quota) * 100)) : 0;
  return (
    <SectionCard
      icon="⏳"
      title="TEMPS"
      on={quotaEnabled || windowEnabled}
      onToggle={onToggle}
      overridden={overridden}
      onConfigure={onConfigure}
    >
      <p className="muted">
        Quota autorisé :{" "}
        <strong>{quotaEnabled ? `${minutesToLabel(quota - bonusMinutes)}/jour` : "illimité"}</strong>
        {quotaEnabled && bonusMinutes > 0 && (
          <span className="bonus-tag"> (+{minutesToLabel(bonusMinutes)} bonus aujourd'hui)</span>
        )}
      </p>
      <p className="muted">
        Plage horaire :{" "}
        <strong>{windowEnabled ? `${config.time.windowStart} → ${config.time.windowEnd}` : "toute la journée"}</strong>
      </p>
      {quotaEnabled ? (
        <div className="gauge-wrap">
          <svg viewBox="0 0 120 120" className="gauge">
            <circle cx="60" cy="60" r="52" className="gauge-track" />
            <circle
              cx="60"
              cy="60"
              r="52"
              className="gauge-fill"
              strokeDasharray={`${(pct / 100) * 326.7} 326.7`}
            />
          </svg>
          <div className="gauge-text">
            <strong>{minutesToLabel(used)}</strong>
            <span>/ {minutesToLabel(quota)} utilisé</span>
          </div>
        </div>
      ) : (
        <p className="muted">Temps utilisé aujourd'hui : {minutesToLabel(used)} (pas de quota)</p>
      )}
    </SectionCard>
  );
}

function formatAppsLabel(apps) {
  if (apps.length === 0) return "Aucune app";
  if (apps.length === 1) return apps[0];
  if (apps.length === 2) return `${apps[0]} et ${apps[1]}`;
  return `${apps[0]}, ${apps[1]} et ${apps.length - 2} autres`;
}

function AppCard({ config, activity, overridden, onToggle, onConfigure }) {
  return (
    <SectionCard
      icon="📱"
      title="APPS"
      on={config.apps.supervisionOn}
      onToggle={onToggle}
      overridden={overridden}
      onConfigure={onConfigure}
    >
      <p className="muted">Limites par app</p>
      {config.apps.limits.length === 0 ? (
        <p className="empty">Aucune limite définie</p>
      ) : (
        <ul className="app-limit-list">
          {config.apps.limits.map((limit) => (
            <li key={limit.id}>
              <span>{formatAppsLabel(limit.apps)}</span>
              <span className="muted">{minutesToLabel(limit.minutesPerDay)}, tous les jours</span>
            </li>
          ))}
        </ul>
      )}
      <p className="muted">
        Apps bloquées : <strong>{config.apps.blockedApps.length}</strong>
        {config.apps.blockedApps.length > 0 && ` (${config.apps.blockedApps.join(", ")})`}
      </p>
      <p className="muted">Apps les plus actives</p>
      <ul className="app-list">
        {activity.topApps.length === 0 && <li className="empty">Aucune donnée</li>}
        {activity.topApps.map((a) => (
          <li key={a.name}>
            <span>{a.name}</span>
            <span className="muted">{a.minutes}min</span>
          </li>
        ))}
      </ul>
    </SectionCard>
  );
}

function SiteListsPanel({ config, overridden }) {
  const { whitelistMode, blacklist, whitelist } = config.web;
  return (
    <div className="card">
      <div className="card-header">
        <div className="card-title">
          <span className="card-icon">🚫</span>
          <span>Listes de sites</span>
          {overridden && <span className="chip">override appareil</span>}
        </div>
      </div>
      <div className="card-body site-lists">
        <div className="site-col">
          <p className="muted">
            Liste noire (interdits) {whitelistMode && <em>— inactive en mode liste blanche</em>}
          </p>
          {blacklist.length === 0 ? (
            <p className="empty">Aucun site interdit</p>
          ) : (
            <ul className="site-list">
              {blacklist.map((s) => (
                <li key={s}>
                  <span>{s}</span>
                </li>
              ))}
            </ul>
          )}
        </div>
        <div className="site-col">
          <p className="muted">
            Liste blanche (autorisés) {whitelistMode ? "— mode restrictif actif" : "— inactive"}
          </p>
          {whitelist.length === 0 ? (
            <p className="empty">Aucun site en liste blanche</p>
          ) : (
            <ul className="site-list">
              {whitelist.map((s) => (
                <li key={s}>
                  <span>{s}</span>
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>
      <p className="muted modal-hint">Édition via le bouton ⚙ de la carte WEB ci-dessus.</p>
    </div>
  );
}

export default function App() {
  const [children, setChildren] = useState(null);
  const [error, setError] = useState(null);
  const [selectedChildId, setSelectedChildId] = useState(null);
  const [deviceByChild, setDeviceByChild] = useState({});
  const [deviceDetail, setDeviceDetail] = useState(null);
  const [lockStatus, setLockStatus] = useState("");
  const [modal, setModal] = useState(null);
  const [saving, setSaving] = useState(false);
  const [childModal, setChildModal] = useState(null); // { mode: 'add' | 'profile' }
  const [addDeviceOpen, setAddDeviceOpen] = useState(false);
  const [pendingRequests, setPendingRequests] = useState([]);
  const [alertsOpen, setAlertsOpen] = useState(false);
  const [requestBusyId, setRequestBusyId] = useState(null);

  const refreshChildren = () => fetchChildren().then(setChildren).catch((e) => setError(e.message));
  const refreshRequests = () => fetchRequests("pending").then(setPendingRequests).catch(() => {});

  useEffect(() => {
    fetchChildren()
      .then((list) => {
        setChildren(list);
        if (list.length > 0) {
          setSelectedChildId(list[0].id);
          setDeviceByChild(Object.fromEntries(list.map((c) => [c.id, c.devices[0]?.id || ALL_DEVICES])));
        }
      })
      .catch((e) => setError(e.message));
    refreshRequests();
  }, []);

  // Sondage léger des demandes en attente (même logique que le sondage d'appairage) : pas de
  // canal temps réel dashboard<->serveur pour l'instant, juste un rafraîchissement périodique.
  useEffect(() => {
    const interval = setInterval(refreshRequests, 5000);
    return () => clearInterval(interval);
  }, []);

  const child = children ? children.find((c) => c.id === selectedChildId) || null : null;
  const rawSelectedDeviceId = selectedChildId ? deviceByChild[selectedChildId] : null;
  const selectedDeviceId = child
    ? rawSelectedDeviceId || (child.devices.length > 0 ? child.devices[0].id : ALL_DEVICES)
    : null;
  const isAllDevices = selectedDeviceId === ALL_DEVICES;

  useEffect(() => {
    if (!selectedDeviceId || isAllDevices) {
      setDeviceDetail(null);
      return;
    }
    setDeviceDetail(null);
    fetchDevice(selectedDeviceId)
      .then(setDeviceDetail)
      .catch((e) => setError(e.message));
  }, [selectedDeviceId, isAllDevices]);

  if (error) {
    return (
      <div className="app">
        <p className="empty">
          Impossible de joindre le serveur Nautilyou ({error}). Vérifie que l'API tourne sur{" "}
          {import.meta.env.VITE_API_URL || "http://localhost:4100/api"}.
        </p>
      </div>
    );
  }

  if (!children) {
    return (
      <div className="app">
        <p className="muted">Chargement…</p>
      </div>
    );
  }

  const displayDetail = child ? (isAllDevices ? syntheticAllDevicesDetail(child) : deviceDetail) : null;

  function openDeviceModule(section) {
    setModal({ section, value: displayDetail.config[section], forAllDevices: isAllDevices, installedApps: [] });
    if (section === "apps") {
      // Liste des applications installees remontee par l'appareil (ou par tous les appareils de l'enfant).
      const load = isAllDevices ? fetchChildApps(child.id) : fetchDeviceApps(selectedDeviceId);
      load
        .then((apps) => setModal((m) => (m && m.section === "apps" ? { ...m, installedApps: apps } : m)))
        .catch(() => {});
    }
  }

  function quickToggleSection(section, sectionValue) {
    if (isAllDevices) {
      updateChildConfig(selectedChildId, { ...child.defaultConfig, [section]: sectionValue })
        .then(() => refreshChildren())
        .catch((e) => setError(e.message));
      return;
    }
    const newOverrides = { ...deviceDetail.overrides, [section]: sectionValue };
    updateDeviceOverrides(selectedDeviceId, newOverrides)
      .then((d) => {
        setDeviceDetail(d);
        refreshChildren();
      })
      .catch((e) => setError(e.message));
  }

  function saveModal() {
    setSaving(true);
    if (modal.forAllDevices) {
      updateChildConfig(selectedChildId, { ...child.defaultConfig, [modal.section]: modal.value })
        .then(() => {
          setModal(null);
          refreshChildren();
        })
        .catch((e) => setError(e.message))
        .finally(() => setSaving(false));
    } else {
      const newOverrides = { ...deviceDetail.overrides, [modal.section]: modal.value };
      updateDeviceOverrides(selectedDeviceId, newOverrides)
        .then((d) => {
          setDeviceDetail(d);
          setModal(null);
          refreshChildren();
        })
        .catch((e) => setError(e.message))
        .finally(() => setSaving(false));
    }
  }

  function handleChildSubmit({ name, avatarColor }) {
    setSaving(true);
    const action =
      childModal.mode === "add"
        ? createChild(name, avatarColor)
        : updateChild(selectedChildId, { name, avatarColor });
    action
      .then((c) => {
        setChildModal(null);
        if (childModal.mode === "add") {
          setSelectedChildId(c.id);
          setDeviceByChild((s) => ({ ...s, [c.id]: ALL_DEVICES }));
        }
        refreshChildren();
      })
      .catch((e) => setError(e.message))
      .finally(() => setSaving(false));
  }

  function handleChildDelete() {
    setSaving(true);
    deleteChild(selectedChildId)
      .then(() =>
        fetchChildren().then((list) => {
          setChildModal(null);
          setChildren(list);
          setSelectedChildId(list[0]?.id || null);
        })
      )
      .catch((e) => setError(e.message))
      .finally(() => setSaving(false));
  }

  // Sondage léger pendant l'appairage : compare le nombre d'appareils de l'enfant avant/après.
  // Provisoire — sera remplacé par une notification P2P en temps réel une fois le client développé.
  const devicesCountBeforePairing = child ? child.devices.length : 0;
  function checkForNewDevice() {
    return fetchChildren().then((list) => {
      setChildren(list);
      const updated = list.find((c) => c.id === selectedChildId);
      return Boolean(updated && updated.devices.length > devicesCountBeforePairing);
    });
  }

  function handleDeviceAdded() {
    setAddDeviceOpen(false);
    refreshChildren();
  }

  function handleDeviceDelete() {
    deleteDevice(selectedDeviceId)
      .then(() => {
        setDeviceByChild((s) => ({ ...s, [selectedChildId]: ALL_DEVICES }));
        refreshChildren();
      })
      .catch((e) => setError(e.message));
  }

  function handleApproveRequest(id) {
    setRequestBusyId(id);
    approveRequest(id)
      .then(() => {
        refreshRequests();
        refreshChildren();
        if (selectedDeviceId && !isAllDevices) {
          fetchDevice(selectedDeviceId).then(setDeviceDetail);
        }
      })
      .catch((e) => setError(e.message))
      .finally(() => setRequestBusyId(null));
  }

  function handleDenyRequest(id) {
    setRequestBusyId(id);
    denyRequest(id)
      .then(() => refreshRequests())
      .catch((e) => setError(e.message))
      .finally(() => setRequestBusyId(null));
  }

  return (
    <div className="app">
      <TopBar
        children={children}
        selectedChildId={selectedChildId}
        onSelectChild={(id) => {
          setSelectedChildId(id);
          setLockStatus("");
        }}
        alertsCount={pendingRequests.length}
        onOpenAlerts={() => setAlertsOpen(true)}
        onAddChild={() => setChildModal({ mode: "add" })}
      />

      <div className="panel">
        {children.length === 0 ? (
          <p className="empty">Aucun enfant pour l'instant. Clique sur "Ajouter enfant" pour commencer.</p>
        ) : !child ? (
          <p className="muted">Chargement…</p>
        ) : (
          <>
            <QuickActions
              lockStatus={lockStatus}
              onOpenProfile={() => setChildModal({ mode: "profile" })}
              onAddDevice={() => setAddDeviceOpen(true)}
              instantActionsDisabled={isAllDevices}
              onAddBonus={(min) =>
                postBonus(selectedDeviceId, min).then((d) => {
                  setDeviceDetail(d);
                  refreshChildren();
                })
              }
              onLock={() =>
                postLock(selectedDeviceId).then(() => {
                  setLockStatus("Commande de verrouillage envoyée");
                  setTimeout(() => setLockStatus(""), 3000);
                })
              }
            />

            <div className="device-row">
              <span className="muted">
                {isAllDevices ? "Config par défaut (tous les appareils)" : "Résumé d'activité (7 derniers jours)"}
              </span>
              <div className="device-row-controls">
                <DeviceSelector
                  devices={child.devices}
                  selectedDeviceId={selectedDeviceId}
                  onSelect={(id) => setDeviceByChild((s) => ({ ...s, [selectedChildId]: id }))}
                />
                {!isAllDevices && (
                  <DeleteDeviceButton deviceName={displayDetail?.name || ""} onConfirm={handleDeviceDelete} />
                )}
              </div>
            </div>

            {child.devices.length === 0 && (
              <p className="empty">
                {child.name} n'a aucun appareil appairé pour l'instant — ces réglages serviront de config par
                défaut au prochain appareil ajouté.
              </p>
            )}

            {!displayDetail ? (
              <p className="muted">Chargement de l'appareil…</p>
            ) : (
              <>
                <div className="cards-grid">
                  <WebCard
                    config={displayDetail.config}
                    activity={displayDetail.activity}
                    overridden={displayDetail.hasOverride.web}
                    onToggle={(v) =>
                      quickToggleSection("web", { ...displayDetail.config.web, supervisionOn: v })
                    }
                    onConfigure={() => openDeviceModule("web")}
                  />
                  <TimeCard
                    config={displayDetail.config}
                    activity={displayDetail.activity}
                    overridden={displayDetail.hasOverride.time}
                    onToggle={(v) =>
                      quickToggleSection("time", {
                        ...displayDetail.config.time,
                        quotaEnabled: v,
                        windowEnabled: v,
                      })
                    }
                    onConfigure={() => openDeviceModule("time")}
                    bonusMinutes={displayDetail.bonusMinutesToday}
                  />
                  <AppCard
                    config={displayDetail.config}
                    activity={displayDetail.activity}
                    overridden={displayDetail.hasOverride.apps}
                    onToggle={(v) =>
                      quickToggleSection("apps", { ...displayDetail.config.apps, supervisionOn: v })
                    }
                    onConfigure={() => openDeviceModule("apps")}
                  />
                </div>

                <SiteListsPanel config={displayDetail.config} overridden={displayDetail.hasOverride.web} />
              </>
            )}
          </>
        )}
      </div>

      <ConfigModal modal={modal} setModal={setModal} onSave={saveModal} saving={saving} />

      {childModal && (
        <ChildModal
          mode={childModal.mode}
          initial={childModal.mode === "profile" ? child : null}
          onClose={() => setChildModal(null)}
          onSubmit={handleChildSubmit}
          onDelete={handleChildDelete}
          saving={saving}
        />
      )}

      {addDeviceOpen && child && (
        <AddDeviceModal
          childId={selectedChildId}
          childName={child.name}
          onClose={() => setAddDeviceOpen(false)}
          checkForNewDevice={checkForNewDevice}
          onDeviceAdded={handleDeviceAdded}
        />
      )}

      {alertsOpen && (
        <AlertsModal
          requests={pendingRequests}
          onClose={() => setAlertsOpen(false)}
          onApprove={handleApproveRequest}
          onDeny={handleDenyRequest}
          busyId={requestBusyId}
        />
      )}
    </div>
  );
}
