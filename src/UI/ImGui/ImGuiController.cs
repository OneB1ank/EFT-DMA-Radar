/*
* Moulman's EFT DMA Radar - ImGui Controller
* Based on Lone's ImGui implementation
*
MIT License
Copyright (c) 2025 Lone DMA
Copyright (c) 2025 Moulman
*/

using System.Runtime.InteropServices;
using System.Numerics;

using GlImGui = ImGuiNET.ImGui;
using ImGuiIOPtr = ImGuiNET.ImGuiIOPtr;
using ImGuiKey = ImGuiNET.ImGuiKey;
using ImGuiStyle = ImGuiNET.ImGuiStyle;
using ImGuiCol = ImGuiNET.ImGuiCol;
using ImGuiConfigFlags = ImGuiNET.ImGuiConfigFlags;
using ImGuiBackendFlags = ImGuiNET.ImGuiBackendFlags;
using ImDrawDataPtr = ImGuiNET.ImDrawDataPtr;
using ImDrawVert = ImGuiNET.ImDrawVert;

using Silk.NET.Input;
using Silk.NET.OpenGL;

namespace LoneEftDmaRadar.UI.ImGui
{
    /// <summary>
    /// ImGui Controller for hybrid WPF + ImGui integration.
    /// </summary>
    public sealed class ImGuiController : IDisposable
    {
        private readonly GL _gl;
        private readonly IInputContext _input;
        private readonly bool _ownsInputContext;

        private uint _vao;
        private uint _vbo;
        private uint _ebo;
        private uint _fontTexture;
        private uint _shaderProgram;

        private int _attribLocationTex;
        private int _attribLocationProjMtx;
        private uint _attribLocationVtxPos;
        private uint _attribLocationVtxUV;
        private uint _attribLocationVtxColor;

        private int _viewportWidth;
        private int _viewportHeight;
        private readonly List<char> _pressedChars = [];

        public event Action? OnRenderUI;

        public ImGuiController(GL gl, IInputContext input, int width, int height)
        {
            _gl = gl;
            _input = input;
            _ownsInputContext = false;
            _viewportWidth = width;
            _viewportHeight = height;

            InitializeImGui();
            CreateDeviceResources();
            SetupInput();
        }

        private void InitializeImGui()
        {
            GlImGui.CreateContext();

            var io = GlImGui.GetIO();
            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
            io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard | ImGuiConfigFlags.DockingEnable;
            io.FontGlobalScale = 1.0f;

            unsafe
            {
                string iniPath = Path.Combine(App.ConfigPath.FullName, "imgui.ini");
                byte* iniPathPtr = (byte*)Marshal.StringToHGlobalAnsi(iniPath);
                ImGuiNET.ImGuiNative.igGetIO()->IniFilename = iniPathPtr;
            }

            io.Fonts.AddFontDefault();
            RecreateFontDeviceTexture();

            GlImGui.StyleColorsDark();
            CustomizeStyle();
        }

