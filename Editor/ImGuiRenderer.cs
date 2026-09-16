// Derived from the ImGui.NET sample renderer.
// Copyright (c) 2017 Eric Mellino and ImGui.NET contributors, MIT (licenses/ImGui.NET-MIT.txt).

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace PixelCore.Editor;

public class ImGuiRenderer : IDisposable
{
    private Game _game;
    private GraphicsDevice _graphicsDevice;
    private BasicEffect _effect = null!;
    private RasterizerState _rasterizerState = null!;

    private float _dpiScale = 1f;

    public float DpiScale => _dpiScale;
    private WindowSpaces _spaces;

    private byte[] _vertexData = Array.Empty<byte>();
    private VertexBuffer? _vertexBuffer;
    private int _vertexBufferSize;

    private byte[] _indexData = Array.Empty<byte>();
    private IndexBuffer? _indexBuffer;
    private int _indexBufferSize;

    private Dictionary<IntPtr, Texture2D> _loadedTextures = new();
    private int _textureId = 1;
    private IntPtr? _fontTextureId;
    private IntPtr _checkerMaskId;

    private ushort[]? _iconRange;
    private GCHandle _iconRangeHandle;
    private ImFontGlyphRangesBuilderPtr _semiBoldRanges;
    private ImFontGlyphRangesBuilderPtr _mainRanges;

    private int _scrollWheelValue;
    private float _pendingWheel;
    private readonly List<int> _keys = new();

    internal const float TrackpadDamp = 0.2f;

    internal static float TameScrollDelta(int frameDelta)
        => Math.Abs(frameDelta) >= 120 ? frameDelta : frameDelta * TrackpadDamp;

    public ImGuiRenderer(Game game)
    {
        _game = game;
        _graphicsDevice = game.GraphicsDevice;

        var context = ImGui.CreateContext();
        ImGui.SetCurrentContext(context);

        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        io.ConfigDockingWithShift = true;
        io.ConfigDragClickToInputText = true;
        if (OperatingSystem.IsMacOS()) io.ConfigMacOSXBehaviors = true;

        SetupInput();
        EditorTheme.Apply();

        var checkerMask = new Texture2D(_graphicsDevice, 2, 2, false, SurfaceFormat.Color);
        checkerMask.SetData(new[] { Color.White, Color.Transparent, Color.Transparent, Color.White });
        _checkerMaskId = BindTexture(checkerMask);
        EditorTheme.CheckerMaskId = _checkerMaskId;
    }

    private float CurrentDpiScale() => WindowSpaces.Capture(_game.Window, _graphicsDevice).Scale;

    public static ImFontPtr MonoFont;

    public static ImFontPtr SemiBoldFont;

    public static ImFontPtr SmallFont;

    public static ImFontPtr GizmoIconFont;

    public const float GizmoIconSize = 20f;

    private const string UiPunctuation = "—–…‘’“”←→↑↓✓";

    private const string SemiBoldExtraGlyphs = "";

