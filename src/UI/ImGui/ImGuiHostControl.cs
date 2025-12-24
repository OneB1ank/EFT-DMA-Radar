/*
* Moulman's EFT DMA Radar - ImGui Host Control
* Hybrid WPF + ImGui integration container
*
MIT License
Copyright (c) 2025 Moulman
*/

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Numerics;
using System.Threading;
using System.Runtime.InteropServices;

using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

// Type aliases to resolve conflicts
using SilkWindow = Silk.NET.Windowing.IWindow;
using SilkWindowState = Silk.NET.Windowing.WindowState;

namespace LoneEftDmaRadar.UI.ImGui
{
    /// <summary>
    /// WPF HwndHost that embeds an ImGui rendering surface using Silk.NET.
    /// Creates a child window that's properly parented to the WPF window.
    /// </summary>
    public class ImGuiHostControl : HwndHost
    {
        #region Win32 API

        private const string WINDOW_CLASS_NAME = "ImGuiHostWindow";
        private static readonly IntPtr WNDPROC_PTR;
        private static bool _classRegistered = false;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreateWindowEx(
            uint dwExStyle, string lpClassName, string lpWindowName,
            uint dwStyle, int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll")]
        private static extern int SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

        // Window styles
        private const uint WS_CHILD = 0x40000000;
        private const uint WS_VISIBLE = 0x10000000;
        private const uint WS_CLIPSIBLINGS = 0x04000000;
        private const uint WS_CLIPCHILDREN = 0x02000000;
        private const uint WS_POPUP = 0x80000000;

        private const uint WS_EX_NOPARENTNOTIFY = 0x00000004;
        private const uint WS_EX_TOOLWINDOW = 0x00000080;

        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;

        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;

        #endregion

        // Silk.NET window and rendering
        private SilkWindow? _silkWindow;
        private GL? _gl;
        private ImGuiController? _imguiController;
        private IInputContext? _inputContext;

        // Window thread
        private Thread? _windowThread;
        private CancellationTokenSource? _cancellationToken;

        // State tracking
        private bool _isInitialized;
        private bool _isVisible;
        private bool _isInitializing;
        private bool _shouldRender;
        private Vector2D<int> _cachedSize;
        private readonly object _lockObj = new object();

        // Child window handle (created by BuildWindowCore)
        private IntPtr _childHwnd;

        /// <summary>
        /// Event raised when ImGui is ready to render UI content each frame.
        /// </summary>
        public event Action? OnRenderImGui;

        /// <summary>
        /// Gets the ImGuiController instance for direct access.
        /// </summary>
        public ImGuiController? Controller => _imguiController;

        /// <summary>
        /// Gets whether the ImGui host is initialized and ready.
        /// </summary>
        public new bool IsInitialized => _isInitialized;

        /// <summary>
        /// Gets whether the ImGui context is currently valid for rendering.
        /// </summary>
        public bool IsContextValid => _isInitialized && _shouldRender;

        static ImGuiHostControl()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(ImGuiHostControl),
                new FrameworkPropertyMetadata(typeof(ImGuiHostControl)));

            // Create a delegate for the window procedure
            WndProcDelegate proc = CustomWndProc;
            WNDPROC_PTR = Marshal.GetFunctionPointerForDelegate(proc);
        }

        private static IntPtr CustomWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        public ImGuiHostControl()
        {
            Loaded += OnLoaded;
            // Don't use Unloaded for tab switching - handle cleanup via Window.Closing event instead
            // Unloaded += OnUnloaded;
            SizeChanged += OnSizeChanged;
            IsVisibleChanged += OnIsVisibleChanged;
            LayoutUpdated += OnLayoutUpdated;

            // Register for application exit to properly cleanup
            Application.Current.Exit += OnApplicationExit;
        }

        private void OnApplicationExit(object? sender, ExitEventArgs e)
        {
            ShutdownSilkWindow();
        }

