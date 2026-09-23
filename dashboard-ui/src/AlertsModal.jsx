function minutesToLabel(min) {
  const h = Math.floor(min / 60);
  const m = min % 60;
  if (h === 0) return `${m}min`;
  if (m === 0) return `${h}h`;
  return `${h}h${String(m).padStart(2, "0")}`;
}

function describeRequest(req) {
  if (req.type === "time") {
    return `demande ${minutesToLabel(req.payload.minutes || 0)} de temps en plus`;
  }
  return `demande l'accès à "${req.payload.domain || "?"}"`;
}

export default function AlertsModal({ requests, onClose, onApprove, onDeny, busyId }) {
  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <h3>Alertes &amp; demandes</h3>
          <button className="modal-close" onClick={onClose}>
            ✕
          </button>
        </div>

        <div className="modal-body">
          {requests.length === 0 ? (
            <p className="empty">Aucune demande en attente.</p>
          ) : (
            <ul className="request-list">
              {requests.map((req) => (
                <li key={req.id} className="request-item">
                  <div className="request-info">
                    <strong>{req.childName}</strong>
                    <span className="muted"> · {req.deviceName}</span>
                    <p>{describeRequest(req)}</p>
                  </div>
                  <div className="request-actions">
                    <button
                      className="pill-btn danger"
                      disabled={busyId === req.id}
                      onClick={() => onDeny(req.id)}
                    >
                      Refuser
                    </button>
                    <button
                      className="pill-btn primary"
                      disabled={busyId === req.id}
                      onClick={() => onApprove(req.id)}
                    >
                      Approuver
                    </button>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </div>

        <div className="modal-footer">
          <button className="link-btn" onClick={onClose}>
            Fermer
          </button>
        </div>
      </div>
    </div>
  );
}
