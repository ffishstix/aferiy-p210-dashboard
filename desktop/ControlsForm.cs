using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace BatteryWatch;

internal sealed class ControlsForm : Form
{
    private sealed record Field(string Key, string Name, string Help, string Kind, int Min = 0, int Max = 0, (int Value, string Name)[]? Options = null);
    private sealed record Group(string Name, Field[] Fields);
    private static readonly Group[] Groups =
    [
        new("Outputs", [
            new("acOutput", "AC output", "Power for the AC sockets. Turning this off cuts power to anything plugged in.", "toggle"),
            new("dcOutput", "DC output", "Power for the DC sockets and car socket.", "toggle"),
            new("usbOutput", "USB output", "Power for the USB charging ports.", "toggle"),
            new("ledMode", "LED light", "Sets the built-in light to steady, SOS, flashing, or off.", "select", Options: [(0,"Off"),(1,"On"),(2,"SOS"),(3,"Flash")])
        ]),
        new("Charging", [
            new("acChargingRate", "AC charging power", "Target power drawn from a wall socket. Actual power may vary.", "select", Options: [(1,"300 W"),(2,"500 W"),(3,"700 W"),(4,"900 W"),(5,"1100 W")]),
            new("dcInputType", "DC input mode", "Solar is for panels. DC source is for a compatible external power supply.", "select", Options: [(0,"Solar (MPPT)"),(1,"DC source")]),
            new("dcInputCurrent", "DC charging current", "Maximum current drawn from the solar or DC input, in amps.", "number", 1, 20),
            new("chargeLimit", "Charge limit", "Upper battery percentage for AC charging in UPS mode.", "number", 60, 100),
            new("dischargeLimit", "Discharge limit", "Battery percentage at which discharging stops.", "number", 0, 50),
            new("silentCharging", "Silent charging", "Charges more slowly from a wall socket to reduce fan noise.", "toggle")
        ]),
        new("Sound and standby", [
            new("buzzer", "Button sound", "Turns the beep from the battery buttons on or off.", "toggle"),
            new("usbStandbyTime", "USB standby", "Turns USB output off after this long with no load.", "select", Options: [(0,"Never"),(3,"3 min"),(5,"5 min"),(10,"10 min"),(30,"30 min")]),
            new("acStandbyTime", "AC standby", "Turns AC output off after this long with no load.", "select", Options: [(0,"Never"),(480,"8 h"),(960,"16 h"),(1440,"24 h")]),
            new("dcStandbyTime", "DC standby", "Turns DC output off after this long with no load.", "select", Options: [(0,"Never"),(480,"8 h"),(960,"16 h"),(1440,"24 h")]),
            new("screenRestTime", "Screen rest", "Dims the battery screen after this long.", "select", Options: [(0,"Never"),(180,"3 min"),(300,"5 min"),(600,"10 min"),(1800,"30 min")]),
            new("sleepTime", "Battery idle shutoff", "Turns the battery off after this long when idle.", "select", Options: [(5,"5 min"),(10,"10 min"),(30,"30 min"),(480,"8 h")])
        ])
    ];

    private readonly HttpClient client;
    private readonly Label message = new();
    private readonly Label updated = new();
    private readonly Panel signIn = new();
    private readonly TextBox password = new();
    private readonly FlowLayoutPanel fields = new();
    private readonly Panel tabs = new();
    private readonly Button[] tabButtons = new Button[3];
    private readonly Dictionary<string, (object Value, DateTimeOffset At)> confirmedChanges = new();
    private JsonElement settings;
    private int selectedGroup;
    private bool busy;
    private bool triedSavedPassword;
    private bool dialogOpen;