        private void CustomizeStyle()
        {
            var style = GlImGui.GetStyle();
            style.WindowRounding = 4.0f;
            style.FrameRounding = 2.0f;
            style.GrabRounding = 2.0f;
            style.ScrollbarRounding = 2.0f;
            style.TabRounding = 2.0f;

            var colors = style.Colors;
            colors[(int)ImGuiCol.WindowBg] = new Vector4(0.10f, 0.10f, 0.10f, 0.95f);
            colors[(int)ImGuiCol.Header] = new Vector4(0.20f, 0.20f, 0.20f, 1.00f);
            colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.30f, 0.30f, 0.35f, 1.00f);
            colors[(int)ImGuiCol.HeaderActive] = new Vector4(0.15f, 0.15f, 0.20f, 1.00f);
            colors[(int)ImGuiCol.Button] = new Vector4(0.20f, 0.20f, 0.25f, 1.00f);
            colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.30f, 0.30f, 0.40f, 1.00f);
            colors[(int)ImGuiCol.ButtonActive] = new Vector4(0.15f, 0.15f, 0.20f, 1.00f);
            colors[(int)ImGuiCol.FrameBg] = new Vector4(0.15f, 0.15f, 0.18f, 1.00f);
            colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.22f, 0.22f, 0.26f, 1.00f);
            colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.18f, 0.18f, 0.22f, 1.00f);
            colors[(int)ImGuiCol.CheckMark] = new Vector4(0.26f, 0.59f, 0.98f, 1.00f);
            colors[(int)ImGuiCol.SliderGrab] = new Vector4(0.26f, 0.59f, 0.98f, 1.00f);
            colors[(int)ImGuiCol.SliderGrabActive] = new Vector4(0.46f, 0.79f, 1.00f, 1.00f);
        }

        private void CreateDeviceResources()
        {
            _vao = _gl.GenVertexArray();
            _gl.BindVertexArray(_vao);

            _vbo = _gl.GenBuffer();
            _ebo = _gl.GenBuffer();

            const string vertexShaderSource = """
                #version 330 core
                layout (location = 0) in vec2 Position;
                layout (location = 1) in vec2 UV;
                layout (location = 2) in vec4 Color;
                uniform mat4 ProjMtx;
                out vec2 Frag_UV;
                out vec4 Frag_Color;
                void main()
                {
                    Frag_UV = UV;
                    Frag_Color = Color;
                    gl_Position = ProjMtx * vec4(Position.xy, 0, 1);
                }
                """;

            const string fragmentShaderSource = """
                #version 330 core
                in vec2 Frag_UV;
                in vec4 Frag_Color;
                uniform sampler2D Texture;
                layout (location = 0) out vec4 Out_Color;
                void main()
                {
                    Out_Color = Frag_Color * texture(Texture, Frag_UV.st);
                }
                """;

            var vertexShader = CompileShader(GLEnum.VertexShader, vertexShaderSource);
            var fragmentShader = CompileShader(GLEnum.FragmentShader, fragmentShaderSource);

            _shaderProgram = _gl.CreateProgram();
            _gl.AttachShader(_shaderProgram, vertexShader);
            _gl.AttachShader(_shaderProgram, fragmentShader);
            _gl.LinkProgram(_shaderProgram);

            _gl.GetProgram(_shaderProgram, GLEnum.LinkStatus, out int linkStatus);
            if (linkStatus == 0)
            {
                var infoLog = _gl.GetProgramInfoLog(_shaderProgram);
                throw new InvalidOperationException($"Error linking shader program: {infoLog}");
            }

            _gl.DeleteShader(vertexShader);
            _gl.DeleteShader(fragmentShader);

            _attribLocationTex = _gl.GetUniformLocation(_shaderProgram, "Texture");
            _attribLocationProjMtx = _gl.GetUniformLocation(_shaderProgram, "ProjMtx");
            _attribLocationVtxPos = (uint)_gl.GetAttribLocation(_shaderProgram, "Position");
            _attribLocationVtxUV = (uint)_gl.GetAttribLocation(_shaderProgram, "UV");
            _attribLocationVtxColor = (uint)_gl.GetAttribLocation(_shaderProgram, "Color");

            _gl.BindVertexArray(0);
        }

        private uint CompileShader(GLEnum type, string source)
        {
            var shader = _gl.CreateShader(type);
            _gl.ShaderSource(shader, source);
            _gl.CompileShader(shader);

            _gl.GetShader(shader, GLEnum.CompileStatus, out int compileStatus);
            if (compileStatus == 0)
            {
                var infoLog = _gl.GetShaderInfoLog(shader);
                throw new InvalidOperationException($"Error compiling {type}: {infoLog}");
            }

            return shader;
        }

        private unsafe void RecreateFontDeviceTexture()
        {
            var io = GlImGui.GetIO();
            io.Fonts.GetTexDataAsRGBA32(out nint pixels, out int width, out int height, out int _);

            _fontTexture = _gl.GenTexture();
            _gl.BindTexture(GLEnum.Texture2D, _fontTexture);
            _gl.TexImage2D(
                GLEnum.Texture2D, 0,
                InternalFormat.Rgba,
                (uint)width, (uint)height, 0,
                GLEnum.Rgba, GLEnum.UnsignedByte,
                (void*)pixels
            );

            _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.Linear);
            _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Linear);
            _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)GLEnum.ClampToEdge);
            _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)GLEnum.ClampToEdge);

            io.Fonts.SetTexID((nint)_fontTexture);
        }

        private void SetupInput()
        {
            foreach (var keyboard in _input.Keyboards)
            {
                keyboard.KeyDown += OnKeyDown;
                keyboard.KeyUp += OnKeyUp;
                keyboard.KeyChar += OnKeyChar;
            }

            foreach (var mouse in _input.Mice)
            {
                mouse.MouseMove += OnMouseMove;
                mouse.MouseDown += OnMouseDown;
                mouse.MouseUp += OnMouseUp;
                mouse.Scroll += OnScroll;
            }
        }

        #region Input Event Handlers

        private void OnKeyDown(IKeyboard keyboard, Key key, int scancode)
        {
            var io = GlImGui.GetIO();
            var imKey = TranslateKey(key);
            if (imKey != ImGuiKey.None)
                io.AddKeyEvent(imKey, true);
            UpdateModifiers(keyboard, io);
        }

        private void OnKeyUp(IKeyboard keyboard, Key key, int scancode)
        {
            var io = GlImGui.GetIO();
            var imKey = TranslateKey(key);
            if (imKey != ImGuiKey.None)
                io.AddKeyEvent(imKey, false);
            UpdateModifiers(keyboard, io);
        }

        private static void UpdateModifiers(IKeyboard keyboard, ImGuiIOPtr io)
        {
            io.AddKeyEvent(ImGuiKey.ModCtrl, keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight));
            io.AddKeyEvent(ImGuiKey.ModShift, keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight));
            io.AddKeyEvent(ImGuiKey.ModAlt, keyboard.IsKeyPressed(Key.AltLeft) || keyboard.IsKeyPressed(Key.AltRight));
            io.AddKeyEvent(ImGuiKey.ModSuper, keyboard.IsKeyPressed(Key.SuperLeft) || keyboard.IsKeyPressed(Key.SuperRight));
        }

        private void OnKeyChar(IKeyboard keyboard, char character)
        {
            _pressedChars.Add(character);
        }

        private void OnMouseMove(IMouse mouse, Vector2 position)
        {
            var io = GlImGui.GetIO();
            io.AddMousePosEvent(position.X, position.Y);
        }

        private void OnMouseDown(IMouse mouse, MouseButton button)
        {
            var io = GlImGui.GetIO();
            io.AddMouseButtonEvent((int)button, true);
        }

        private void OnMouseUp(IMouse mouse, MouseButton button)
        {
            var io = GlImGui.GetIO();
            io.AddMouseButtonEvent((int)button, false);
        }

        private void OnScroll(IMouse mouse, ScrollWheel wheel)
        {
            var io = GlImGui.GetIO();
            io.AddMouseWheelEvent(wheel.X, wheel.Y);
        }

        private static ImGuiKey TranslateKey(Key key) => key switch
        {
            Key.Tab => ImGuiKey.Tab,
            Key.Left => ImGuiKey.LeftArrow,
            Key.Right => ImGuiKey.RightArrow,
            Key.Up => ImGuiKey.UpArrow,
            Key.Down => ImGuiKey.DownArrow,
            Key.PageUp => ImGuiKey.PageUp,
            Key.PageDown => ImGuiKey.PageDown,
            Key.Home => ImGuiKey.Home,
            Key.End => ImGuiKey.End,
            Key.Insert => ImGuiKey.Insert,
            Key.Delete => ImGuiKey.Delete,
            Key.Backspace => ImGuiKey.Backspace,
            Key.Space => ImGuiKey.Space,
            Key.Enter => ImGuiKey.Enter,
            Key.Escape => ImGuiKey.Escape,
            Key.Apostrophe => ImGuiKey.Apostrophe,
            Key.Comma => ImGuiKey.Comma,
            Key.Minus => ImGuiKey.Minus,
            Key.Period => ImGuiKey.Period,
            Key.Slash => ImGuiKey.Slash,
            Key.Semicolon => ImGuiKey.Semicolon,
            Key.Equal => ImGuiKey.Equal,
            Key.LeftBracket => ImGuiKey.LeftBracket,
            Key.BackSlash => ImGuiKey.Backslash,
            Key.RightBracket => ImGuiKey.RightBracket,
            Key.GraveAccent => ImGuiKey.GraveAccent,
            Key.CapsLock => ImGuiKey.CapsLock,
            Key.ScrollLock => ImGuiKey.ScrollLock,
            Key.NumLock => ImGuiKey.NumLock,
            Key.PrintScreen => ImGuiKey.PrintScreen,
            Key.Pause => ImGuiKey.Pause,
            Key.Keypad0 => ImGuiKey.Keypad0,
            Key.Keypad1 => ImGuiKey.Keypad1,
            Key.Keypad2 => ImGuiKey.Keypad2,
            Key.Keypad3 => ImGuiKey.Keypad3,
            Key.Keypad4 => ImGuiKey.Keypad4,
            Key.Keypad5 => ImGuiKey.Keypad5,
            Key.Keypad6 => ImGuiKey.Keypad6,
            Key.Keypad7 => ImGuiKey.Keypad7,
            Key.Keypad8 => ImGuiKey.Keypad8,
            Key.Keypad9 => ImGuiKey.Keypad9,
            Key.KeypadDecimal => ImGuiKey.KeypadDecimal,
            Key.KeypadDivide => ImGuiKey.KeypadDivide,
            Key.KeypadMultiply => ImGuiKey.KeypadMultiply,
            Key.KeypadSubtract => ImGuiKey.KeypadSubtract,
            Key.KeypadAdd => ImGuiKey.KeypadAdd,
            Key.KeypadEnter => ImGuiKey.KeypadEnter,
            Key.KeypadEqual => ImGuiKey.KeypadEqual,
            Key.ShiftLeft => ImGuiKey.LeftShift,
            Key.ControlLeft => ImGuiKey.LeftCtrl,
            Key.AltLeft => ImGuiKey.LeftAlt,
            Key.SuperLeft => ImGuiKey.LeftSuper,
            Key.ShiftRight => ImGuiKey.RightShift,
            Key.ControlRight => ImGuiKey.RightCtrl,
            Key.AltRight => ImGuiKey.RightAlt,
            Key.SuperRight => ImGuiKey.RightSuper,
            Key.Menu => ImGuiKey.Menu,
            Key.Number0 => ImGuiKey._0,
            Key.Number1 => ImGuiKey._1,
            Key.Number2 => ImGuiKey._2,
            Key.Number3 => ImGuiKey._3,
            Key.Number4 => ImGuiKey._4,
            Key.Number5 => ImGuiKey._5,
            Key.Number6 => ImGuiKey._6,
            Key.Number7 => ImGuiKey._7,
            Key.Number8 => ImGuiKey._8,
            Key.Number9 => ImGuiKey._9,
            Key.A => ImGuiKey.A,
            Key.B => ImGuiKey.B,
            Key.C => ImGuiKey.C,
            Key.D => ImGuiKey.D,
            Key.E => ImGuiKey.E,
            Key.F => ImGuiKey.F,
            Key.G => ImGuiKey.G,
            Key.H => ImGuiKey.H,
            Key.I => ImGuiKey.I,
            Key.J => ImGuiKey.J,
            Key.K => ImGuiKey.K,
            Key.L => ImGuiKey.L,
            Key.M => ImGuiKey.M,
            Key.N => ImGuiKey.N,
            Key.O => ImGuiKey.O,
            Key.P => ImGuiKey.P,
            Key.Q => ImGuiKey.Q,
            Key.R => ImGuiKey.R,
            Key.S => ImGuiKey.S,
            Key.T => ImGuiKey.T,
            Key.U => ImGuiKey.U,
            Key.V => ImGuiKey.V,
            Key.W => ImGuiKey.W,
            Key.X => ImGuiKey.X,
            Key.Y => ImGuiKey.Y,
            Key.Z => ImGuiKey.Z,
            Key.F1 => ImGuiKey.F1,
            Key.F2 => ImGuiKey.F2,
            Key.F3 => ImGuiKey.F3,
            Key.F4 => ImGuiKey.F4,
            Key.F5 => ImGuiKey.F5,
            Key.F6 => ImGuiKey.F6,
            Key.F7 => ImGuiKey.F7,
            Key.F8 => ImGuiKey.F8,
            Key.F9 => ImGuiKey.F9,
            Key.F10 => ImGuiKey.F10,
            Key.F11 => ImGuiKey.F11,
            Key.F12 => ImGuiKey.F12,
            _ => ImGuiKey.None
        };

        #endregion

        #region Public API

        public void Update(float deltaTime)
        {
            var io = GlImGui.GetIO();
            io.DisplaySize = new Vector2(_viewportWidth, _viewportHeight);
            io.DeltaTime = deltaTime > 0 ? deltaTime : 1f / 60f;

            foreach (var c in _pressedChars)
                io.AddInputCharacter(c);
            _pressedChars.Clear();

            GlImGui.NewFrame();
        }

        public void Render()
        {
            OnRenderUI?.Invoke();

            GlImGui.Render();
            RenderDrawData(GlImGui.GetDrawData());
        }

        public void Resize(int width, int height)
        {
            _viewportWidth = width;
            _viewportHeight = height;
        }

        /// <summary>
        /// Create or update an OpenGL texture from pixel data.
        /// </summary>
        /// <param name="pixels">Pointer to pixel data (RGBA format)</param>
        /// <param name="width">Texture width in pixels</param>
        /// <param name="height">Texture height in pixels</param>
        /// <param name="existingTextureId">Existing texture ID to update, or 0 to create new</param>
        /// <returns>OpenGL texture ID</returns>
        public unsafe uint CreateOrUpdateTexture(nint pixels, int width, int height, uint existingTextureId = 0)
        {
            uint textureId = existingTextureId;

            if (textureId == 0)
            {
                textureId = _gl.GenTexture();
            }

            _gl.BindTexture(GLEnum.Texture2D, textureId);
            _gl.TexImage2D(
                GLEnum.Texture2D, 0,
                InternalFormat.Rgba,
                (uint)width, (uint)height, 0,
                GLEnum.Rgba, GLEnum.UnsignedByte,
                (void*)pixels
            );

            _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.Linear);
            _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Linear);
            _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)GLEnum.ClampToEdge);
            _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)GLEnum.ClampToEdge);

            return textureId;
        }

        /// <summary>
        /// Delete an OpenGL texture.
        /// </summary>
        public void DeleteTexture(uint textureId)
        {
            if (textureId != 0)
            {
                _gl.DeleteTexture(textureId);
            }
        }

        private unsafe void RenderDrawData(ImDrawDataPtr drawData)
        {
            if (drawData.CmdListsCount == 0)
                return;

            _gl.GetInteger(GLEnum.ActiveTexture, out int lastActiveTexture);
            _gl.GetInteger(GLEnum.CurrentProgram, out int lastProgram);
            _gl.GetInteger(GLEnum.TextureBinding2D, out int lastTexture);
            _gl.GetInteger(GLEnum.ArrayBufferBinding, out int lastArrayBuffer);
            _gl.GetInteger(GLEnum.VertexArrayBinding, out int lastVertexArrayObject);

            Span<int> lastViewport = stackalloc int[4];
            _gl.GetInteger(GLEnum.Viewport, lastViewport);

            Span<int> lastScissorBox = stackalloc int[4];
            _gl.GetInteger(GLEnum.ScissorBox, lastScissorBox);

            _gl.GetInteger(GLEnum.BlendSrcRgb, out int lastBlendSrcRgb);
            _gl.GetInteger(GLEnum.BlendDstRgb, out int lastBlendDstRgb);
            _gl.GetInteger(GLEnum.BlendSrcAlpha, out int lastBlendSrcAlpha);
            _gl.GetInteger(GLEnum.BlendDstAlpha, out int lastBlendDstAlpha);

            _gl.GetInteger(GLEnum.BlendEquationRgb, out int lastBlendEquationRgb);
            _gl.GetInteger(GLEnum.BlendEquationAlpha, out int lastBlendEquationAlpha);

            var lastEnableBlend = _gl.IsEnabled(GLEnum.Blend);
            var lastEnableCullFace = _gl.IsEnabled(GLEnum.CullFace);
            var lastEnableDepthTest = _gl.IsEnabled(GLEnum.DepthTest);
            var lastEnableStencilTest = _gl.IsEnabled(GLEnum.StencilTest);
            var lastEnableScissorTest = _gl.IsEnabled(GLEnum.ScissorTest);

            _gl.ActiveTexture(GLEnum.Texture0);
            _gl.Enable(GLEnum.Blend);
            _gl.BlendEquation(GLEnum.FuncAdd);
            _gl.BlendFuncSeparate(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha, GLEnum.One, GLEnum.OneMinusSrcAlpha);
            _gl.Disable(GLEnum.CullFace);
            _gl.Disable(GLEnum.DepthTest);
            _gl.Disable(GLEnum.StencilTest);
            _gl.Enable(GLEnum.ScissorTest);

            float L = drawData.DisplayPos.X;
            float R = drawData.DisplayPos.X + drawData.DisplaySize.X;
            float T = drawData.DisplayPos.Y;
            float B = drawData.DisplayPos.Y + drawData.DisplaySize.Y;

            Span<float> orthoProjection =
            [
                2.0f / (R - L), 0.0f, 0.0f, 0.0f,
                0.0f, 2.0f / (T - B), 0.0f, 0.0f,
                0.0f, 0.0f, -1.0f, 0.0f,
                (R + L) / (L - R), (T + B) / (B - T), 0.0f, 1.0f
            ];

            _gl.UseProgram(_shaderProgram);
            _gl.Uniform1(_attribLocationTex, 0);
            _gl.UniformMatrix4(_attribLocationProjMtx, 1, false, orthoProjection);

            _gl.BindVertexArray(_vao);
            _gl.BindBuffer(GLEnum.ArrayBuffer, _vbo);
            _gl.BindBuffer(GLEnum.ElementArrayBuffer, _ebo);

            _gl.EnableVertexAttribArray(_attribLocationVtxPos);
            _gl.EnableVertexAttribArray(_attribLocationVtxUV);
            _gl.EnableVertexAttribArray(_attribLocationVtxColor);

            _gl.VertexAttribPointer(_attribLocationVtxPos, 2, GLEnum.Float, false, (uint)sizeof(ImDrawVert), (void*)0);
            _gl.VertexAttribPointer(_attribLocationVtxUV, 2, GLEnum.Float, false, (uint)sizeof(ImDrawVert), (void*)8);
            _gl.VertexAttribPointer(_attribLocationVtxColor, 4, GLEnum.UnsignedByte, true, (uint)sizeof(ImDrawVert), (void*)16);

            var clipOff = drawData.DisplayPos;
            var clipScale = drawData.FramebufferScale;

            for (int n = 0; n < drawData.CmdListsCount; n++)
            {
                var cmdList = drawData.CmdLists[n];

                _gl.BufferData(GLEnum.ArrayBuffer, (nuint)(cmdList.VtxBuffer.Size * sizeof(ImDrawVert)), (void*)cmdList.VtxBuffer.Data, GLEnum.StreamDraw);
                _gl.BufferData(GLEnum.ElementArrayBuffer, (nuint)(cmdList.IdxBuffer.Size * sizeof(ushort)), (void*)cmdList.IdxBuffer.Data, GLEnum.StreamDraw);

                for (int cmdIdx = 0; cmdIdx < cmdList.CmdBuffer.Size; cmdIdx++)
                {
                    var pcmd = cmdList.CmdBuffer[cmdIdx];

                    if (pcmd.UserCallback != nint.Zero)
                        continue;

                    var clipMin = new Vector2((pcmd.ClipRect.X - clipOff.X) * clipScale.X, (pcmd.ClipRect.Y - clipOff.Y) * clipScale.Y);
                    var clipMax = new Vector2((pcmd.ClipRect.Z - clipOff.X) * clipScale.X, (pcmd.ClipRect.W - clipOff.Y) * clipScale.Y);

                    if (clipMax.X <= clipMin.X || clipMax.Y <= clipMin.Y)
                        continue;

                    _gl.Scissor((int)clipMin.X, (int)(drawData.DisplaySize.Y * clipScale.Y - clipMax.Y), (uint)(clipMax.X - clipMin.X), (uint)(clipMax.Y - clipMin.Y));
                    _gl.BindTexture(GLEnum.Texture2D, (uint)pcmd.TextureId);
                    _gl.DrawElementsBaseVertex(GLEnum.Triangles, pcmd.ElemCount, GLEnum.UnsignedShort, (void*)(pcmd.IdxOffset * sizeof(ushort)), (int)pcmd.VtxOffset);
                }
            }

            _gl.UseProgram((uint)lastProgram);
            _gl.BindTexture(GLEnum.Texture2D, (uint)lastTexture);
            _gl.ActiveTexture((GLEnum)lastActiveTexture);
            _gl.BindVertexArray((uint)lastVertexArrayObject);
            _gl.BindBuffer(GLEnum.ArrayBuffer, (uint)lastArrayBuffer);

            _gl.BlendEquationSeparate((GLEnum)lastBlendEquationRgb, (GLEnum)lastBlendEquationAlpha);
            _gl.BlendFuncSeparate((GLEnum)lastBlendSrcRgb, (GLEnum)lastBlendDstRgb, (GLEnum)lastBlendSrcAlpha, (GLEnum)lastBlendDstAlpha);

            if (lastEnableBlend) _gl.Enable(GLEnum.Blend); else _gl.Disable(GLEnum.Blend);
            if (lastEnableCullFace) _gl.Enable(GLEnum.CullFace); else _gl.Disable(GLEnum.CullFace);
            if (lastEnableDepthTest) _gl.Enable(GLEnum.DepthTest); else _gl.Disable(GLEnum.DepthTest);
            if (lastEnableStencilTest) _gl.Enable(GLEnum.StencilTest); else _gl.Disable(GLEnum.StencilTest);
            if (lastEnableScissorTest) _gl.Enable(GLEnum.ScissorTest); else _gl.Disable(GLEnum.ScissorTest);

            _gl.Viewport(lastViewport[0], lastViewport[1], (uint)lastViewport[2], (uint)lastViewport[3]);
            _gl.Scissor(lastScissorBox[0], lastScissorBox[1], (uint)lastScissorBox[2], (uint)lastScissorBox[3]);
        }

        #endregion

        public void Dispose()
        {
            foreach (var keyboard in _input.Keyboards)
            {
                keyboard.KeyDown -= OnKeyDown;
                keyboard.KeyUp -= OnKeyUp;
                keyboard.KeyChar -= OnKeyChar;
            }

            foreach (var mouse in _input.Mice)
            {
                mouse.MouseMove -= OnMouseMove;
                mouse.MouseDown -= OnMouseDown;
                mouse.MouseUp -= OnMouseUp;
                mouse.Scroll -= OnScroll;
            }

            if (_ownsInputContext)
            {
                _input.Dispose();
            }

            _gl.DeleteBuffer(_vbo);
            _gl.DeleteBuffer(_ebo);
            _gl.DeleteVertexArray(_vao);
            _gl.DeleteTexture(_fontTexture);
            _gl.DeleteProgram(_shaderProgram);

            GlImGui.DestroyContext();
        }
    }
}