        #region HwndHost Overrides

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            System.Diagnostics.Debug.WriteLine($"[ImGuiHostControl] BuildWindowCore called, parent handle: {hwndParent.Handle}");

            // Register window class if not already done
            if (!_classRegistered)
            {
                var wndClass = new WNDCLASS
                {
                    style = 0,
                    lpfnWndProc = WNDPROC_PTR,
                    cbClsExtra = 0,
                    cbWndExtra = 0,
                    hInstance = GetModuleHandle(null),
                    hIcon = IntPtr.Zero,
                    hCursor = IntPtr.Zero,
                    hbrBackground = IntPtr.Zero,
                    lpszMenuName = null,
                    lpszClassName = WINDOW_CLASS_NAME
                };

                // Note: In production, you'd properly register the class, but for now we'll use a simpler approach
                // by letting Silk.NET handle window creation and then parenting it
            }

            // Create a placeholder child window - Silk.NET will create the actual OpenGL window
            // We'll store the parent handle and return a dummy handle
            // The actual window is created in the background thread and parented properly

            // For now, create a message-only window as placeholder
            _childHwnd = CreateWindowEx(
                0,
                "STATIC", // Use predefined static class
                "ImGuiHost",
                WS_CHILD,
                0, 0, 0, 0,
                hwndParent.Handle,
                IntPtr.Zero,
                GetModuleHandle(null),
                IntPtr.Zero);

            if (_childHwnd == IntPtr.Zero)
            {
                System.Diagnostics.Debug.WriteLine($"[ImGuiHostControl] CreateWindowEx failed, error: {Marshal.GetLastWin32Error()}");
            }