    public void RebuildFontAtlas()
    {
        var io = ImGui.GetIO();

        _dpiScale = CurrentDpiScale();
        Console.WriteLine($"[ImGui] font atlas rebuild: dpiScale {_dpiScale:0.##} " +
                          $"(backbuffer {_graphicsDevice.PresentationParameters.BackBufferWidth}x{_graphicsDevice.PresentationParameters.BackBufferHeight}, " +
                          $"window {_game.Window.ClientBounds.Width}x{_game.Window.ClientBounds.Height})");

        io.Fonts.Clear();

        var fontDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content", "Fonts");
        var mainFont = Path.Combine(fontDir, "Pretendard-Regular.ttf");
        var iconFontPath = Path.Combine(fontDir, "phosphor-bold.ttf");

        const float BaseFontSize = 14f;
        float fontSize = BaseFontSize * _dpiScale;

        if (File.Exists(mainFont))
        {
            unsafe
            {
                ImFontConfigPtr cfg = ImGuiNative.ImFontConfig_ImFontConfig();
                cfg.OversampleH = 1;
                cfg.OversampleV = 1;
                cfg.PixelSnapH = true;
                _mainRanges = new ImFontGlyphRangesBuilderPtr(
                    ImGuiNative.ImFontGlyphRangesBuilder_ImFontGlyphRangesBuilder());
                _mainRanges.AddRanges(io.Fonts.GetGlyphRangesDefault());
                _mainRanges.AddText(UiPunctuation);
                _mainRanges.BuildRanges(out ImVector mainRangeData);

                io.Fonts.AddFontFromFileTTF(mainFont, fontSize, cfg, mainRangeData.Data);
                ImGuiNative.ImFontConfig_destroy(cfg.NativePtr);

                if (File.Exists(iconFontPath))
                {
                    if (_iconRangeHandle.IsAllocated) _iconRangeHandle.Free();
                    _iconRange = new ushort[] { 0xe000, 0xf8ff, 0 };
                    _iconRangeHandle = GCHandle.Alloc(_iconRange, GCHandleType.Pinned);

                    ImFontConfigPtr icfg = ImGuiNative.ImFontConfig_ImFontConfig();
                    icfg.MergeMode = true;
                    icfg.PixelSnapH = true;
                    icfg.GlyphMinAdvanceX = fontSize;
                    icfg.GlyphOffset = new System.Numerics.Vector2(0f, 2f * _dpiScale);
                    io.Fonts.AddFontFromFileTTF(iconFontPath, fontSize * 0.92f, icfg, _iconRangeHandle.AddrOfPinnedObject());
                    ImGuiNative.ImFontConfig_destroy(icfg.NativePtr);
                }

                var semiBoldPath = Path.Combine(fontDir, "Pretendard-SemiBold.ttf");
                if (File.Exists(semiBoldPath))
                {
                    _semiBoldRanges = new ImFontGlyphRangesBuilderPtr(
                        ImGuiNative.ImFontGlyphRangesBuilder_ImFontGlyphRangesBuilder());
                    _semiBoldRanges.AddRanges(io.Fonts.GetGlyphRangesDefault());
                    if (SemiBoldExtraGlyphs.Length > 0) _semiBoldRanges.AddText(SemiBoldExtraGlyphs);
                    _semiBoldRanges.BuildRanges(out ImVector sbRanges);

                    ImFontConfigPtr sbcfg = ImGuiNative.ImFontConfig_ImFontConfig();
                    sbcfg.OversampleH = 1;
                    sbcfg.OversampleV = 1;
                    sbcfg.PixelSnapH = true;
                    SemiBoldFont = io.Fonts.AddFontFromFileTTF(semiBoldPath, fontSize, sbcfg, sbRanges.Data);
                    ImGuiNative.ImFontConfig_destroy(sbcfg.NativePtr);

                    if (File.Exists(iconFontPath) && _iconRangeHandle.IsAllocated)
                    {
                        ImFontConfigPtr sicfg = ImGuiNative.ImFontConfig_ImFontConfig();
                        sicfg.MergeMode = true;
                        sicfg.PixelSnapH = true;
                        sicfg.GlyphMinAdvanceX = fontSize;
                        sicfg.GlyphOffset = new System.Numerics.Vector2(0f, 2f * _dpiScale);
                        io.Fonts.AddFontFromFileTTF(iconFontPath, fontSize * 0.92f, sicfg, _iconRangeHandle.AddrOfPinnedObject());
                        ImGuiNative.ImFontConfig_destroy(sicfg.NativePtr);
                    }

                    float smallSize = MathF.Round(fontSize * 0.78f);
                    ImFontConfigPtr smcfg = ImGuiNative.ImFontConfig_ImFontConfig();
                    smcfg.OversampleH = 1;
                    smcfg.OversampleV = 1;
                    smcfg.PixelSnapH = true;
                    SmallFont = io.Fonts.AddFontFromFileTTF(semiBoldPath, smallSize, smcfg, sbRanges.Data);
                    ImGuiNative.ImFontConfig_destroy(smcfg.NativePtr);

                    if (File.Exists(iconFontPath) && _iconRangeHandle.IsAllocated)
                    {
                        ImFontConfigPtr smicfg = ImGuiNative.ImFontConfig_ImFontConfig();
                        smicfg.MergeMode = true;
                        smicfg.PixelSnapH = true;
                        smicfg.GlyphMinAdvanceX = smallSize;
                        smicfg.GlyphOffset = new System.Numerics.Vector2(0f, 1.5f * _dpiScale);
                        io.Fonts.AddFontFromFileTTF(iconFontPath, smallSize * 0.92f, smicfg, _iconRangeHandle.AddrOfPinnedObject());
                        ImGuiNative.ImFontConfig_destroy(smicfg.NativePtr);
                    }
                }

                bool monoLoaded = false;
                var monoPath = "/System/Library/Fonts/Menlo.ttc";
                float monoSize = 13f * _dpiScale;
                if (File.Exists(monoPath))
                {
                    ImFontConfigPtr mcfg = ImGuiNative.ImFontConfig_ImFontConfig();
                    mcfg.OversampleH = 1;
                    mcfg.OversampleV = 1;
                    mcfg.PixelSnapH = true;
                    MonoFont = io.Fonts.AddFontFromFileTTF(monoPath, monoSize, mcfg);
                    ImGuiNative.ImFontConfig_destroy(mcfg.NativePtr);
                    monoLoaded = true;

                    ImFontConfigPtr kcfg = ImGuiNative.ImFontConfig_ImFontConfig();
                    kcfg.MergeMode = true;
                    kcfg.OversampleH = 1;
                    kcfg.OversampleV = 1;
                    kcfg.PixelSnapH = true;
                    io.Fonts.AddFontFromFileTTF(mainFont, monoSize, kcfg, io.Fonts.GetGlyphRangesDefault());
                    ImGuiNative.ImFontConfig_destroy(kcfg.NativePtr);

                    if (File.Exists(iconFontPath) && _iconRangeHandle.IsAllocated)
                    {
                        ImFontConfigPtr micfg = ImGuiNative.ImFontConfig_ImFontConfig();
                        micfg.MergeMode = true;
                        micfg.PixelSnapH = true;
                        micfg.GlyphOffset = new System.Numerics.Vector2(0f, 2f * _dpiScale);
                        io.Fonts.AddFontFromFileTTF(iconFontPath, monoSize * 0.92f, micfg, _iconRangeHandle.AddrOfPinnedObject());
                        ImGuiNative.ImFontConfig_destroy(micfg.NativePtr);
                    }
                }
                if (File.Exists(iconFontPath) && _iconRangeHandle.IsAllocated)
                {
                    ImFontConfigPtr gcfg = ImGuiNative.ImFontConfig_ImFontConfig();
                    gcfg.OversampleH = 1;
                    gcfg.OversampleV = 1;
                    gcfg.PixelSnapH = true;
                    GizmoIconFont = io.Fonts.AddFontFromFileTTF(
                        iconFontPath, GizmoIconSize * _dpiScale, gcfg, _iconRangeHandle.AddrOfPinnedObject());
                    ImGuiNative.ImFontConfig_destroy(gcfg.NativePtr);
                }

                if (!monoLoaded && io.Fonts.Fonts.Size > 0)
                    MonoFont = io.Fonts.Fonts[0];
                if (SemiBoldFont.NativePtr == null && io.Fonts.Fonts.Size > 0)
                    SemiBoldFont = io.Fonts.Fonts[0];
                if (SmallFont.NativePtr == null && io.Fonts.Fonts.Size > 0)
                    SmallFont = io.Fonts.Fonts[0];
                if (GizmoIconFont.NativePtr == null && io.Fonts.Fonts.Size > 0)
                    GizmoIconFont = io.Fonts.Fonts[0];
            }
        }
        else
        {
            io.Fonts.AddFontDefault();
            if (io.Fonts.Fonts.Size > 0) { MonoFont = io.Fonts.Fonts[0]; SemiBoldFont = io.Fonts.Fonts[0]; SmallFont = io.Fonts.Fonts[0]; }
        }
        io.FontGlobalScale = 1f / _dpiScale;

        io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out int bytesPerPixel);

