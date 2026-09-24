import { useState } from "react";
import Toggle from "./Toggle";
import { BLOCKLISTS, childFilterOf } from "./blocklists";

const TAB_LABELS = { web: "🌐 Web", time: "⏳ Temps", apps: "📱 Apps" };

function SwitchRow({ label, checked, onChange }) {
  return (
    <div className="switch-row">
      <span>{label}</span>
      <Toggle checked={checked} onChange={onChange} />
    </div>
  );
}

function TagEditor({ items, onAdd, onRemove, placeholder }) {
  const [value, setValue] = useState("");
  const submit = () => {
    const v = value.trim();
    if (v) {
      onAdd(v);
      setValue("");
    }
  };
  return (
    <div className="tag-editor">
      <div className="tag-row">
        {items.length === 0 && <span className="empty">Aucun</span>}
        {items.map((it) => (
          <span className="tag removable" key={it}>
            {it}
            <button type="button" onClick={() => onRemove(it)}>
              ✕
            </button>
          </span>
        ))}
      </div>
      <div className="tag-input-row">
        <input
          value={value}
          onChange={(e) => setValue(e.target.value)}
          onKeyDown={(e) => e.key === "Enter" && (e.preventDefault(), submit())}
          placeholder={placeholder}
        />
        <button type="button" className="mini-add-btn" onClick={submit}>
          Ajouter
        </button>
      </div>
    </div>
  );
}

// Filtre enfant (-18) : un interrupteur et des cases a cocher parmi les listes publiques.
function ChildFilterSection({ value, onChange }) {
  const filter = childFilterOf(value);
  const setFilter = (next) => onChange({ ...value, childFilter: { ...filter, ...next } });
  const toggleList = (id) =>
    setFilter({ lists: filter.lists.includes(id) ? filter.lists.filter((l) => l !== id) : [...filter.lists, id] });

  return (
    <div className="child-filter">
      <SwitchRow
        label="Filtre enfant (contenu adulte)"
        checked={filter.enabled}
        onChange={(enabled) => setFilter({ enabled })}
      />
      <fieldset className="form-fieldset" disabled={!filter.enabled || value.whitelistMode}>
        {value.whitelistMode && (
          <p className="muted app-picker-hint">Sans effet en mode liste blanche (déjà restrictif).</p>
        )}
        {BLOCKLISTS.map((list) => (
          <label className="child-filter-option" key={list.id}>
            <input type="checkbox" checked={filter.lists.includes(list.id)} onChange={() => toggleList(list.id)} />
            <span>
              <strong>{list.name}</strong>
              {list.recommended && <span className="chip">recommandée</span>}
              <span className="muted child-filter-desc">
                {list.description} Licence : {list.license}.
              </span>
            </span>
          </label>
        ))}
        <p className="muted app-picker-hint">
          Chaque PC télécharge ces listes lui-même depuis leur source et les met à jour chaque semaine.
        </p>
      </fieldset>
    </div>
  );
}

function WebForm({ value, onChange }) {
  const disabled = !value.supervisionOn;
  return (
    <div className="form-stack">
      <SwitchRow
        label="Supervision web activée"
        checked={value.supervisionOn}
        onChange={(v) => onChange({ ...value, supervisionOn: v })}
      />

      <fieldset className="form-fieldset" disabled={disabled}>
        <label className="field-label">Mode de filtrage</label>
        <div className="radio-group">
          <label>
            <input
              type="radio"
              checked={!value.whitelistMode}
              onChange={() => onChange({ ...value, whitelistMode: false })}
            />
            Liste noire — tout est autorisé sauf les sites listés
          </label>
          <label>
            <input
              type="radio"
              checked={value.whitelistMode}
              onChange={() => onChange({ ...value, whitelistMode: true })}
            />
            Liste blanche — tout est bloqué sauf les sites listés (restrictif)
          </label>
        </div>

        <ChildFilterSection value={value} onChange={onChange} />

        <label className="field-label">Liste noire (sites interdits)</label>
        <TagEditor
          items={value.blacklist}
          placeholder="ex: tiktok.com"
          onAdd={(site) => onChange({ ...value, blacklist: [...value.blacklist, site] })}
          onRemove={(site) => onChange({ ...value, blacklist: value.blacklist.filter((s) => s !== site) })}
        />

        <label className="field-label">
          {value.whitelistMode ? "Liste blanche (sites autorisés)" : "Exceptions (sites toujours autorisés)"}
        </label>
        {!value.whitelistMode && (
          <p className="muted app-picker-hint">
            En mode liste noire, ces sites restent accessibles même s'ils figurent dans une liste du filtre enfant.
          </p>
        )}
        <TagEditor
          items={value.whitelist}
          placeholder="ex: wikipedia.org"
          onAdd={(site) => onChange({ ...value, whitelist: [...value.whitelist, site] })}
          onRemove={(site) => onChange({ ...value, whitelist: value.whitelist.filter((s) => s !== site) })}
        />
      </fieldset>
    </div>
  );
}

