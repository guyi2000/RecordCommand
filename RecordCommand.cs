using Autodesk.Internal.Windows;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Autodesk.Windows;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Commands
{
    [TransactionAttribute(TransactionMode.Manual)]
    [RegenerationAttribute(RegenerationOption.Manual)]
    public class RecordCommands : IExternalApplication
    {
        private MouseEventHandler MouseMoveEventHandler = null;
        private MouseEventHandler MouseClickEventHandler = null;
        private MouseEventHandler MouseWhellEventHandler = null;
        private readonly MouseHook mouseHook = new MouseHook();

        private KeyEventHandler KeyboardUpEventHandler = null;
        private KeyEventHandler KeyboardDownEventHandler = null;
        private readonly KeyboardHook keyboardHook = new KeyboardHook();

        private Thread GetScreenThread = null;

        private StreamWriter psw = null;
        private StreamWriter msw = null;
        private StreamWriter mmsw = null;
        private StreamWriter ksw = null;
        private SocketStream client = null;

        public static RecordCommands thisApp = null;
        public Result OnStartup(UIControlledApplication application)
        {
            thisApp = this;
            try
            {
                ComponentManager.ItemExecuted +=
                    new EventHandler<RibbonItemExecutedEventArgs>(CommandExecuted);
                application.ControlledApplication.DocumentChanged +=
                    new EventHandler<DocumentChangedEventArgs>(DocumentChangeTracker);
                application.ControlledApplication.FailuresProcessing +=
                    new EventHandler<FailuresProcessingEventArgs>(FailureTracker);
                application.ControlledApplication.DocumentOpened +=
                    new EventHandler<DocumentOpenedEventArgs>(DocumentOpenedTracker);
                // Command Document Failure Tracker start

                if (!Directory.Exists("D:\\revit"))
                    Directory.CreateDirectory("D:\\revit");
                if (!Directory.Exists("D:\\revit\\journal"))
                    Directory.CreateDirectory("D:\\revit\\journal");
                if (!Directory.Exists("D:\\revit\\picture"))
                    Directory.CreateDirectory("D:\\revit\\picture");
                // Working Directory Create
            }
            catch (Exception)
            {
                return Result.Failed;
            }

            try
            {
                client = new SocketStream();
                // client.Start(new IPEndPoint(IPAddress.Parse("192.168.0.3"), 9090));
            }
            catch (Exception)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Warning", "Can't connect to server!\n" +
                                                             "Using Offline mode!");
            }

            try
            {
                string tempFolder = "D:\\revit\\journal";
                string outputFile = Path.Combine(tempFolder, "Project.log");
                psw = new StreamWriter(outputFile, true);
                outputFile = Path.Combine(tempFolder, "Mouse.log");
                msw = new StreamWriter(outputFile, true);
                outputFile = Path.Combine(tempFolder, "MouseMove.log");
                mmsw = new StreamWriter(outputFile, true);
                outputFile = Path.Combine(tempFolder, "Keyboard.log");
                ksw = new StreamWriter(outputFile, true);

                DateTime now = DateTime.Now;
                psw.WriteLine("Revit started up at " + (now.Ticks / 10000).ToString() + ".");
                client.WriteLine("Revit started up at " + (now.Ticks / 10000).ToString() + ".");

                GetScreenThread = new Thread(new ThreadStart(GetScreen));
                GetScreenThread.Start();
                StartListen();
            }
            catch (Exception)
            {
                return Result.Failed;
            }
            return Result.Succeeded;
        }
        public Result OnShutdown(UIControlledApplication application)
        {
            application.ControlledApplication.DocumentOpened -= DocumentOpenedTracker;
            application.ControlledApplication.DocumentChanged -= DocumentChangeTracker;
            application.ControlledApplication.FailuresProcessing -= FailureTracker;
            ComponentManager.ItemExecuted -= CommandExecuted;
            // Command Document Failure Tracker stop

            DateTime now = DateTime.Now;
            psw.WriteLine("Revit shut down at " + (now.Ticks / 10000).ToString() + ".");
            client.WriteLine("Revit shut down at " + (now.Ticks / 10000).ToString() + ".");
            GetScreenThread.Abort();
            StopListen(); // mouse and keyboard tracker stop
            client.Stop();

            psw.Flush();
            msw.Flush();
            mmsw.Flush();
            ksw.Flush();

            psw.Close();
            msw.Close();
            mmsw.Close();
            ksw.Close();

            return Result.Succeeded;
        }
        private void CommandExecuted(object sender, RibbonItemExecutedEventArgs args)
        {
            try
            {
                Autodesk.Windows.RibbonItem it = args.Item;
                if (args != null)
                {
                    DateTime now = DateTime.Now;
                    psw.WriteLine("Command, " + (now.Ticks / 10000).ToString() + ", " + it.ToString() +
                                    ", " + it.Id + ", " + it.Cookie);
                    client.WriteLine("Command, " + (now.Ticks / 10000).ToString() + ", " + it.ToString() +
                                    ", " + it.Id + ", " + it.Cookie);
                }
            }
            catch (Exception ex)
            {
                Autodesk.Revit.UI.TaskDialog.Show("Add-In Fail", ex.Message);
            }
        }
        public void DocumentChangeTracker(object sender, DocumentChangedEventArgs args)
        {
            Autodesk.Revit.ApplicationServices.Application app =
                sender as Autodesk.Revit.ApplicationServices.Application;
            UIApplication uiapp = new UIApplication(app);
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;
            Autodesk.Revit.DB.View currentView = uidoc.ActiveView;
            string user = doc.Application.Username;

            Selection sel = uidoc.Selection;
            ICollection<ElementId> deletedElements = args.GetDeletedElementIds();
            ICollection<ElementId> changedElements = args.GetModifiedElementIds();
            ICollection<ElementId> addedElements = args.GetAddedElementIds();
            ICollection<ElementId> selectedIds = sel.GetElementIds();
            DateTime now = DateTime.Now;
            if (deletedElements.Count != 0)
            {
                foreach (ElementId id in deletedElements)
                {
                    psw.WriteLine("ElementChange, " +
                        user + ", " + (now.Ticks / 10000).ToString() + ", Deleted, " +
                        id.IntegerValue);
                    client.WriteLine("ElementChange, " +
                        user + ", " + (now.Ticks / 10000).ToString() + ", Deleted, " +
                        id.IntegerValue);
                }
            }
            if (addedElements.Count != 0)
            {
                foreach (ElementId id in addedElements)
                {
                    BoundingBoxXYZ box = doc.GetElement(id).get_BoundingBox(null);
                    psw.WriteLine("ElementChange, " + user + ", " + (now.Ticks / 10000).ToString() + ", ADDED, " +
                        doc.GetElement(id).Category.Name + ", " + box.Min.ToString() +
                        ", " + box.Max.ToString() + ", " + doc.GetElement(id).GetType().Name +
                        ", " + doc.GetElement(id).Name + ", " + id.IntegerValue +
                        ", " + currentView.Name + ", " + doc.PathName);
                    client.WriteLine("ElementChange, " + user + ", " + (now.Ticks / 10000).ToString() + ", ADDED, " +
                        doc.GetElement(id).Category.Name + ", " + box.Min.ToString() +
                        ", " + box.Max.ToString() + ", " + doc.GetElement(id).GetType().Name +
                        ", " + doc.GetElement(id).Name + ", " + id.IntegerValue +
                        ", " + currentView.Name + ", " + doc.PathName);
                }
            }
            if (changedElements.Count != 0)
            {
                foreach (ElementId id in changedElements)
                {
                    if (selectedIds.Contains(id))
                    {
                        BoundingBoxXYZ box = doc.GetElement(id).get_BoundingBox(null);
                        psw.WriteLine("ElementChange, " + user + ", " + (now.Ticks / 10000).ToString() + ", MODIFIED, " +
                            doc.GetElement(id).Category.Name + ", " + box.Min.ToString() +
                            ", " + box.Max.ToString() + ", " + doc.GetElement(id).GetType().Name +
                            ", " + doc.GetElement(id).Name + ", " + id.IntegerValue +
                            ", " + currentView.Name + ", " + doc.PathName);
                        client.WriteLine("ElementChange, " + user + ", " + (now.Ticks / 10000).ToString() + ", MODIFIED, " +
                            doc.GetElement(id).Category.Name + ", " + box.Min.ToString() +
                            ", " + box.Max.ToString() + ", " + doc.GetElement(id).GetType().Name +
                            ", " + doc.GetElement(id).Name + ", " + id.IntegerValue +
                            ", " + currentView.Name + ", " + doc.PathName);
                    } 
                }
            }
        }
        public void DocumentOpenedTracker(object sender, DocumentOpenedEventArgs e)
        {
            Document doc = e.Document;
            string pathname = doc.PathName;
            DateTime now = DateTime.Now;
            psw.WriteLine("Opened " + pathname + "at " + (now.Ticks / 10000).ToString() + ".");
            client.WriteLine("Opened " + pathname + "at " + (now.Ticks / 10000).ToString() + ".");
        }
        private void FailureTracker(object sender, FailuresProcessingEventArgs e)
        {
            Autodesk.Revit.ApplicationServices.Application app =
                sender as Autodesk.Revit.ApplicationServices.Application;
            UIApplication uiapp = new UIApplication(app);
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            string user = doc.Application.Username;
            FailuresAccessor failuresAccessor = e.GetFailuresAccessor();
            IList<FailureMessageAccessor> fmas =
                failuresAccessor.GetFailureMessages();
            failuresAccessor.JournalFailures(fmas);

            DateTime now = DateTime.Now;
            if (fmas.Count != 0)
            {
                foreach (FailureMessageAccessor fma in fmas)
                {
                    if (failuresAccessor.GetSeverity() == FailureSeverity.Error)
                    {
                        psw.WriteLine("Error, " + user + ", " + (now.Ticks / 10000).ToString() + ", " +
                            failuresAccessor.GetTransactionName() +
                            ", " + fma.GetDescriptionText() +
                            ", " + fma.GetFailureDefinitionId());
                        client.WriteLine("Error, " + user + ", " + (now.Ticks / 10000).ToString() + ", " +
                            failuresAccessor.GetTransactionName() +
                            ", " + fma.GetDescriptionText() +
                            ", " + fma.GetFailureDefinitionId());
                    }
                    else
                    {
                        psw.WriteLine("Warning, " + user + ", " + (now.Ticks / 10000).ToString() + ", " +
                            failuresAccessor.GetTransactionName() +
                            ", " + fma.GetDescriptionText() +
                            ", " + fma.GetFailureDefinitionId());
                        client.WriteLine("Warning, " + user + ", " + (now.Ticks / 10000).ToString() + ", " +
                            failuresAccessor.GetTransactionName() +
                            ", " + fma.GetDescriptionText() +
                            ", " + fma.GetFailureDefinitionId());
                    }
                }
            }
        }
        public void GetScreen()
        {
            while (true)
            {
                System.Drawing.Rectangle ScreenArea = Screen.PrimaryScreen.Bounds;
                Bitmap bmp = new Bitmap(ScreenArea.Width, ScreenArea.Height);
                string tempFolder = "D:\\revit\\picture";
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(0, 0, 0, 0, new Size(ScreenArea.Width, ScreenArea.Height));
                    string dateTime = DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss");
                    string outputFile = dateTime + ".jpg";
                    bmp.Save(Path.Combine(tempFolder, outputFile));
                }
                Thread.Sleep(5000);
            }
        }
        public void HookMouseMove(object sender, MouseEventArgs e)
        {
            DateTime now = DateTime.Now;
            mmsw.WriteLine("Mouse, " + (now.Ticks / 10000).ToString() + ", " + e.X + ", " + e.Y + ", Move");
            //client.WriteLine("Mouse, " + (now.Ticks / 10000).ToString() + ", " + e.X + ", " + e.Y + ", Move");
        }
        public void HookMouseWhell(object sender, MouseEventArgs e)
        {
            DateTime now = DateTime.Now;
            msw.WriteLine("Mouse, " + (now.Ticks / 10000).ToString() + ", " + e.X + ", " + e.Y + ", Whell, " + e.Delta);
            //client.WriteLine("Mouse, " + (now.Ticks / 10000).ToString() + ", " + e.X + ", " + e.Y + ", Whell, " + e.Delta);
        }
        public void HookMouseClick(object sender, MouseEventArgs e)
        {
            DateTime now = DateTime.Now;
            if (e.Clicks == 1)
            {
                msw.WriteLine("Mouse, " + (now.Ticks / 10000).ToString() + ", " + e.X + ", " + e.Y + ", " + e.Button + ", Down");
                client.WriteLine("Mouse, " + (now.Ticks / 10000).ToString() + ", " + e.X + ", " + e.Y + ", " + e.Button + ", Down");
            }
            else if (e.Clicks == 0)
            {
                msw.WriteLine("Mouse, " + (now.Ticks / 10000).ToString() + ", " + e.X + ", " + e.Y + ", " + e.Button + ", Up");
                client.WriteLine("Mouse, " + (now.Ticks / 10000).ToString() + ", " + e.X + ", " + e.Y + ", " + e.Button + ", Up");
            }
            else
            {
                msw.WriteLine("Mouse, " + (now.Ticks / 10000).ToString() + ", " + e.X + ", " + e.Y + ", " + e.Button + ", Double");
                client.WriteLine("Mouse, " + (now.Ticks / 10000).ToString() + ", " + e.X + ", " + e.Y + ", " + e.Button + ", Double");
            }
        }
        public void HookKeyboardDown(object sender, KeyEventArgs e)
        {
            DateTime now = DateTime.Now;
            ksw.WriteLine("Keyboard, " + (now.Ticks / 10000).ToString() + ", " + e.KeyValue.ToString() + ", Down");
            client.WriteLine("Keyboard, " + (now.Ticks / 10000).ToString() + ", " + e.KeyValue.ToString() + ", Down");
        }
        public void HookKeyboardUp(object sender, KeyEventArgs e)
        {
            DateTime now = DateTime.Now;
            ksw.WriteLine("Keyboard, " + (now.Ticks / 10000).ToString() + ", " + e.KeyValue.ToString() + ", Up");
            client.WriteLine("Keyboard, " + (now.Ticks / 10000) + ", " + e.KeyValue.ToString() + ", Up");
        }
        public void StartListen()
        {
            MouseMoveEventHandler = new MouseEventHandler(HookMouseMove);
            MouseClickEventHandler = new MouseEventHandler(HookMouseClick);
            MouseWhellEventHandler = new MouseEventHandler(HookMouseWhell);

            KeyboardUpEventHandler = new KeyEventHandler(HookKeyboardUp);
            KeyboardDownEventHandler = new KeyEventHandler(HookKeyboardDown);

            mouseHook.MouseMoveEvent += MouseMoveEventHandler;
            mouseHook.MouseClickEvent += MouseClickEventHandler;
            mouseHook.MouseWhellEvent += MouseWhellEventHandler;

            keyboardHook.KeyUpEvent += KeyboardUpEventHandler;
            keyboardHook.KeyDownEvent += KeyboardDownEventHandler;

            mouseHook.Start();
            keyboardHook.Start();
        }
        public void StopListen()
        {
            if (MouseMoveEventHandler != null)
            {
                mouseHook.MouseMoveEvent -= MouseMoveEventHandler;
                MouseMoveEventHandler = null;
            }
            if (MouseClickEventHandler != null)
            {
                mouseHook.MouseClickEvent -= MouseClickEventHandler;
                MouseClickEventHandler = null;
            }
            if (MouseWhellEventHandler != null)
            {
                mouseHook.MouseWhellEvent -= MouseWhellEventHandler;
                MouseWhellEventHandler = null;
            }
            if (KeyboardUpEventHandler != null)
            {
                keyboardHook.KeyUpEvent -= KeyboardUpEventHandler;
                KeyboardUpEventHandler = null;
            }
            if (KeyboardDownEventHandler != null)
            {
                keyboardHook.KeyDownEvent -= KeyboardDownEventHandler;
                KeyboardDownEventHandler = null;
            }
            mouseHook.Stop();
            keyboardHook.Stop();
        }
    }

    public class KeyboardHook
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x100;
        private const int WM_KEYUP = 0x101;
        private const int WM_SYSKEYDOWN = 0x104;
        private const int WM_SYSKEYUP = 0x105;
        static int hKeyboardHook = 0;

        public event KeyEventHandler KeyDownEvent;
        public event KeyPressEventHandler KeyPressEvent;
        public event KeyEventHandler KeyUpEvent;

        public delegate int HookProc(
            int nCode,
            Int32 wParam,
            IntPtr lParam);

        HookProc KeyboardHookProcedure;

        [StructLayout(LayoutKind.Sequential)]
        public class KeyboardHookStruct
        {
            public int vkCode;
            public int scanCode;
            public int flags;
            public int time;
            public int dwExtraInfo;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto,
            CallingConvention = CallingConvention.StdCall,
            SetLastError = true)]
        public static extern int SetWindowsHookEx(
            int idHook,
            HookProc lpfn,
            IntPtr hInstance,
            int threadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto,
            CallingConvention = CallingConvention.StdCall,
            SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(int idHook);

        [DllImport("user32.dll", CharSet = CharSet.Auto,
            CallingConvention = CallingConvention.StdCall)]
        public static extern int CallNextHookEx(
            int idHook,
            int nCode,
            Int32 wParam,
            IntPtr lParam);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetModuleHandle(string name);

        public void Start()
        {
            if (hKeyboardHook == 0)
            {
                KeyboardHookProcedure = new HookProc(KeyboardHookProc);
                hKeyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL,
                                                 KeyboardHookProcedure,
                                                 GetModuleHandle("user32"),
                                                 0);
                if (hKeyboardHook == 0)
                {
                    Stop();
                    throw new Exception("Keyboard Hook FAILED!");
                }
            }
        }

        public void Stop()
        {
            bool retKeyboard = true;
            if (hKeyboardHook != 0)
            {
                retKeyboard = UnhookWindowsHookEx(hKeyboardHook);
                hKeyboardHook = 0;
            }
            if (!(retKeyboard)) throw new Exception("Keyboard UnHook FAILED!");
        }

        [DllImport("user32")]
        public static extern int ToAscii(int uVirtKey,
                                         int uScanCode,
                                         byte[] lpbKeyState,
                                         byte[] lpwTransKey,
                                         int fuState);
        [DllImport("user32")]
        public static extern int GetKeyboardState(byte[] pbKeyState);

        [DllImport("user32.dll",
            CharSet = CharSet.Auto,
            CallingConvention = CallingConvention.StdCall)]
        private static extern short GetKeyState(int vKey);

        private int KeyboardHookProc(int nCode, Int32 wParam, IntPtr lParam)
        {
            if ((nCode >= 0) &&
                (KeyDownEvent != null ||
                   KeyUpEvent != null ||
                KeyPressEvent != null))
            {
                KeyboardHookStruct MyKeyboardHookStruct =
                    (KeyboardHookStruct)Marshal.PtrToStructure(
                        lParam,
                        typeof(KeyboardHookStruct));
                if (KeyDownEvent != null &&
                    (wParam == WM_KEYDOWN ||
                     wParam == WM_SYSKEYDOWN))
                {
                    Keys keyData = (Keys)MyKeyboardHookStruct.vkCode;
                    KeyEventArgs e = new KeyEventArgs(keyData);
                    KeyDownEvent(this, e);
                }
                if (KeyPressEvent != null && wParam == WM_KEYDOWN)
                {
                    byte[] keyState = new byte[256];
                    GetKeyboardState(keyState);

                    byte[] inBuffer = new byte[2];
                    if (ToAscii(MyKeyboardHookStruct.vkCode,
                                MyKeyboardHookStruct.scanCode,
                                keyState,
                                inBuffer,
                                MyKeyboardHookStruct.flags) == 1)
                    {
                        KeyPressEventArgs e = new KeyPressEventArgs((char)inBuffer[0]);
                        KeyPressEvent(this, e);
                    }
                }
                if (KeyUpEvent != null &&
                    (wParam == WM_KEYUP ||
                     wParam == WM_SYSKEYUP))
                {
                    Keys keyData = (Keys)MyKeyboardHookStruct.vkCode;
                    KeyEventArgs e = new KeyEventArgs(keyData);
                    KeyUpEvent(this, e);
                }
            }
            return CallNextHookEx(hKeyboardHook, nCode, wParam, lParam);
        }
        ~KeyboardHook() { Stop(); }
    }

    public class MouseHook
    {
        public const int WH_MOUSE_LL = 14;
        private const int WM_MOUSEMOVE = 0x200;
        private const int WM_LBUTTONDOWN = 0x201;
        private const int WM_RBUTTONDOWN = 0x204;
        private const int WM_MBUTTONDOWN = 0x207;
        private const int WM_LBUTTONUP = 0x202;
        private const int WM_RBUTTONUP = 0x205;
        private const int WM_MBUTTONUP = 0x208;
        private const int WM_LBUTTONDBLCLK = 0x203;
        private const int WM_RBUTTONDBLCLK = 0x206;
        private const int WM_MBUTTONDBLCLK = 0x209;
        private const int WM_MOUSEWHELL = 0x20A;

        static int hMouseHook = 0;

        public event MouseEventHandler MouseClickEvent;
        public event MouseEventHandler MouseMoveEvent;
        public event MouseEventHandler MouseWhellEvent;

        public delegate int HookProc(
            int nCode,
            Int32 wParam,
            IntPtr lParam);

        HookProc MouseHookProcedure;

        [StructLayout(LayoutKind.Sequential)]
        public class POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public class MouseHookStruct
        {
            public POINT pt;
            public int mouseData;
            public int flags;
            public int time;
            public int dwExtraInfo;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto,
            CallingConvention = CallingConvention.StdCall,
            SetLastError = true)]
        public static extern int SetWindowsHookEx(
            int idHook,
            HookProc lpfn,
            IntPtr hInstance,
            int threadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto,
            CallingConvention = CallingConvention.StdCall,
            SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(int idHook);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll", CharSet = CharSet.Auto,
            CallingConvention = CallingConvention.StdCall)]
        public static extern int CallNextHookEx(int idHook, int nCode, Int32 wParam, IntPtr lParam);

        ~MouseHook() { Stop(); }

        public void Start()
        {
            if (hMouseHook == 0)
            {
                MouseHookProcedure = new HookProc(MouseHookProc);
                hMouseHook = SetWindowsHookEx(
                    WH_MOUSE_LL,
                    MouseHookProcedure,
                    GetModuleHandle("user32"),
                    0);
                if (hMouseHook == 0)
                {
                    Stop();
                    throw new Exception("Mouse Hook FAILED!");
                }
            }
        }
        public void Stop()
        {
            bool retMouse = true;
            if (hMouseHook != 0)
            {
                retMouse = UnhookWindowsHookEx(hMouseHook);
                hMouseHook = 0;
            }
            if (!(retMouse)) throw new Exception("Mouse UnHook FAILED!");
        }

        private int MouseHookProc(int nCode, Int32 wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (MouseClickEvent != null ||
                MouseMoveEvent != null || MouseWhellEvent != null))
            {
                MouseHookStruct MyMouseHookStruct =
                    (MouseHookStruct)Marshal.PtrToStructure(
                        lParam,
                        typeof(MouseHookStruct));

                if (wParam == WM_MOUSEMOVE && (nCode >= 0) && (MouseMoveEvent != null))
                {
                    MouseEventArgs e = new MouseEventArgs(
                            MouseButtons.None,
                            0,
                            MyMouseHookStruct.pt.x,
                            MyMouseHookStruct.pt.y,
                            0);
                    MouseMoveEvent(this, e);
                }

                if (wParam == WM_MOUSEWHELL && (nCode >= 0) && (MouseWhellEvent != null))
                {
                    MouseEventArgs e = new MouseEventArgs(
                            MouseButtons.None,
                            0,
                            MyMouseHookStruct.pt.x,
                            MyMouseHookStruct.pt.y,
                            (short)((MyMouseHookStruct.mouseData >> 16) & 0xffff));
                    MouseWhellEvent(this, e);
                }

                if ((wParam == WM_LBUTTONUP || wParam == WM_LBUTTONDOWN ||
                     wParam == WM_RBUTTONUP || wParam == WM_RBUTTONDOWN ||
                     wParam == WM_MBUTTONUP || wParam == WM_MBUTTONDOWN ||
                     wParam == WM_LBUTTONDBLCLK || wParam == WM_RBUTTONDBLCLK ||
                     wParam == WM_MBUTTONDBLCLK) &&
                     (nCode >= 0) && (MouseClickEvent != null))
                {
                    MouseButtons button = MouseButtons.None;
                    int clickCount = 0;
                    switch (wParam)
                    {
                        case WM_LBUTTONDOWN:
                            button = MouseButtons.Left;
                            clickCount = 1;
                            break;
                        case WM_LBUTTONUP:
                            button = MouseButtons.Left;
                            clickCount = 0;
                            break;
                        case WM_LBUTTONDBLCLK:
                            button = MouseButtons.Left;
                            clickCount = 2;
                            break;
                        case WM_RBUTTONDOWN:
                            button = MouseButtons.Right;
                            clickCount = 1;
                            break;
                        case WM_RBUTTONUP:
                            button = MouseButtons.Right;
                            clickCount = 0;
                            break;
                        case WM_RBUTTONDBLCLK:
                            button = MouseButtons.Right;
                            clickCount = 2;
                            break;
                        case WM_MBUTTONDOWN:
                            button = MouseButtons.Middle;
                            clickCount = 1;
                            break;
                        case WM_MBUTTONUP:
                            button = MouseButtons.Middle;
                            clickCount = 0;
                            break;
                        case WM_MBUTTONDBLCLK:
                            button = MouseButtons.Middle;
                            clickCount = 2;
                            break;
                    }
                    MouseEventArgs e = new MouseEventArgs(
                        button,
                        clickCount,
                        MyMouseHookStruct.pt.x,
                        MyMouseHookStruct.pt.y,
                        0);
                    MouseClickEvent(this, e);
                }
            }
            return CallNextHookEx(hMouseHook, nCode, wParam, lParam);
        }
    }
}