using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace BatteryWatch;

internal static class Program
{
    public const string DashboardUrl = "https://battery.fishstix.uk";

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--verify-update-manifest")
        {
            try { _ = UpdateService.VerifyManifest(File.ReadAllText(args[1])); Environment.Exit(0); }
            catch { Environment.Exit(1); }
        }
        if (args.Length == 1 && args[0] == "--check-update-download")
        {
            try
            {
                var staged = UpdateService.CheckAndStageAsync(new Version(0, 0, 0)).GetAwaiter().GetResult();
                if (staged == null) Environment.Exit(1);
                File.Delete(staged);
                Environment.Exit(0);
            }
            catch { Environment.Exit(1); }
        }
        if (args.Length == 1 && args[0] == "--exercise-update")
        {
            try
            {
                var staged = UpdateService.CheckAndStageAsync(new Version(0, 0, 0)).GetAwaiter().GetResult();
                if (staged == null) Environment.Exit(1);
                UpdateService.ReplaceAfterExit(staged, smokeTest: true);
                Environment.Exit(0);
            }
            catch { Environment.Exit(1); }
        }
        if (args.Contains("--self-test"))
        {
            if (BatteryState.Classify(new Sample { ChargePercent = 14, Discharging = 1, GridPresent = 0, RecordedAt = DateTimeOffset.UtcNow }) != "low") Environment.Exit(1);
            if (BatteryState.Classify(new Sample { InputW = 451, OutputW = 151, RecordedAt = DateTimeOffset.UtcNow }) != "charging") Environment.Exit(1);
            if (BatteryState.Classify(new Sample { InputW = 161, OutputW = 160, GridPresent = 1, RecordedAt = DateTimeOffset.UtcNow }) != "passthrough") Environment.Exit(1);
            var fresh = DateTimeOffset.UtcNow;
            if (PlugAdvice.Evaluate(new Sample { RecordedAt = fresh, Discharging = 1, AcInputW = 0 }, new FarmSample { RecordedAt = fresh, GeneratorRunning = 1, GeneratorW = 900, MainBatteryW = 100 }) != "on_generator") Environment.Exit(1);
            if (PlugAdvice.Evaluate(new Sample { RecordedAt = fresh, AcInputW = 0 }, new FarmSample { RecordedAt = fresh, SolarW = 1500, MainBatteryW = 1250 }) != "on_solar") Environment.Exit(1);
            if (PlugAdvice.Evaluate(new Sample { RecordedAt = fresh, AcInputW = 500 }, new FarmSample { RecordedAt = fresh, SolarW = 100, MainBatteryW = -200 }) != "off") Environment.Exit(1);
            if (PlugAdvice.Evaluate(new Sample { RecordedAt = fresh.AddMinutes(-3), AcInputW = 500 }, new FarmSample { RecordedAt = fresh, MainBatteryW = -200 }) != null) Environment.Exit(1);
            ApplicationConfiguration.Initialize();
            using var view = new DockForm(() => { }, () => { }, () => { }, () => { }, () => { }, _ => { }, false);
            view.UpdateReading(new Sample { ChargePercent = 29, InputW = 450, OutputW = 151, RecordedAt = DateTimeOffset.UtcNow }, "charging", false);
            _ = view.Handle;
            var previewPath = Environment.GetEnvironmentVariable("BATTERY_WATCH_PREVIEW");
            if (!string.IsNullOrWhiteSpace(previewPath))
            {
                view.StartPosition = FormStartPosition.Manual;
                view.Location = new Point(-2000, -2000);
                view.Show();
                Application.DoEvents();
                using var preview = new Bitmap(view.Width, view.Height);
                view.DrawToBitmap(preview, new Rectangle(0, 0, view.Width, view.Height));
                preview.Save(previewPath);
                view.Hide();
            }
            using var previewClient = new HttpClient { BaseAddress = new Uri(DashboardUrl) };
            using var controlView = new ControlsForm(previewClient);
            controlView.PreviewSettings();
            _ = controlView.Handle;
            var controlPreviewPath = Environment.GetEnvironmentVariable("BATTERY_WATCH_CONTROLS_PREVIEW");
            if (!string.IsNullOrWhiteSpace(controlPreviewPath))
            {
                controlView.StartPosition = FormStartPosition.Manual;
                controlView.Location = new Point(-2000, -2000);
                controlView.Show();
                Application.DoEvents();
                using var preview = new Bitmap(controlView.Width, controlView.Height);
                controlView.DrawToBitmap(preview, new Rectangle(0, 0, controlView.Width, controlView.Height));
                preview.Save(controlPreviewPath);
                controlView.Hide();
            }
            using var farmView = new FarmControlsForm(previewClient);
            farmView.PreviewSettings();
            _ = farmView.Handle;
            var farmPreviewPath = Environment.GetEnvironmentVariable("BATTERY_WATCH_FARM_PREVIEW");
            if (!string.IsNullOrWhiteSpace(farmPreviewPath))
            {
                farmView.StartPosition = FormStartPosition.Manual;
                farmView.Location = new Point(-2000, -2000);
                farmView.Show();
                Application.DoEvents();
                using var preview = new Bitmap(farmView.Width, farmView.Height);
                farmView.DrawToBitmap(preview, new Rectangle(0, 0, farmView.Width, farmView.Height));
                preview.Save(farmPreviewPath);
                farmView.Hide();
            }
            Environment.Exit(0);
        }
        using var mutex = new Mutex(true, @"Local\AferiyBatteryWatchDesktop", out var owner);
        if (!owner) return;
        ApplicationConfiguration.Initialize();
        Application.Run(new BatteryApp());
    }
}

