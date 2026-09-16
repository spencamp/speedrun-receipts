using System;
using System.Drawing;
using System.Drawing.Printing;
using System.Windows.Forms;
using System.Xml;
using LiveSplit.Model;
using LiveSplit.UI;
using LiveSplit.UI.Components;

[assembly: ComponentFactory(typeof(LiveSplit.ThermalReceipt.ReceiptFactory))]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
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
        public Version Version { get { return new Version(1, 0, 0); } }
        public IComponent Create(LiveSplitState state) { return new ReceiptComponent(state); }
    }
    public sealed class ReceiptSettings : UserControl
    {
        private readonly CheckBox enabled = new CheckBox { Text = "Enable receipt printing", AutoSize = true };
        private readonly ComboBox queue = new ComboBox { Width = 330, DropDownStyle = ComboBoxStyle.DropDown };
        private readonly Label status = new Label { AutoSize = true, MaximumSize = new Size(360, 0) };
        public readonly FortuneBag Fortunes = new FortuneBag();
        public event Action TestRequested;
        public bool PrintingEnabled { get { return enabled.Checked; } set { enabled.Checked = value; } }
        public string Queue { get { return queue.Text; } set { queue.Text = value ?? ""; } }
        public void ShowStatus(string value) { if (status.Text != value) status.Text = value; }
        public ReceiptSettings()
        {
            Size = new Size(390, 230);
            var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(8) };
            var test = new Button { Text = "Print Test Receipt", AutoSize = true };
            var refresh = new Button { Text = "Refresh printer queues", AutoSize = true };
            layout.Controls.Add(enabled); layout.Controls.Add(new Label { Text = "Windows printer queue", AutoSize = true });
            layout.Controls.Add(queue); layout.Controls.Add(refresh); layout.Controls.Add(test); layout.Controls.Add(status); Controls.Add(layout);
            Queue = "Thermal Printer POS-58";
            // Enumerate only when settings are shown or the user requests refresh.
            Load += delegate { RefreshQueues(); }; refresh.Click += delegate { RefreshQueues(); };
            test.Click += delegate { if (TestRequested != null) TestRequested(); };
        }
        private void RefreshQueues()
        {
            try { string selected = Queue; queue.Items.Clear(); foreach (string p in PrinterSettings.InstalledPrinters) queue.Items.Add(p); Queue = selected; }
            catch (Exception ex) { ShowStatus("Printer enumeration failed: " + ex.Message); Log.Write(ex.ToString()); }
        }
        public XmlNode Save(XmlDocument document)
        {
            var root = document.CreateElement("Settings");
            foreach (var item in new[] { new[] { "Version", "1" }, new[] { "Enabled", PrintingEnabled.ToString() }, new[] { "PrinterQueue", Queue }, new[] { "FortuneBag", Fortunes.Save() } })
            { var node = document.CreateElement(item[0]); node.InnerText = item[1]; root.AppendChild(node); }
            return root;
        }
        public void Restore(XmlNode root)
        {
            bool value; PrintingEnabled = Boolean.TryParse(Read(root, "Enabled"), out value) && value;
            Queue = Read(root, "PrinterQueue") ?? "Thermal Printer POS-58"; Fortunes.Load(Read(root, "FortuneBag"));
        }
        private static string Read(XmlNode root, string name) { var node = root == null ? null : root.SelectSingleNode(name); return node == null ? null : node.InnerText; }
    }
    public sealed class ReceiptComponent : LogicComponent
    {
        private readonly ReceiptSettings settings = new ReceiptSettings();
        private readonly ReceiptRenderer renderer = new ReceiptRenderer();
        private readonly PrintDispatcher dispatcher;
        private readonly RunCapture capture;
        private bool disposed;
        public ReceiptComponent(LiveSplitState state) : this(state, new WindowsRawPrinter()) { }
        public ReceiptComponent(LiveSplitState state, IReceiptPrinter printer)
        {
            dispatcher = new PrintDispatcher(printer);
            capture = new RunCapture(state, () => settings.PrintingEnabled, () => settings.Fortunes.Next(), run => dispatcher.Submit(settings.Queue, renderer.Render(run)));
            settings.TestRequested += Test;
        }
        private void Test()
        {
            try { dispatcher.Submit(settings.Queue, renderer.Render(SyntheticReceipt.Create())); settings.ShowStatus(dispatcher.Status); }
            catch (Exception ex) { settings.ShowStatus("Test receipt failed: " + ex.Message); Log.Write(ex.ToString()); }
        }
        public override string ComponentName { get { return "Thermal Run Receipt"; } }
        public override Control GetSettingsControl(LayoutMode mode) { return settings; }
        public override XmlNode GetSettings(XmlDocument document) { return settings.Save(document); }
        public override void SetSettings(XmlNode node) { try { settings.Restore(node); } catch (Exception ex) { Log.Write("Settings error " + ex); } }
        public override void Update(IInvalidator invalidator, LiveSplitState state, float width, float height, LayoutMode mode)
        { if (!disposed) settings.ShowStatus(dispatcher.Status); }
        public override void Dispose() { if (disposed) return; disposed = true; capture.Dispose(); settings.TestRequested -= Test; settings.Dispose(); }
    }
}