        unsafe
        {
            if (_semiBoldRanges.NativePtr != null)
            {
                _semiBoldRanges.Destroy();
                _semiBoldRanges = default;
            }
        }

        var fontTexture = new Texture2D(_graphicsDevice, width, height, false, SurfaceFormat.Color);
        var data = new byte[width * height * bytesPerPixel];
        Marshal.Copy(pixels, data, 0, data.Length);
        fontTexture.SetData(data);

        if (_fontTextureId.HasValue)
            UnbindTexture(_fontTextureId.Value);

        _fontTextureId = BindTexture(fontTexture);
        io.Fonts.SetTexID(_fontTextureId.Value);
        io.Fonts.ClearTexData();
    }

    public IntPtr BindTexture(Texture2D texture)
    {
        var id = new IntPtr(_textureId++);
        _loadedTextures.Add(id, texture);
        return id;
    }

    public void UnbindTexture(IntPtr textureId)
    {
        _loadedTextures.Remove(textureId);
    }

    public void BeginLayout(GameTime gameTime)
    {
        if (MathF.Abs(CurrentDpiScale() - _dpiScale) > 0.01f)
            RebuildFontAtlas();

        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        ImGui.GetIO().DeltaTime = dt > 0f ? dt : 1f / 60f;
        UpdateInput();
        ImGui.NewFrame();
    }

    public void EndLayout()
    {
        DrawImePreedit();
        UpdateImeInputRect();
        UpdateMouseCursor();
        using (FrameProf.Measure("  imgui.Render()")) ImGui.Render();
        var dd = ImGui.GetDrawData();
        FrameProf.Count("#vertices", dd.TotalVtxCount);
        FrameProf.Count("#cmdLists", dd.CmdListsCount);
        RenderDrawData(dd);
    }

    private void SetupInput()
    {
        var io = ImGui.GetIO();

        _keys.Add((int)ImGuiKey.Tab);
        _keys.Add((int)ImGuiKey.LeftArrow);
        _keys.Add((int)ImGuiKey.RightArrow);
        _keys.Add((int)ImGuiKey.UpArrow);
        _keys.Add((int)ImGuiKey.DownArrow);
        _keys.Add((int)ImGuiKey.PageUp);
        _keys.Add((int)ImGuiKey.PageDown);
        _keys.Add((int)ImGuiKey.Home);
        _keys.Add((int)ImGuiKey.End);
        _keys.Add((int)ImGuiKey.Delete);
        _keys.Add((int)ImGuiKey.Backspace);
        _keys.Add((int)ImGuiKey.Enter);
        _keys.Add((int)ImGuiKey.Escape);
        _keys.Add((int)ImGuiKey.A);
        _keys.Add((int)ImGuiKey.C);
        _keys.Add((int)ImGuiKey.V);
        _keys.Add((int)ImGuiKey.X);
        _keys.Add((int)ImGuiKey.Y);
        _keys.Add((int)ImGuiKey.Z);

        TextInputEXT.TextInput += c =>
        {
            if (c != '\t' && (c < '' || c > ''))
                io.AddInputCharacter(c);
        };

        SetupIme();
        SetupClipboard();
        SetupMouseCursors();
    }

    private static string _preedit = "";

    public static string ImePreedit => _preedit;
    private static System.Numerics.Vector2 _imeCaret;
    private static float _imeLineHeight;
    private static bool _imeWantVisible;

    private unsafe void SetupIme()
    {
        TextInputEXT.TextEditing += (text, _, _) =>
        {
            _preedit = text ?? "";
        };

        var pio = ImGui.GetPlatformIO();
        pio.Platform_SetImeDataFn = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void>)&OnSetImeData;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    private static void OnSetImeData(IntPtr ctx, IntPtr viewport, IntPtr data)
    {
        if (data == IntPtr.Zero) return;
        var d = new ImGuiPlatformImeDataPtr(data);
        _imeWantVisible = d.WantVisible;
        _imeCaret = d.InputPos;
        _imeLineHeight = d.InputLineHeight;
    }

    private void DrawImePreedit()
    {
        if (_preedit.Length == 0) return;

        if (!ImGui.GetIO().WantTextInput) { _preedit = ""; return; }

        var dl = ImGui.GetForegroundDrawList();
        var size = ImGui.CalcTextSize(_preedit);
        float h = _imeLineHeight > 0f ? _imeLineHeight : size.Y;
        var min = _imeCaret;
        var max = new System.Numerics.Vector2(min.X + size.X + 4f, min.Y + h);

        dl.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.FrameBgActive));
        dl.AddText(new System.Numerics.Vector2(min.X + 2f, min.Y), ImGui.GetColorU32(ImGuiCol.Text), _preedit);
        dl.AddLine(new System.Numerics.Vector2(min.X, max.Y - 1f),
                   new System.Numerics.Vector2(max.X, max.Y - 1f),
                   ImGui.GetColorU32(EditorTheme.Accent), 1f);
    }

    private void UpdateImeInputRect()
    {
        if (!_imeWantVisible) return;
        float h = _imeLineHeight > 0f ? _imeLineHeight : 16f;
        TextInputEXT.SetInputRectangle(new Rectangle(
            (int)_imeCaret.X, (int)_imeCaret.Y, 1, (int)h));
    }

    private static IntPtr _clipboardUtf8;

    private static IntPtr _clipboardEmpty;

    private const int ClipboardMaxChars = 64 * 1024;

    private unsafe void SetupClipboard()
    {
        if (_clipboardEmpty == IntPtr.Zero) _clipboardEmpty = Marshal.StringToCoTaskMemUTF8("");

        var pio = ImGui.GetPlatformIO();
        pio.Platform_GetClipboardTextFn =
            (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr>)&OnGetClipboardText;
        pio.Platform_SetClipboardTextFn =
            (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)&OnSetClipboardText;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    private static IntPtr OnGetClipboardText(IntPtr ctx)
    {
        try
        {
            string s = SDL3.SDL.SDL_GetClipboardText() ?? "";
            if (s.Length > ClipboardMaxChars)
            {
                int cut = ClipboardMaxChars;
                if (char.IsHighSurrogate(s[cut - 1])) cut--;
                s = s.Substring(0, cut);
            }

            if (_clipboardUtf8 != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(_clipboardUtf8);
                _clipboardUtf8 = IntPtr.Zero;
            }
            if (s.Length == 0) return _clipboardEmpty;

            _clipboardUtf8 = Marshal.StringToCoTaskMemUTF8(s);
            return _clipboardUtf8;
        }
        catch
        {
            return _clipboardEmpty;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    private static void OnSetClipboardText(IntPtr ctx, IntPtr utf8Text)
    {
        try
        {
            if (utf8Text == IntPtr.Zero) return;
            string? s = Marshal.PtrToStringUTF8(utf8Text);
            if (s == null) return;
            SDL3.SDL.SDL_SetClipboardText(s);
        }
        catch
        {
        }
    }

    private IntPtr[] _sdlCursors = Array.Empty<IntPtr>();
    private IntPtr _lastSdlCursor;
    private bool _cursorShown = true;

    private void SetupMouseCursors()
    {
        _sdlCursors = new IntPtr[(int)ImGuiMouseCursor.COUNT];

        Make(ImGuiMouseCursor.Arrow,      SDL3.SDL.SDL_SystemCursor.SDL_SYSTEM_CURSOR_DEFAULT);
        Make(ImGuiMouseCursor.TextInput,  SDL3.SDL.SDL_SystemCursor.SDL_SYSTEM_CURSOR_TEXT);
        Make(ImGuiMouseCursor.ResizeAll,  SDL3.SDL.SDL_SystemCursor.SDL_SYSTEM_CURSOR_MOVE);
        Make(ImGuiMouseCursor.ResizeNS,   SDL3.SDL.SDL_SystemCursor.SDL_SYSTEM_CURSOR_NS_RESIZE);
        Make(ImGuiMouseCursor.ResizeEW,   SDL3.SDL.SDL_SystemCursor.SDL_SYSTEM_CURSOR_EW_RESIZE);
        Make(ImGuiMouseCursor.ResizeNESW, SDL3.SDL.SDL_SystemCursor.SDL_SYSTEM_CURSOR_NESW_RESIZE);
        Make(ImGuiMouseCursor.ResizeNWSE, SDL3.SDL.SDL_SystemCursor.SDL_SYSTEM_CURSOR_NWSE_RESIZE);
        Make(ImGuiMouseCursor.Hand,       SDL3.SDL.SDL_SystemCursor.SDL_SYSTEM_CURSOR_POINTER);
        Make(ImGuiMouseCursor.NotAllowed, SDL3.SDL.SDL_SystemCursor.SDL_SYSTEM_CURSOR_NOT_ALLOWED);

        ImGui.GetIO().BackendFlags |= ImGuiBackendFlags.HasMouseCursors;

        void Make(ImGuiMouseCursor slot, SDL3.SDL.SDL_SystemCursor id)
            => _sdlCursors[(int)slot] = SDL3.SDL.SDL_CreateSystemCursor(id);
    }

    private void UpdateMouseCursor()
    {
        if (_sdlCursors.Length == 0) return;

        var io = ImGui.GetIO();
        if ((io.ConfigFlags & ImGuiConfigFlags.NoMouseCursorChange) != 0) return;

        var cursor = ImGui.GetMouseCursor();

        if (io.MouseDrawCursor || cursor == ImGuiMouseCursor.None)
        {
            if (_cursorShown) { SDL3.SDL.SDL_HideCursor(); _cursorShown = false; }
            return;
        }

        int slot = (int)cursor;
        IntPtr want = (uint)slot < (uint)_sdlCursors.Length ? _sdlCursors[slot] : IntPtr.Zero;
        if (want == IntPtr.Zero) want = _sdlCursors[(int)ImGuiMouseCursor.Arrow];

        if (want != IntPtr.Zero && want != _lastSdlCursor)
        {
            SDL3.SDL.SDL_SetCursor(want);
            _lastSdlCursor = want;
        }
        if (!_cursorShown) { SDL3.SDL.SDL_ShowCursor(); _cursorShown = true; }
    }

    private void UpdateInput()
    {
        var io = ImGui.GetIO();
        _spaces = WindowSpaces.Capture(_game.Window, _graphicsDevice);

        if (!_game.IsActive)
        {
            io.AddMouseButtonEvent(0, false);
            io.AddMouseButtonEvent(1, false);
            io.AddMouseButtonEvent(2, false);
            _scrollWheelValue = Mouse.GetState().ScrollWheelValue;
            _pendingWheel = 0f;
            return;
        }

        var mouse = Mouse.GetState();
        var keyboard = Keyboard.GetState();

        var mp = _spaces.MouseToPoint(mouse.X, mouse.Y);
        io.AddMousePosEvent(mp.X, mp.Y);

        bool leftDown = mouse.LeftButton == ButtonState.Pressed && !WindowChrome.ShouldForceMouseUp();
        io.AddMouseButtonEvent(0, leftDown);
        io.AddMouseButtonEvent(1, mouse.RightButton == ButtonState.Pressed);
        io.AddMouseButtonEvent(2, mouse.MiddleButton == ButtonState.Pressed);

        var scrollDelta = mouse.ScrollWheelValue - _scrollWheelValue;
        _scrollWheelValue = mouse.ScrollWheelValue;
        _pendingWheel += TameScrollDelta(scrollDelta) / 120f;
        if (_pendingWheel != 0f)
        {
            float emit = _pendingWheel * (1f - MathF.Exp(-io.DeltaTime * 18f));
            if (MathF.Abs(_pendingWheel - emit) < 0.002f) emit = _pendingWheel;
            io.AddMouseWheelEvent(0, emit);
            _pendingWheel -= emit;
        }

        io.AddKeyEvent(ImGuiKey.Tab, keyboard.IsKeyDown(Keys.Tab));
        io.AddKeyEvent(ImGuiKey.LeftArrow, keyboard.IsKeyDown(Keys.Left));
        io.AddKeyEvent(ImGuiKey.RightArrow, keyboard.IsKeyDown(Keys.Right));
        io.AddKeyEvent(ImGuiKey.UpArrow, keyboard.IsKeyDown(Keys.Up));
        io.AddKeyEvent(ImGuiKey.DownArrow, keyboard.IsKeyDown(Keys.Down));
        io.AddKeyEvent(ImGuiKey.PageUp, keyboard.IsKeyDown(Keys.PageUp));
        io.AddKeyEvent(ImGuiKey.PageDown, keyboard.IsKeyDown(Keys.PageDown));
        io.AddKeyEvent(ImGuiKey.Home, keyboard.IsKeyDown(Keys.Home));
        io.AddKeyEvent(ImGuiKey.End, keyboard.IsKeyDown(Keys.End));
        io.AddKeyEvent(ImGuiKey.Delete, keyboard.IsKeyDown(Keys.Delete));
        io.AddKeyEvent(ImGuiKey.Backspace, keyboard.IsKeyDown(Keys.Back));
        io.AddKeyEvent(ImGuiKey.Enter, keyboard.IsKeyDown(Keys.Enter));
        io.AddKeyEvent(ImGuiKey.Escape, keyboard.IsKeyDown(Keys.Escape));
        io.AddKeyEvent(ImGuiKey.A, keyboard.IsKeyDown(Keys.A));
        io.AddKeyEvent(ImGuiKey.C, keyboard.IsKeyDown(Keys.C));
        io.AddKeyEvent(ImGuiKey.V, keyboard.IsKeyDown(Keys.V));
        io.AddKeyEvent(ImGuiKey.X, keyboard.IsKeyDown(Keys.X));
        io.AddKeyEvent(ImGuiKey.Y, keyboard.IsKeyDown(Keys.Y));
        io.AddKeyEvent(ImGuiKey.Z, keyboard.IsKeyDown(Keys.Z));

        io.AddKeyEvent(ImGuiKey.ModCtrl, keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl));
        io.AddKeyEvent(ImGuiKey.ModShift, keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift));
        io.AddKeyEvent(ImGuiKey.ModAlt, keyboard.IsKeyDown(Keys.LeftAlt) || keyboard.IsKeyDown(Keys.RightAlt));
        io.AddKeyEvent(ImGuiKey.ModSuper, keyboard.IsKeyDown(Keys.LeftWindows) || keyboard.IsKeyDown(Keys.RightWindows));

        io.DisplaySize = _spaces.DisplaySize;
        io.DisplayFramebufferScale = new System.Numerics.Vector2(_dpiScale, _dpiScale);
    }

    private void RenderDrawData(ImDrawDataPtr drawData)
    {
        var lastViewport = _graphicsDevice.Viewport;
        var lastScissorBox = _graphicsDevice.ScissorRectangle;

        _graphicsDevice.BlendFactor = Color.White;
        _graphicsDevice.BlendState = BlendState.NonPremultiplied;
        _graphicsDevice.RasterizerState = _rasterizerState ?? (_rasterizerState = new RasterizerState
        {
            CullMode = CullMode.None,
            DepthBias = 0,
            FillMode = FillMode.Solid,
            MultiSampleAntiAlias = false,
            ScissorTestEnable = true,
            SlopeScaleDepthBias = 0
        });
        _graphicsDevice.DepthStencilState = DepthStencilState.None;

        drawData.ScaleClipRects(ImGui.GetIO().DisplayFramebufferScale);

        _graphicsDevice.Viewport = new Viewport(0, 0,
            _graphicsDevice.PresentationParameters.BackBufferWidth,
            _graphicsDevice.PresentationParameters.BackBufferHeight);

        using (FrameProf.Measure("  imgui.buffers")) UpdateBuffers(drawData);

        using (FrameProf.Measure("  imgui.drawcalls")) RenderCommandLists(drawData);

        _graphicsDevice.Viewport = lastViewport;
        _graphicsDevice.ScissorRectangle = lastScissorBox;
    }

    private unsafe void UpdateBuffers(ImDrawDataPtr drawData)
    {
        if (drawData.TotalVtxCount == 0) return;

        if (drawData.TotalVtxCount > _vertexBufferSize)
        {
            _vertexBuffer?.Dispose();
            _vertexBufferSize = (int)(drawData.TotalVtxCount * 1.5f);
            _vertexBuffer = new VertexBuffer(_graphicsDevice, ImGuiDrawVertDeclaration.Declaration, _vertexBufferSize, BufferUsage.None);
            _vertexData = new byte[_vertexBufferSize * ImGuiDrawVertDeclaration.Size];
        }

        if (drawData.TotalIdxCount > _indexBufferSize)
        {
            _indexBuffer?.Dispose();
            _indexBufferSize = (int)(drawData.TotalIdxCount * 1.5f);
            _indexBuffer = new IndexBuffer(_graphicsDevice, IndexElementSize.SixteenBits, _indexBufferSize, BufferUsage.None);
            _indexData = new byte[_indexBufferSize * sizeof(ushort)];
        }

        int vtxOffset = 0;
        int idxOffset = 0;

        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            var cmdList = drawData.CmdLists[n];
            fixed (void* vtxDstPtr = &_vertexData[vtxOffset * ImGuiDrawVertDeclaration.Size])
            fixed (void* idxDstPtr = &_indexData[idxOffset * sizeof(ushort)])
            {
                Buffer.MemoryCopy((void*)cmdList.VtxBuffer.Data, vtxDstPtr, _vertexData.Length, cmdList.VtxBuffer.Size * ImGuiDrawVertDeclaration.Size);
                Buffer.MemoryCopy((void*)cmdList.IdxBuffer.Data, idxDstPtr, _indexData.Length, cmdList.IdxBuffer.Size * sizeof(ushort));
            }
            vtxOffset += cmdList.VtxBuffer.Size;
            idxOffset += cmdList.IdxBuffer.Size;
        }

        _vertexBuffer!.SetData(_vertexData, 0, drawData.TotalVtxCount * ImGuiDrawVertDeclaration.Size);
        _indexBuffer!.SetData(_indexData, 0, drawData.TotalIdxCount * sizeof(ushort));
    }

    private void RenderCommandLists(ImDrawDataPtr drawData)
    {
        _graphicsDevice.SetVertexBuffer(_vertexBuffer);
        _graphicsDevice.Indices = _indexBuffer;

        int vtxOffset = 0;
        int idxOffset = 0;

        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            var cmdList = drawData.CmdLists[n];

            for (int cmdi = 0; cmdi < cmdList.CmdBuffer.Size; cmdi++)
            {
                var drawCmd = cmdList.CmdBuffer[cmdi];

                if (!_loadedTextures.ContainsKey(drawCmd.TextureId))
                    throw new InvalidOperationException($"Could not find texture id '{drawCmd.TextureId}'");

                _graphicsDevice.ScissorRectangle = new Rectangle(
                    (int)drawCmd.ClipRect.X,
                    (int)drawCmd.ClipRect.Y,
                    (int)(drawCmd.ClipRect.Z - drawCmd.ClipRect.X),
                    (int)(drawCmd.ClipRect.W - drawCmd.ClipRect.Y)
                );

                var textureId = drawCmd.TextureId;
                bool isFont = _fontTextureId.HasValue && textureId == _fontTextureId.Value;
                _graphicsDevice.SamplerStates[0] =
                    isFont ? SamplerState.LinearClamp :
                    textureId == _checkerMaskId ? SamplerState.PointWrap :
                    SamplerState.PointClamp;

                FrameProf.Count("#drawCalls", 1);
                var effect = UpdateEffect(_loadedTextures[textureId]);
                foreach (var pass in effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    _graphicsDevice.DrawIndexedPrimitives(
                        PrimitiveType.TriangleList,
                        (int)drawCmd.VtxOffset + vtxOffset,
                        0,
                        cmdList.VtxBuffer.Size,
                        (int)drawCmd.IdxOffset + idxOffset,
                        (int)drawCmd.ElemCount / 3
                    );
                }
            }

            vtxOffset += cmdList.VtxBuffer.Size;
            idxOffset += cmdList.IdxBuffer.Size;
        }
    }

    private Effect UpdateEffect(Texture2D texture)
    {
        _effect ??= new BasicEffect(_graphicsDevice);

        var io = ImGui.GetIO();

        _effect.World = Matrix.Identity;
        _effect.View = Matrix.Identity;
        _effect.Projection = Matrix.CreateOrthographicOffCenter(0f, io.DisplaySize.X, io.DisplaySize.Y, 0f, -1f, 1f);
        _effect.TextureEnabled = true;
        _effect.Texture = texture;
        _effect.VertexColorEnabled = true;

        return _effect;
    }

    public void Dispose()
    {
        if (_sdlCursors.Length > 0)
        {
            SDL3.SDL.SDL_SetCursor(SDL3.SDL.SDL_GetDefaultCursor());
            foreach (var c in _sdlCursors)
                if (c != IntPtr.Zero) SDL3.SDL.SDL_DestroyCursor(c);
            _sdlCursors = Array.Empty<IntPtr>();
            _lastSdlCursor = IntPtr.Zero;
        }

        _vertexBuffer?.Dispose();
        _indexBuffer?.Dispose();
        _effect?.Dispose();
        _rasterizerState?.Dispose();
        if (_iconRangeHandle.IsAllocated) _iconRangeHandle.Free();
    }
}

public static class ImGuiDrawVertDeclaration
{
    public static readonly VertexDeclaration Declaration;
    public static readonly int Size;

    static ImGuiDrawVertDeclaration()
    {
        unsafe { Size = sizeof(ImDrawVert); }

        Declaration = new VertexDeclaration(
            Size,
            new VertexElement(0, VertexElementFormat.Vector2, VertexElementUsage.Position, 0),
            new VertexElement(8, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
            new VertexElement(16, VertexElementFormat.Color, VertexElementUsage.Color, 0)
        );
    }
}
