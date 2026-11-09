using System.Drawing;
using System.Windows.Forms;
using ZmkVolumeFader;

internal static class SetupDialogSmoke
{
    public static void Run(string? imagePath)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { Exercise(imagePath); }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(15))) throw new TimeoutException("Setup UI smoke test timed out.");
        if (failure != null) throw new InvalidOperationException("Setup UI smoke test failed.", failure);
        Console.WriteLine("Setup UI passed: one MIDI device entry, default 14-bit, profile switching, live capture and saved-profile restore.");
    }

    static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }
    static T Find<T>(Control root, string name) where T : Control => (T)root.Controls.Find(name, true).Single();
    static IEnumerable<Button> Buttons(Control parent) => parent.Controls.Cast<Control>()
        .SelectMany(c => (c is Button b ? new[] { b } : Enumerable.Empty<Button>()).Concat(Buttons(c)));
    static void Tick()
    {
        Thread.Sleep(65);
        Application.DoEvents();
    }

    static void Exercise(string? imagePath)
    {
        var theme = new MainForm.Theme(true, Color.FromArgb(35, 37, 43), Color.FromArgb(43, 46, 54),
            Color.FromArgb(21, 23, 27), Color.WhiteSmoke, Color.Silver, Color.FromArgb(70, 224, 122),
            Color.FromArgb(28, 30, 35), Color.FromArgb(58, 61, 68));
        string qmk = FaderSourceLogic.MidiKey("fake-pact", MidiSourceMode.Qmk);
        string cc14 = FaderSourceLogic.WithMidiMode(qmk, MidiSourceMode.Cc14);
        string cc7 = FaderSourceLogic.WithMidiMode(qmk, MidiSourceMode.Cc7);
        var devices = new[]
        {
            new SetupDialog.DeviceOption { Key = "hid", Name = "LiberArk68", Connected = true },
            new SetupDialog.DeviceOption { Key = qmk, Name = "ZodiPact [ABC123]", Connected = true },
            new SetupDialog.DeviceOption { Key = cc14, Name = "ZodiPact [ABC123]", Connected = true },
            new SetupDialog.DeviceOption { Key = cc7, Name = "ZodiPact [ABC123]", Connected = true },
        };
        int value = 0;
        string? monitored = null;
        int[] Raw(string key)
        {
            var values = Enumerable.Repeat(int.MinValue, FaderSourceLogic.Axes(key)).ToArray();
            values[16] = value;
            return values;
        }
        using (var dialog = new SetupDialog(theme, Raw, devices, Array.Empty<SetupDialog.Item>(),
            key => monitored = key, _ => true))
        {
            dialog.Opacity = 0; // Exercise this isolated dialog without showing a test window.
            dialog.Show();
            Application.DoEvents();
            Find<Button>(dialog, "AddPhysical").PerformClick();
            var device = Find<ComboBox>(dialog, "CaptureDevice");
            var profile = Find<ComboBox>(dialog, "CaptureProfile");
            Check(device.Items.Count == 2, "MIDI profiles still appear as separate device rows.");
            device.SelectedIndex = device.Items.Cast<SetupDialog.DeviceOption>().ToList().FindIndex(d => FaderSourceLogic.IsMidi(d.Key));
            Check(profile.Visible && profile.SelectedItem?.ToString() == "14-bit CC" && monitored == cc14,
                "New MIDI device did not default to generic 14-bit CC.");
            Tick();
            value = 13200;
            Tick();
            Check(Find<Button>(dialog, "Capture").Enabled, "14-bit sweep was not captured.");
            Check(Find<Label>(dialog, "CaptureDetail").Height > 30, "Capture instructions have no usable layout height.");
            if (imagePath != null)
            {
                using var image = new Bitmap(dialog.Width, dialog.Height);
                dialog.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                image.Save(Path.GetFullPath(imagePath));
            }
            profile.SelectedIndex = 1;
            Check(monitored == cc7 && !Find<Button>(dialog, "Capture").Enabled, "Changing format did not reset capture and monitoring.");
            profile.SelectedIndex = 2;
            Check(monitored == qmk, "Heartbeat profile did not monitor the saved QMK key.");
            device.SelectedIndex = 0;
            Check(!profile.Visible && monitored == "hid", "HID device exposes or uses a MIDI profile.");
            dialog.Close();
        }
        using (var dialog = new SetupDialog(theme, Raw, devices, new[]
        {
            new SetupDialog.Item { Kind = SetupDialog.ItemKind.Physical, DeviceKey = qmk,
                DeviceName = "ZodiPact", Axis = 16, Min = 0, Max = 13200, SourceIndex = 0 },
        }, key => monitored = key, _ => true))
        {
            dialog.Opacity = 0;
            dialog.Show();
            Application.DoEvents();
            Buttons(dialog).Single(b => b.Text == "Source…").PerformClick();
            Check(Find<ComboBox>(dialog, "CaptureProfile").SelectedIndex == 2 && monitored == qmk,
                "Editing an existing source changed its saved profile to the new default.");
            dialog.Close();
        }
    }
}
