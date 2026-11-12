using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace BatteryWatch;

internal sealed class FarmControlsForm : Form
{
    private sealed record Choice(int Value, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed record Setting(string Kind, int? Port, string Key, string Label, string Current,
        string Category, string Group, string? Unit, string[] Options, decimal? Min, decimal? Max, bool DeviceOnline);

    private static readonly (string Key, string Name)[] Categories =
    [
        ("all", "All settings"), ("generator", "Generator & AC"), ("battery", "Battery & charging"),
        ("inverter", "Inverter"), ("solar", "Solar controllers"), ("system", "System & schedules")
    ];

    private readonly HttpClient client;
    private readonly Panel signIn = new();
    private readonly Panel content = new();
    private readonly TextBox password = new();
    private readonly TextBox search = new();
    private readonly ComboBox category = new();
    private readonly FlowLayoutPanel rows = new();
    private readonly Label updated = new();
    private readonly Label message = new();
    private readonly Button refresh;
    private readonly Button logout;
    private JsonElement settings;
    private bool busy;
    private bool signedIn;
    private bool requireRefresh;
    private bool dialogOpen;

    public FarmControlsForm(HttpClient client)
    {
        this.client = client;
        Width = 624; Height = 690;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false; TopMost = true;
        BackColor = Color.FromArgb(19, 29, 37); ForeColor = Color.FromArgb(238, 247, 245);
        Font = new Font("Segoe UI", 9);
        DoubleBuffered = true;

        var top = new Panel { Bounds = new Rectangle(0, 0, 624, 72), BackColor = Color.FromArgb(26, 45, 51) };
        top.Controls.Add(new Label { Text = "BATTERY WATCH  /  FARM SETTINGS", Font = new Font("Segoe UI Semibold", 12), Bounds = new Rectangle(23, 24, 470, 28) });
        top.Controls.Add(Button("×", 568, 19, 31, 31, Hide));
        Controls.Add(top);
        updated.Bounds = new Rectangle(23, 85, 390, 26); updated.ForeColor = Color.FromArgb(154, 178, 180);
        Controls.Add(updated);
        refresh = Button("Refresh", 434, 82, 80, 31, () => _ = RefreshAsync());
        logout = Button("Sign out", 520, 82, 79, 31, () => _ = LogoutAsync());
        Controls.AddRange([refresh, logout]);

        signIn.Bounds = new Rectangle(23, 160, 576, 210);
        signIn.Controls.Add(new Label { Text = "Sign in to change farm settings", Font = new Font("Segoe UI Semibold", 16), Bounds = new Rectangle(0, 0, 550, 37) });
        signIn.Controls.Add(new Label { Text = "Use the separate farm control password. Your sign-in lasts for this app session.", ForeColor = Color.FromArgb(154, 178, 180), Bounds = new Rectangle(0, 47, 560, 28) });
        password.UseSystemPasswordChar = true; password.Bounds = new Rectangle(0, 92, 353, 32);
        password.BackColor = Color.FromArgb(14, 25, 31); password.ForeColor = Color.White;
        password.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; _ = LoginAsync(); } };
        signIn.Controls.Add(password);
        signIn.Controls.Add(Button("Sign in", 366, 92, 112, 32, () => _ = LoginAsync()));
        Controls.Add(signIn);

        content.Bounds = new Rectangle(23, 130, 576, 490);
        content.Controls.Add(new Label { Text = "Find a setting", Bounds = new Rectangle(0, 0, 120, 22) });
        search.Bounds = new Rectangle(0, 24, 342, 30); search.BackColor = Color.FromArgb(14, 25, 31); search.ForeColor = Color.White;
        search.TextChanged += (_, _) => RenderSettings();
        content.Controls.Add(search);
        category.Bounds = new Rectangle(354, 24, 222, 30); category.DropDownStyle = ComboBoxStyle.DropDownList;
        category.BackColor = Color.FromArgb(14, 25, 31); category.ForeColor = Color.White;
        foreach (var item in Categories) category.Items.Add(item.Name);
        category.SelectedIndex = 0;
        category.SelectedIndexChanged += (_, _) => RenderSettings();
        content.Controls.Add(category);
        rows.Bounds = new Rectangle(0, 66, 576, 424); rows.FlowDirection = FlowDirection.TopDown;
        rows.WrapContents = false; rows.AutoScroll = true; rows.BackColor = BackColor;
        content.Controls.Add(rows); Controls.Add(content);

