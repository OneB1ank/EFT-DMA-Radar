/*
* Moulman's EFT DMA Radar - ImGui Tab
* Hybrid WPF + ImGui integration
*
MIT License
Copyright (c) 2025 Moulman
*/

using System.Windows.Controls;
using System.Numerics;
using System.Runtime.InteropServices;
using Collections.Pooled;

using GlImGui = ImGuiNET.ImGui;
using ImGuiCol = ImGuiNET.ImGuiCol;
using ImGuiWindowFlags = ImGuiNET.ImGuiWindowFlags;
using ImGuiCond = ImGuiNET.ImGuiCond;
using ImGuiTreeNodeFlags = ImGuiNET.ImGuiTreeNodeFlags;
using ImGuiMouseButton = ImGuiNET.ImGuiMouseButton;
using ImGuiChildFlags = ImGuiNET.ImGuiChildFlags;
using ImDrawListPtr = ImGuiNET.ImDrawListPtr;

using SkiaSharp;
using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.Tarkov.GameWorld.Player.Helpers;
using LoneEftDmaRadar.Tarkov.GameWorld.Loot;
using LoneEftDmaRadar.UI.Radar.Maps;
using LoneEftDmaRadar.UI.Radar.ViewModels;
using LoneEftDmaRadar.UI.Skia;
using LoneEftDmaRadar.Misc;

namespace LoneEftDmaRadar.UI.ImGui
{
    /// <summary>
    /// Interaction logic for ImGuiTab.xaml
    /// This tab hosts ImGui content within the WPF application.
    /// </summary>
    public sealed partial class ImGuiTab : UserControl
    {
        // ImGui render views
        private ImGuiTextureRadar? _textureRadar;
        private ImGuiDebugView? _debugView;
        private ImGuiPerformanceView? _perfView;
        private ImGuiSettingsView? _settingsView;

        // Track initialization to prevent duplicate view creation
        private bool _viewsInitialized = false;

        // Track event subscription to prevent duplicate subscriptions
        private bool _isSubscribedToRender = false;

        // UI State
        private bool _showDemoWindow = false;
        private bool _showRadar = true;
        private bool _showDebug = false;
        private bool _showPerformance = true;
        private bool _showSettings = false;

        // FPS tracking
        private float _fps = 60f;
        private readonly float[] _fpsHistory = new float[120];
        private int _fpsIndex;
        private int _lastVisibleIndex = -1; // Track where we were last visible

        public ImGuiTab()
        {
            InitializeComponent();

            Loaded += OnLoaded;
            // Unloaded removed - ImGui system persists across tab switches
        }

        private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (ImGuiHost is null)
                return;

            // Subscribe to ImGui rendering only once
            if (!_isSubscribedToRender)
            {
                ImGuiHost.OnRenderImGui += RenderImGuiContent;
                _isSubscribedToRender = true;
                System.Diagnostics.Debug.WriteLine("[ImGuiTab] Subscribed to render event");
            }

            // Don't initialize views yet - wait for controller to be ready
            // The controller is created when the ImGui window is initialized
            System.Diagnostics.Debug.WriteLine("[ImGuiTab] Loaded, waiting for ImGui controller...");
        }

        private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
        {
            // Don't dispose views or unsubscribe - the ImGui system persists across tab switches
            // Cleanup only happens on application exit via Application.Current.Exit event
            // This prevents crashes when switching between tabs
        }

