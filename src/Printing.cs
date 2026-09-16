using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace LiveSplit.ThermalReceipt
{
    public interface IReceiptPrinter { void Print(string queue, byte[] bytes); }
    public sealed class WindowsRawPrinter : IReceiptPrinter
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private sealed class DocInfo { public string Name = "LiveSplit run receipt"; public string Output; public string DataType = "RAW"; }
        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool OpenPrinter(string name, out IntPtr handle, IntPtr defaults);
        [DllImport("winspool.drv", SetLastError = true)] private static extern bool ClosePrinter(IntPtr handle);
        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)] private static extern int StartDocPrinter(IntPtr handle, int level, [In] DocInfo info);
        [DllImport("winspool.drv", SetLastError = true)] private static extern bool EndDocPrinter(IntPtr handle);
        [DllImport("winspool.drv", SetLastError = true)] private static extern bool AbortPrinter(IntPtr handle);
        [DllImport("winspool.drv", SetLastError = true)] private static extern bool StartPagePrinter(IntPtr handle);
        [DllImport("winspool.drv", SetLastError = true)] private static extern bool EndPagePrinter(IntPtr handle);
        [DllImport("winspool.drv", SetLastError = true)] private static extern bool WritePrinter(IntPtr handle, IntPtr data, int count, out int written);
        private static void Check(bool success, string operation) { if (!success) throw new Win32Exception(Marshal.GetLastWin32Error(), operation + " failed"); }
        public void Print(string queue, byte[] bytes)
        {
            if (String.IsNullOrWhiteSpace(queue)) throw new ArgumentException("Choose a Windows printer queue.");
            IntPtr handle; Check(OpenPrinter(queue, out handle, IntPtr.Zero), "OpenPrinter (" + queue + ")");
            bool started = false, completed = false;
            try
            {
                Check(StartDocPrinter(handle, 1, new DocInfo()) != 0, "StartDocPrinter"); started = true;
                Check(StartPagePrinter(handle), "StartPagePrinter");
                var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                try
                {
                    int offset = 0;
                    while (offset < bytes.Length)
                    {
                        int written; Check(WritePrinter(handle, IntPtr.Add(pinned.AddrOfPinnedObject(), offset), bytes.Length - offset, out written), "WritePrinter");
                        if (written <= 0 || written > bytes.Length - offset) throw new IOException("WritePrinter returned an invalid byte count.");
                        offset += written;
                    }
                }
                finally { pinned.Free(); }
                Check(EndPagePrinter(handle), "EndPagePrinter"); Check(EndDocPrinter(handle), "EndDocPrinter"); completed = true;
            }
            finally { if (started && !completed) AbortPrinter(handle); ClosePrinter(handle); }
        }
    }
    public static class Log
    {
        private static readonly object Gate = new object();
        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiveSplit", "ThermalReceipt");
                    Directory.CreateDirectory(folder); string path = Path.Combine(folder, "receipt.log");
                    if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024) { string old = path + ".old"; if (File.Exists(old)) File.Delete(old); File.Move(path, old); }
                    File.AppendAllText(path, DateTimeOffset.Now.ToString("o") + " " + Format.Ascii(message) + Environment.NewLine);
                }
            }
            catch { /* Logging must not escape into LiveSplit, including disk failures. */ }
        }
    }
    public sealed class PrintDispatcher
    {
        private readonly IReceiptPrinter printer;
        private readonly object gate = new object();
        private Task tail = Task.FromResult(0);
        private int pending;
        public volatile string Status = "Ready";
        public PrintDispatcher(IReceiptPrinter printer) { this.printer = printer; }
        public Task Drain { get { lock (gate) return tail; } }
        public void Submit(string queue, byte[] bytes)
        {
            if (String.IsNullOrWhiteSpace(queue)) { Status = "Choose a Windows receipt printer queue; nothing printed."; return; }
            lock (gate)
            {
                if (pending >= 32) { Status = "Print backlog full; receipt not queued. See log."; Log.Write(Status); return; }
                pending++; Status = "Receipt queued";
                tail = tail.ContinueWith(delegate
                {
                    try { Log.Write("Print start queue=" + queue + " bytes=" + bytes.Length); printer.Print(queue, bytes); Status = "Receipt sent to Windows spooler"; Log.Write(Status); }
                    catch (Exception ex) { Status = "Printing failed: " + ex.Message; var native = ex as Win32Exception; Log.Write("Print failure queue=" + queue + (native == null ? "" : " win32=" + native.NativeErrorCode) + " " + ex); }
                    finally { lock (gate) pending--; }
                }, TaskScheduler.Default);
            }
        }
    }
}
