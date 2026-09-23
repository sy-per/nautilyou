import { useEffect, useRef, useState } from "react";
import { generatePairingCode } from "./api";

function formatCountdown(ms) {
  if (ms <= 0) return "expiré";
  const totalSeconds = Math.floor(ms / 1000);
  const m = Math.floor(totalSeconds / 60);
  const s = totalSeconds % 60;
  return `${m}:${String(s).padStart(2, "0")}`;
}

export default function AddDeviceModal({ childId, childName, onClose, onDeviceAdded, checkForNewDevice }) {
  const [deviceName, setDeviceName] = useState("");
  const [pairing, setPairing] = useState(null); // { code, expiresAt }
  const [error, setError] = useState(null);
  const [now, setNow] = useState(Date.now());
  const pollRef = useRef(null);
  const tickRef = useRef(null);

  useEffect(() => {
    tickRef.current = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(tickRef.current);
  }, []);

  useEffect(() => {
    if (!pairing) return;
    pollRef.current = setInterval(() => {
      checkForNewDevice().then((added) => {
        if (added) {
          clearInterval(pollRef.current);
          onDeviceAdded();
        }
      });
    }, 3000);
    return () => clearInterval(pollRef.current);
  }, [pairing]);

  function handleGenerate() {
    setError(null);
    generatePairingCode(childId, deviceName.trim() || undefined)
      .then(setPairing)
      .catch((e) => setError(e.message));
  }

  const remainingMs = pairing ? new Date(pairing.expiresAt).getTime() - now : 0;
  const expired = pairing && remainingMs <= 0;

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <h3>Ajouter un appareil — {childName}</h3>
          <button className="modal-close" onClick={onClose}>
            ✕
          </button>
        </div>

        <div className="modal-body">
          {!pairing ? (
            <div className="form-stack">
              <label className="field-label">Nom de l'appareil (optionnel)</label>
              <input
                type="text"
                className="text-input"
                placeholder="ex: PC Chambre"
                value={deviceName}
                onChange={(e) => setDeviceName(e.target.value)}
              />
              <p className="muted">
                Un code à 6 chiffres sera généré. Saisis-le dans l'application Nautilyou sur l'appareil de
                l'enfant pour l'appairer.
              </p>
              {error && <p className="empty">{error}</p>}
            </div>
          ) : (
            <div className="pairing-display">
              <p className="muted">Code d'appairage — valable 15 minutes</p>
              <div className="pairing-code">{pairing.code}</div>
              <p className="muted">
                {expired ? (
                  <span className="danger-text">Code expiré — génère-en un nouveau.</span>
                ) : (
                  <>Expire dans {formatCountdown(remainingMs)}</>
                )}
              </p>
              <p className="muted">
                Ouvre l'application Nautilyou sur l'appareil de {childName}, saisis ce code pour terminer
                l'appairage. Cette fenêtre se fermera automatiquement dès que l'appareil sera détecté.
              </p>
              <div className="pairing-spinner" aria-hidden="true" />
              <p className="muted pairing-waiting">En attente de l'appareil…</p>
            </div>
          )}
        </div>

        <div className="modal-footer">
          <button className="link-btn" onClick={onClose}>
            {pairing ? "Fermer" : "Annuler"}
          </button>
          {(!pairing || expired) && (
            <button className="pill-btn primary" onClick={handleGenerate}>
              {pairing ? "Générer un nouveau code" : "Générer le code"}
            </button>
          )}
        </div>
      </div>
    </div>
  );
}
