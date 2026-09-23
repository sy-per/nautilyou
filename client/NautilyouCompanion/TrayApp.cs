using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace NautilyouCompanion;

// Interface minimale enfant : icône systray + petite fenêtre sobre affichant le temps restant et
// le temps passé par app, avec un bouton pour demander plus de temps. Processus séparé du
// service (NautilyouService) — voir dev-context : un service Windows tourne en Session 0, isolée
// de la session interactive, et ne peut donc pas afficher d'icône systray. Ce Companion tourne
// dans la session de l'utilisateur et parle au service via un pipe local (PipeClient).
//
// Tant que le service n'est pas appairé, cette fenêtre affiche un formulaire de pairing
// (URL + code) à la place du statut habituel (ajouté le 2026-09-23, suite à une remarque
// utilisateur — le pairing se faisait avant uniquement via la console du service, injouable pour
// un vrai parent).
public static class TrayApp
{
    public static void Run(ClientStatus status, PipeClient pipeClient, SessionLockState sessionLockState)
    {
        ApplicationConfiguration.Initialize();
        sessionLockState.AttachToSystemEvents(pipeClient);

        using var icon = CreateIcon();
        var statusForm = new StatusForm(status, pipeClient);
        statusForm.ShowAndFocus(); // au premier lancement, on veut que le formulaire de pairing soit visible d'emblee

        // Derniers sites bloques (le plus recent en premier), alimentes par le Service via le pipe.
        var recentBlocked = new List<string>();
        string? lastBalloonDomain = null;

        var menu = new ContextMenuStrip();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            menu.Items.Add("Nautilyou", null, (_, _) => statusForm.ShowAndFocus());
            string[] blocked;
            lock (recentBlocked) blocked = recentBlocked.ToArray();
            if (blocked.Length > 0)
            {
                menu.Items.Add(new ToolStripSeparator());
                foreach (var domain in blocked)
                {
                    var d = domain;
                    menu.Items.Add($"Demander l'accès à {d}", null, (_, _) => _ = pipeClient.SendAccessRequestAsync(d));
                }
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Quitter", null, (_, _) => Application.Exit());
        };

        using var notifyIcon = new NotifyIcon
        {
            Icon = icon,
            Text = "Nautilyou",
            Visible = true,
            ContextMenuStrip = menu,
        };
        notifyIcon.DoubleClick += (_, _) => statusForm.ShowAndFocus();

        // Cliquer sur la bulle "site bloque" envoie directement la demande d'acces au parent.
        notifyIcon.BalloonTipClicked += (_, _) =>
        {
            var domain = lastBalloonDomain;
            if (domain is null) return;
            lastBalloonDomain = null;
            _ = pipeClient.SendAccessRequestAsync(domain);
        };

        pipeClient.BlockedSiteReceived += domain =>
        {
            lock (recentBlocked)
            {
                recentBlocked.Remove(domain);
                recentBlocked.Insert(0, domain);
                if (recentBlocked.Count > 5) recentBlocked.RemoveAt(recentBlocked.Count - 1);
            }
            try
            {
                lastBalloonDomain = domain;
                notifyIcon.BalloonTipTitle = "Site bloqué";
                notifyIcon.BalloonTipText = $"{domain} est bloqué. Clique ici pour demander l'accès à un parent.";
                notifyIcon.ShowBalloonTip(6000);
            }
            catch
            {
                // pas grave si la notif echoue, le menu reste disponible
            }
        };

        pipeClient.BalloonReceived += balloon =>
        {
            try
            {
                lastBalloonDomain = null;
                notifyIcon.BalloonTipTitle = balloon.Title;
                notifyIcon.BalloonTipText = balloon.Text;
                notifyIcon.ShowBalloonTip(5000);
            }
            catch
            {
                // pas grave si la notif echoue, l'app continue
            }
        };

        Application.Run();
    }

    // Icône générée en mémoire (cercle bleu) pour ne pas dépendre d'un fichier .ico externe.
    private static Icon CreateIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(Color.FromArgb(79, 124, 255));
            g.FillEllipse(brush, 2, 2, 28, 28);
            using var pen = new Pen(Color.White, 2);
            g.DrawEllipse(pen, 10, 8, 12, 12);
            g.DrawLine(pen, 16, 18, 16, 24);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}

// Fenêtre sobre à 3 vues possibles : chargement (au tout début), pairing (tant que non appairé),
// statut (temps restant, temps par app, bouton "Demander du temps").
public class StatusForm : Form
{
    private readonly ClientStatus _status;
    private readonly PipeClient _pipeClient;
    private readonly System.Windows.Forms.Timer _refreshTimer;