function TimeForm({ value, onChange }) {
  const quotaEnabled = value.quotaEnabled !== false;
  const windowEnabled = value.windowEnabled !== false;
  return (
    <div className="form-stack">
      <SwitchRow
        label="Quota quotidien actif"
        checked={quotaEnabled}
        onChange={(v) => onChange({ ...value, quotaEnabled: v })}
      />
      <fieldset className="form-fieldset" disabled={!quotaEnabled}>
        <label className="field-label">Quota quotidien autorisé (minutes)</label>
        <input
          type="number"
          min="0"
          className="text-input"
          value={value.dailyQuotaMinutes}
          onChange={(e) => onChange({ ...value, dailyQuotaMinutes: Number(e.target.value) })}
        />
      </fieldset>

      <SwitchRow
        label="Plage horaire active"
        checked={windowEnabled}
        onChange={(v) => onChange({ ...value, windowEnabled: v })}
      />
      <fieldset className="form-fieldset" disabled={!windowEnabled}>
        <label className="field-label">Plage horaire autorisée</label>
        <div className="time-range-row">
          <input
            type="time"
            className="text-input"
            value={value.windowStart}
            onChange={(e) => onChange({ ...value, windowStart: e.target.value })}
          />
          <span>→</span>
          <input
            type="time"
            className="text-input"
            value={value.windowEnd}
            onChange={(e) => onChange({ ...value, windowEnd: e.target.value })}
          />
        </div>
      </fieldset>
    </div>
  );
}

let limitIdCounter = 0;
function newLimitId() {
  limitIdCounter += 1;
  return `new-${Date.now()}-${limitIdCounter}`;
}

// Selecteur d'applications : le parent choisit dans la liste des applications installees remontee par
// l'appareil (recherche par nom), au lieu de devoir connaitre le nom exact. La saisie manuelle reste
// possible pour une application absente de la liste.
function AppPicker({ items, options, onAdd, onRemove, placeholder }) {
  const [query, setQuery] = useState("");
  const [open, setOpen] = useState(false);
  const q = query.trim().toLowerCase();
  const matches = options.filter((o) => !items.includes(o) && (!q || o.toLowerCase().includes(q))).slice(0, 40);
  const canAddManual = q && !options.some((o) => o.toLowerCase() === q) && !items.some((i) => i.toLowerCase() === q);

  const pick = (name) => {
    onAdd(name);
    setQuery("");
    setOpen(false);
  };

  return (
    <div className="tag-editor app-picker">
      <div className="tag-row">
        {items.length === 0 && <span className="empty">Aucune</span>}
        {items.map((it) => (
          <span className="tag removable" key={it}>
            {it}
            <button type="button" onClick={() => onRemove(it)}>
              ✕
            </button>
          </span>
        ))}
      </div>
      <div className="app-picker-box">
        <input
          className="app-picker-input"
          value={query}
          placeholder={placeholder}
          onChange={(e) => {
            setQuery(e.target.value);
            setOpen(true);
          }}
          onFocus={() => setOpen(true)}
          onBlur={() => setTimeout(() => setOpen(false), 150)}
          onKeyDown={(e) => {
            if (e.key !== "Enter") return;
            e.preventDefault();
            if (matches.length === 1) pick(matches[0]);
            else if (canAddManual) pick(query.trim());
          }}
        />
        {open && (matches.length > 0 || canAddManual) && (
          <div className="app-picker-list">
            {matches.map((m) => (
              <button
                type="button"
                className="app-picker-option"
                key={m}
                onMouseDown={(e) => {
                  e.preventDefault();
                  pick(m);
                }}
              >
                {m}
              </button>
            ))}
            {canAddManual && (
              <button
                type="button"
                className="app-picker-option app-picker-manual"
                onMouseDown={(e) => {
                  e.preventDefault();
                  pick(query.trim());
                }}
              >
                Ajouter « {query.trim()} » manuellement
              </button>
            )}
          </div>
        )}
      </div>
      {options.length === 0 && (
        <p className="muted app-picker-hint">
          Aucune liste d'applications reçue de l'appareil pour l'instant : saisie manuelle.
        </p>
      )}
    </div>
  );
}