internal sealed class Sample
{
    [JsonPropertyName("recorded_at")] public DateTimeOffset RecordedAt { get; set; }
    [JsonPropertyName("charge_percent")] public double? ChargePercent { get; set; }
    [JsonPropertyName("input_w")] public double? InputW { get; set; }
    [JsonPropertyName("output_w")] public double? OutputW { get; set; }
    [JsonPropertyName("ac_input_w")] public double? AcInputW { get; set; }
    [JsonPropertyName("dc_input_w")] public double? DcInputW { get; set; }
    [JsonPropertyName("remaining_minutes")] public double? RemainingMinutes { get; set; }
    [JsonPropertyName("grid_present")] public int? GridPresent { get; set; }
    [JsonPropertyName("discharging")] public int? Discharging { get; set; }
    [JsonPropertyName("fault")] public int? Fault { get; set; }
}

internal sealed class StatusResult
{
    [JsonPropertyName("sample")] public Sample? Sample { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
}

internal sealed class FarmSample
{
    [JsonPropertyName("recorded_at")] public DateTimeOffset RecordedAt { get; set; }
    [JsonPropertyName("solar_w")] public double? SolarW { get; set; }
    [JsonPropertyName("generator_w")] public double? GeneratorW { get; set; }
    [JsonPropertyName("generator_running")] public int? GeneratorRunning { get; set; }
    [JsonPropertyName("main_charge_percent")] public double? MainChargePercent { get; set; }
    [JsonPropertyName("main_battery_w")] public double? MainBatteryW { get; set; }
    [JsonPropertyName("main_battery_v")] public double? MainBatteryV { get; set; }
    [JsonPropertyName("load_w")] public double? LoadW { get; set; }
    [JsonPropertyName("ac_source")] public string? AcSource { get; set; }
}

internal sealed class FarmStatusResult
{
    [JsonPropertyName("sample")] public FarmSample? Sample { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
}

internal static class PlugAdvice
{
    public static string? Evaluate(Sample? aferiy, FarmSample? farm)
    {
        if (aferiy == null || farm == null || aferiy.RecordedAt == default || farm.RecordedAt == default) return null;
        var now = DateTimeOffset.UtcNow;
        if (now - aferiy.RecordedAt > TimeSpan.FromMinutes(2) || now - farm.RecordedAt > TimeSpan.FromMinutes(2)) return null;
        // Leave at least 100 W charging the main bank before recommending the AFERIY charger.
        if (aferiy.AcInputW is > 50 && farm.MainBatteryW is < 100) return "off";
        if (aferiy.AcInputW is > 50) return null;
        if (farm.GeneratorRunning == 1 && farm.GeneratorW is >= 400 && farm.MainBatteryW is >= 0 && aferiy.Discharging == 1) return "on_generator";
        // The P210 can draw up to 1100 W; require that plus a 100 W reserve.
        if (farm.SolarW is >= 1200 && farm.MainBatteryW is >= 1200) return "on_solar";
        return null;
    }
}

internal static class BatteryState
{
    public static string Classify(Sample? sample)
    {
        if (sample == null) return "offline";
        if (sample.RecordedAt == default || DateTimeOffset.UtcNow - sample.RecordedAt > TimeSpan.FromMinutes(2)) return "stale";
        if (sample.Fault == 1) return "fault";
        if (sample.Discharging == 1 && sample.GridPresent != 1) return sample.ChargePercent is < 15 ? "low" : "discharging";
        if (sample.GridPresent == 1 && sample.OutputW > 0 && Math.Abs((sample.InputW ?? 0) - sample.OutputW.Value) <= 25) return "passthrough";
        if ((sample.InputW ?? 0) > (sample.OutputW ?? 0) + 25) return "charging";
        return "idle";
    }