            return new HandleRef(this, _childHwnd);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            if (_childHwnd != IntPtr.Zero)
            {
                DestroyWindow(_childHwnd);
                _childHwnd = IntPtr.Zero;
            }
            // Don't shutdown here - cleanup happens in OnApplicationExit
            // This prevents crashes during tab switching
        }

        #endregion

        #region Event Handlers

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
        }

        // OnUnloaded removed - tab switching should not shutdown the entire ImGui system
        // Cleanup now happens in OnApplicationExit

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_silkWindow is not null && _isInitialized)
            {
                UpdateWindowSize();
            }
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            _isVisible = IsVisible;

            // Stop rendering when hidden, start when visible
            _shouldRender = IsVisible && _isInitialized;

            // Only initialize when becoming visible
            if (_isVisible && !_isInitialized && !_isInitializing)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (HasValidBounds())
                    {
                        InitializeSilkWindow();
                    }
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }

            // Don't touch Silk window from UI thread - let the render loop handle visibility
        }

        private void OnLayoutUpdated(object? sender, EventArgs e)
        {
            if (_isVisible && !_isInitialized && !_isInitializing && HasValidBounds())
            {
                InitializeSilkWindow();
            }

            if (_isInitialized && _isVisible)
            {
                UpdateWindowSize();
            }
        }

        #endregion

        #region Silk.NET Window Management

        private bool HasValidBounds()
        {
            return ActualWidth > 10 && ActualHeight > 10;
        }

        private void InitializeSilkWindow()
        {
            if (_isInitialized || _isInitializing || _silkWindow is not null)
                return;

            _isInitializing = true;

            try
            {
                if (!HasValidBounds() || _childHwnd == IntPtr.Zero)
                {
                    _isInitializing = false;
                    return;
                }

                var width = (int)Math.Max(ActualWidth, 100);
                var height = (int)Math.Max(ActualHeight, 100);

                if (width <= 0 || height <= 0)
                {
                    _isInitializing = false;
                    return;
                }

                _cachedSize = new Vector2D<int>(width, height);
                _shouldRender = true;

                System.Diagnostics.Debug.WriteLine($"[ImGuiHostControl] Creating embedded window size {_cachedSize.X}x{_cachedSize.Y}, parent HWND: 0x{_childHwnd.ToInt64():X}");

                _cancellationToken = new CancellationTokenSource();
                var sizeCopy = _cachedSize;
                var parentCopy = _childHwnd;

                _windowThread = new Thread(() => RunWindowThread(sizeCopy, parentCopy, _cancellationToken.Token));
                _windowThread.SetApartmentState(ApartmentState.STA);
                _windowThread.IsBackground = true;
                _windowThread.Start();

                _isInitialized = true;
                System.Diagnostics.Debug.WriteLine("[ImGuiHostControl] Window initialization started successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ImGuiHostControl] Failed to initialize: {ex.Message}");
                _isInitialized = false;
            }
            finally
            {
                _isInitializing = false;
            }
        }

        private void RunWindowThread(Vector2D<int> size, IntPtr parentHwnd, CancellationToken token)
        {
            try
            {
                // Create window options
                var options = WindowOptions.Default;
                options.Title = "ImGuiHost";
                options.Size = size;
                options.WindowBorder = WindowBorder.Hidden;
                options.VSync = true;
                options.API = GraphicsAPI.Default;

                // Create the Silk.NET window
                var window = Silk.NET.Windowing.Window.Create(options);

                lock (_lockObj)
                {
                    _silkWindow = window;
                }

                // Wire up window events
                window.Load += () => OnWindowLoad(window);
                window.Closing += () => OnWindowClosing();
                window.Resize += (newSize) => OnWindowResize(newSize);

                // Initialize the window
                window.Initialize();

                // Parent the window to our child window
                var nativeHandles = window.Native?.Win32 ?? default;
                var silkWindowHandle = nativeHandles.Item1;
                if (silkWindowHandle != IntPtr.Zero && parentHwnd != IntPtr.Zero)
                {
                    SetParent(silkWindowHandle, parentHwnd);
                    System.Diagnostics.Debug.WriteLine($"[ImGuiHostControl] Set parent: 0x{silkWindowHandle.ToInt64():X} -> 0x{parentHwnd.ToInt64():X}");

                    // Position and size the window (relative to parent)
                    SetWindowPos(silkWindowHandle, IntPtr.Zero, 0, 0, size.X, size.Y,
                        SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                }

                System.Diagnostics.Debug.WriteLine("[ImGuiHostControl] Window thread starting render loop");

                // Run the window's main loop
                while (!token.IsCancellationRequested && !window.IsClosing)
                {
                    // Check if we should render before doing any OpenGL operations
                    bool doRender;
                    lock (_lockObj)
                    {
                        doRender = _shouldRender && _isInitialized;
                    }

                    if (!doRender)
                    {
                        // Not rendering - just sleep and continue
                        Thread.Sleep(16); // ~60 FPS check rate
                        continue;
                    }

                    try
                    {
                        // Double-check state before calling MakeCurrent
                        bool stillOk;
                        lock (_lockObj)
                        {
                            stillOk = _shouldRender && _isInitialized && _gl is not null;
                        }

                        if (!stillOk)
                            continue;

                        window.MakeCurrent();
                        window.DoEvents();

                        OnWindowRender(1.0 / 60.0);
                        window.SwapBuffers();

                        Thread.Sleep(1);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ImGuiHostControl] Render loop error: {ex.Message}");
                        // Stop rendering on error to prevent continuous crashes
                        lock (_lockObj)
                        {
                            _shouldRender = false;
                            _isInitialized = false;
                        }
                        break; // Exit the loop on error
                    }
                }

                System.Diagnostics.Debug.WriteLine("[ImGuiHostControl] Window thread exiting");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ImGuiHostControl] Window thread error: {ex.Message}");
            }
        }

        private void OnWindowLoad(SilkWindow window)
        {
            try
            {
                _gl = GL.GetApi(window);
                _inputContext = window.CreateInput();

                _imguiController = new ImGuiController(
                    _gl,
                    _inputContext,
                    window.Size.X,
                    window.Size.Y
                );

                _imguiController.OnRenderUI += () => OnRenderImGui?.Invoke();

                System.Diagnostics.Debug.WriteLine("[ImGuiHostControl] Window loaded and ImGui initialized");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ImGuiHostControl] Window load error: {ex.Message}");
            }
        }

        private void OnWindowRender(double deltaTime)
        {
            // Check if we should render - lock to prevent race conditions
            bool shouldRender;
            lock (_lockObj)
            {
                shouldRender = _shouldRender && _gl is not null && _imguiController is not null && _isInitialized;
            }

            if (!shouldRender)
                return;

            try
            {
                _gl.ClearColor(0.1f, 0.1f, 0.1f, 1.0f);
                _gl.Clear(Silk.NET.OpenGL.ClearBufferMask.ColorBufferBit | Silk.NET.OpenGL.ClearBufferMask.DepthBufferBit);

                _imguiController.Update((float)deltaTime);
                _imguiController.Render();
            }
            catch (Exception ex)
            {
                // On render error, stop rendering to prevent continuous crashes
                System.Diagnostics.Debug.WriteLine($"[ImGuiHostControl] Render error: {ex.Message}");
                lock (_lockObj)
                {
                    _shouldRender = false;
                }
            }
        }

        private void OnWindowResize(Vector2D<int> size)
        {
            if (_imguiController is not null)
            {
                _imguiController.Resize(size.X, size.Y);
            }
        }

        private void OnWindowClosing()
        {
        }

        private void ShutdownSilkWindow()
        {
            System.Diagnostics.Debug.WriteLine("[ImGuiHostControl] Shutdown started");

            // First, stop rendering
            lock (_lockObj)
            {
                _shouldRender = false;
                _isInitialized = false;
            }

            // Cancel the render loop
            _cancellationToken?.Cancel();

            // Wait for the render thread to finish - give it more time
            if (_windowThread is not null)
            {
                if (!_windowThread.Join(TimeSpan.FromSeconds(2)))
                {
                    System.Diagnostics.Debug.WriteLine("[ImGuiHostControl] Window thread did not exit gracefully, forcing shutdown");
                }
                _windowThread = null;
            }

            // NOW it's safe to dispose resources
            try
            {
                _imguiController?.Dispose();
                _imguiController = null;
            }
            catch { }

            try
            {
                _inputContext?.Dispose();
                _inputContext = null;
            }
            catch { }

            try
            {
                lock (_lockObj)
                {
                    _silkWindow?.Dispose();
                    _silkWindow = null;
                }
            }
            catch { }

            _gl = null;
            _isInitializing = false;
            _cancellationToken?.Dispose();
            _cancellationToken = null;

            System.Diagnostics.Debug.WriteLine("[ImGuiHostControl] Shutdown complete");
        }

        private void UpdateWindowSize()
        {
            if (_silkWindow is null || !_isInitialized)
                return;

            try
            {
                var width = (int)Math.Max(ActualWidth, 100);
                var height = (int)Math.Max(ActualHeight, 100);
                var newSize = new Vector2D<int>(width, height);

                if (Vector2D.Abs(newSize - _cachedSize).X > 2 || Vector2D.Abs(newSize - _cachedSize).Y > 2)
                {
                    _cachedSize = newSize;
                    _silkWindow.Size = _cachedSize;

                    var nativeHandles = _silkWindow.Native?.Win32 ?? default;
                    var silkWindowHandle = nativeHandles.Item1;
                    if (silkWindowHandle != IntPtr.Zero)
                    {
                        SetWindowPos(silkWindowHandle, IntPtr.Zero, 0, 0, width, height,
                            SWP_NOZORDER | SWP_NOACTIVATE);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ImGuiHostControl] Update size error: {ex.Message}");
            }
        }

        #endregion

        public void Refresh()
        {
            if (_silkWindow is not null && _isVisible)
            {
                try
                {
                    _silkWindow.DoEvents();
                }
                catch { }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private class WNDCLASS
        {
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
        }
    }
}