        message.Bounds = new Rectangle(23, 626, 570, 47);
        message.ForeColor = Color.FromArgb(179, 199, 198); Controls.Add(message);
        ShowSignedIn(false);
        Resize += (_, _) => RoundedWindow.Apply(this);
        HandleCreated += (_, _) => RoundedWindow.Apply(this);
        Deactivate += (_, _) => BeginInvoke(new Action(() => { if (!dialogOpen && !ContainsFocus) Hide(); }));
    }

    private static Button Button(string text, int x, int y, int width, int height, Action click)
    {
        var button = new Button { Text = text, Bounds = new Rectangle(x, y, width, height), FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(48, 68, 72), ForeColor = Color.White, Cursor = Cursors.Hand };
        button.FlatAppearance.BorderSize = 0;
        button.Click += (_, _) => click();
        return button;
    }

    private static JsonElement Get(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var found) ? found : default;

    private static IEnumerable<JsonElement> Items(JsonElement value) =>
        value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];

    private static string ValueText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? "",
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => ""
    };

    private static bool IsTrue(JsonElement value) => value.ValueKind == JsonValueKind.True;
    private static int Number(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n) ? n : 0;
    private static decimal? Decimal(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var n) ? n : null;

    private void ShowSignedIn(bool value)
    {
        signedIn = value; signIn.Visible = !value; content.Visible = value; logout.Visible = value;
    }

    private void ShowMessage(string text, bool error = false)
    {
        message.ForeColor = error ? Color.FromArgb(246, 167, 162) : Color.FromArgb(179, 199, 198);
        message.Text = text;
    }

    public void PositionBottomRight()
    {
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Right - Width - 15, area.Bottom - Height - 12);
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

    private static string Error(JsonElement body, HttpStatusCode status) =>
        ValueText(Get(body, "error")) is { Length: > 0 } error ? error : $"Request failed ({(int)status})";

    public async Task RefreshAsync()
    {
        if (busy) return;
        busy = true; refresh.Enabled = false;
        ShowMessage("Reading current farm settings…");
        try
        {
            var result = await RequestAsync("/api/farm-control/settings");
            if (result.Status == HttpStatusCode.Unauthorized)
            {
                settings = default; ShowSignedIn(false); updated.Text = "";
                ShowMessage("Sign in to view farm settings.");
                return;
            }
            if ((int)result.Status >= 300) throw new InvalidOperationException(Error(result.Body, result.Status));
            settings = result.Body; requireRefresh = false; ShowSignedIn(true);
            var readAt = ValueText(Get(settings, "readAt"));
            var online = IsTrue(Get(settings, "online"));
            var pending = Number(Get(settings, "pending"));
            updated.Text = DateTimeOffset.TryParse(readAt, out var date)
                ? $"Read {date.ToLocalTime():g} · {(online ? "Online" : "Offline")}{(pending > 0 ? $" · {pending} pending" : "")}" : "Farm settings read";
            RenderSettings();
            ShowMessage(!online ? "Farm gateway is offline. Settings are view only." : pending > 0
                ? "OpticsRE has pending changes. Refresh before sending another."
                : "Review each change before sending it to OpticsRE.", !online);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            ShowMessage(error.Message, true);
        }
        finally { busy = false; refresh.Enabled = true; }
    }

    private async Task LoginAsync()
    {
        if (busy || string.IsNullOrWhiteSpace(password.Text)) return;
        busy = true;
        try
        {
            var result = await RequestAsync("/api/farm-control/login", new { password = password.Text });
            if ((int)result.Status >= 300) throw new InvalidOperationException(Error(result.Body, result.Status));
            password.Clear(); ShowSignedIn(true);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            ShowMessage(error.Message, true); return;
        }
        finally { busy = false; }
        await RefreshAsync();
    }

    private async Task LogoutAsync()
    {
        if (busy) return;
        busy = true;
        try
        {
            var result = await RequestAsync("/api/farm-control/logout", new { });
            if ((int)result.Status >= 300) throw new InvalidOperationException(Error(result.Body, result.Status));
            settings = default; requireRefresh = false; rows.Controls.Clear();
            ShowSignedIn(false); updated.Text = ""; ShowMessage("Signed out of farm controls.");
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            ShowMessage(error.Message, true);
        }
        finally { busy = false; }
    }

    private bool CanChange => signedIn && !busy && !requireRefresh && IsTrue(Get(settings, "online")) && Number(Get(settings, "pending")) == 0;

    private static string CategoryFor(string model, string tab, string label)
    {
        if (model.StartsWith("FM", StringComparison.OrdinalIgnoreCase)) return "solar";
        if (tab is "AGS" or "AC Input") return "generator";
        if (tab is "Battery Charging" or "Global Charge" or "HBX Mode") return "battery";
        if (tab == "Modes") return label.Contains("charg", StringComparison.OrdinalIgnoreCase) || label.Contains("battery", StringComparison.OrdinalIgnoreCase) ? "battery" : "inverter";
        if (model.Contains("FX", StringComparison.OrdinalIgnoreCase)) return "inverter";
        return "system";
    }

    private IEnumerable<Setting> AllSettings()
    {
        foreach (var device in Items(Get(settings, "devices")))
        {
            var model = ValueText(Get(device, "model"));
            var port = Number(Get(device, "port"));
            var online = IsTrue(Get(device, "online"));
            foreach (var tab in Items(Get(device, "tabs")))
            foreach (var section in Items(Get(tab, "subsections")))
            foreach (var attribute in Items(Get(section, "attributes")))
            {
                var label = ValueText(Get(attribute, "label"));
                var tabName = ValueText(Get(tab, "name"));
                var options = Items(Get(attribute, "options")).Select(option => ValueText(Get(option, "text"))).Where(option => option.Length > 0).ToArray();
                var range = Get(attribute, "range");
                yield return new Setting("setting", port, ValueText(Get(attribute, "key")), label, ValueText(Get(attribute, "current")),
                    CategoryFor(model, tabName, label), $"{tabName} · {model} · port {port}", ValueText(Get(attribute, "unit")), options,
                    Decimal(Get(range, "min")), Decimal(Get(range, "max")), online);
            }
        }
        foreach (var attribute in Items(Get(settings, "setup")))
        {
            var range = Get(attribute, "range");
            yield return new Setting("system", null, ValueText(Get(attribute, "key")), ValueText(Get(attribute, "label")), ValueText(Get(attribute, "current")),
                ValueText(Get(attribute, "category")), "System setup", ValueText(Get(attribute, "unit")),
                Items(Get(attribute, "options")).Select(option => ValueText(Get(option, "text"))).Where(option => option.Length > 0).ToArray(),
                Decimal(Get(range, "min")), Decimal(Get(range, "max")), true);
        }
    }

    private void RenderSettings()
    {
        if (settings.ValueKind != JsonValueKind.Object) return;
        rows.SuspendLayout(); rows.Controls.Clear();
        var selectedCategory = Categories[Math.Max(0, category.SelectedIndex)].Key;
        var query = search.Text.Trim();
        if (selectedCategory == "all") RenderQuick();
        var filtered = AllSettings().Where(field => (selectedCategory == "all" || field.Category == selectedCategory)
            && (query.Length == 0 || $"{field.Label} {field.Key} {field.Group}".Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
        string? previous = null;
        foreach (var field in filtered)
        {
            if (field.Group != previous)
            {
                rows.Controls.Add(new Label { Text = field.Group.ToUpperInvariant(), Font = new Font("Segoe UI Semibold", 9),
                    ForeColor = Color.FromArgb(187, 245, 147), Width = 535, Height = 28, Padding = new Padding(5, 8, 0, 0) });
                previous = field.Group;
            }
            rows.Controls.Add(SettingRow(field));
        }
        if (filtered.Length == 0 && selectedCategory != "all") rows.Controls.Add(new Label { Text = "No settings match this search.", Width = 535, Height = 30 });
        rows.ResumeLayout();
    }

    private void RenderQuick()
    {
        var quick = Get(settings, "quick");
        foreach (var (key, label) in new[] { ("generator", "Generator"), ("inverter", "Inverter"), ("acPower", "AC input"), ("battery", "Battery charge") })
        {
            var item = Get(quick, key);
            if (item.ValueKind != JsonValueKind.Object) continue;
            var current = Number(Get(item, "current"));
            var options = Items(Get(item, "options"))
                .Where(option => !IsTrue(Get(option, "isDisabled")))
                .Select(option => new Choice(Number(Get(option, "value")), ValueText(Get(option, "text")))).ToArray();
            var row = RowShell(label, ValueText(Get(item, "status")) is { Length: > 0 } status ? status : $"Current: {current}");
            var combo = new ComboBox { Bounds = new Rectangle(290, 29, 135, 30), DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(14, 25, 31), ForeColor = Color.White };
            combo.Items.AddRange(options.Cast<object>().ToArray());
            combo.SelectedItem = options.FirstOrDefault(option => option.Value == current);
            combo.Enabled = CanChange && !IsTrue(Get(item, "disabled")) && options.Length > 0;
            row.Controls.Add(combo);
            var send = Button("Send", 435, 29, 80, 30, () =>
            {
                if (combo.SelectedItem is Choice choice) _ = SendAsync("quick", null, key, label, current.ToString(CultureInfo.InvariantCulture), choice.Value, choice.Name,
                    options.FirstOrDefault(option => option.Value == current)?.Name);
            });
            send.Enabled = combo.Enabled; row.Controls.Add(send); rows.Controls.Add(row);
        }
    }

    private static Panel RowShell(string label, string detail)
    {
        var row = new Panel { Width = 535, Height = 66, Margin = new Padding(0, 0, 0, 5), BackColor = Color.FromArgb(28, 44, 50) };
        row.HandleCreated += (_, _) => RoundedWindow.Apply(row, 8);
        var title = new Label { Text = label, Font = new Font("Segoe UI Semibold", 9), Bounds = new Rectangle(12, 8, 410, 22), AutoEllipsis = true };
        row.Controls.Add(title);
        row.Controls.Add(new Label { Text = detail, ForeColor = Color.FromArgb(154, 178, 180), Bounds = new Rectangle(12, 35, 270, 23), AutoEllipsis = true });
        return row;
    }

    private Panel SettingRow(Setting field)
    {
        var detail = $"Current: {field.Current}{(string.IsNullOrWhiteSpace(field.Unit) || field.Unit == "Enum" ? "" : $" {field.Unit}")}";
        var row = RowShell(field.Label, detail);
        var editable = CanChange && field.DeviceOnline && (field.Options.Length > 0 || field.Min != null || field.Kind == "system" && field.Key == "name");
        Func<string> value;
        Control input;
        if (field.Options.Length > 0)
        {
            var combo = new ComboBox { Bounds = new Rectangle(290, 29, 135, 30), DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(14, 25, 31), ForeColor = Color.White };
            combo.Items.AddRange(field.Options.Cast<object>().ToArray());
            combo.SelectedItem = field.Options.FirstOrDefault(option => option == field.Current);
            value = () => combo.SelectedItem as string ?? field.Current;
            input = combo;
        }
        else
        {
            var box = new TextBox { Bounds = new Rectangle(290, 29, 135, 30), Text = field.Current,
                BackColor = Color.FromArgb(14, 25, 31), ForeColor = Color.White, MaxLength = field.Kind == "system" && field.Key == "name" ? 80 : 100 };
            value = () => box.Text.Trim(); input = box;
            if (field.Min != null) new ToolTip().SetToolTip(box, $"Allowed: {field.Min} to {field.Max}{(field.Unit is { Length: > 0 } ? $" {field.Unit}" : "")}");
        }
        input.Enabled = editable; row.Controls.Add(input);
        var save = Button("Save", 435, 29, 80, 30, () =>
        {
            var next = value();
            if (field.Min is decimal min)
            {
                if (!decimal.TryParse(next, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || number < min || number > field.Max)
                {
                    ShowMessage($"Choose a value from {field.Min} to {field.Max} for {field.Label}.", true);
                    return;
                }
            }
            _ = SendAsync(field.Kind, field.Port, field.Key, field.Label, field.Current, next, next);
        });
        save.Enabled = editable; row.Controls.Add(save);
        return row;
    }

    private async Task SendAsync(string kind, int? port, string key, string label, string current, object next, string nextText, string? currentText = null)
    {
        if (!CanChange) return;
        if (Convert.ToString(next, CultureInfo.InvariantCulture) == current) { ShowMessage($"{label} already has that value."); return; }
        dialogOpen = true;
        try
        {
            if (MessageBox.Show(this, $"Send {label} change to the farm?\n\nCurrent: {currentText ?? current}\nNew: {nextText}\n\nOpticsRE may queue the request. Refresh to check the actual device state.",
                "Confirm farm change", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        }
        finally { dialogOpen = false; }
        busy = true; rows.Enabled = false;
        ShowMessage($"Sending {label.ToLowerInvariant()} change to OpticsRE…");
        try
        {
            var payload = new Dictionary<string, object> { ["kind"] = kind, ["key"] = key, ["expectedCurrent"] = current, ["value"] = next };
            if (port != null) payload["port"] = port.Value;
            var result = await RequestAsync("/api/farm-control/command", payload);
            if (result.Status == HttpStatusCode.Unauthorized)
            {
                settings = default; ShowSignedIn(false);
                throw new InvalidOperationException("Farm session expired. Sign in again.");
            }
            if ((int)result.Status >= 300) throw new InvalidOperationException(Error(result.Body, result.Status));
            if (!IsTrue(Get(result.Body, "accepted"))) throw new InvalidOperationException("OpticsRE did not accept the change.");
            requireRefresh = true;
            ShowMessage(ValueText(Get(result.Body, "message")) is { Length: > 0 } text ? text : "Request accepted. Refresh to verify the farm device.");
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            ShowMessage(error.Message, true);
        }
        finally { busy = false; rows.Enabled = true; if (settings.ValueKind == JsonValueKind.Object) RenderSettings(); }
    }

    internal void PreviewSettings()
    {
        using var json = JsonDocument.Parse("""
            {"online":true,"pending":0,"readAt":"2026-11-12T12:00:00Z","quick":{"generator":{"status":"Off","current":0,"isDisabled":false,"options":[{"value":0,"text":"Off","isDisabled":false},{"value":1,"text":"On","isDisabled":false}]}} ,"devices":[{"model":"VFX3048E","port":1,"online":true,"tabs":[{"name":"Battery Charging","subsections":[{"name":"Limits","attributes":[{"key":"chargeLimit","label":"Charge limit","current":"54.0","range":{"min":40,"max":62},"unit":"V"}]}]}]}],"setup":[{"key":"name","label":"System name","current":"Farm power","type":"text","category":"system"}]}
            """);
        settings = json.RootElement.Clone(); ShowSignedIn(true);
        updated.Text = "Farm settings preview · Online";
        RenderSettings(); ShowMessage("Review each change before sending it to OpticsRE.");
    }
}