    public static string Title(string state) => state switch
    {
        "low" => "Low battery",
        "discharging" => "Running on battery",
        "charging" => "Charging",
        "passthrough" => "Power passing through",
        "fault" => "Battery fault",
        "stale" => "Reading is old",
        "idle" => "Standing by",
        _ => "Connection unavailable"
    };

    public static Color Colour(string state) => state switch
    {
        "low" or "fault" => Color.FromArgb(244, 112, 106),
        "discharging" => Color.FromArgb(245, 198, 101),
        "charging" => Color.FromArgb(135, 227, 163),
        "passthrough" => Color.FromArgb(115, 205, 226),
        _ => Color.FromArgb(155, 177, 184)
    };

    public static string Eta(Sample? sample)
    {
        if (sample?.RemainingMinutes is not double minutes || minutes < 0) return "Estimate unavailable";
        int value = (int)Math.Round(minutes);
        return value < 60 ? $"About {value} min left" : $"About {value / 60} h {value % 60} min left";
    }
}

internal static class SecretStore
{
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AferiyMonitor");
    private static readonly string FilePath = Path.Combine(Folder, "desktop-password.dat");

    public static string? Read()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            byte[] protectedBytes = File.ReadAllBytes(FilePath);
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException) { return null; }
        catch (IOException) { return null; }
    }

    public static void Save(string password)
    {
        Directory.CreateDirectory(Folder);
        byte[] bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(password), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(FilePath, bytes);
    }

    public static void Delete()
    {
        if (File.Exists(FilePath)) File.Delete(FilePath);
    }
}

