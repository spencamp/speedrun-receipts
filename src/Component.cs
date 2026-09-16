using System;
using System.Drawing;
using System.Drawing.Printing;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using LiveSplit.Model;
using LiveSplit.UI;
using LiveSplit.UI.Components;

[assembly: ComponentFactory(typeof(LiveSplit.ThermalReceipt.ReceiptFactory))]
[assembly: System.Reflection.AssemblyVersion("1.1.0.0")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8.1")]

namespace LiveSplit.ThermalReceipt
{
    public sealed class ReceiptFactory : IComponentFactory
    {
        public string ComponentName { get { return "Thermal Run Receipt"; } }
        public string Description { get { return "Print a thermal receipt when an attempt completes."; } }
        public ComponentCategory Category { get { return ComponentCategory.Other; } }
        public string UpdateName { get { return ComponentName; } }
        public string XMLURL { get { return ""; } }
        public string UpdateURL { get { return ""; } }
        public Version Version { get { return new Version(1, 1, 0); } }
        public IComponent Create(LiveSplitState state) { return new ReceiptComponent(state); }
    }
    public sealed class ReceiptSettings : UserControl
    {
        private readonly CheckBox enabled = new CheckBox { Text = "Enable receipt printing", AutoSize = true };
        private readonly CheckBox confirm = new CheckBox { Text = "Confirm before printing completed runs", AutoSize = true };
        private readonly CheckBox fortunes = new CheckBox { Text = "Print fortunes", AutoSize = true, Checked = true };
        private readonly ComboBox queue = new ComboBox { Width = 330, DropDownStyle = ComboBoxStyle.DropDown };
        private readonly ComboBox profile = new ComboBox { Width = 330, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly FlowLayoutPanel custom = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        private readonly NumericUpDown dots = new NumericUpDown { Minimum = 192, Maximum = 832, Value = 384 };
        private readonly NumericUpDown fontA = new NumericUpDown { Minimum = 24, Maximum = 96, Value = 32 };
        private readonly NumericUpDown fontB = new NumericUpDown { Minimum = 32, Maximum = 128, Value = 42 };
        private readonly CheckBox reverse = new CheckBox { Text = "Supports reverse text", AutoSize = true };
        private readonly CheckBox images = new CheckBox { Text = "Supports standalone 24-dot borders", AutoSize = true };
        private readonly CheckBox cutter = new CheckBox { Text = "Supports ESC/POS cutter (GS V 0)", AutoSize = true };
        private readonly CheckBox cut = new CheckBox { Text = "Cut after receipt", AutoSize = true };
        private readonly Label notes = new Label { AutoSize = true, MaximumSize = new Size(350, 0) };
        private readonly Button test = new Button { Text = "Print Test Receipt", AutoSize = true, Enabled = false };
        private readonly Label status = new Label { AutoSize = true, MaximumSize = new Size(360, 0) };
        public readonly FortuneBag Fortunes = new FortuneBag();
        public event Action TestRequested;
        public event Action HistoryRequested;
        public event Action<int, TimingMethod> ArchiveRequested;
        private readonly ListBox history = new ListBox { Width = 350, Height = 130 };
        private readonly ComboBox archiveMethod = new ComboBox { Width = 330, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly Button archivePrint = new Button { Text = "Print Selected Receipt", AutoSize = true, Enabled = false };
        private Attempt[] archiveAttempts = new Attempt[0];
        public TimingMethod ArchiveMethod { get { return archiveMethod.SelectedIndex == 1 ? TimingMethod.GameTime : TimingMethod.RealTime; } }
        public void SetHistory(Attempt[] attempts)
        {
            archiveAttempts = attempts; history.Items.Clear();
            foreach (var a in attempts) history.Items.Add((a.Ended.HasValue ? a.Ended.Value.Time.ToLocalTime().ToString("MMM dd yyyy h:mm tt") : "Date unknown")
                + "   " + Format.Time(a.Time[ArchiveMethod], 2) + "   #" + a.Index);
            UpdateArchiveButton();
        }
        private void UpdateArchiveButton() { archivePrint.Enabled = history.SelectedIndex >= 0 && !String.IsNullOrWhiteSpace(Queue); }
        public bool PrintingEnabled { get { return enabled.Checked; } set { enabled.Checked = value; } }
        public bool ConfirmBeforePrinting { get { return confirm.Checked; } set { confirm.Checked = value; } }
        public bool PrintFortunes { get { return fortunes.Checked; } set { fortunes.Checked = value; } }
        public string Queue { get { return queue.Text; } set { queue.Text = value ?? ""; } }
        public string ProfileId
        {
            get { return ((PrinterProfile)profile.SelectedItem).Id; }
            set { profile.SelectedItem = PrinterProfiles.All.FirstOrDefault(p => p.Id == value) ?? PrinterProfiles.Pos58; }
        }
        public PrinterProfile SelectedProfile
        {
            get { return ProfileId == "custom" ? new PrinterProfile("custom", "Custom", (int)dots.Value, (int)fontA.Value, (int)fontB.Value, reverse.Checked, images.Checked, cutter.Checked) : (PrinterProfile)profile.SelectedItem; }
        }
        public bool CutAfterReceipt { get { return ProfileId == "custom" && cutter.Checked && cut.Checked; } }
        public void ShowStatus(string value) { if (status.Text != value) status.Text = value; }
        public ReceiptSettings()
        {
            Size = new Size(390, 600);
            var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(8) };
            var refresh = new Button { Text = "Refresh printer queues", AutoSize = true };
            layout.Controls.Add(enabled); layout.Controls.Add(confirm); layout.Controls.Add(fortunes); layout.Controls.Add(new Label { Text = "Windows printer queue", AutoSize = true });
            layout.Controls.Add(queue); layout.Controls.Add(refresh);
            layout.Controls.Add(new Label { Text = "Select an ESC/POS receipt printer, not an office printer.", AutoSize = true, MaximumSize = new Size(350, 0) });
            layout.Controls.Add(new Label { Text = "Printer profile", AutoSize = true }); layout.Controls.Add(profile); layout.Controls.Add(notes);
            layout.Controls.Add(new Label { Text = "Warning: Choosing the wrong printer profile may result in a lot of wasted paper. Check your printer's capabilities before using Test Receipt.", AutoSize = true, MaximumSize = new Size(350, 0) });
            foreach (var field in new[] { Tuple.Create("Printable width (dots)", dots), Tuple.Create("Font A columns", fontA), Tuple.Create("Font B columns", fontB) })
            { custom.Controls.Add(new Label { Text = field.Item1, AutoSize = true }); custom.Controls.Add(field.Item2); }
            custom.Controls.Add(reverse); custom.Controls.Add(images); custom.Controls.Add(cutter); custom.Controls.Add(cut);
            layout.Controls.Add(custom); layout.Controls.Add(test); layout.Controls.Add(status); Controls.Add(layout);
            layout.Controls.Add(new Label { Text = "ARCHIVE / RECEIPT HISTORY", AutoSize = true });
            layout.Controls.Add(new Label { Text = "Choose stored timing data (original selection is not saved).", AutoSize = true, MaximumSize = new Size(350, 0) });
            archiveMethod.Items.AddRange(new object[] { "REAL TIME", "GAME TIME" }); archiveMethod.SelectedIndex = 0;
            layout.Controls.Add(archiveMethod); layout.Controls.Add(history);
            var refreshHistory = new Button { Text = "Refresh completed attempts", AutoSize = true };
            layout.Controls.Add(refreshHistory); layout.Controls.Add(archivePrint);
            layout.Controls.Add(new Label { Text = "Reconstructed from retained history. Edited/imported records may be incomplete; unknown values are omitted. Attempts appear after LiveSplit saves them on reset.", AutoSize = true, MaximumSize = new Size(350, 0) });
            archiveMethod.SelectedIndexChanged += delegate { if (HistoryRequested != null) HistoryRequested(); };
            refreshHistory.Click += delegate { if (HistoryRequested != null) HistoryRequested(); };
            history.SelectedIndexChanged += delegate { UpdateArchiveButton(); };
            archivePrint.Click += delegate { if (archivePrint.Enabled && ArchiveRequested != null) ArchiveRequested(archiveAttempts[history.SelectedIndex].Index, ArchiveMethod); };
            profile.Items.AddRange(PrinterProfiles.All);
            profile.SelectedIndexChanged += delegate { custom.Visible = ProfileId == "custom"; notes.Text = ((PrinterProfile)profile.SelectedItem).Notes; };
            ProfileId = "pos58";
            fontA.ValueChanged += delegate { if (fontB.Value < fontA.Value) fontB.Value = fontA.Value; };
            fontB.ValueChanged += delegate { if (fontB.Value < fontA.Value) fontA.Value = fontB.Value; };
            cutter.CheckedChanged += delegate { cut.Enabled = cutter.Checked; if (!cutter.Checked) cut.Checked = false; };
            cut.Enabled = false;
            queue.TextChanged += delegate { test.Enabled = !String.IsNullOrWhiteSpace(Queue); UpdateArchiveButton(); };
            // Enumerate only when settings are shown or the user requests refresh.
            Load += delegate { RefreshQueues(); }; refresh.Click += delegate { RefreshQueues(); };
            test.Click += delegate { if (!String.IsNullOrWhiteSpace(Queue) && TestRequested != null) TestRequested(); };
        }
        public void SetAvailableQueues(System.Collections.Generic.IEnumerable<string> queues)
        {
            // Materialize before mutating the UI, so enumeration failures retain the selection.
            string[] available = queues.ToArray();
            string selected = Queue; queue.Items.Clear(); queue.Items.AddRange(available); Queue = selected;
        }
        private void RefreshQueues()
        {
            try { SetAvailableQueues(PrinterSettings.InstalledPrinters.Cast<string>()); }
            catch (Exception ex) { ShowStatus("Printer enumeration failed: " + ex.Message); Log.Write(ex.ToString()); }
        }
        public XmlNode Save(XmlDocument document)
        {
            var root = document.CreateElement("Settings");
            foreach (var item in new[] { new[] { "Version", "2" }, new[] { "Enabled", PrintingEnabled.ToString() }, new[] { "PrinterQueue", Queue }, new[] { "FortuneBag", Fortunes.Save() },
                new[] { "ConfirmBeforePrinting", ConfirmBeforePrinting.ToString() },
                new[] { "PrintFortunes", PrintFortunes.ToString() },
                new[] { "PrinterProfile", ProfileId }, new[] { "PrintableDots", dots.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                new[] { "FontAColumns", fontA.Value.ToString() }, new[] { "FontBColumns", fontB.Value.ToString() },
                new[] { "SupportsReverse", reverse.Checked.ToString() }, new[] { "Supports24DotImages", images.Checked.ToString() },
                new[] { "SupportsCut", cutter.Checked.ToString() }, new[] { "CutAfterReceipt", cut.Checked.ToString() } })
            { var node = document.CreateElement(item[0]); node.InnerText = item[1]; root.AppendChild(node); }
            return root;
        }
        public void Restore(XmlNode root)
        {
            bool value; PrintingEnabled = Boolean.TryParse(Read(root, "Enabled"), out value) && value;
            ConfirmBeforePrinting = Flag(root, "ConfirmBeforePrinting");
            PrintFortunes = !Boolean.TryParse(Read(root, "PrintFortunes"), out value) || value;
            Queue = Read(root, "PrinterQueue") ?? ""; Fortunes.Load(Read(root, "FortuneBag"));
            ProfileId = Read(root, "PrinterProfile");
            dots.Value = Number(root, "PrintableDots", 384, 192, 832);
            fontA.Value = Number(root, "FontAColumns", 32, 24, 96);
            fontB.Value = Math.Max(fontA.Value, Number(root, "FontBColumns", 42, 32, 128));
            reverse.Checked = Flag(root, "SupportsReverse"); images.Checked = Flag(root, "Supports24DotImages");
            cutter.Checked = Flag(root, "SupportsCut"); cut.Checked = cutter.Checked && Flag(root, "CutAfterReceipt");
            // Unknown future profile IDs must not silently enable a different command set.
            string savedProfile = Read(root, "PrinterProfile");
            if (!String.IsNullOrEmpty(savedProfile) && !PrinterProfiles.All.Any(p => p.Id == savedProfile))
            { ProfileId = "generic58"; PrintingEnabled = false; ShowStatus("Unknown printer profile. Select a profile and test before enabling printing."); }
        }
        private static bool Flag(XmlNode root, string name) { bool value; return Boolean.TryParse(Read(root, name), out value) && value; }
        private static int Number(XmlNode root, string name, int fallback, int min, int max)
        { int value; return Int32.TryParse(Read(root, name), out value) && value >= min && value <= max ? value : fallback; }
        private static string Read(XmlNode root, string name) { var node = root == null ? null : root.SelectSingleNode(name); return node == null ? null : node.InnerText; }
    }
    public sealed class ReceiptComponent : LogicComponent
    {
        private readonly ReceiptSettings settings = new ReceiptSettings();
        private readonly PrintDispatcher dispatcher;
        private readonly RunCapture capture;
        private readonly Func<bool> confirmPrint;
        private readonly LiveSplitState state;
        private readonly HistoricalReceiptReconstructor history = new HistoricalReceiptReconstructor();
        private IRun listedRun;
        private bool disposed;
        public ReceiptComponent(LiveSplitState state) : this(state, new WindowsRawPrinter()) { }
        public ReceiptComponent(LiveSplitState state, IReceiptPrinter printer) : this(state, printer, ConfirmPrint) { }
        public ReceiptComponent(LiveSplitState state, IReceiptPrinter printer, Func<bool> confirmPrint)
        {
            if (confirmPrint == null) throw new ArgumentNullException("confirmPrint");
            this.confirmPrint = confirmPrint;
            this.state = state;
            dispatcher = new PrintDispatcher(printer);
            capture = new RunCapture(state, () => settings.PrintingEnabled && !String.IsNullOrWhiteSpace(settings.Queue), () => settings.PrintFortunes ? settings.Fortunes.Next() : "", PrintCompletedRun);
            settings.TestRequested += Test;
            settings.HistoryRequested += RefreshHistory; settings.ArchiveRequested += PrintArchive;
        }
        private void RefreshHistory()
        {
            try { settings.SetHistory(history.Completed(state.Run, settings.ArchiveMethod)); listedRun = state.Run; }
            catch (Exception ex) { settings.SetHistory(new Attempt[0]); settings.ShowStatus("History unavailable: " + ex.Message); Log.Write(ex.ToString()); }
        }
        public void PrintArchive(int attempt, TimingMethod method)
        {
            try
            {
                if (disposed || String.IsNullOrWhiteSpace(settings.Queue)) throw new InvalidOperationException("Select a printer queue first.");
                if (!ReferenceEquals(listedRun, state.Run)) { RefreshHistory(); throw new InvalidOperationException("The loaded run changed. Select an attempt from the refreshed list."); }
                // Validate/reconstruct before consuming a fortune; never touch automatic completion claims.
                var run = history.Reconstruct(state.Run, attempt, method, DateTime.Now, "");
                run = new ReceiptRun(run.Game, run.Category, run.Final, run.PreviousPB, run.PreviousBest, run.NewBest,
                    run.Comparison, run.TimingMethod, run.Attempt, run.Finished, run.Splits, run.Recent,
                    settings.PrintFortunes ? settings.Fortunes.Next() : "", true, run.Reprinted, run.RunDateKnown, run.HistoricalPbKnown);
                dispatcher.Submit(settings.Queue, new ReceiptRenderer(settings.SelectedProfile, settings.CutAfterReceipt, settings.PrintFortunes).Render(run));
                settings.ShowStatus(dispatcher.Status);
            }
            catch (Exception ex) { settings.ShowStatus("Archive receipt failed: " + ex.Message); Log.Write(ex.ToString()); }
        }
        private static bool ConfirmPrint()
        {
            return MessageBox.Show(Form.ActiveForm, "Would you like to print your Run Receipt?", "Run Receipt",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }
        private void PrintCompletedRun(ReceiptRun run)
        {
            // Capture queue and rendering settings before the modal dialog can pump UI events.
            // RunCapture has already frozen the run and claimed this completion.
            string queue = settings.Queue;
            var renderer = new ReceiptRenderer(settings.SelectedProfile, settings.CutAfterReceipt, settings.PrintFortunes);
            if (settings.ConfirmBeforePrinting && !confirmPrint())
            { dispatcher.Status = "Run receipt skipped"; return; }
            if (!disposed) dispatcher.Submit(queue, renderer.Render(run));
        }
        private void Test()
        {
            try { dispatcher.Submit(settings.Queue, new ReceiptRenderer(settings.SelectedProfile, settings.CutAfterReceipt, settings.PrintFortunes).Render(SyntheticReceipt.Create())); settings.ShowStatus(dispatcher.Status); }
            catch (Exception ex) { settings.ShowStatus("Test receipt failed: " + ex.Message); Log.Write(ex.ToString()); }
        }
        public override string ComponentName { get { return "Thermal Run Receipt"; } }
        public override Control GetSettingsControl(LayoutMode mode) { RefreshHistory(); return settings; }
        public override XmlNode GetSettings(XmlDocument document) { return settings.Save(document); }
        public override void SetSettings(XmlNode node) { try { settings.Restore(node); } catch (Exception ex) { Log.Write("Settings error " + ex); } }
        public override void Update(IInvalidator invalidator, LiveSplitState state, float width, float height, LayoutMode mode)
        { if (!disposed) settings.ShowStatus(dispatcher.Status); }
        public override void Dispose() { if (disposed) return; disposed = true; capture.Dispose(); settings.TestRequested -= Test; settings.Dispose(); }
    }
}
