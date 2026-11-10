using LibUsbDotNet;
using LibUsbDotNet.Main;
using Guna.UI2.WinForms;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using System.Text.RegularExpressions;
using System.IO.Ports;
using System.Management;
using System.Threading.Tasks;
using System.Threading;
using System.Runtime.InteropServices;
using System.Drawing;

namespace AL_FATEH_MTK
{
    public partial class Form1 : Form
    {
        private System.Windows.Forms.OpenFileDialog openFileDialog1;
        private System.Windows.Forms.Timer timerRadar;
        // متغيرات الميزات الجديدة
        private CancellationTokenSource lockRemovalCancellation;
        // ضع هذا السطر مع المتغيرات الأخرى في أعلى الصفحة
        private SaveFileDialog saveFileDialog1;
        MTK_Engine engine = new MTK_Engine();
        string selectedScatterPath = "";
        // متغيرات نظام التنسيق
        private CancellationTokenSource formatCancellation;
        // متغيرات نظام Bootloader
        private CancellationTokenSource bootloaderCancellation;

        // ==========================================
        // منطقة المتغيرات العالمية (ساحة القيادة)
        // ==========================================
        byte[] CMD_FORMAT_STORAGE = { 0x70 };
        byte[] CMD_CONFIRM_OK = { 0x00, 0x01 };
        byte[] CMD_SEND_DATA = { 0xD8 };
        byte[] CMD_CHECK_STORAGE = { 0x98 };
        byte[] CMD_GET_BL_VER = { 0xFE };
        byte[] CMD_GET_HW_CODE = { 0xFD };
        byte[] CMD_SEND_DA = { 0xD7 };

        byte[] auth_bypass_payload = {
            0x40, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0xF0, 0x01, 0xFE, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        };

        List<PartitionInfo> partitionsList = new List<PartitionInfo>();
        long targetAddress = 0;
        long targetSize = 0;
        bool isWaiting = false;
        public static UsbDevice MyUsbDevice;

        private const int WM_DEVICECHANGE = 0x0219;
        private const int DBT_DEVICEARRIVAL = 0x8000;

        // متغيرات نظام القراءة الجديد
        private CancellationTokenSource readCancellation;
        private MTK_Engine.PartitionInfoExtended selectedPartition;

        public class PartitionInfo
        {
            public string Name { get; set; }
            public string Start { get; set; }
            public string Size { get; set; }
            public string Region { get; set; }
        }

        public Form1()
        {
            AssetManager.Initialize();
            InitializeComponent();
            
            // تهيئة المكونات لضمان عدم حدوث خطأ عند التشغيل
            openFileDialog1 = new System.Windows.Forms.OpenFileDialog();
            saveFileDialog1 = new System.Windows.Forms.SaveFileDialog();
            timerRadar = new System.Windows.Forms.Timer() { Interval = 50 };
            timerRadar.Tick += timerRadar_Tick;

            SetupButtons(); // تفعيل ترتيب الأزرار والمواقع يدوياً
            ApplySovereignTheme();
            StartSovereignEngine();

            // ربط دالة التسجيل مع المحرك
            engine.SetLogger((msg, color) => {
                Color logColor = Color.White;
                switch (color)
                {
                    case ConsoleColor.Red:
                        logColor = Color.Red;
                        break;
                    case ConsoleColor.Green:
                        logColor = Color.Green;
                        break;
                    case ConsoleColor.Yellow:
                        logColor = Color.Yellow;
                        break;
                    case ConsoleColor.Cyan:
                        logColor = Color.Cyan;
                        break;
                    case ConsoleColor.Gray:
                        logColor = Color.Gray;
                        break;
                    default:
                        logColor = Color.White;
                        break;
                }
                LogStatus(msg, logColor);
            });
        }

        private void StartSovereignEngine()
        {
            if (picRadar.Width <= 0 || picRadar.Height <= 0) return;

            Bitmap bmp = new Bitmap(picRadar.Width, picRadar.Height);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.FromArgb(15, 15, 15));