    private readonly Panel _loadingPanel;
    private readonly Panel _pairingPanel;
    private readonly Panel _statusPanel;

    // -- Pairing panel --
    private readonly TextBox _serverUrlBox;
    private readonly TextBox _codeBox;
    private readonly Button _pairButton;
    private readonly Label _pairMessageLabel;

    // -- Status panel --
    private readonly Label _remainingLabel;
    private readonly ListBox _appList;

    public StatusForm(ClientStatus status, PipeClient pipeClient)
    {
        _status = status;
        _pipeClient = pipeClient;

        Text = "Nautilyou";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(300, 340);
        BackColor = Color.White;
        ShowInTaskbar = false;

        // --- Panneau "chargement" (etat initial, avant le premier message du service) ---
        _loadingPanel = new Panel { Size = ClientSize, Location = new Point(0, 0), Visible = true };
        _loadingPanel.Controls.Add(new Label
        {
            Text = "Connexion au service Nautilyou...",
            Font = new Font("Segoe UI", 10),
            ForeColor = Color.Gray,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Size = new Size(260, 40),
            Location = new Point(20, 150),
        });

        // --- Panneau de pairing ---
        _pairingPanel = new Panel { Size = ClientSize, Location = new Point(0, 0), Visible = false };
        _pairingPanel.Controls.Add(new Label
        {
            Text = "Appairage requis",
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            AutoSize = false,
            Size = new Size(260, 30),
            Location = new Point(20, 24),
        });
        _pairingPanel.Controls.Add(new Label
        {
            Text = "Demande ces informations a un parent (visibles dans le dashboard).",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.Gray,
            AutoSize = false,
            Size = new Size(260, 34),
            Location = new Point(20, 56),
        });

        _pairingPanel.Controls.Add(new Label
        {
            Text = "URL du serveur",
            Font = new Font("Segoe UI", 9),
            AutoSize = false,
            Size = new Size(260, 18),
            Location = new Point(20, 100),
        });
        _serverUrlBox = new TextBox
        {
            PlaceholderText = "ex: 192.168.1.50",
            Location = new Point(20, 120),
            Size = new Size(260, 24),
        };
        _pairingPanel.Controls.Add(_serverUrlBox);

        _pairingPanel.Controls.Add(new Label
        {
            Text = "Code d'appairage (6 chiffres)",
            Font = new Font("Segoe UI", 9),
            AutoSize = false,
            Size = new Size(260, 18),
            Location = new Point(20, 156),
        });
        _codeBox = new TextBox
        {
            PlaceholderText = "ex: 123456",
            Location = new Point(20, 176),
            Size = new Size(260, 24),
            MaxLength = 6,
        };
        _pairingPanel.Controls.Add(_codeBox);

        _pairButton = new Button
        {
            Text = "Appairer",
            Location = new Point(20, 216),
            Size = new Size(120, 32),
            BackColor = Color.FromArgb(79, 124, 255),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };
        _pairButton.FlatAppearance.BorderSize = 0;
        _pairButton.Click += OnPairClicked;
        _pairingPanel.Controls.Add(_pairButton);

        _pairMessageLabel = new Label
        {
            Text = "",
            Font = new Font("Segoe UI", 8.5f),
            AutoSize = false,
            Size = new Size(260, 60),
            Location = new Point(20, 256),
        };
        _pairingPanel.Controls.Add(_pairMessageLabel);

        // --- Panneau de statut (une fois appaire) ---
        _statusPanel = new Panel { Size = ClientSize, Location = new Point(0, 0), Visible = false };

        var title = new Label
        {
            Text = "Temps d'écran restant",
            Font = new Font("Segoe UI", 10, FontStyle.Regular),
            ForeColor = Color.Gray,
            AutoSize = false,
            Size = new Size(260, 20),
            Location = new Point(20, 18),
        };

        _remainingLabel = new Label
        {
            Text = "—",
            Font = new Font("Segoe UI", 26, FontStyle.Bold),
            AutoSize = false,
            Size = new Size(260, 50),
            Location = new Point(20, 40),
        };

        var appsTitle = new Label
        {
            Text = "Temps par app aujourd'hui",
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.Gray,
            AutoSize = false,
            Size = new Size(260, 20),
            Location = new Point(20, 105),
        };

        _appList = new ListBox
        {
            Location = new Point(20, 128),
            Size = new Size(260, 120),
            BorderStyle = BorderStyle.FixedSingle,
        };

        var requestLabel = new Label
        {
            Text = "Demander plus de temps",
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.Gray,
            AutoSize = false,
            Size = new Size(260, 20),
            Location = new Point(20, 258),
        };

        var btn15 = MakeRequestButton("+15 min", new Point(20, 282), 15);
        var btn30 = MakeRequestButton("+30 min", new Point(120, 282), 30);
        var btn60 = MakeRequestButton("+1 h", new Point(220, 282), 60);

        _statusPanel.Controls.AddRange(new Control[] { title, _remainingLabel, appsTitle, _appList, requestLabel, btn15, btn30, btn60 });

        Controls.Add(_loadingPanel);
        Controls.Add(_pairingPanel);
        Controls.Add(_statusPanel);

        pipeClient.PairingNeeded += () =>
        {
            if (IsHandleCreated) BeginInvoke(() => ShowPanel(_pairingPanel));
        };
        pipeClient.PairResultReceived += result =>
        {
            if (!IsHandleCreated) return;
            BeginInvoke(() =>
            {
                _pairMessageLabel.ForeColor = result.Success ? Color.SeaGreen : Color.Firebrick;
                _pairMessageLabel.Text = result.Message;
                _pairButton.Enabled = true;
                _pairButton.Text = "Appairer";
                // Si succes, le panneau statut prendra le relais des reception du prochain "status".
            });
        };
        pipeClient.StatusReceived += s =>
        {
            _status.Update(s.RemainingMinutes, s.QuotaEnabled, s.AppMinutes, s.ConnectedToServer);
            if (IsHandleCreated)
            {
                BeginInvoke(() =>
                {
                    ShowPanel(_statusPanel);
                    RefreshFromStatus();
                });
            }
        };

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _refreshTimer.Tick += (_, _) => { if (_statusPanel.Visible) RefreshFromStatus(); };
        _refreshTimer.Start();
    }