        /// <summary>
        /// Main ImGui rendering callback. This is called every frame by ImGuiHostControl.
        /// NOTE: This is called from the background render thread, not the UI thread!
        /// </summary>
        private void RenderImGuiContent()
        {
            // Check if ImGui context is valid before doing anything
            if (ImGuiHost is null || !ImGuiHost.IsContextValid)
            {
                _lastVisibleIndex = -1; // Reset tracking when not visible
                return;
            }

            // Lazy initialize views when controller becomes available (only once!)
            if (!_viewsInitialized && ImGuiHost?.Controller is ImGuiController controller)
            {
                try
                {
                    _textureRadar = new ImGuiTextureRadar(controller);
                    _debugView = new ImGuiDebugView();
                    _perfView = new ImGuiPerformanceView();
                    _settingsView = new ImGuiSettingsView();
                    _viewsInitialized = true;

                    // Hide loading text - must use Dispatcher since we're on render thread
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (LoadingText is not null)
                            LoadingText.Visibility = Visibility.Collapsed;
                    }));

                    System.Diagnostics.Debug.WriteLine("[ImGuiTab] ImGui views initialized");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ImGuiTab] Failed to initialize views: {ex.Message}");
                    // Show error - must use Dispatcher since we're on render thread
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (ErrorText is not null)
                        {
                            ErrorText.Text = $"ImGui initialization failed: {ex.Message}";
                            ErrorText.Visibility = Visibility.Visible;
                        }
                        if (LoadingText is not null)
                            LoadingText.Visibility = Visibility.Collapsed;
                    }));
                    return;
                }
            }

            try
            {
                // Update FPS (only when actually rendering)
                UpdateFPS();

                // Draw main menu bar
                DrawMainMenuBar();

                // Draw ImGui windows
                if (_showDemoWindow)
                {
                    GlImGui.SetNextWindowSize(new Vector2(800, 600), ImGuiCond.FirstUseEver);
                    GlImGui.ShowDemoWindow(ref _showDemoWindow);
                }

                if (_showRadar && _textureRadar is not null)
                {
                    _textureRadar.Render();
                }

                if (_showDebug && _debugView is not null)
                {
                    _debugView.Render();
                }

                if (_showPerformance && _perfView is not null)
                {
                    _perfView.Render(_fps, _fpsHistory, _fpsIndex, _lastVisibleIndex);
                }

                if (_showSettings && _settingsView is not null)
                {
                    _settingsView.Render();
                }

                // Track that we've rendered this frame
                _lastVisibleIndex = _fpsIndex;
            }
            catch (Exception ex)
            {
                // Log but don't crash - might be transient during tab switch
                System.Diagnostics.Debug.WriteLine($"[ImGuiTab] Render error: {ex.Message}");
            }
        }

        private void UpdateFPS()
        {
            try
            {
                var io = GlImGui.GetIO();
                _fps = io.Framerate;
                _fpsHistory[_fpsIndex] = _fps;
                _fpsIndex = (_fpsIndex + 1) % _fpsHistory.Length;
            }
            catch
            {
                // ImGui not ready yet
                _fps = 0f;
            }
        }

        private void DrawMainMenuBar()
        {
            try
            {
                if (GlImGui.BeginMainMenuBar())
                {
                    if (GlImGui.BeginMenu("ImGui"))
                    {
                        if (GlImGui.MenuItem("Radar View", "", _showRadar))
                        {
                            _showRadar = !_showRadar;
                        }
                        if (GlImGui.MenuItem("Performance", "", _showPerformance))
                        {
                            _showPerformance = !_showPerformance;
                        }
                        if (GlImGui.MenuItem("Debug Info", "", _showDebug))
                        {
                            _showDebug = !_showDebug;
                        }
                        if (GlImGui.MenuItem("Settings", "", _showSettings))
                        {
                            _showSettings = !_showSettings;
                        }
                        GlImGui.Separator();
                        if (GlImGui.MenuItem("Demo Window", "", _showDemoWindow))
                        {
                            _showDemoWindow = !_showDemoWindow;
                        }
                        GlImGui.EndMenu();
                    }

                    // Show FPS and memory info on the right
                    string statusText = $"{_fps:F1} FPS | " +
                        $"GC: {GC.GetTotalMemory(false) / 1024 / 1024}MB";

                    var textSize = GlImGui.CalcTextSize(statusText);
                    GlImGui.SetCursorPosX(GlImGui.GetWindowWidth() - textSize.X - 10);
                    GlImGui.Text(statusText);

                    GlImGui.EndMainMenuBar();
                }
            }
            catch
            {
                // ImGui not ready yet, skip menu bar
            }
        }
    }

    #region ImGui Texture Radar View

    /// <summary>
    /// Radar view that renders to a SkiaSharp bitmap and displays it as an ImGui texture.
    /// This reuses the existing RadarViewModel rendering logic.
    /// </summary>
    internal class ImGuiTextureRadar : IDisposable
    {
        // Use field instead of property for ref parameter support
        public bool IsVisible = true;

        // Radar rendering
        private readonly int _radarWidth = 800;
        private readonly int _radarHeight = 600;
        private SKBitmap? _bitmap;
        private SKCanvas? _canvas;
        private uint _glTextureId = 0;
        private bool _isInitialized = false;
        private long _lastFrameTicks;

        // ImGui image size
        private Vector2 _imageSize;

        // Reference to ImGui controller for texture creation
        private readonly ImGuiController _controller;

        public ImGuiTextureRadar(ImGuiController controller)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _imageSize = new Vector2(_radarWidth, _radarHeight);
            InitializeBitmap();
        }

        private void InitializeBitmap()
        {
            _bitmap = new SKBitmap(_radarWidth, _radarHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            _canvas = new SKCanvas(_bitmap);
        }

        public unsafe void Render()
        {
            GlImGui.SetNextWindowPos(new Vector2(10, 30), ImGuiCond.FirstUseEver);
            GlImGui.SetNextWindowSize(new Vector2(850, 700), ImGuiCond.FirstUseEver);

            if (GlImGui.Begin("Radar View (SkiaSharp)###RadarView1", ref IsVisible))
            {
                // Update radar rendering
                UpdateRadarTexture();

                // Display the texture
                if (_glTextureId != 0)
                {
                    GlImGui.Image((nint)_glTextureId, _imageSize, Vector2.Zero, Vector2.One, new Vector4(1, 1, 1, 1), new Vector4(0, 0, 0, 1));
                }
                else
                {
                    GlImGui.Text("Radar texture not available");
                }

                // Show info below
                GlImGui.Separator();
                if (GlImGui.CollapsingHeader("Info", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    GlImGui.Text($"Texture ID: {_glTextureId}");
                    GlImGui.Text($"Size: {_radarWidth}x{_radarHeight}");
                    GlImGui.Text($"In Raid: {Memory.InRaid}");
                    GlImGui.Text($"Map: {Memory.MapID ?? "None"}");
                    GlImGui.Text($"Players: {Memory.Players?.Count() ?? 0}");
                    GlImGui.Text($"Loot: {Memory.Loot?.FilteredLoot?.Count() ?? 0}");
                }
            }
            GlImGui.End();
        }

        private unsafe void UpdateRadarTexture()
        {
            // FPS cap - similar to RadarViewModel
            int maxFps = App.Config.UI.RadarMaxFPS;
            if (maxFps > 0)
            {
                long now = System.Diagnostics.Stopwatch.GetTimestamp();
                double elapsedMs = (now - _lastFrameTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                double targetMs = 1000.0 / maxFps;

                if (elapsedMs < targetMs)
                {
                    return; // Skip this frame
                }

                _lastFrameTicks = now;
            }
            else
            {
                _lastFrameTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            }

            // Always render when FPS cap allows (real-time updates)
            RenderRadarToBitmap();
            UploadTextureToGL();
        }

        /// <summary>
        /// Compute map parameters for bitmap rendering without needing an SKGLElement control.
        /// This is a simplified version of IEftMap.GetParameters for ImGui texture rendering.
        /// </summary>
        private EftMapParams ComputeMapParameters(IEftMap map, int zoom, ref Vector2 localPlayerMapPos, SKRect canvasBounds)
        {
            // Get map bounds
            var mapBounds = map.GetBounds();
            if (mapBounds.IsEmpty)
            {
                return new EftMapParams
                {
                    Map = map.Config,
                    Bounds = SKRect.Empty,
                    XScale = 1f,
                    YScale = 1f
                };
            }

            float fullWidth = mapBounds.Width;
            float fullHeight = mapBounds.Height;

            var zoomWidth = fullWidth * (0.01f * zoom);
            var zoomHeight = fullHeight * (0.01f * zoom);

            var bounds = new SKRect(
                localPlayerMapPos.X - zoomWidth * 0.5f,
                localPlayerMapPos.Y - zoomHeight * 0.5f,
                localPlayerMapPos.X + zoomWidth * 0.5f,
                localPlayerMapPos.Y + zoomHeight * 0.5f
            );

            // Apply bounds constraint (simplified - without the wasZoomApplied logic)
            bounds = ConstrainBounds(bounds, fullWidth, fullHeight, ref localPlayerMapPos);

            // Apply aspect fill to match our canvas size
            var size = new SKSize(canvasBounds.Width, canvasBounds.Height);
            bounds = bounds.AspectFill(size);

            return new EftMapParams
            {
                Map = map.Config,
                Bounds = bounds,
                XScale = (float)size.Width / bounds.Width,
                YScale = (float)size.Height / bounds.Height
            };
        }

        /// <summary>
        /// Constrain bounds to map dimensions.
        /// </summary>
        private SKRect ConstrainBounds(SKRect bounds, float mapWidth, float mapHeight, ref Vector2 playerPos)
        {
            // If the view is smaller than the map, constrain to map bounds
            if (bounds.Width < mapWidth && bounds.Height < mapHeight)
            {
                float x = bounds.Left;
                float y = bounds.Top;

                // Clamp left edge
                if (x < 0)
                {
                    x = 0;
                    playerPos.X = bounds.Width * 0.5f;
                }
                // Clamp right edge
                else if (bounds.Right > mapWidth)
                {
                    x = mapWidth - bounds.Width;
                    playerPos.X = mapWidth - bounds.Width * 0.5f;
                }

                // Clamp top edge
                if (y < 0)
                {
                    y = 0;
                    playerPos.Y = bounds.Height * 0.5f;
                }
                // Clamp bottom edge
                else if (bounds.Bottom > mapHeight)
                {
                    y = mapHeight - bounds.Height;
                    playerPos.Y = mapHeight - bounds.Height * 0.5f;
                }

                return new SKRect(x, y, x + bounds.Width, y + bounds.Height);
            }

            return bounds;
        }

        private void RenderRadarToBitmap()
        {
            if (_canvas is null || _bitmap is null)
                return;

            try
            {
                // Clear canvas
                _canvas.Clear(SKColors.Black);

                // Check if Memory interface is available
                if (Memory is null)
                {
                    DrawStatusText(_canvas, "DMA not initialized");
                    return;
                }

                // Check if in raid
                var inRaid = Memory.InRaid;
                var localPlayer = Memory.LocalPlayer;
                var mapID = Memory.MapID;

                if (!inRaid)
                {
                    DrawStatusText(_canvas, "Not in raid");
                    return;
                }

                if (localPlayer is null)
                {
                    DrawStatusText(_canvas, "No local player");
                    return;
                }

                if (string.IsNullOrEmpty(mapID))
                {
                    DrawStatusText(_canvas, "No map ID");
                    return;
                }

                // Load map if needed
                if (!string.Equals(mapID, EftMapManager.Map?.ID, StringComparison.OrdinalIgnoreCase))
                {
                    EftMapManager.LoadMap(mapID);
                }

                var map = EftMapManager.Map;
                if (map is null)
                {
                    DrawStatusText(_canvas, "Map not loaded");
                    return;
                }

                // Get map parameters (similar to RadarViewModel)
                var targetPos = localPlayer.Position;
                var targetMapPos = targetPos.ToMapPos(map.Config);
                var mapCanvasBounds = new SKRect(0, 0, _radarWidth, _radarHeight);

                // Compute map parameters directly for bitmap rendering
                var mapParams = ComputeMapParameters(map, App.Config.UI.Zoom, ref targetMapPos, mapCanvasBounds);

                // Draw map
                float floorHeight = localPlayer.Position.Y;
                map.Draw(_canvas, floorHeight, mapParams.Bounds, mapCanvasBounds);

                // Draw loot
                if (App.Config.Loot.Enabled)
                {
                    if (App.Config.Containers.Enabled)
                    {
                        var containerConfig = App.Config.Containers;
                        var containers = Memory.Loot?.StaticContainers;
                        if (containers is not null)
                        {
                            foreach (var container in containers)
                            {
                                var id = container.ID ?? "NULL";
                                if (containerConfig.SelectAll || containerConfig.Selected.ContainsKey(id))
                                {
                                    container.Draw(_canvas, mapParams, localPlayer);
                                }
                            }
                        }
                    }

                    var loot = Memory.Loot?.FilteredLoot;
                    if (loot is not null)
                    {
                        foreach (var item in loot)
                        {
                            if (App.Config.Loot.HideCorpses && item is LootCorpse)
                                continue;
                            item.Draw(_canvas, mapParams, localPlayer);
                        }
                    }
                }

                // Draw mines
                if (App.Config.UI.ShowMines && mapID != null &&
                    StaticGameData.Mines.TryGetValue(mapID, out var mines))
                {
                    foreach (ref var mine in mines.Span)
                    {
                        var mineZoomedPos = mine.ToMapPos(map.Config).ToZoomedPos(mapParams);
                        mineZoomedPos.DrawMineMarker(_canvas);
                    }
                }

                // Draw explosives
                var explosives = Memory.Explosives;
                if (explosives is not null)
                {
                    foreach (var explosive in explosives)
                    {
                        explosive.Draw(_canvas, mapParams, localPlayer);
                    }
                }

                // Draw exits
                var exits = Memory.Exits;
                if (exits is not null)
                {
                    foreach (var exit in exits)
                    {
                        exit.Draw(_canvas, mapParams, localPlayer);
                    }
                }

                // Draw players
                var allPlayers = Memory.Players?.Where(x => !x.HasExfild);
                if (allPlayers is not null)
                {
                    foreach (var player in allPlayers)
                    {
                        if (player == localPlayer)
                            continue;
                        player.Draw(_canvas, mapParams, localPlayer);
                    }

                    // Draw local player on top
                    localPlayer.Draw(_canvas, mapParams, localPlayer);
                }

                // Flush to ensure drawing is complete
                _canvas.Flush();
            }
            catch (Exception ex)
            {
                var errorMsg = $"Render error: {ex.GetType().Name} - {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[ImGuiTextureRadar] {errorMsg}");
                System.Diagnostics.Debug.WriteLine($"[ImGuiTextureRadar] Stack: {ex.StackTrace}");
                try
                {
                    if (_canvas is not null)
                    {
                        DrawStatusText(_canvas, errorMsg);
                    }
                }
                catch { }
            }
        }

        private void DrawStatusText(SKCanvas canvas, string message)
        {
            var paint = new SKPaint
            {
                Color = SKColors.White,
                TextSize = 24,
                IsAntialias = true,
                TextAlign = SKTextAlign.Center
            };

            canvas.DrawText(message, _radarWidth / 2f, _radarHeight / 2f, paint);
        }

        private unsafe void UploadTextureToGL()
        {
            if (_bitmap is null)
                return;

            // Get pixel data from bitmap
            var pixels = _bitmap.GetPixels();
            if (pixels == IntPtr.Zero)
                return;

            // Create or update OpenGL texture via ImGuiController
            _glTextureId = _controller.CreateOrUpdateTexture(pixels, _radarWidth, _radarHeight, _glTextureId);
        }

        public void Dispose()
        {
            _canvas?.Dispose();
            _bitmap?.Dispose();

            // Delete GL texture if it exists
            if (_glTextureId != 0)
            {
                _controller.DeleteTexture(_glTextureId);
                _glTextureId = 0;
            }
        }
    }

    #endregion

    #region ImGui Debug View

    /// <summary>
    /// Debug information view showing system and game state.
    /// </summary>
    internal class ImGuiDebugView
    {
        // Use field instead of property for ref parameter support
        public bool IsVisible = false;

        public void Render()
        {
            GlImGui.SetNextWindowPos(new Vector2(520, 30), ImGuiCond.FirstUseEver);
            GlImGui.SetNextWindowSize(new Vector2(350, 250), ImGuiCond.FirstUseEver);

            if (GlImGui.Begin("Debug Info###DebugView1", ref IsVisible))
            {
                GlImGui.Text("System Information");
                GlImGui.Separator();

                var io = GlImGui.GetIO();
                GlImGui.Text($"Display Size: {io.DisplaySize.X:F0} x {io.DisplaySize.Y:F0}");
                GlImGui.Text($"DeltaTime: {io.DeltaTime:F4} s");
                GlImGui.Text($"Framerate: {io.Framerate:F1} FPS");

                GlImGui.Separator();
                GlImGui.Text("Memory Status:");

                var mem = GC.GetTotalMemory(false);
                GlImGui.Text($"  Managed Memory: {mem / 1024 / 1024:F1} MB");
                GlImGui.Text($"  Gen 0: {GC.CollectionCount(0)}");
                GlImGui.Text($"  Gen 1: {GC.CollectionCount(1)}");
                GlImGui.Text($"  Gen 2: {GC.CollectionCount(2)}");

                GlImGui.Separator();
                GlImGui.Text("Game Status:");
                GlImGui.Text($"  Starting: {Memory.Starting}");
                GlImGui.Text($"  Ready: {Memory.Ready}");
                GlImGui.Text($"  In Raid: {Memory.InRaid}");

                if (Memory.InRaid)
                {
                    GlImGui.Separator();
                    GlImGui.Text("Game Data:");
                    GlImGui.Text($"  Map ID: {Memory.MapID ?? "null"}");
                    GlImGui.Text($"  Players: {Memory.Players?.Count() ?? 0}");
                    GlImGui.Text($"  Loot Items: {Memory.Loot?.FilteredLoot?.Count() ?? 0}");
                }
            }
            GlImGui.End();
        }
    }

    #endregion

    #region ImGui Performance View

    /// <summary>
    /// Performance monitoring view with FPS graph.
    /// </summary>
    internal class ImGuiPerformanceView
    {
        // Use field instead of property for ref parameter support
        public bool IsVisible = false;

        public void Render(float fps, float[] fpsHistory, int fpsIndex, int lastVisibleIndex)
        {
            GlImGui.SetNextWindowPos(new Vector2(10, 440), ImGuiCond.FirstUseEver);
            GlImGui.SetNextWindowSize(new Vector2(350, 180), ImGuiCond.FirstUseEver);

            if (GlImGui.Begin("Performance###PerfView1", ref IsVisible))
            {
                GlImGui.Text("Performance Metrics");
                GlImGui.Separator();

                // FPS display
                var fpsColor = fps >= 55 ? new Vector4(0, 1, 0, 1) :
                              fps >= 30 ? new Vector4(1, 1, 0, 1) :
                              new Vector4(1, 0, 0, 1);

                GlImGui.TextColored(fpsColor, $"FPS: {fps:F1}");
                GlImGui.SameLine();
                GlImGui.Text($"({1000f / fps:F2} ms/frame)");

                // FPS graph - use unique ID to prevent state conflicts
                var displayIndex = lastVisibleIndex >= 0 ? lastVisibleIndex : fpsIndex;
                var displayCount = lastVisibleIndex >= 0 ? lastVisibleIndex + 1 : fpsHistory.Length;

                var overlay = string.Format("Min: {0:F1} Max: {1:F1} Avg: {2:F1}",
                    fpsHistory.Take(Math.Max(0, displayCount)).Min(),
                    fpsHistory.Take(Math.Max(0, displayCount)).Max(),
                    fpsHistory.Take(Math.Max(0, displayCount)).Average());

                GlImGui.PlotLines(
                    "##FPSHistory###PerfGraph1",
                    ref fpsHistory[0],
                    Math.Min(fpsHistory.Length, Math.Max(1, displayCount)),
                    displayIndex,
                    overlay,
                    0f,
                    200f,
                    new Vector2(320, 60)
                );

                // Memory info
                GlImGui.Separator();
                var totalMem = GC.GetTotalMemory(false);
                GlImGui.Text($"Memory: {totalMem / 1024 / 1024:F1} MB");

                if (GlImGui.Button("Force GC"))
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                }
                GlImGui.SameLine();
                if (GlImGui.Button("Purge SK Resources"))
                {
                    try
                    {
                        typeof(LoneEftDmaRadar.UI.Radar.Views.RadarTab)
                            .GetMethod("PurgeSKResources")?
                            .Invoke(null, null);
                    }
                    catch { }
                }
            }
            GlImGui.End();
        }
    }

    #endregion

    #region ImGui Settings View

    /// <summary>
    /// Settings view for ImGui configuration.
    /// </summary>
    internal class ImGuiSettingsView
    {
        // Use field instead of property for ref parameter support
        public bool IsVisible = false;

        // Settings state
        private bool _enableVSync = true;
        private float _uiScale = 1.0f;
        private int _themeSelection = 0;
        private readonly string[] _themes = { "Dark", "Light", "Classic" };

        public void Render()
        {
            GlImGui.SetNextWindowPos(new Vector2(380, 30), ImGuiCond.FirstUseEver);
            GlImGui.SetNextWindowSize(new Vector2(300, 200), ImGuiCond.FirstUseEver);

            if (GlImGui.Begin("ImGui Settings###SettingsView1", ref IsVisible))
            {
                GlImGui.Text("ImGui Configuration");
                GlImGui.Separator();

                if (GlImGui.BeginCombo("Theme", _themes[_themeSelection]))
                {
                    for (int i = 0; i < _themes.Length; i++)
                    {
                        bool isSelected = _themeSelection == i;
                        if (GlImGui.Selectable(_themes[i], isSelected))
                        {
                            _themeSelection = i;
                            ApplyTheme(i);
                        }
                        if (isSelected)
                        {
                            GlImGui.SetItemDefaultFocus();
                        }
                    }
                    GlImGui.EndCombo();
                }

                GlImGui.SliderFloat("UI Scale", ref _uiScale, 0.5f, 2.0f);
                if (GlImGui.IsItemEdited())
                {
                    var style = GlImGui.GetStyle();
                    style.ScaleAllSizes(_uiScale);
                }

                GlImGui.Checkbox("VSync", ref _enableVSync);

                GlImGui.Separator();
                if (GlImGui.Button("Reset to Defaults"))
                {
                    _uiScale = 1.0f;
                    _themeSelection = 0;
                    ApplyTheme(0);
                }
            }
            GlImGui.End();
        }

        private void ApplyTheme(int theme)
        {
            switch (theme)
            {
                case 0: // Dark
                    GlImGui.StyleColorsDark();
                    break;
                case 1: // Light
                    GlImGui.StyleColorsLight();
                    break;
                case 2: // Classic
                    GlImGui.StyleColorsClassic();
                    break;
            }
        }
    }

    #endregion
}