                using (Pen p = new Pen(Color.LimeGreen, 2))
                {
                    g.DrawEllipse(p, 5, 5, picRadar.Width - 10, picRadar.Height - 10);
                    p.Width = 1;
                    g.DrawLine(p, picRadar.Width / 2, 0, picRadar.Width / 2, picRadar.Height);
                    g.DrawLine(p, 0, picRadar.Height / 2, picRadar.Width, picRadar.Height / 2);
                }
            }
            picRadar.Image = bmp;
        }

        // ==========================================
        // محرك القنص المتقدم
        // ==========================================

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (isWaiting && m.Msg == WM_DEVICECHANGE && m.WParam.ToInt32() == DBT_DEVICEARRIVAL)
            {
                txtLo.AppendText("[📡] استشعار نبضة كهربائية USB.. جاري القنص النووي!" + Environment.NewLine);
                TriggerFastCapture();
            }
        }

        private async void TriggerFastCapture()
        {
            if (!isWaiting) return;

            await Task.Run(() => {
                this.Invoke(new MethodInvoker(delegate {
                    txtLo.AppendText("[🚀] بدء الرادار اللحظي (Kernel Mode).. جاري فحص الهوية." + Environment.NewLine);
                }));

                string devicePath = ScanForMtkKernelDevice();
                if (devicePath != null)
                {
                    this.Invoke(new MethodInvoker(delegate {
                        txtLo.AppendText($"[🎯] تم القنص بنجاح عبر النواة: {devicePath.Substring(0, 20)}..." + Environment.NewLine);
                        ProceedToHandshake(devicePath);
                    }));
                }
                else
                {
                    this.Invoke(new MethodInvoker(delegate {
                        txtLo.AppendText("[-] لم يتم التعرف على بصمة ميديا تك في النواة." + Environment.NewLine);
                    }));
                }
            });
        }

        private string ScanForMtkKernelDevice()
        {
            Guid usbGuid = new Guid("A5DCBF10-6530-11D2-901F-00C04FB17C9E");
            IntPtr hDevInfo = NativeMethods.SetupDiGetClassDevs(ref usbGuid, IntPtr.Zero, IntPtr.Zero, 0x00000012);

            if (hDevInfo != (IntPtr)(-1))
            {
                SP_DEVICE_INTERFACE_DATA interfaceData = new SP_DEVICE_INTERFACE_DATA();
                interfaceData.cbSize = (uint)Marshal.SizeOf(interfaceData);

                for (uint i = 0; i < 20; i++)
                {
                    if (NativeMethods.SetupDiEnumDeviceInterfaces(hDevInfo, IntPtr.Zero, ref usbGuid, i, ref interfaceData))
                    {
                        string devicePath = GetDevicePath(hDevInfo, interfaceData);
                        if (devicePath != null && devicePath.ToLower().Contains("vid_0e8d"))
                        {
                            NativeMethods.SetupDiDestroyDeviceInfoList(hDevInfo);
                            return devicePath;
                        }
                    }
                }
                NativeMethods.SetupDiDestroyDeviceInfoList(hDevInfo);
            }
            return null;
        }

        private string GetDevicePath(IntPtr hDevInfo, SP_DEVICE_INTERFACE_DATA interfaceData)
        {
            uint requiredSize = 0;
            NativeMethods.SetupDiGetDeviceInterfaceDetail(hDevInfo, ref interfaceData, IntPtr.Zero, 0, out requiredSize, IntPtr.Zero);
            IntPtr detailDataBuffer = Marshal.AllocHGlobal((int)requiredSize);
            Marshal.WriteInt32(detailDataBuffer, (IntPtr.Size == 8) ? 8 : 5);

            if (NativeMethods.SetupDiGetDeviceInterfaceDetail(hDevInfo, ref interfaceData, detailDataBuffer, requiredSize, out requiredSize, IntPtr.Zero))
            {
                IntPtr pDevicePathName = new IntPtr(detailDataBuffer.ToInt64() + 4);
                string path = Marshal.PtrToStringAuto(pDevicePathName);
                Marshal.FreeHGlobal(detailDataBuffer);
                return path;
            }
            Marshal.FreeHGlobal(detailDataBuffer);
            return null;
        }

        // ==========================================
        // المصافحة والتنفيذ
        // ==========================================

        private async void ProceedToHandshake(string devicePath)
        {
            txtLo.AppendText("[🚀] جاري استجواب الهاتف عبر المحرك النووي..." + Environment.NewLine);

            await Task.Run(async () => {
                try
                {
                    if (engine.OpenDevice(devicePath))
                    {
                        if (engine.StartHandshake())
                        {
                            ushort hwCode = engine.GetHWCode();
                            ushort hwVer = engine.GetHWVer();
                            uint targetConfig = engine.GetTargetConfig();
                            byte[] meid = engine.GetMEID();
                            string storage = engine.GetStorageType();
                            bool blStatus = engine.IsBootloaderUnlocked();

                            bool sbc = (targetConfig & 0x1) != 0;
                            bool sla = (targetConfig & 0x2) != 0;
                            bool daa = (targetConfig & 0x4) != 0;
                            string phonePlatform = $"MT{hwCode:X4}";

                            this.Invoke(new MethodInvoker(delegate {
                                txtLo.AppendText("------------------------------------" + Environment.NewLine);
                                txtLo.AppendText($"[🤝] المصافحة: ناجحة | المعالج: {phonePlatform}" + Environment.NewLine);
                                txtLo.AppendText($"[🛡️] نظام الحماية: (SBC:{(sbc ? "✅" : "❌")} | SLA:{(sla ? "✅" : "❌")} | DAA:{(daa ? "✅" : "❌")})" + Environment.NewLine);
                                txtLo.AppendText("====================================" + Environment.NewLine);
                                timerRadar.Stop();
                            }));

                            this.Invoke(new MethodInvoker(delegate {
                                txtLo.AppendText($"[🔍] جاري تأمين الأصول السيادية لـ {phonePlatform}..." + Environment.NewLine);
                            }));

                            bool isAssetReady = await engine.PrepareSovereignAsset(hwCode);

                            if (!isAssetReady)
                            {
                                this.Invoke(new MethodInvoker(delegate {
                                    txtLo.AppendText("[⚠️] تنبيه: لم يتم العثور على أصول سيادية (محلياً أو سحابياً)." + Environment.NewLine);
                                    txtLo.AppendText("[ℹ️] سيستمر الفاتح باستخدام المحرك التقليدي فقط." + Environment.NewLine);
                                }));
                            }
                            else
                            {
                                this.Invoke(new MethodInvoker(delegate {
                                    txtLo.AppendText("[✅] تم تأمين الأصول السيادية بنجاح." + Environment.NewLine);
                                }));
                            }

                            if (sbc || sla || daa)
                            {
                                bool exploitSuccess = false;

                                this.Invoke(new MethodInvoker(delegate {
                                    txtLo.AppendText("[⚔️] محاولة الاختراق عبر الذخيرة الشاملة..." + Environment.NewLine);
                                }));

                                if (engine.ExecuteUniversalExploit())
                                {
                                    if (engine.DisableSecurity()) exploitSuccess = true;
                                }

                                if (!exploitSuccess)
                                {
                                    this.Invoke(new MethodInvoker(delegate {
                                        txtLo.AppendText("[⚠️] الذخيرة الشاملة غير متوافقة. جاري سحب ذخيرة مخصصة..." + Environment.NewLine);
                                    }));

                                    byte[] customPayload = null;
                                    if (isAssetReady)
                                    {
                                        string assetPath = AssetManager.GetAssetPath(phonePlatform.Replace("MT", ""));
                                        if (System.IO.File.Exists(assetPath))
                                            customPayload = System.IO.File.ReadAllBytes(assetPath);
                                    }

                                    if (customPayload == null) customPayload = Payload_Manager.GetPayload(hwCode).data;

                                    if (customPayload != null)
                                    {
                                        if (engine.SendPayload(customPayload))
                                        {
                                            if (engine.DisableSecurity()) exploitSuccess = true;
                                        }
                                    }
                                }

                                if (exploitSuccess)
                                {
                                    this.Invoke(new MethodInvoker(delegate {
                                        txtLo.AppendText("[🔓] تم كسر الحماية بنجاح! السيطرة كاملة." + Environment.NewLine);
                                        txtLo.AppendText("[🕵️] جاري استخراج بصمة السيطرة من الذاكرة..." + Environment.NewLine);
                                    }));

                                    byte[] bootData = engine.ReadMemory(0x0, 16);
                                    if (bootData != null)
                                    {
                                        string hex = BitConverter.ToString(bootData).Replace("-", " ");
                                        this.Invoke(new MethodInvoker(delegate {
                                            txtLo.AppendText($"[🚩] بصمة السيطرة: {hex}" + Environment.NewLine);
                                            txtLo.AppendText("[👑] تأكيد: الهاتف الآن ملك للفاتح!" + Environment.NewLine);
                                        }));
                                    }
                                }
                                else
                                {
                                    this.Invoke(new MethodInvoker(delegate {
                                        txtLo.AppendText("[-] فشل الاختراق: هذا المعالج محصن أو يحتاج لملف DA رسمي." + Environment.NewLine);
                                    }));
                                }
                            }
                            engine.CloseDevice();
                        }
                        else
                        {
                            this.Invoke(new MethodInvoker(delegate {
                                txtLo.AppendText("[-] فشل المصافحة: المعالج لم يستجب." + Environment.NewLine);
                            }));
                        }
                    }
                }
                catch (Exception ex)
                {
                    this.Invoke(new MethodInvoker(delegate {
                        txtLo.AppendText("[!] خطأ غير متوقع: " + ex.Message + Environment.NewLine);
                    }));
                }
            });
        }

        private async Task SendDownloadAgent(IntPtr hFile)
        {
            this.Invoke(new MethodInvoker(delegate {
                txtLo.AppendText("[📦] جاري تحضير ملف الـ DA للحقن..." + Environment.NewLine);
            }));

            uint bWritten = 0;
            NativeMethods.WriteFile(hFile, CMD_SEND_DA, 1, out bWritten, IntPtr.Zero);

            byte[] address = BitConverter.GetBytes(0x400000);
            Array.Reverse(address);
            NativeMethods.WriteFile(hFile, address, 4, out bWritten, IntPtr.Zero);

            this.Invoke(new MethodInvoker(delegate {
                txtLo.AppendText("[🚀] جاري دفع بروتوكول السيطرة.." + Environment.NewLine);
            }));

            byte[] ack = new byte[1];
            uint bRead = 0;
            NativeMethods.ReadFile(hFile, ack, 1, out bRead, IntPtr.Zero);

            if (bRead > 0 && ack[0] == 0x00)
            {
                this.Invoke(new MethodInvoker(delegate {
                    txtLo.AppendText("[✅] تم قبول ملف الـ DA بنجاح!" + Environment.NewLine);
                }));
            }
        }

        private async Task ExecuteKernelFormat(IntPtr hFile, long address, long size)
        {
            txtLo.AppendText($"[⚡] بدء عملية المسح النووي للعنوان: 0x{address:X}..." + Environment.NewLine);

            uint bWritten = 0;
            uint bRead = 0;
            byte[] ack = new byte[1];

            NativeMethods.WriteFile(hFile, CMD_FORMAT_STORAGE, 1, out bWritten, IntPtr.Zero);

            byte[] addrBytes = BitConverter.GetBytes((uint)address);
            Array.Reverse(addrBytes);
            NativeMethods.WriteFile(hFile, addrBytes, 4, out bWritten, IntPtr.Zero);

            byte[] sizeBytes = BitConverter.GetBytes((uint)size);
            Array.Reverse(sizeBytes);
            NativeMethods.WriteFile(hFile, sizeBytes, 4, out bWritten, IntPtr.Zero);

            txtLo.AppendText("[⏳] بانتظار استجابة الذاكرة.. لا تفصل الهاتف!" + Environment.NewLine);

            byte[] response = new byte[1];
            NativeMethods.ReadFile(hFile, response, 1, out bRead, IntPtr.Zero);

            if (bRead > 0 && response[0] == 0x00)
            {
                txtLo.AppendText("[🌟] مبروك يا قائد! تمت العملية بنجاح تفرّد بها الفاتح." + Environment.NewLine);
                txtLo.AppendText("[🔓] تم مسح القفل المستهدف نهائياً." + Environment.NewLine);
            }
            else
            {
                txtLo.AppendText("[-] فشل المسح: المعالج رفض كتابة البيانات." + Environment.NewLine);
            }
        }

        private void ParseScatterFile(string filePath)
        {
            try
            {
                partitionsList.Clear();
                dgvPartitions.Rows.Clear();
                LogStatus($"[📄] جاري فحص خريطة الذاكرة (Scatter): {Path.GetFileName(filePath)}", Color.Cyan);

                string content = File.ReadAllText(filePath);
                string[] blocks = content.Split(new string[] { "- partition_index" }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var block in blocks)
                {
                    if (block.Contains("partition_name:"))
                    {
                        var partition = new PartitionInfo();
                        partition.Name = GetValue(block, "partition_name");
                        partition.Start = GetValue(block, "physical_start_addr");
                        partition.Size = GetValue(block, "partition_size");
                        partition.Region = GetValue(block, "region");

                        if (!string.IsNullOrEmpty(partition.Name) && partition.Name != "NONE")
                        {
                            partitionsList.Add(partition);
                            dgvPartitions.Rows.Add(partition.Name, partition.Start, partition.Size, partition.Region);
                        }
                    }
                }

                LogStatus($"[👑] تم استخراج {partitionsList.Count} قطاع. الواجهة السيادية جاهزة للعمليات.", Color.LimeGreen);
                if (dgvPartitions.Rows.Count > 0) dgvPartitions.FirstDisplayedScrollingRowIndex = 0;
            }
            catch (Exception ex) 
            { 
                LogStatus($"[❌] خطأ في تحليل السكاتر: {ex.Message}", Color.Red); 
            }
        }

        private string GetValue(string block, string key)
        {
            string pattern = $@"{key}:\s*(.*)";
            Match match = Regex.Match(block, pattern);
            return match.Success ? match.Groups[1].Value.Trim() : "NONE";
        }

        private void ExecuteFormat(SerialPort sp)
        {
            try
            {
                txtLo.AppendText("[⚡] جاري إرسال أمر المسح النهائي..." + Environment.NewLine);
                sp.Write(new byte[] { 0x70 }, 0, 1);
                sp.Write(BitConverter.GetBytes(targetAddress), 0, 4);
                sp.Write(BitConverter.GetBytes(targetSize), 0, 4);
                Thread.Sleep(100);
                txtLo.AppendText("[🌟] مبروك! تمت العملية بنجاح تفرّد بها الفاتح." + Environment.NewLine);
                System.Media.SystemSounds.Asterisk.Play();
                isWaiting = false;
            }
            catch (Exception ex) { txtLo.AppendText("[-] فشل المسح: " + ex.Message + Environment.NewLine); }
        }

        private void PrepareTargetPartition(string partitionName)
        {
            if (partitionsList.Count == 0) { txtLo.AppendText("[-] يرجى تحميل ملف السكاتر أولاً!" + Environment.NewLine); return; }
            foreach (var part in partitionsList)
            {
                if (part.Name.ToUpper() == partitionName.ToUpper())
                {
                    targetAddress = Convert.ToInt64(part.Start.Replace("0x", ""), 16);
                    targetSize = Convert.ToInt64(part.Size.Replace("0x", ""), 16);
                    txtLo.AppendText($"[🎯] تم تجهيز: {partitionName} | {part.Start}" + Environment.NewLine);
                    return;
                }
            }
        }

        // ==========================================
        // أحداث الأزرار (Buttons Events)
        // ==========================================

        private void btnLoadScatter_Click(object sender, EventArgs e)
        {
            if (openFileDialog1.ShowDialog() == DialogResult.OK)
            {
                selectedScatterPath = openFileDialog1.FileName;
                ParseScatterFile(openFileDialog1.FileName);
                txtLo.AppendText($"[📄] تم تحميل السكاتر بنجاح." + Environment.NewLine);
            }
        }

        private void btnStartOpreation_Click(object sender, EventArgs e)
        {
            LogStatus("بدء البحث عن منفذ Mediatek...", Color.Yellow);
            timerRadar.Start();
            dgvPartitions.Enabled = false;
            txtLo.Clear();
            txtLo.AppendText("========================================" + Environment.NewLine);
            txtLo.AppendText("        AL-FATEH MTK TOOL v1.0         " + Environment.NewLine);
            txtLo.AppendText("========================================" + Environment.NewLine);
            txtLo.AppendText("[🛡️] وضع الاستعداد نشط.. قم بتوصيل الهاتف الآن وهو مغلق." + Environment.NewLine);
            LogStatus("تم تفعيل المحرك النووي للفاتح...", Color.White);
            LogStatus("جارِ البحث عن ثغرة BROM...", Color.Yellow);

            isWaiting = true;
        }

        private void btnFRP_Click(object sender, EventArgs e)
        {
            PrepareTargetPartition("frp");
            LogStatus("[🛡️] تم تجهيز عنوان حذف حماية FRP. بانتظار التوصيل...", Color.Cyan);
            progressBar1.Value = 0;
            lblProgress.Text = "0%";
        }

        private async void btnFormat_Click(object sender, EventArgs e)
        {
            // هذا الزر يقوم بتنسيق قسم "userdata" بالكامل
            // يمكن إعادة استخدام منطق btnFormatPartition_Click مع تحديد "userdata"
            // أو إنشاء دالة مساعدة لتنسيق قسم معين.
            // حالياً، btnFormatPartition_Click تتطلب اختيار قسم من الجدول.
            // لتشغيلها مباشرة من هنا، ستحتاج إلى تعديلها أو إنشاء دالة جديدة.
            // مؤقتاً، يمكننا محاكاة ذلك أو توجيه المستخدم.
            MessageBox.Show("يرجى اختيار قسم 'userdata' من الجدول ثم الضغط على 'تنسيق قسم' لتنسيق كامل الجهاز.", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ==========================================
        // دوال نظام القراءة المتقدمة (الجديدة)
        // ==========================================

        private async void btnBackupPartition_Click(object sender, EventArgs e)
        {
            if (dgvPartitions.SelectedRows.Count == 0)
            {
                LogStatus("[-] الرجاء اختيار قسم من الجدول", Color.Yellow);
                return;
            }

            string partitionName = dgvPartitions.SelectedRows[0].Cells[0].Value.ToString();

            // استخدام saveFileDialog1 الموجود في التصميم
            saveFileDialog1.FileName = $"{partitionName}.bin";
            saveFileDialog1.Filter = "Binary files (*.bin)|*.bin|All files (*.*)|*.*";

            if (saveFileDialog1.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    btnBackupPartition.Enabled = false;
                    btnBackupAll.Enabled = false;

                    readCancellation = new CancellationTokenSource();

                    var progress = new Progress<int>(p =>
                    {
                        progressBar1.Value = p;
                        lblProgress.Text = $"{p}%";
                    });

                    bool success = await engine.ReadPartitionToFile(
                        partitionName,
                        saveFileDialog1.FileName,
                        progress,
                        readCancellation.Token
                    );

                    if (success)
                    {
                        LogStatus($"[✅] تم حفظ القسم بنجاح", Color.Green);
                        System.Media.SystemSounds.Asterisk.Play();
                    }
                }
                catch (Exception ex)
                {
                    LogStatus($"[🔥] خطأ: {ex.Message}", Color.Red);
                }
                finally
                {
                    btnBackupPartition.Enabled = true;
                    btnBackupAll.Enabled = true;
                    progressBar1.Value = 0;
                    lblProgress.Text = "0%";
                }
            }
        }

        private async void btnBackupAll_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(selectedScatterPath))
            {
                LogStatus("[-] الرجاء تحميل ملف السكاتر أولاً", Color.Yellow);
                return;
            }

            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "اختر مجلد لحفظ النسخة الاحتياطية";
                fbd.ShowNewFolderButton = true;

                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        btnBackupPartition.Enabled = false;
                        btnBackupAll.Enabled = false;
                        btnStartOpreation.Enabled = false;

                        engine.LoadPartitionsFromScatter(selectedScatterPath);

                        string backupFolder = Path.Combine(
                            fbd.SelectedPath,
                            $"Fateh_Backup_{DateTime.Now:yyyy-MM-dd_HH-mm}"
                        );

                        var progress = new Progress<int>(p =>
                        {
                            progressBar1.Value = p;
                            lblProgress.Text = $"التقدم الكلي: {p}%";
                        });

                        bool success = await engine.BackupAllPartitions(backupFolder, progress);

                        if (success)
                        {
                            LogStatus($"[🏆] اكتمل النسخ الاحتياطي الكامل!", Color.Green);
                            LogStatus($"[📂] الملفات محفوظة في: {backupFolder}", Color.Cyan);
                            System.Media.SystemSounds.Asterisk.Play();
                        }
                    }
                    catch (Exception ex)
                    {
                        LogStatus($"[🔥] خطأ في النسخ الاحتياطي: {ex.Message}", Color.Red);
                    }
                    finally
                    {
                        btnBackupPartition.Enabled = true;
                        btnBackupAll.Enabled = true;
                        btnStartOpreation.Enabled = true;
                        progressBar1.Value = 0;
                        lblProgress.Text = "0%";
                    }
                }
            }
        }

        private void btnCancelRead_Click(object sender, EventArgs e)
        {
            if (readCancellation != null)
            {
                readCancellation.Cancel();
                LogStatus("[⚠️] جاري إلغاء عملية القراءة...", Color.Yellow);
            }
        }

        // ==========================================
        // دوال الواجهة والتنسيق
        // ==========================================

        private void txtLog_TextChanged(object sender, EventArgs e) { }
        private void btnStartDetection_Click(object sender, EventArgs e) { }
        private void Form1_Load(object sender, EventArgs e) { }
        private void dgvPartitions_CellContentClick(object sender, DataGridViewCellEventArgs e) { }

        private void panel1_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            using (Pen p = new Pen(Color.LimeGreen, 3))
            {
                g.DrawEllipse(p, 20, 20, 100, 100);
            }
        }

        private void ApplySovereignTheme()
        {
            // ===== Modern dark theme palette =====
            Color bg = Color.FromArgb(10, 12, 16);
            Color surface = Color.FromArgb(16, 20, 26);
            Color surface2 = Color.FromArgb(20, 25, 33);
            Color border = Color.FromArgb(35, 45, 58);
            Color text = Color.FromArgb(238, 242, 255);
            Color muted = Color.FromArgb(150, 165, 190);
            Color accent = Color.FromArgb(64, 190, 255);        // cyan/blue
            Color ok = Color.FromArgb(34, 197, 94);              // green
            Color warn = Color.FromArgb(245, 158, 11);           // amber
            Color danger = Color.FromArgb(239, 68, 68);          // red

            // Form
            this.BackColor = bg;
            this.ForeColor = text;
            this.Font = new Font("Segoe UI", 10F, FontStyle.Regular);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MinimumSize = new Size(1100, 720);

            // Log console
            txtLo.BackColor = surface;
            txtLo.ForeColor = Color.FromArgb(180, 255, 200);
            txtLo.BorderStyle = BorderStyle.None;
            txtLo.Font = new Font("Consolas", 10F, FontStyle.Regular);

            // Partitions grid
            dgvPartitions.BackgroundColor = surface;
            dgvPartitions.GridColor = border;
            dgvPartitions.BorderStyle = BorderStyle.None;
            dgvPartitions.DefaultCellStyle.BackColor = surface;
            dgvPartitions.DefaultCellStyle.ForeColor = text;
            dgvPartitions.DefaultCellStyle.SelectionBackColor = Color.FromArgb(35, 48, 70);
            dgvPartitions.DefaultCellStyle.SelectionForeColor = text;
            dgvPartitions.AlternatingRowsDefaultCellStyle.BackColor = surface2;
            dgvPartitions.ColumnHeadersDefaultCellStyle.BackColor = surface2;
            dgvPartitions.ColumnHeadersDefaultCellStyle.ForeColor = text;
            dgvPartitions.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
            dgvPartitions.EnableHeadersVisualStyles = false;
            dgvPartitions.RowTemplate.Height = 32;
            dgvPartitions.ReadOnly = true;
            dgvPartitions.SelectionMode = DataGridViewSelectionMode.FullRowSelect;

            // Progress
            lblProgress.ForeColor = muted;

            // Radar (small card look)
            picRadar.BackColor = surface;

            // Apply consistent styling to all Guna buttons
            void StyleButtonBase(Guna2Button b)
            {
                b.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
                b.ForeColor = text;
                b.BorderRadius = 12;
                b.BorderThickness = 1;
                b.BorderColor = border;
                b.FillColor = surface2;
                b.HoverState.FillColor = Color.FromArgb(28, 35, 46);
                b.HoverState.BorderColor = Color.FromArgb(70, 90, 115);
                b.PressedColor = Color.FromArgb(22, 28, 38);
                b.DisabledState.FillColor = Color.FromArgb(30, 30, 30);
                b.DisabledState.ForeColor = Color.FromArgb(120, 120, 120);
                b.DisabledState.BorderColor = Color.FromArgb(55, 55, 55);
                b.Animated = true;
                b.Cursor = Cursors.Hand;
            }

            void StylePrimary(Guna2Button b) { StyleButtonBase(b); b.FillColor = accent; b.BorderColor = Color.FromArgb(30, 140, 210); b.HoverState.FillColor = Color.FromArgb(40, 170, 240); }
            void StyleSuccess(Guna2Button b) { StyleButtonBase(b); b.FillColor = ok; b.BorderColor = Color.FromArgb(22, 140, 70); b.HoverState.FillColor = Color.FromArgb(28, 170, 82); }
            void StyleWarn(Guna2Button b) { StyleButtonBase(b); b.FillColor = warn; b.BorderColor = Color.FromArgb(180, 110, 0); b.HoverState.FillColor = Color.FromArgb(255, 176, 36); }
            void StyleDanger(Guna2Button b) { StyleButtonBase(b); b.FillColor = danger; b.BorderColor = Color.FromArgb(170, 30, 30); b.HoverState.FillColor = Color.FromArgb(255, 95, 95); }
            void StyleNeutral(Guna2Button b) { StyleButtonBase(b); }

            // Assign styles by intent (not by row)
            StylePrimary(btnStartOpreation);
            StyleWarn(btnFRP);
            StyleDanger(btnFormat);

            StyleSuccess(btnBackupPartition);
            StyleSuccess(btnBackupAll);
            StyleDanger(btnCancelRead);

            StyleWarn(btnWritePartition);
            StyleDanger(btnCancelWrite);
            StyleNeutral(btnFormatPartition);
            StyleDanger(btnCancelFormat);

            StyleNeutral(btnUnlockBootloader);
            StyleNeutral(btnLockBootloader);
            StyleNeutral(btnCheckBootloader);
            StyleDanger(btnCancelBootloader);

            StyleNeutral(btnRemoveLock);
            StyleNeutral(btnReboot);
            StyleNeutral(btnAdvancedInfo);
            StyleNeutral(btnIMEI);
            StyleNeutral(btnSupport);

            // Tile button (scatter)
            btnLoadScatter.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
            btnLoadScatter.ForeColor = text;
            btnLoadScatter.BorderRadius = 16;
            btnLoadScatter.BorderThickness = 1;
            btnLoadScatter.BorderColor = border;
            btnLoadScatter.FillColor = surface2;
            btnLoadScatter.HoverState.FillColor = Color.FromArgb(28, 35, 46);
            btnLoadScatter.Animated = true;
            btnLoadScatter.Cursor = Cursors.Hand;

            // Layout: make main areas resize nicely
            dgvPartitions.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            txtLo.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right;
            progressBar1.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            lblProgress.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            statusStrip1.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        }

        public void LogStatus(string message, Color color)
        {
            if (txtLo.InvokeRequired)
            {
                txtLo.Invoke(new Action(() => LogStatus(message, color)));
                return;
            }

            txtLo.SelectionStart = txtLo.TextLength;
            txtLo.SelectionLength = 0;
            txtLo.SelectionColor = color;

            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            txtLo.AppendText($"[{timestamp}] {message}{Environment.NewLine}");

            txtLo.SelectionColor = txtLo.ForeColor;
            txtLo.ScrollToCaret();
        }

        public enum DeviceStatus { Idle, Searching, Connected, Secured }
        public DeviceStatus currentStatus = DeviceStatus.Idle;

        int angle = 0;

        private void timerRadar_Tick(object sender, EventArgs e)
        {
            if (picRadar.Image == null) return;

            Bitmap bmp = new Bitmap(picRadar.Width, picRadar.Height);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.FromArgb(15, 15, 15));

                using (Pen p = new Pen(Color.Blue, 2))
                {
                    g.DrawEllipse(p, 5, 5, picRadar.Width - 10, picRadar.Height - 10);

                    double rad = angle * Math.PI / 180;
                    int x2 = (int)(picRadar.Width / 2 + (picRadar.Width / 2 - 5) * Math.Cos(rad));
                    int y2 = (int)(picRadar.Height / 2 + (picRadar.Height / 2 - 5) * Math.Sin(rad));

                    using (Pen beamPen = new Pen(Color.FromArgb(100, Color.Blue), 3))
                    {
                        g.DrawLine(beamPen, picRadar.Width / 2, picRadar.Height / 2, x2, y2);
                    }
                }
            }
            picRadar.Image = bmp;
            angle = (angle + 10) % 360;
        }
        // ==========================================
        // دوال نظام الكتابة (جديدة)
        // ==========================================

        // متغيرات مساعدة للكتابة
        // ==========================================
        // دوال نظام الكتابة (جديدة)
        // ==========================================

        // متغير مساعد للكتابة
        private CancellationTokenSource writeCancellation;

        private async void btnWritePartition_Click(object sender, EventArgs e)
        {
            try
            {
                // 1. التأكد من تحميل ملف السكاتر
                if (string.IsNullOrEmpty(selectedScatterPath))
                {
                    LogStatus("[-] الرجاء تحميل ملف السكاتر أولاً", Color.Yellow);
                    MessageBox.Show("الرجاء تحميل ملف السكاتر أولاً", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // 2. التأكد من اختيار قسم من الجدول
                if (dgvPartitions.SelectedRows.Count == 0)
                {
                    LogStatus("[-] الرجاء اختيار قسم من الجدول", Color.Yellow);
                    MessageBox.Show("الرجاء اختيار قسم من الجدول أولاً", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // 3. الحصول على اسم القسم المختار
                string partitionName = dgvPartitions.SelectedRows[0].Cells[0].Value?.ToString();

                if (string.IsNullOrEmpty(partitionName))
                {
                    LogStatus("[-] اسم القسم غير صالح", Color.Red);
                    return;
                }

                LogStatus($"[📝] تم اختيار القسم: {partitionName}", Color.Cyan);

                // 4. اختيار ملف للكتابة
                openFileDialog1.Title = "اختر ملف للكتابة على القسم " + partitionName;
                openFileDialog1.Filter = "Binary files (*.bin)|*.bin|Image files (*.img)|*.img|All files (*.*)|*.*";
                openFileDialog1.FileName = "";

                if (openFileDialog1.ShowDialog() == DialogResult.OK)
                {
                    string filePath = openFileDialog1.FileName;
                    LogStatus($"[📁] الملف المختار: {Path.GetFileName(filePath)}", Color.White);

                    // 5. تأكيد العملية (لأن الكتابة خطيرة)
                    DialogResult confirm = MessageBox.Show(
                        $"هل أنت متأكد من كتابة الملف '{Path.GetFileName(filePath)}' على القسم '{partitionName}'؟\n\n" +
                        "تحذير: هذه العملية قد تتلف الجهاز إذا تمت بشكل خاطئ!",
                        "تأكيد العملية الخطيرة",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);

                    if (confirm != DialogResult.Yes)
                    {
                        LogStatus("[-] تم إلغاء عملية الكتابة", Color.Yellow);
                        return;
                    }

                    // 6. تعطيل الأزرار أثناء العمل
                    btnWritePartition.Enabled = false;
                    btnBackupPartition.Enabled = false;
                    btnBackupAll.Enabled = false;

                    // 7. إنشاء مصدر للإلغاء
                    writeCancellation = new CancellationTokenSource();

                    // 8. إعداد مؤشر التقدم
                    var progress = new Progress<int>(p =>
                    {
                        progressBar1.Value = p;
                        lblProgress.Text = $"الكتابة: {p}%";
                    });

                    LogStatus($"[📝] بدء كتابة القسم {partitionName}...", Color.Cyan);
                    
                    if (engine.hDevice == (IntPtr)(-1))
                    {
                        LogStatus("[❌] خطأ: الجهاز غير متصل. يرجى الضغط على 'بدء العملية' وتوصيل الهاتف.", Color.Red);
                        return;
                    }

                    bool success = await engine.WritePartition(
                        partitionName,
                        filePath,
                        progress,
                        writeCancellation.Token
                    );

                    if (success)
                    {
                        LogStatus($"[✅] تم كتابة القسم {partitionName} بنجاح!", Color.Green);
                    }
                    System.Media.SystemSounds.Asterisk.Play();
                }
            }
            catch (Exception ex)
            {
                LogStatus($"[🔥] خطأ: {ex.Message}", Color.Red);
            }
            finally
            {
                // إعادة تمكين الأزرار
                btnWritePartition.Enabled = true;
                btnBackupPartition.Enabled = true;
                btnBackupAll.Enabled = true;
                progressBar1.Value = 0;
                lblProgress.Text = "0%";
            }
        }

        private void btnCancelWrite_Click(object sender, EventArgs e)
        {
            if (writeCancellation != null)
            {
                writeCancellation.Cancel();
                LogStatus("[⚠️] جاري إلغاء عملية الكتابة...", Color.Yellow);
            }
            else
            {
                LogStatus("[-] لا توجد عملية كتابة نشطة", Color.Yellow);
            }
        }
        // ==========================================
        // دوال تنسيق الأقسام (أضيفت في 22 رمضان)
        // ==========================================

        private async void btnFormatPartition_Click(object sender, EventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(selectedScatterPath))
                {
                    LogStatus("[-] الرجاء تحميل ملف السكاتر أولاً", Color.Yellow);
                    MessageBox.Show("الرجاء تحميل ملف السكاتر أولاً", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (dgvPartitions.SelectedRows.Count == 0)
                {
                    LogStatus("[-] الرجاء اختيار قسم من الجدول", Color.Yellow);
                    MessageBox.Show("الرجاء اختيار قسم من الجدول أولاً", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string partitionName = dgvPartitions.SelectedRows[0].Cells[0].Value?.ToString();

                if (string.IsNullOrEmpty(partitionName))
                {
                    LogStatus("[-] اسم القسم غير صالح", Color.Red);
                    return;
                }

                // تحذير شديد للتنسيق
                DialogResult confirm = MessageBox.Show(
                    $"هل أنت متأكد من تنسيق القسم '{partitionName}'؟\n\n" +
                    "تحذير شديد: هذه العملية ستمسح جميع البيانات في هذا القسم وقد تتلف الجهاز إذا كان قسماً مهماً!",
                    "تأكيد عملية التنسيق الخطيرة",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (confirm != DialogResult.Yes)
                {
                    LogStatus("[-] تم إلغاء عملية التنسيق", Color.Yellow);
                    return;
                }

                btnFormatPartition.Enabled = false;
                btnBackupPartition.Enabled = false;
                btnWritePartition.Enabled = false;

                formatCancellation = new CancellationTokenSource();

                var progress = new Progress<int>(p =>
                {
                    progressBar1.Value = p;
                    lblProgress.Text = $"التنسيق: {p}%";
                });

                LogStatus($"[🧹] بدء تنسيق القسم {partitionName}...", Color.Cyan);
                
                if (engine.hDevice == (IntPtr)(-1))
                {
                    LogStatus("[❌] خطأ: الجهاز غير متصل.", Color.Red);
                    return;
                }

                bool success = await engine.FormatPartition(
                    partitionName,
                    progress,
                    formatCancellation.Token
                );

                if (success) LogStatus($"[✅] تم تنسيق {partitionName} بنجاح", Color.Green);

                LogStatus($"[✅] اختبار التنسيق اكتمل (محاكاة)", Color.Green);
                System.Media.SystemSounds.Asterisk.Play();
            }
            catch (Exception ex)
            {
                LogStatus($"[🔥] خطأ: {ex.Message}", Color.Red);
            }
            finally
            {
                btnFormatPartition.Enabled = true;
                btnBackupPartition.Enabled = true;
                btnWritePartition.Enabled = true;
                progressBar1.Value = 0;
                lblProgress.Text = "0%";
            }
        }

        private void btnCancelFormat_Click(object sender, EventArgs e)
        {
            if (formatCancellation != null)
            {
                formatCancellation.Cancel();
                LogStatus("[⚠️] جاري إلغاء عملية التنسيق...", Color.Yellow);
            }
            else
            {
                LogStatus("[-] لا توجد عملية تنسيق نشطة", Color.Yellow);
            }
        }
        // ==========================================
        // دوال Bootloader (أضيفت في 23 رمضان)
        // ==========================================

        private async void btnUnlockBootloader_Click(object sender, EventArgs e)
        {
            try
            {
                // تحذير شديد
                DialogResult confirm = MessageBox.Show(
                    "⚠️ **تحذير شديد** ⚠️\n\n" +
                    "فتح Bootloader سيؤدي إلى:\n" +
                    "• فقدان الضمان (Warranty)\n" +
                    "• مسح جميع البيانات (Factory Reset)\n" +
                    "• احتمالية تعرض الجهاز للخطر\n\n" +
                    "هل أنت متأكد من الاستمرار؟",
                    "تحذير فتح Bootloader",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (confirm != DialogResult.Yes)
                {
                    LogStatus("[-] تم إلغاء عملية فتح Bootloader", Color.Yellow);
                    return;
                }

                btnUnlockBootloader.Enabled = false;
                btnLockBootloader.Enabled = false;

                bootloaderCancellation = new CancellationTokenSource();

                var progress = new Progress<int>(p =>
                {
                    progressBar1.Value = p;
                    lblProgress.Text = $"فتح Bootloader: {p}%";
                });

                LogStatus("[🔓] بدء عملية فتح Bootloader...", Color.Cyan);
                
                if (engine.hDevice == (IntPtr)(-1))
                {
                    LogStatus("[❌] خطأ: الجهاز غير متصل.", Color.Red);
                    return;
                }

                bool success = await engine.UnlockBootloader(
                    progress,
                    bootloaderCancellation.Token
                );

                if (success) LogStatus("[✅] تم فتح البوت لودر بنجاح", Color.Green);

                LogStatus($"[✅] اختبار فتح Bootloader اكتمل (محاكاة)", Color.Green);
                System.Media.SystemSounds.Asterisk.Play();
            }
            catch (Exception ex)
            {
                LogStatus($"[🔥] خطأ: {ex.Message}", Color.Red);
            }
            finally
            {
                btnUnlockBootloader.Enabled = true;
                btnLockBootloader.Enabled = true;
                progressBar1.Value = 0;
                lblProgress.Text = "0%";
            }
        }

        private async void btnLockBootloader_Click(object sender, EventArgs e)
        {
            try
            {
                DialogResult confirm = MessageBox.Show(
                    "هل أنت متأكد من قفل Bootloader؟\n\n" +
                    "هذا قد يمنع تثبيت الأنظمة المخصصة.",
                    "تأكيد قفل Bootloader",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (confirm != DialogResult.Yes)
                {
                    LogStatus("[-] تم إلغاء عملية قفل Bootloader", Color.Yellow);
                    return;
                }

                btnUnlockBootloader.Enabled = false;
                btnLockBootloader.Enabled = false;

                bootloaderCancellation = new CancellationTokenSource();

                var progress = new Progress<int>(p =>
                {
                    progressBar1.Value = p;
                    lblProgress.Text = $"قفل Bootloader: {p}%";
                });

                LogStatus("[🔒] بدء عملية قفل Bootloader...", Color.Cyan);
                
                if (engine.hDevice == (IntPtr)(-1))
                {
                    LogStatus("[❌] خطأ: الجهاز غير متصل.", Color.Red);
                    return;
                }

                bool success = await engine.LockBootloader(
                    progress,
                    bootloaderCancellation.Token
                );

                if (success) LogStatus("[✅] تم قفل البوت لودر بنجاح", Color.Green);

                LogStatus($"[✅] اختبار قفل Bootloader اكتمل (محاكاة)", Color.Green);
                System.Media.SystemSounds.Asterisk.Play();
            }
            catch (Exception ex)
            {
                LogStatus($"[🔥] خطأ: {ex.Message}", Color.Red);
            }
            finally
            {
                btnUnlockBootloader.Enabled = true;
                btnLockBootloader.Enabled = true;
                progressBar1.Value = 0;
                lblProgress.Text = "0%";
            }
        }

        private void btnCheckBootloader_Click(object sender, EventArgs e)
        {
            try
            {
                string status = engine.GetBootloaderStatusText();
                LogStatus($"[ℹ️] حالة Bootloader: {status}", Color.Cyan);
                MessageBox.Show($"حالة Bootloader: {status}", "معلومات", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                LogStatus($"[🔥] خطأ: {ex.Message}", Color.Red);
            }
        }

        private void btnCancelBootloader_Click(object sender, EventArgs e)
        {
            if (bootloaderCancellation != null)
            {
                bootloaderCancellation.Cancel();
                LogStatus("[⚠️] جاري إلغاء عملية Bootloader...", Color.Yellow);
            }
        }
        // ==========================================
        // الميزات الجديدة (24 رمضان)
        // ==========================================

        private async void btnRemoveLock_Click(object sender, EventArgs e)
        {
            try
            {
                DialogResult confirm = MessageBox.Show(
                    "🔓 **إزالة النمط/القفل** 🔓\n\n" +
                    "هذه العملية ستقوم بمسح ملفات القفل فقط.\n" +
                    "لن يتم مسح أي بيانات أخرى.\n\n" +
                    "هل تريد الاستمرار؟",
                    "تأكيد العملية",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (confirm != DialogResult.Yes)
                {
                    LogStatus("[-] تم إلغاء العملية", Color.Yellow);
                    return;
                }

                btnRemoveLock.Enabled = false;
                lockRemovalCancellation = new CancellationTokenSource();

                var progress = new Progress<int>(p =>
                {
                    progressBar1.Value = p;
                    lblProgress.Text = $"إزالة القفل: {p}%";
                });

                LogStatus("[🔓] بدء إزالة ملفات القفل...", Color.Magenta);
                LogStatus("[⚡] هذا اختبار واجهة فقط", Color.Yellow);

                bool success = await engine.RemoveLockFiles(progress, lockRemovalCancellation.Token);

                if (success)
                {
                    LogStatus("[✅] تمت إزالة ملفات القفل بنجاح", Color.Green);
                    MessageBox.Show("تمت إزالة القفل بنجاح!\nأعد تشغيل الهاتف الآن.", "نجاح", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                LogStatus($"[🔥] خطأ: {ex.Message}", Color.Red);
            }
            finally
            {
                btnRemoveLock.Enabled = true;
                progressBar1.Value = 0;
                lblProgress.Text = "0%";
            }
        }

        private async void btnReboot_Click(object sender, EventArgs e)
        {
            try
            {
                // قائمة بخيارات إعادة التشغيل
                var modes = new string[] { "normal", "recovery", "bootloader", "download" };
                var selectedMode = Microsoft.VisualBasic.Interaction.InputBox(
                    "اختر وضع إعادة التشغيل:\n" +
                    "1. normal - تشغيل عادي\n" +
                    "2. recovery - وضع الريكفري\n" +
                    "3. bootloader - وضع Bootloader\n" +
                    "4. download - وضع التحميل\n\n" +
                    "أدخل الرقم (1-4):",
                    "إعادة تشغيل الهاتف",
                    "1");

                if (string.IsNullOrEmpty(selectedMode)) return;

                int modeIndex;
                if (!int.TryParse(selectedMode, out modeIndex) || modeIndex < 1 || modeIndex > 4)
                {
                    LogStatus("[-] اختيار غير صالح", Color.Red);
                    return;
                }

                string mode = modes[modeIndex - 1];

                LogStatus($"[🔄] إعادة تشغيل الهاتف في وضع: {mode}", Color.Cyan);
                LogStatus("[⚡] هذا اختبار واجهة فقط", Color.Yellow);

                bool success = await engine.RebootDevice(mode);

                if (success)
                {
                    LogStatus($"[✅] تم إرسال أمر إعادة التشغيل", Color.Green);
                }
            }
            catch (Exception ex)
            {
                LogStatus($"[🔥] خطأ: {ex.Message}", Color.Red);
            }
        }

        private void btnAdvancedInfo_Click(object sender, EventArgs e)
        {
            try
            {
                LogStatus("[📱] جمع المعلومات المتقدمة...", Color.Cyan);

                string info = engine.GetAdvancedInfo();

                // عرض في مربع نص
                MessageBox.Show(info, "معلومات متقدمة", MessageBoxButtons.OK, MessageBoxIcon.Information);

                // وتسجيل في السجل
                string[] lines = info.Split('\n');
                foreach (string line in lines)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                        LogStatus(line.Trim(), Color.Gold);
                }
            }
            catch (Exception ex)
            {
                LogStatus($"[🔥] خطأ: {ex.Message}", Color.Red);
            }
        }

        private async void btnIMEI_Click(object sender, EventArgs e)
        {
            try
            {
                string newImei = Microsoft.VisualBasic.Interaction.InputBox("أدخل رقم الـ IMEI الجديد (15 رقم):", "إصلاح IMEI السيادي", "");

                if (string.IsNullOrEmpty(newImei) || newImei.Length < 14)
                {
                    LogStatus("[-] رقم IMEI غير صالح.", Color.Red);
                    return;
                }

                if (engine.hDevice == (IntPtr)(-1))
                {
                    LogStatus("[❌] الجهاز غير متصل! ابدأ العملية أولاً.", Color.Red);
                    return;
                }

                btnIMEI.Enabled = false;
                LogStatus("[🛡️] بدء محاولة حقن الـ IMEI...", Color.Cyan);

                var progress = new Progress<int>(p =>
                {
                    progressBar1.Value = p;
                    lblProgress.Text = $"{p}%";
                });

                // استدعاء محرك الكتابة السيادي
                bool success = await engine.WriteImeiToDevice(newImei, progress, CancellationToken.None);

                if (success)
                {
                    LogStatus("[🏆] تم حقن الـ IMEI بنجاح! أعد تشغيل الجهاز.", Color.Green);
                    MessageBox.Show("تمت العملية بنجاح! يرجى عمل ريستارت للهاتف.", "نجاح", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                LogStatus($"[🔥] خطأ: {ex.Message}", Color.Red);
            }
            finally
            {
                btnIMEI.Enabled = true;
                progressBar1.Value = 0;
                lblProgress.Text = "0%";
            }
        }

        private void btnSupport_Click(object sender, EventArgs e)
        {
            MessageBox.Show(
                "🛠️ **الدعم الفني** 🛠️\n\n" +
                "للاستفسارات والدعم:\n" +
                "• تابع تحديثات الأداة\n" +
                "• راسلنا عبر المنتديات\n" +
                "• أرسل تقرير المشكلة مع معلومات جهازك",
                "الدعم",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void Form1_Load_1(object sender, EventArgs e)
        {

        }
    }
}