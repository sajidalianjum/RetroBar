using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using ManagedShell.Common.Helpers;
using ManagedShell.Interop;
using ManagedShell.WindowsTasks;
using RetroBar.Converters;
using RetroBar.Utilities;

namespace RetroBar.Controls
{
    /// <summary>
    /// Interaction logic for TaskButton.xaml
    /// </summary>
    public partial class TaskButton : UserControl
    {
        // Variable Declarations
        private ApplicationWindow Window;
        private TaskButtonStyleConverter StyleConverter = new TaskButtonStyleConverter();
        private ApplicationWindow.WindowState PressedWindowState = ApplicationWindow.WindowState.Inactive;
        private bool _isLoaded;
        private bool inDrag;
        private DispatcherTimer dragTimer;

        // P/Invoke Declarations
        [DllImport("user32.dll")]
        static extern int EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        // Delegate types
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        public TaskButton()
        {
            InitializeComponent();
            SetStyle();
        }

        private void SetStyle()
        {
            MultiBinding multiBinding = new MultiBinding
            {
                Converter = StyleConverter
            };

            multiBinding.Bindings.Add(new Binding { RelativeSource = RelativeSource.Self });
            multiBinding.Bindings.Add(new Binding("State"));

            AppButton.SetBinding(StyleProperty, multiBinding);
        }

        private void ScrollIntoView()
        {
            if (Window == null) return;

            if (Window.State == ApplicationWindow.WindowState.Active)
            {
                BringIntoView();
            }
        }

        private void TaskButton_OnLoaded(object sender, RoutedEventArgs e)
        {
            Window = DataContext as ApplicationWindow;

            Settings.Instance.PropertyChanged += Settings_PropertyChanged;

            // drag support - delayed activation using system setting
            dragTimer = new DispatcherTimer { Interval = SystemParameters.MouseHoverTime };
            dragTimer.Tick += dragTimer_Tick;

            if (Window != null)
            {
                Window.PropertyChanged += Window_PropertyChanged;
            }

            _isLoaded = true;
        }

        private void Window_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "State")
            {
                ScrollIntoView();
            }
        }

        private void TaskButton_OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;

            Settings.Instance.PropertyChanged -= Settings_PropertyChanged;

            if (Window != null)
            {
                Window.PropertyChanged -= Window_PropertyChanged;
            }

            _isLoaded = false;

            BringForegroundAppToFocus();
        }

        private void AppButton_OnContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (Window == null) return;

            NativeMethods.WindowShowStyle wss = Window.ShowStyle;
            int ws = Window.WindowStyles;

            // Disable window operations depending on current window state
            MaximizeMenuItem.IsEnabled = (wss != NativeMethods.WindowShowStyle.ShowMaximized && (ws & (int)NativeMethods.WindowStyles.WS_MAXIMIZEBOX) != 0);
            MinimizeMenuItem.IsEnabled = (wss != NativeMethods.WindowShowStyle.ShowMinimized && (ws & (int)NativeMethods.WindowStyles.WS_MINIMIZEBOX) != 0);
            RestoreMenuItem.IsEnabled = (wss != NativeMethods.WindowShowStyle.ShowNormal);

            if (RestoreMenuItem.IsEnabled)
            {
                CloseMenuItem.FontWeight = FontWeights.Normal;
                RestoreMenuItem.FontWeight = FontWeights.Bold;
            }

            if (!RestoreMenuItem.IsEnabled || (RestoreMenuItem.IsEnabled && !MaximizeMenuItem.IsEnabled))
            {
                CloseMenuItem.FontWeight = FontWeights.Bold;
                RestoreMenuItem.FontWeight = FontWeights.Normal;
            }

            MoveMenuItem.IsEnabled = wss == NativeMethods.WindowShowStyle.ShowNormal;
            SizeMenuItem.IsEnabled = (wss == NativeMethods.WindowShowStyle.ShowNormal && (ws & (int)NativeMethods.WindowStyles.WS_MAXIMIZEBOX) != 0);
        }

        private void CloseMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            Window?.Close();
        }

        private void RestoreMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            Window?.Restore();
        }

        private void MoveMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            Window?.Move();
        }

        private void SizeMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            Window?.Size();
        }

        private void MinimizeMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            Window?.Minimize();
            BringForegroundAppToFocus();
        }

        private void MaximizeMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            Window?.Maximize();
        }

        private void AppButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (PressedWindowState == ApplicationWindow.WindowState.Active)
            {
                Window?.Minimize();
                BringForegroundAppToFocus();
            }
            else
            {
                Window?.BringToFront();
            }
        }

        private void AppButton_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                PressedWindowState = Window.State;
            }
        }

        private void AppButton_OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                if (Window == null || Settings.Instance.TaskMiddleClickAction == TaskMiddleClickOption.DoNothing)
                {
                    return;
                }

                if (Settings.Instance.TaskMiddleClickAction == TaskMiddleClickOption.CloseTask)
                {
                    Window?.Close();
                }
                else
                {
                    ShellHelper.StartProcess(Window.IsUWP ? "appx:" + Window.AppUserModelID : Window.WinFileName);
                }
            }
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.Theme))
            {
                SetStyle();
            }
        }

        #region Drag
        private void dragTimer_Tick(object sender, EventArgs e)
        {
            if (inDrag)
            {
                Window?.BringToFront();
            }

            dragTimer.Stop();
        }

        private void AppButton_OnDragEnter(object sender, DragEventArgs e)
        {
            // Ignore drag operations from a reorder
            if (!inDrag && !e.Data.GetDataPresent("GongSolutions.Wpf.DragDrop"))
            {
                inDrag = true;
                dragTimer.Start();
            }
        }

        private void AppButton_OnDragLeave(object sender, DragEventArgs e)
        {
            if (inDrag)
            {
                dragTimer.Stop();
                inDrag = false;
            }
        }
        #endregion

        private void BringForegroundAppToFocus()
        {
            IntPtr nextWindow = IntPtr.Zero;

            EnumWindows(delegate (IntPtr hWnd, IntPtr lParam)
            {
                // Skip the current foreground window and invisible or minimized windows
                if (IsWindowVisible(hWnd) && !IsIconic(hWnd))
                {
                    StringBuilder title = new StringBuilder(256);
                    GetWindowText(hWnd, title, title.Capacity);
                    Console.WriteLine($"Switched to window: {title}");

                    if (title.ToString() != "RetroBar Taskbar" && title.ToString().Length > 0)
                    {
                        nextWindow = hWnd;
                        return false; // Stop enumeration
                    }
                }
                return true; // Continue enumeration
            }, IntPtr.Zero);

            Console.WriteLine(nextWindow);
            if (nextWindow != IntPtr.Zero)
            {
                SetForegroundWindow(nextWindow);
            }
        }
    }
}