internal sealed class BatteryApp : ApplicationContext
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
    private readonly CookieContainer cookies = new();
    private readonly HttpClient client;
    private readonly NotifyIcon tray;
    private readonly DockForm dock;
    private readonly ControlsForm controls;
    private readonly FarmControlsForm farmControls;
    private readonly System.Windows.Forms.Timer timer;
    private readonly System.Windows.Forms.Timer updateTimer;
    private string? password;
    private string previousState = "offline";
    private int alertLevel;
    private bool fetching;
    private bool loginRequired;
    private bool prompted;
    private bool updateChecking;
    private bool exiting;
    private bool pendingFresh;
    private string? previousPlugAdvice;
    private int plugAdviceStreak;
    private DateTimeOffset lastPlugAlert;

    public BatteryApp()
    {
        client = new HttpClient(new HttpClientHandler { CookieContainer = cookies }) { BaseAddress = new Uri(Program.DashboardUrl), Timeout = TimeSpan.FromSeconds(40) };
        password = SecretStore.Read();
        controls = new ControlsForm(client);
        farmControls = new FarmControlsForm(client);
        dock = new DockForm(() => _ = RefreshAsync(true), () => OpenWebsite(Program.DashboardUrl), ShowControls, ShowFarmControls, Uninstall, SetStartup, StartupEnabled());
        _ = dock.Handle;
        dock.FormClosed += (_, _) => ExitThread();
        tray = new NotifyIcon
        {
            Text = "P210: waiting for readings",
            Icon = CreateIcon("offline", null),
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip()
        };
        tray.ContextMenuStrip.Items.Add("Show battery details", null, (_, _) => ToggleDock());
        tray.ContextMenuStrip.Items.Add("Battery controls", null, (_, _) => ShowControls());
        tray.ContextMenuStrip.Items.Add("Farm settings", null, (_, _) => ShowFarmControls());
        tray.ContextMenuStrip.Items.Add("Open website", null, (_, _) => OpenWebsite(Program.DashboardUrl));
        tray.ContextMenuStrip.Items.Add("Change control password", null, (_, _) => ChangePassword());
        tray.ContextMenuStrip.Items.Add("Check for updates", null, async (_, _) => await CheckForUpdatesAsync(true));
        tray.ContextMenuStrip.Items.Add("Uninstall", null, (_, _) => Uninstall());
        tray.ContextMenuStrip.Items.Add("Exit", null, (_, _) => ExitThread());
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ToggleDock(); };
        tray.BalloonTipClicked += (_, _) => ShowDock();
        timer = new System.Windows.Forms.Timer { Interval = 30_000 };
        timer.Tick += async (_, _) => await RefreshAsync();
        timer.Start();
        updateTimer = new System.Windows.Forms.Timer { Interval = 6 * 60 * 60 * 1000 };
        updateTimer.Tick += async (_, _) => await CheckForUpdatesAsync();
        updateTimer.Start();
        dock.BeginInvoke(new Action(async () => await RefreshAsync(true)));
        dock.BeginInvoke(new Action(async () => { await Task.Delay(10_000); if (!exiting) await CheckForUpdatesAsync(); }));
    }

    private static void OpenWebsite(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private void ToggleDock() { if (dock.Visible) dock.Hide(); else ShowDock(); }
    private void ShowDock() { controls.Hide(); farmControls.Hide(); dock.PositionBottomRight(); dock.Show(); dock.Activate(); }

    private void ShowControls()
    {
        dock.Hide();
        farmControls.Hide();
        controls.PositionBottomRight();
        controls.Show(); controls.Activate();
        _ = controls.RefreshAsync();
    }

    private void ShowFarmControls()
    {
        dock.Hide();
        controls.Hide();
        farmControls.PositionBottomRight();
        farmControls.Show(); farmControls.Activate();
        _ = farmControls.RefreshAsync();
    }

    private async Task CheckForUpdatesAsync(bool reportCurrent = false)
    {
        if (updateChecking || exiting) return;
        updateChecking = true;
        try
        {
            var staged = await UpdateService.CheckAndStageAsync();
            if (exiting) return;
            if (staged == null)
            {
                if (reportCurrent) Alert("Battery Watch is up to date", $"Version {UpdateService.CurrentVersion.ToString(3)} is installed.", ToolTipIcon.Info);
                return;
            }
            UpdateService.ReplaceAfterExit(staged);
            ExitThread();
        }
        catch (Exception error)
        {
            if (reportCurrent && !exiting) Alert("Battery Watch update failed", error.Message, ToolTipIcon.Warning);
        }
        finally { updateChecking = false; }
    }

    private void Uninstall()
    {
        if (MessageBox.Show(dock, "Remove Battery Watch from startup, delete its saved password, and remove this downloaded app file? Cloud history and the website will stay available.", "Uninstall Battery Watch", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable) || !Path.GetFileName(executable).StartsWith("BatteryWatch", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(dock, "The app file could not be identified. Uninstall was cancelled.", "Battery Watch", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        try
        {
            var escaped = executable.Replace("'", "''");
            var script = $"Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue; Remove-Item -LiteralPath '{escaped}' -Force -ErrorAction SilentlyContinue";
            var helper = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
            };
            helper.ArgumentList.Add("-NoProfile"); helper.ArgumentList.Add("-NonInteractive");
            helper.ArgumentList.Add("-WindowStyle"); helper.ArgumentList.Add("Hidden");
            helper.ArgumentList.Add("-EncodedCommand"); helper.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
            using var helperProcess = Process.Start(helper) ?? throw new System.ComponentModel.Win32Exception("Could not start the uninstall helper");
            SetStartup(false);
            SecretStore.Delete();
            UpdateService.DeleteStagedFiles();
            ExitThread();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(dock, $"Could not complete uninstall: {error.Message}", "Battery Watch", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ChangePassword()
    {
        using var prompt = new PasswordForm();
        if (prompt.ShowDialog(dock) != DialogResult.OK) return;
        password = prompt.Password;
        SecretStore.Save(password);
        cookies.GetCookies(new Uri(Program.DashboardUrl)).Cast<Cookie>().ToList().ForEach(c => c.Expired = true);
        _ = RefreshAsync();
    }

    private static bool StartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue("BatteryWatch") != null;
    }

    private void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        if (key == null) return;
        if (enabled) key.SetValue("BatteryWatch", $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue("BatteryWatch", false);
    }

    private async Task<Sample?> GetSampleAsync(bool fresh)
    {
        loginRequired = false;
        var path = fresh ? "/api/status?fresh=1" : "/api/status";
        using var response = await client.GetAsync(path);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            if (string.IsNullOrEmpty(password)) { loginRequired = true; return null; }
            using var login = await client.PostAsJsonAsync("/api/login", new { password });
            if (!login.IsSuccessStatusCode) { loginRequired = true; return null; }
            using var retry = await client.GetAsync(path);
            retry.EnsureSuccessStatusCode();
            return (await retry.Content.ReadFromJsonAsync<StatusResult>())?.Sample;
        }
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<StatusResult>())?.Sample;
    }

    private async Task RefreshAsync(bool fresh = false)
    {
        if (fetching) { if (fresh) pendingFresh = true; return; }
        fetching = true;
        try
        {
            var sample = await GetSampleAsync(fresh);
            Update(sample);
            try
            {
                var farm = await client.GetFromJsonAsync<FarmStatusResult>($"/api/farm/status?at={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
                dock.UpdateFarm(farm?.Sample, farm?.Status == "live");
                UpdatePlugAdvice(PlugAdvice.Evaluate(sample, farm?.Status == "live" ? farm.Sample : null));
            }
            catch (HttpRequestException) { dock.UpdateFarm(null, false); UpdatePlugAdvice(null); }
            catch (TaskCanceledException) { dock.UpdateFarm(null, false); UpdatePlugAdvice(null); }
            catch (JsonException) { dock.UpdateFarm(null, false); UpdatePlugAdvice(null); }
            if (loginRequired && !prompted)
            {
                prompted = true;
                ShowDock();
                ChangePassword();
            }
        }
        catch (HttpRequestException) { Update(null); }
        catch (TaskCanceledException) { Update(null); }
        catch (JsonException) { Update(null); }
        finally
        {
            fetching = false;
            if (pendingFresh && !exiting)
            {
                pendingFresh = false;
                _ = RefreshAsync(true);
            }
        }
    }

    private void Update(Sample? sample)
    {
        var state = BatteryState.Classify(sample);
        dock.UpdateReading(sample, state, loginRequired);
        var old = tray.Icon;
        tray.Icon = CreateIcon(state, sample?.ChargePercent);
        old?.Dispose();
        var text = $"P210: {BatteryState.Title(state)}";
        if (sample?.ChargePercent is double percent) text += $" · {percent:0}%";
        tray.Text = text.Length > 63 ? text[..63] : text;
        if (state is "discharging" or "low" && previousState is not ("discharging" or "low"))
            Alert("P210 running on battery", $"{sample?.ChargePercent:0}% charge. {BatteryState.Eta(sample)}.", ToolTipIcon.Warning);
        if (state == "low" && sample?.ChargePercent is double charge)
        {
            int level = charge <= 5 ? 3 : charge <= 10 ? 2 : 1;
            if (level > alertLevel)
            {
                Alert("Save your work — P210 battery low", $"{charge:0}% left. Save your work and shut down this PC soon. {BatteryState.Eta(sample)}.", ToolTipIcon.Warning);
                alertLevel = level;
            }
        }
        else if (state is "charging" or "passthrough" or "idle" && sample?.ChargePercent >= 17) alertLevel = 0;
        if (state is "offline" or "stale" && previousState is "discharging" or "low")
            Alert("P210 reading unavailable", "The last battery reading is old. Check the power station.", ToolTipIcon.Warning);
        previousState = state;
    }

    private void UpdatePlugAdvice(string? advice)
    {
        if (advice != previousPlugAdvice) { previousPlugAdvice = advice; plugAdviceStreak = 0; }
        if (advice == null || ++plugAdviceStreak < 2 || DateTimeOffset.UtcNow - lastPlugAlert < TimeSpan.FromMinutes(30)) return;
        lastPlugAlert = DateTimeOffset.UtcNow;
        plugAdviceStreak = 0;
        switch (advice)
        {
            case "on_generator": Alert("Turn on the AFERIY input plug", "The generator is supplying power while the AFERIY is draining. Turn on its charging plug.", ToolTipIcon.Info); break;
            case "on_solar": Alert("Solar power available for AFERIY", "The main battery has enough solar charging headroom. Turn on the AFERIY input plug.", ToolTipIcon.Info); break;
            case "off": Alert("Unplug the AFERIY input", "The AFERIY is charging while the main battery has little or no charging headroom. Turn off its input plug.", ToolTipIcon.Warning); break;
        }
    }

    private void Alert(string title, string message, ToolTipIcon icon) => tray.ShowBalloonTip(10000, title, message, icon);

    private static Icon CreateIcon(string state, double? percent)
    {
        using var bmp = new Bitmap(48, 48);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var colour = BatteryState.Colour(state);
        using var pen = new Pen(colour, 4);
        using var brush = new SolidBrush(colour);
        using var path = new GraphicsPath();
        path.AddArc(7, 10, 12, 12, 180, 90);
        path.AddArc(28, 10, 12, 12, 270, 90);
        path.AddArc(28, 30, 12, 12, 0, 90);
        path.AddArc(7, 30, 12, 12, 90, 90);
        path.CloseFigure();
        g.DrawPath(pen, path);
        g.FillRectangle(brush, 17, 4, 13, 7);
        if (percent is double value && state is not ("offline" or "stale"))
        {
            var height = (int)Math.Round(Math.Clamp(value, 0, 100) / 100 * 23);
            if (height > 0) g.FillRectangle(brush, 13, 36 - height, 21, height);
        }
        var handle = bmp.GetHicon();
        try { using var icon = Icon.FromHandle(handle); return (Icon)icon.Clone(); }
        finally { DestroyIcon(handle); }
    }

    protected override void ExitThreadCore()
    {
        exiting = true;
        updateTimer.Stop(); updateTimer.Dispose();
        timer.Stop(); timer.Dispose();
        tray.Visible = false; tray.Icon?.Dispose(); tray.Dispose();
        controls.Dispose(); farmControls.Dispose(); dock.Dispose(); client.Dispose();
        base.ExitThreadCore();
    }
}

internal sealed class PasswordForm : Form
{
    private readonly TextBox input = new() { UseSystemPasswordChar = true, Width = 290, Font = new Font("Segoe UI", 11) };
    public string Password => input.Text;
    public PasswordForm()
    {
        Text = "Connect Battery Watch"; Width = 390; Height = 225; StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; BackColor = Color.FromArgb(24, 36, 44); ForeColor = Color.White;
        var title = new Label { Text = "Dashboard password", Font = new Font("Segoe UI Semibold", 13), Bounds = new Rectangle(28, 24, 320, 28) };
        var help = new Label { Text = "Use the password for battery.fishstix.uk", ForeColor = Color.FromArgb(160, 181, 186), Bounds = new Rectangle(28, 56, 320, 22) };
        input.Location = new Point(28, 88);
        var save = new Button { Text = "Connect", Bounds = new Rectangle(238, 130, 80, 33), DialogResult = DialogResult.OK };
        save.Click += (_, _) => { if (string.IsNullOrWhiteSpace(input.Text)) DialogResult = DialogResult.None; };
        Controls.AddRange([title, help, input, save]); AcceptButton = save;
    }
}