    private void ShowPanel(Panel panel)
    {
        _loadingPanel.Visible = panel == _loadingPanel;
        _pairingPanel.Visible = panel == _pairingPanel;
        _statusPanel.Visible = panel == _statusPanel;
    }

    private async void OnPairClicked(object? sender, EventArgs e)
    {
        var url = _serverUrlBox.Text.Trim();
        var code = _codeBox.Text.Trim();
        if (url.Length == 0 || code.Length == 0)
        {
            _pairMessageLabel.ForeColor = Color.Firebrick;
            _pairMessageLabel.Text = "Renseigne l'URL et le code.";
            return;
        }

        _pairButton.Enabled = false;
        _pairButton.Text = "Envoi...";
        _pairMessageLabel.ForeColor = Color.Gray;
        _pairMessageLabel.Text = "Envoi de la demande au service...";

        await _pipeClient.SendPairRequestAsync(url, code);
    }

    private Button MakeRequestButton(string text, Point location, int minutes)
    {
        var btn = new Button
        {
            Text = text,
            Location = location,
            Size = new Size(80, 30),
            FlatStyle = FlatStyle.Flat,
        };
        btn.FlatAppearance.BorderColor = Color.FromArgb(79, 124, 255);
        btn.ForeColor = Color.FromArgb(79, 124, 255);
        btn.Click += (_, _) =>
        {
            _ = _pipeClient.SendTimeRequestAsync(minutes);
            btn.Enabled = false;
            btn.Text = "Envoyé";
        };
        return btn;
    }

    private void RefreshFromStatus()
    {
        var s = _status.Snapshot();
        _remainingLabel.Text = s.QuotaEnabled ? $"{s.RemainingMinutes} min" : "illimité";
        _remainingLabel.ForeColor = s.QuotaEnabled && s.RemainingMinutes <= 0 ? Color.Firebrick : Color.Black;

        _appList.Items.Clear();
        foreach (var kv in s.AppMinutes.OrderByDescending(kv => kv.Value).Take(8))
        {
            _appList.Items.Add($"{kv.Key} — {kv.Value} min");
        }
        if (_appList.Items.Count == 0)
        {
            _appList.Items.Add("(aucune activité pour l'instant)");
        }

        Text = s.Connected ? "Nautilyou" : "Nautilyou (service injoignable)";
    }

    public void ShowAndFocus()
    {
        if (!Visible) Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Fermer la fenêtre ne quitte pas l'app : elle continue en arrière-plan (icône systray).
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }
}
