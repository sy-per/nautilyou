import { useEffect, useState } from "react";
import { authStatus, authSetup, authLogin, authLogout } from "./api";

// Porte d'entree du dashboard : au tout premier acces (aucun compte), demande de creer le compte
// parent ; ensuite, page de connexion. Le dashboard n'est affiche qu'une fois connecte.
export default function AuthGate({ render }) {
  const [state, setState] = useState(null); // { setupRequired, authenticated, username }
  const [error, setError] = useState("");

  const refresh = () =>
    authStatus()
      .then(setState)
      .catch(() => setError("Impossible de joindre le serveur."));

  useEffect(() => {
    refresh();
    const onUnauthorized = () => setState((s) => (s ? { ...s, authenticated: false } : s));
    window.addEventListener("nautilyou:unauthorized", onUnauthorized);
    return () => window.removeEventListener("nautilyou:unauthorized", onUnauthorized);
  }, []);

  if (error) return <AuthShell><p className="auth-error">{error}</p></AuthShell>;
  if (!state) return <AuthShell><p className="muted">Chargement…</p></AuthShell>;

  if (state.authenticated) {
    return render({
      username: state.username,
      logout: () => authLogout().then(refresh),
    });
  }

  return state.setupRequired ? (
    <SetupForm onDone={refresh} />
  ) : (
    <LoginForm onDone={refresh} />
  );
}

function AuthShell({ children }) {
  return (
    <div className="auth-page">
      <div className="auth-card">
        <div className="auth-logo">Nautilyou</div>
        {children}
      </div>
    </div>
  );
}

function SetupForm({ onDone }) {
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  const submit = (e) => {
    e.preventDefault();
    if (password !== confirm) {
      setError("Les deux mots de passe ne correspondent pas.");
      return;
    }
    setBusy(true);
    setError("");
    authSetup(username, password)
      .then(onDone)
      .catch((err) => {
        setError(err.message);
        setBusy(false);
      });
  };

  return (
    <AuthShell>
      <h1 className="auth-title">Bienvenue</h1>
      <p className="muted auth-intro">
        Première utilisation : crée le compte parent qui donnera accès au tableau de bord.
      </p>
      <form className="auth-form" onSubmit={submit}>
        <label className="field-label">Identifiant</label>
        <input
          className="text-input"
          value={username}
          onChange={(e) => setUsername(e.target.value)}
          autoComplete="username"
          autoFocus
        />
        <label className="field-label">Mot de passe (8 caractères minimum)</label>
        <input
          className="text-input"
          type="password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          autoComplete="new-password"
        />
        <label className="field-label">Confirmer le mot de passe</label>
        <input
          className="text-input"
          type="password"
          value={confirm}
          onChange={(e) => setConfirm(e.target.value)}
          autoComplete="new-password"
        />
        {error && <p className="auth-error">{error}</p>}
        <button className="pill-btn primary auth-submit" disabled={busy}>
          {busy ? "Création…" : "Créer le compte"}
        </button>
      </form>
    </AuthShell>
  );
}

function LoginForm({ onDone }) {
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  const submit = (e) => {
    e.preventDefault();
    setBusy(true);
    setError("");
    authLogin(username, password)
      .then(onDone)
      .catch((err) => {
        setError(err.message);
        setBusy(false);
      });
  };

  return (
    <AuthShell>
      <h1 className="auth-title">Connexion</h1>
      <form className="auth-form" onSubmit={submit}>
        <label className="field-label">Identifiant</label>
        <input
          className="text-input"
          value={username}
          onChange={(e) => setUsername(e.target.value)}
          autoComplete="username"
          autoFocus
        />
        <label className="field-label">Mot de passe</label>
        <input
          className="text-input"
          type="password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          autoComplete="current-password"
        />
        {error && <p className="auth-error">{error}</p>}
        <button className="pill-btn primary auth-submit" disabled={busy}>
          {busy ? "Connexion…" : "Se connecter"}
        </button>
      </form>
    </AuthShell>
  );
}
