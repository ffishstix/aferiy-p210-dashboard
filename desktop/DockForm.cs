using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace BatteryWatch;

internal sealed class DockForm : Form
{
    private readonly Label state = new();
    private readonly Label charge = new();
    private readonly Label input = new();
    private readonly Label output = new();
    private readonly Label eta = new();
    private readonly Label updated = new();
    private readonly Label farm = new();
    private readonly Label farmDetail = new();
    private readonly Panel batteryFill = new();
    private readonly Panel batteryTrack = new();
    private readonly CheckBox startup = new();
    private readonly Action onRefresh;
    private readonly Action onWebsite;
    private readonly Action onControls;
    private readonly Action onUninstall;
    private readonly Action<bool> onStartup;
    private bool ready;

    public DockForm(Action onRefresh, Action onWebsite, Action onControls, Action onUninstall, Action<bool> onStartup, bool startupEnabled)
    {
        this.onRefresh = onRefresh;
        this.onWebsite = onWebsite;
        this.onControls = onControls;
        this.onUninstall = onUninstall;
        this.onStartup = onStartup;
        Width = 372; Height = 606;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(18, 29, 37);
        ForeColor = Color.FromArgb(238, 247, 245);
        Font = new Font("Segoe UI", 9);
        DoubleBuffered = true;

        var top = new Panel { Dock = DockStyle.Top, Height = 62, BackColor = Color.FromArgb(26, 45, 51) };
        var mark = new Label { Text = "↯", Font = new Font("Segoe UI Symbol", 21, FontStyle.Bold), ForeColor = Color.FromArgb(185, 241, 137), Bounds = new Rectangle(21, 13, 37, 39) };
        var heading = new Label { Text = "BATTERY WATCH", Font = new Font("Segoe UI Semibold", 12), Bounds = new Rectangle(65, 17, 200, 28) };
        var close = Button("×", 322, 13, 30, 30, () => Hide());
        top.Controls.AddRange([mark, heading, close]); Controls.Add(top);

        state.Font = new Font("Segoe UI Semibold", 18); state.Bounds = new Rectangle(24, 83, 320, 42); Controls.Add(state);
        charge.Font = new Font("Segoe UI", 45, FontStyle.Bold); charge.Bounds = new Rectangle(22, 127, 200, 71); Controls.Add(charge);
        batteryTrack.Bounds = new Rectangle(24, 204, 320, 12); batteryTrack.BackColor = Color.FromArgb(52, 74, 78); Controls.Add(batteryTrack);
        batteryFill.Bounds = new Rectangle(0, 0, 0, 12); batteryFill.BackColor = Color.FromArgb(163, 233, 132); batteryTrack.Controls.Add(batteryFill);

        var rule = new Panel { Bounds = new Rectangle(24, 235, 320, 1), BackColor = Color.FromArgb(57, 75, 79) }; Controls.Add(rule);
        Controls.Add(SmallLabel("POWER IN", 24, 252)); Controls.Add(SmallLabel("POWER OUT", 190, 252));
        input.Font = new Font("Segoe UI Semibold", 21); input.Bounds = new Rectangle(24, 274, 140, 38); Controls.Add(input);
        output.Font = new Font("Segoe UI Semibold", 21); output.Bounds = new Rectangle(190, 274, 150, 38); Controls.Add(output);
        eta.Font = new Font("Segoe UI Semibold", 12); eta.Bounds = new Rectangle(24, 324, 315, 28); Controls.Add(eta);
        updated.ForeColor = Color.FromArgb(151, 174, 178); updated.Bounds = new Rectangle(24, 354, 320, 22); Controls.Add(updated);

        Controls.Add(SmallLabel("FARM POWER", 24, 390));
        farm.Font = new Font("Segoe UI Semibold", 10); farm.Bounds = new Rectangle(24, 409, 320, 25); Controls.Add(farm);
        farmDetail.ForeColor = Color.FromArgb(151, 174, 178); farmDetail.Bounds = new Rectangle(24, 433, 320, 25); Controls.Add(farmDetail);

        var controls = Button("Battery controls  →", 24, 469, 320, 39, onControls);
        controls.BackColor = Color.FromArgb(184, 239, 143); controls.ForeColor = Color.FromArgb(19, 46, 34);
        var web = Button("Open dashboard ↗", 24, 519, 178, 35, onWebsite);
        var refresh = Button("Refresh", 212, 519, 132, 35, onRefresh);
        Controls.AddRange([controls, web, refresh]);
        startup.Text = "Start with Windows"; startup.Checked = startupEnabled; startup.AutoSize = true;
        startup.ForeColor = Color.FromArgb(165, 184, 185); startup.Location = new Point(24, 571);
        startup.CheckedChanged += (_, _) => { if (ready) onStartup(startup.Checked); };
        Controls.Add(startup);
        var uninstall = Button("Uninstall", 266, 566, 78, 27, onUninstall);
        uninstall.ForeColor = Color.FromArgb(246, 167, 162);
        Controls.Add(uninstall);
        ready = true;
        UpdateReading(null, "offline", true);
        UpdateFarm(null, false);
        Resize += (_, _) => RoundedWindow.Apply(this);
        HandleCreated += (_, _) => RoundedWindow.Apply(this);
        Deactivate += (_, _) => BeginInvoke(new Action(() => { if (!ContainsFocus) Hide(); }));
        Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(64, 93, 95), 1);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        };
    }

    private static Label SmallLabel(string text, int x, int y) => new() { Text = text, Font = new Font("Segoe UI Semibold", 8), ForeColor = Color.FromArgb(151, 174, 178), Bounds = new Rectangle(x, y, 140, 20) };

    private static Button Button(string text, int x, int y, int width, int height, Action click)
    {
        var button = new Button { Text = text, Bounds = new Rectangle(x, y, width, height), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(43, 62, 66), ForeColor = Color.White, Cursor = Cursors.Hand, TabStop = true };
        button.FlatAppearance.BorderSize = 0;
        button.Click += (_, _) => click();
        return button;
    }

    public void PositionBottomRight()
    {
        var work = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(work.Right - Width - 15, work.Bottom - Height - 12);
    }

    public void UpdateReading(Sample? sample, string currentState, bool needsPassword)
    {
        state.Text = needsPassword ? "Connect your dashboard" : BatteryState.Title(currentState);
        state.ForeColor = BatteryState.Colour(currentState);
        charge.Text = sample?.ChargePercent is double value ? $"{value:0}%" : "—";
        input.Text = sample?.InputW is double inW ? $"{inW:0} W" : "— W";
        output.Text = sample?.OutputW is double outW ? $"{outW:0} W" : "— W";
        eta.Text = currentState is "discharging" or "low" ? BatteryState.Eta(sample) : "";
        updated.Text = needsPassword ? "Enter your dashboard password to begin" : sample == null ? "Waiting for a fresh cloud reading" : $"Updated {sample.RecordedAt.ToLocalTime():h:mm tt}";
        batteryFill.Width = sample?.ChargePercent is double pct ? (int)Math.Round(320 * Math.Clamp(pct, 0, 100) / 100) : 0;
        batteryFill.BackColor = BatteryState.Colour(currentState);
    }

    public void UpdateFarm(FarmSample? sample, bool live)
    {
        if (sample == null) { farm.Text = "Waiting for farm readings"; farmDetail.Text = "Main battery, solar and generator unavailable"; return; }
        var generator = sample.GeneratorRunning == 0 ? "Off" : sample.GeneratorW is double power ? $"{power:0} W" : sample.GeneratorRunning == 1 ? "Running" : "—";
        farm.Text = $"Solar {Watts(sample.SolarW)}  ·  Generator {generator}";
        var charge = sample.MainChargePercent is double pct ? $"{pct:0}%" : sample.MainBatteryV is double volts ? $"{volts:0.0} V" : "—";
        var flow = sample.MainBatteryW is double watts ? watts > 0 ? "charging" : watts < 0 ? "draining" : "balanced" : "flow unknown";
        farmDetail.Text = $"Main battery {charge}, {flow}" + (live ? "" : " · old reading");
    }

    private static string Watts(double? value) => value is double watts ? $"{watts:0} W" : "—";
}