function AppLimitRow({ limit, options, onChange, onRemove }) {
  return (
    <div className="app-limit-row">
      <div className="app-limit-row-header">
        <input
          type="number"
          min="1"
          className="text-input app-limit-minutes"
          value={limit.minutesPerDay}
          onChange={(e) => onChange({ ...limit, minutesPerDay: Number(e.target.value) })}
        />
        <span className="muted">min / jour</span>
        <button type="button" className="mini-btn app-limit-remove" onClick={onRemove}>
          supprimer
        </button>
      </div>
      <AppPicker
        items={limit.apps}
        options={options}
        placeholder="Rechercher une application…"
        onAdd={(app) => onChange({ ...limit, apps: [...limit.apps, app] })}
        onRemove={(app) => onChange({ ...limit, apps: limit.apps.filter((a) => a !== app) })}
      />
    </div>
  );
}

function AppsForm({ value, onChange, installedApps }) {
  const disabled = !value.supervisionOn;

  const updateLimit = (id, newLimit) =>
    onChange({ ...value, limits: value.limits.map((l) => (l.id === id ? newLimit : l)) });
  const removeLimit = (id) => onChange({ ...value, limits: value.limits.filter((l) => l.id !== id) });
  const addLimit = () =>
    onChange({ ...value, limits: [...value.limits, { id: newLimitId(), apps: [], minutesPerDay: 30 }] });

  return (
    <div className="form-stack">
      <SwitchRow
        label="Supervision apps activée"
        checked={value.supervisionOn}
        onChange={(v) => onChange({ ...value, supervisionOn: v })}
      />

      <fieldset className="form-fieldset" disabled={disabled}>
        <label className="field-label">Limites de temps par app (une ou plusieurs apps par limite)</label>
        {value.limits.length === 0 && <p className="empty">Aucune limite définie</p>}
        <div className="app-limit-list-edit">
          {value.limits.map((limit) => (
            <AppLimitRow
              key={limit.id}
              limit={limit}
              options={installedApps}
              onChange={(l) => updateLimit(limit.id, l)}
              onRemove={() => removeLimit(limit.id)}
            />
          ))}
        </div>
        <button type="button" className="mini-add-btn" onClick={addLimit}>
          + Ajouter une limite
        </button>

        <label className="field-label">Apps totalement bloquées</label>
        <AppPicker
          items={value.blockedApps}
          options={installedApps}
          placeholder="Rechercher une application…"
          onAdd={(app) => onChange({ ...value, blockedApps: [...value.blockedApps, app] })}
          onRemove={(app) => onChange({ ...value, blockedApps: value.blockedApps.filter((a) => a !== app) })}
        />
      </fieldset>
    </div>
  );
}

export default function ConfigModal({ modal, setModal, onSave, saving }) {
  if (!modal) return null;
  const { section, value, forAllDevices, installedApps = [] } = modal;

  const setValue = (v) => setModal({ ...modal, value: v });

  return (
    <div className="modal-overlay" onClick={() => setModal(null)}>
      <div className="modal" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <h3>
            Configurer {TAB_LABELS[section]}
            {forAllDevices && <span className="chip modal-title-chip">tous les appareils</span>}
          </h3>
          <button className="modal-close" onClick={() => setModal(null)}>
            ✕
          </button>
        </div>

        <div className="modal-body">
          {section === "web" && <WebForm value={value} onChange={setValue} />}
          {section === "time" && <TimeForm value={value} onChange={setValue} />}
          {section === "apps" && <AppsForm value={value} onChange={setValue} installedApps={installedApps} />}
        </div>

        <div className="modal-footer">
          <button className="link-btn" onClick={() => setModal(null)}>
            Annuler
          </button>
          <button className="pill-btn primary" onClick={onSave} disabled={saving}>
            {saving ? "Enregistrement…" : "Enregistrer"}
          </button>
        </div>
      </div>
    </div>
  );
}