    public ControlsForm(HttpClient client)
    {
        this.client = client;
        Width = 452; Height = 510;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false; TopMost = true;
        BackColor = Color.FromArgb(19, 29, 37); ForeColor = Color.FromArgb(238, 247, 245);
        Font = new Font("Segoe UI", 9);
        DoubleBuffered = true;
        var top = new Panel { Bounds = new Rectangle(0, 0, 452, 75), BackColor = Color.FromArgb(26, 45, 51) };
        top.Controls.Add(new Label { Text = "BATTERY WATCH  /  CONTROLS", Font = new Font("Segoe UI Semibold", 12), Bounds = new Rectangle(23, 24, 350, 28) });
        var close = Button("×", 397, 19, 30, 31, () => Hide());
        top.Controls.Add(close); Controls.Add(top);
        updated.Bounds = new Rectangle(23, 88, 320, 24); updated.ForeColor = Color.FromArgb(154, 178, 180); Controls.Add(updated);
        var refresh = Button("Refresh", 349, 84, 79, 30, () => _ = RefreshAsync()); Controls.Add(refresh);

        signIn.Bounds = new Rectangle(23, 168, 404, 175);
        signIn.Controls.Add(new Label { Text = "Sign in to change battery settings", Font = new Font("Segoe UI Semibold", 14), Bounds = new Rectangle(0, 0, 380, 33) });
        signIn.Controls.Add(new Label { Text = "Readings stay public. Changes require the control password.", ForeColor = Color.FromArgb(154, 178, 180), Bounds = new Rectangle(0, 39, 395, 26) });
        password.UseSystemPasswordChar = true; password.Bounds = new Rectangle(0, 82, 255, 31); password.BackColor = Color.FromArgb(14, 25, 31); password.ForeColor = Color.White;
        password.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; _ = LoginAsync(); } };
        signIn.Controls.Add(password);
        signIn.Controls.Add(Button("Sign in", 266, 82, 110, 31, () => _ = LoginAsync()));
        Controls.Add(signIn);

        tabs.Bounds = new Rectangle(19, 128, 414, 36);
        var labels = new[] { "Outputs", "Charging", "Standby" };
        for (int i = 0; i < tabButtons.Length; i++)
        {
            int index = i;
            tabButtons[i] = Button(labels[i], i * 138, 0, 132, 34, () => SelectGroup(index));
            tabs.Controls.Add(tabButtons[i]);
        }
        tabs.Visible = false; Controls.Add(tabs);
        fields.Bounds = new Rectangle(19, 170, 414, 282);
        fields.FlowDirection = FlowDirection.TopDown; fields.WrapContents = false; fields.AutoScroll = false;
        fields.BackColor = BackColor; fields.Visible = false; Controls.Add(fields);
        message.Bounds = new Rectangle(23, 460, 400, 43); message.ForeColor = Color.FromArgb(179, 199, 198); Controls.Add(message);
        Resize += (_, _) => RoundedWindow.Apply(this);
        HandleCreated += (_, _) => RoundedWindow.Apply(this);
        Deactivate += (_, _) => BeginInvoke(new Action(() => { if (!dialogOpen && !ContainsFocus) Hide(); }));
    }

    private static Button Button(string text, int x, int y, int width, int height, Action click)
    {
        var button = new Button { Text = text, Bounds = new Rectangle(x, y, width, height), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(48, 68, 72), ForeColor = Color.White, Cursor = Cursors.Hand };
        button.FlatAppearance.BorderSize = 0; button.Click += (_, _) => click(); return button;
    }

    public void PositionBottomRight()
    {
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Right - Width - 15, area.Bottom - Height - 12);
    }

    private void ShowError(string text)
    {
        message.ForeColor = Color.FromArgb(246, 167, 162);
        message.Text = text;
    }

    private void ShowMessage(string text)
    {
        message.ForeColor = Color.FromArgb(179, 199, 198);
        message.Text = text;
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> RequestAsync(string path, object? data = null)
    {
        using var request = new HttpRequestMessage(data == null ? HttpMethod.Get : HttpMethod.Post, path);
        if (data != null)
        {
            request.Content = JsonContent.Create(data);
            request.Headers.TryAddWithoutValidation("Origin", Program.DashboardUrl);
        }
        using var response = await client.SendAsync(request);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (response.StatusCode, json.RootElement.Clone());
    }

    private static string Error(JsonElement body, HttpStatusCode status) => body.TryGetProperty("error", out var error) ? error.GetString() ?? $"Request failed ({(int)status})" : $"Request failed ({(int)status})";

    public async Task RefreshAsync()
    {
        if (busy) return;
        busy = true;
        try
        {
            var result = await RequestAsync("/api/control/state");
            if (result.Status == HttpStatusCode.Unauthorized && !triedSavedPassword)
            {
                triedSavedPassword = true;
                var saved = SecretStore.Read();
                if (!string.IsNullOrEmpty(saved))
                {
                    var login = await RequestAsync("/api/control/login", new { password = saved });
                    if ((int)login.Status < 300) result = await RequestAsync("/api/control/state");
                }
            }
            if (result.Status == HttpStatusCode.Unauthorized)
            {
                fields.Visible = false; tabs.Visible = false; signIn.Visible = true;
                ShowMessage("Sign in to use battery controls.");
                return;
            }
            if ((int)result.Status >= 300) throw new InvalidOperationException(Error(result.Body, result.Status));
            signIn.Visible = false; fields.Visible = true; tabs.Visible = true;
            var body = result.Body;
            updated.Text = body.TryGetProperty("receivedAt", out var time) && time.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(time.GetString(), out var date)
                ? $"Settings read {date.ToLocalTime():g}" : "No settings read";
            if (!body.GetProperty("available").GetBoolean() || !body.TryGetProperty("settings", out settings) || settings.ValueKind != JsonValueKind.Object)
            {
                fields.Controls.Clear(); ShowError(body.TryGetProperty("reason", out var reason) ? reason.GetString() ?? "Settings unavailable" : "Settings unavailable");
                return;
            }
            foreach (var change in confirmedChanges.ToArray())
            {
                if (DateTimeOffset.UtcNow - change.Value.At > TimeSpan.FromMinutes(2) ||
                    (settings.TryGetProperty(change.Key, out var observed) &&
                     (change.Value.Value is bool expectedBool && observed.ValueKind is JsonValueKind.True or JsonValueKind.False && observed.GetBoolean() == expectedBool ||
                      change.Value.Value is int expectedInt && observed.ValueKind == JsonValueKind.Number && observed.GetInt32() == expectedInt)))
                    confirmedChanges.Remove(change.Key);
            }
            RenderSettings(); ShowMessage("Settings are ready. Changes require device confirmation.");
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            ShowError(error.Message);
        }
        finally { busy = false; }
    }

    private async Task LoginAsync()
    {
        if (busy || string.IsNullOrWhiteSpace(password.Text)) return;
        busy = true;
        try
        {
            var result = await RequestAsync("/api/control/login", new { password = password.Text });
            if ((int)result.Status >= 300) throw new InvalidOperationException(Error(result.Body, result.Status));
            SecretStore.Save(password.Text);
            password.Text = "";
            triedSavedPassword = true;
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            ShowError(error.Message); return;
        }
        finally { busy = false; }
        await RefreshAsync();
    }

    private async Task SendAsync(Field field, object value)
    {
        if (busy) return;
        if (field.Key is "acOutput" or "dcOutput" && value is false)
        {
            dialogOpen = true;
            try
            {
                if (MessageBox.Show(this, $"Turn off {field.Name}? Anything plugged into these sockets will lose power.", "Confirm output change", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            }
            finally { dialogOpen = false; }
        }
        busy = true; fields.Enabled = false;
        ShowMessage($"Sending {field.Name.ToLowerInvariant()} to the battery…");
        try
        {
            var result = await RequestAsync("/api/control/command", new { setting = field.Key, value });
            if (result.Status == HttpStatusCode.Unauthorized)
            {
                signIn.Visible = true; fields.Visible = false; tabs.Visible = false;
                throw new InvalidOperationException("Session expired. Sign in again.");
            }
            if ((int)result.Status >= 300) throw new InvalidOperationException(Error(result.Body, result.Status));
            if (!result.Body.TryGetProperty("confirmed", out var confirmed) || confirmed.ValueKind != JsonValueKind.True)
                throw new InvalidOperationException("The new state could not be confirmed. Check BrightEMS before trying again.");
            confirmedChanges[field.Key] = (value, DateTimeOffset.UtcNow);
            RenderSettings();
            ShowMessage($"{field.Name} confirmed. Saved readings may take a minute to update.");
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            ShowError(error.Message);
        }
        finally { busy = false; fields.Enabled = true; }
    }

    private void RenderSettings()
    {
        fields.SuspendLayout(); fields.Controls.Clear();
        var group = Groups[selectedGroup];
        int contentHeight = 30 + group.Fields.Length * 63;
        Height = 170 + contentHeight + 58;
        fields.Height = contentHeight;
        message.Top = Height - 50;
        if (Visible) PositionBottomRight();
        var heading = new Label { Text = group.Name.ToUpperInvariant(), Font = new Font("Segoe UI Semibold", 10), ForeColor = Color.FromArgb(187, 245, 147), Width = 374, Height = 30, Margin = new Padding(0), Padding = new Padding(4, 7, 0, 0) };
        fields.Controls.Add(heading);
        foreach (var field in group.Fields) fields.Controls.Add(Row(field));
        for (int i = 0; i < tabButtons.Length; i++)
        {
            tabButtons[i].BackColor = i == selectedGroup ? Color.FromArgb(54, 92, 65) : Color.FromArgb(43, 62, 66);
            tabButtons[i].ForeColor = i == selectedGroup ? Color.FromArgb(220, 255, 205) : Color.White;
        }
        fields.ResumeLayout();
    }

    private void SelectGroup(int index)
    {
        selectedGroup = index;
        if (settings.ValueKind == JsonValueKind.Object) RenderSettings();
    }

    internal void PreviewSettings()
    {
        using var json = JsonDocument.Parse("""{"acOutput":true,"dcOutput":false,"usbOutput":true,"ledMode":1,"acChargingRate":2,"dcInputType":0,"dcInputMaxCurrent":20,"dcInputCurrent":10,"chargeLimit":90,"dischargeLimit":10,"silentCharging":false,"buzzer":true,"usbStandbyTime":5,"acStandbyTime":480,"dcStandbyTime":480,"screenRestTime":180,"sleepTime":30}""");
        settings = json.RootElement.Clone();
        signIn.Visible = false; fields.Visible = true; tabs.Visible = true;
        updated.Text = "Settings read just now";
        RenderSettings();
        ShowMessage("Settings are ready. Changes require device confirmation.");
    }

    private Panel Row(Field field)
    {
        var row = new Panel { Width = 374, Height = 59, BackColor = Color.FromArgb(28, 44, 50), Margin = new Padding(0, 0, 0, 4) };
        row.HandleCreated += (_, _) => RoundedWindow.Apply(row, 8);
        var name = new Label { Text = field.Name, Font = new Font("Segoe UI Semibold", 9), Bounds = new Rectangle(13, 9, 180, 22) };
        row.Controls.Add(name);
        var help = new Label { Text = "ⓘ", ForeColor = Color.FromArgb(169, 195, 194), Bounds = new Rectangle(179, 10, 18, 22), Cursor = Cursors.Help };
        new ToolTip { AutoPopDelay = 12000, InitialDelay = 200 }.SetToolTip(help, field.Help);
        row.Controls.Add(help);
        var hasValue = settings.TryGetProperty(field.Key, out var raw) && raw.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
        confirmedChanges.TryGetValue(field.Key, out var confirmedChange);
        if (field.Kind == "toggle")
        {
            bool current = confirmedChange.Value is bool boolValue ? boolValue : hasValue && raw.GetBoolean();
            var toggle = Button(hasValue ? current ? "On" : "Off" : "—", 283, 14, 77, 34, () => _ = SendAsync(field, !current));
            toggle.Enabled = hasValue;
            if (current) { toggle.BackColor = Color.FromArgb(54, 92, 65); toggle.ForeColor = Color.FromArgb(220, 255, 205); }
            row.Controls.Add(toggle);
        }
        else
        {
            int current = confirmedChange.Value is int intValue ? intValue : hasValue ? raw.GetInt32() : 0;
            Control input;
            Func<int> selected;
            if (field.Kind == "select")
            {
                var combo = new ComboBox { Bounds = new Rectangle(195, 14, 102, 32), DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(15, 29, 36), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, DrawMode = DrawMode.OwnerDrawFixed };
                combo.DrawItem += (_, e) =>
                {
                    if (e.Index < 0) return;
                    using var brush = new SolidBrush(Color.FromArgb(15, 29, 36));
                    e.Graphics.FillRectangle(brush, e.Bounds);
                    TextRenderer.DrawText(e.Graphics, combo.Items[e.Index]?.ToString() ?? "", combo.Font, e.Bounds, Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                };
                foreach (var option in field.Options!) combo.Items.Add(new Choice(option.Value, option.Name));
                if (hasValue && !field.Options!.Any(o => o.Value == current)) combo.Items.Add(new Choice(current, $"{current} current"));
                combo.SelectedIndex = hasValue ? Math.Max(0, combo.Items.Cast<Choice>().ToList().FindIndex(o => o.Value == current)) : -1;
                selected = () => (combo.SelectedItem as Choice)?.Value ?? current;
                input = combo;
            }
            else
            {
                int max = field.Key == "dcInputCurrent" && settings.TryGetProperty("dcInputMaxCurrent", out var maxValue) && maxValue.ValueKind == JsonValueKind.Number ? Math.Min(field.Max, maxValue.GetInt32()) : field.Max;
                var number = new NumericUpDown { Bounds = new Rectangle(220, 14, 77, 32), Minimum = field.Min, Maximum = Math.Max(field.Min, max), Value = Math.Clamp(current, field.Min, Math.Max(field.Min, max)), BackColor = Color.FromArgb(15, 29, 36), ForeColor = Color.White };
                selected = () => (int)number.Value;
                input = number;
            }
            input.Enabled = hasValue; row.Controls.Add(input);
            var save = Button("Save", 306, 14, 54, 32, () => _ = SendAsync(field, selected()));
            save.Enabled = hasValue; row.Controls.Add(save);
        }
        return row;
    }

    private sealed record Choice(int Value, string Name)
    {
        public override string ToString() => Name;
    }
}
