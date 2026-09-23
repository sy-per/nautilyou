import { useState } from "react";

const COLORS = ["#7c9cff", "#5ec8a8", "#f2a65a", "#e0708a", "#8a7cff", "#4fb0c6"];

export default function ChildModal({ mode, initial, onClose, onSubmit, onDelete, saving }) {
  const [name, setName] = useState(initial?.name || "");
  const [avatarColor, setAvatarColor] = useState(initial?.avatarColor || COLORS[0]);
  const [confirmDelete, setConfirmDelete] = useState(false);

  const isAdd = mode === "add";

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <h3>{isAdd ? "Ajouter un enfant" : "Profil de l'enfant"}</h3>
          <button className="modal-close" onClick={onClose}>
            ✕
          </button>
        </div>

        <div className="modal-body">
          <div className="form-stack">
            <label className="field-label">Prénom</label>
            <input
              type="text"
              className="text-input"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="ex: Eli"
              autoFocus
            />

            <label className="field-label">Couleur avatar</label>
            <div className="color-picker">
              {COLORS.map((c) => (
                <button
                  key={c}
                  type="button"
                  className={`color-swatch ${avatarColor === c ? "selected" : ""}`}
                  style={{ background: c }}
                  onClick={() => setAvatarColor(c)}
                />
              ))}
            </div>
          </div>

          {!isAdd && (
            <div className="danger-zone">
              {!confirmDelete ? (
                <button type="button" className="mini-btn danger-link" onClick={() => setConfirmDelete(true)}>
                  Supprimer cet enfant
                </button>
              ) : (
                <div className="danger-confirm">
                  <p className="muted">
                    Supprimer {initial?.name} et tous ses appareils/données associées ? Cette action est
                    irréversible.
                  </p>
                  <div className="danger-confirm-actions">
                    <button type="button" className="link-btn" onClick={() => setConfirmDelete(false)}>
                      Annuler
                    </button>
                    <button type="button" className="pill-btn danger" onClick={onDelete} disabled={saving}>
                      Confirmer la suppression
                    </button>
                  </div>
                </div>
              )}
            </div>
          )}
        </div>

        <div className="modal-footer">
          <button className="link-btn" onClick={onClose}>
            Annuler
          </button>
          <button
            className="pill-btn primary"
            disabled={!name.trim() || saving}
            onClick={() => onSubmit({ name: name.trim(), avatarColor })}
          >
            {saving ? "Enregistrement…" : isAdd ? "Créer" : "Enregistrer"}
          </button>
        </div>
      </div>
    </div>
  );
}
