﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CefSharp;
using CefSharp.OffScreen;
using OpenTK.Graphics.OpenGL;
using Valve.VR;
using System.Drawing;
using System.Drawing.Imaging;
using CefSharp.Internals;
using System.IO;
using OpenTK;

namespace SteamVR_WebKit
{
    public class WebKitOverlay
    {
        public const int SCROLL_AMOUNT_PER_SWIPE = 1500;

        Uri _uri;
        Overlay _dashboardOverlay;
        Overlay _inGameOverlay;
        Texture_t _textureData;
        string _cachePath;
        double _zoomLevel;
        int _windowWidth;
        int _windowHeight;
        bool _isRendering = false;
        ChromiumWebBrowser _browser;
        bool _wasVisible = false;
        VREvent_t ovrEvent;
        BrowserSettings _browserSettings;
        bool _renderInGameOverlay;
        VREvent_t eventData = new VREvent_t();
        OpenTK.Vector2 mouseClickPosition;
        bool brokeFromJitterThreshold = false;

        //public bool EnableTransparency {
        //    get
        //    {
        //        return _browserSettings.OffScreenTransparentBackground.HasValue ? false : _browserSettings.OffScreenTransparentBackground.Value;
        //    }

        //    set
        //    {
        //        _browserSettings.OffScreenTransparentBackground = value;
        //    }
        //}

        #region OGL Stuff
        int _glInputTextureId = 0;
        int _glOutputTextureId = 0;
        int _glAlphaMaskTextureId = 0;
        int _glFrameBufferId = 0;
        int _glFragmentShaderProgramId = 0;
        //int _glDepthRenderBuffer = 0;
        #endregion

        public string FragmentShaderPath { get; set; }

        public bool UpdateEveryFrame { get; set; }

        bool _dirtySize = true;

        public OverlayMessageHandler MessageHandler { get; }

        Vector2 _lastMousePosition;

        bool _browserDidUpdate;

        string _overlayKey;
        string _overlayName;

        bool _allowScrolling = true;

        public bool AllowScrolling
        {
            get { return _allowScrolling; }
            set {
                _allowScrolling = value;

                if (InGameOverlay != null)
                    InGameOverlay.EnableScrolling = value;

                if (DashboardOverlay != null)
                    DashboardOverlay.EnableScrolling = value;
            }
        }

        bool _isHolding = false;

        public event EventHandler BrowserPreInit;
        public event EventHandler BrowserReady;
        public event EventHandler BrowserRenderUpdate;
        public event EventHandler PageReady;

        public event EventHandler PreUpdateCallback;
        public event EventHandler PostUpdateCallback;

        public event EventHandler PreDrawCallback;
        public event EventHandler PostDrawCallback;

        public delegate void OnFocusedNodeChanged(IWebBrowser browserControl, IBrowser browser, IFrame frame, IDomNode node);
        public delegate void OnContextCreated(IWebBrowser browserControl, IBrowser browser, IFrame frame);

        public event OnFocusedNodeChanged FocusedNodeChanged;
        public event OnContextCreated ContextCreated;

        /// <summary>
        /// Amount of pixels the pointer may move away from where you click+hold before a move is registered. Mostly there because 
        /// </summary>
        public int MouseDeltaTolerance { get; set; }

        public Uri Uri
        {
            get { return _uri; }
        }

        public Overlay DashboardOverlay
        {
            get { return _dashboardOverlay; }
        }

        public Overlay InGameOverlay
        {
            get { return _inGameOverlay; }
        }

        public bool IsRendering
        {
            get { return _isRendering; }
        }

        public int GLTextureID
        {
            get { return _glInputTextureId; }
        }

        public bool EnableKeyboard { get; set; }

        //public BrowserSettings BrowserSettings
        //{
        //    get { if (_browser != null) return _browser.b`.BrowserSettings; else return _browserSettings; }
        //    set { _browserSettings = value; }
        //}

        public bool RenderInGameOverlay
        {
            get { return _renderInGameOverlay; }
            set
            {
                _renderInGameOverlay = value;
                if (InGameOverlay != null)
                {
                    if (_renderInGameOverlay)
                        InGameOverlay.Show();
                    else
                        InGameOverlay.Hide();
                }
            }
        }

        bool _enableNonDashboardInput;

        public bool EnableNonDashboardInput
        {
            get { return _enableNonDashboardInput; }
            set
            {
                _enableNonDashboardInput = value;

                if(InGameOverlay != null)
                {
                    InGameOverlay.ToggleInput(value);
                }
            }
        }

        public List<CefCustomScheme> SchemeHandlers { get; } = new List<CefCustomScheme>();

        public ChromiumWebBrowser Browser
        {
            get { return _browser; }
        }

        public string CachePath { get { return _cachePath; } set { _cachePath = value; } }

        public IRequestContextHandler RequestContextHandler { get; set; }

        public double ZoomLevel { get { return _zoomLevel; } set { _zoomLevel = value; } }

        public Bitmap AlphaMask { get; set; }

        [Obsolete("Please use the newer constructor that lets you define whether to show in dashboard, in-game or both instead.")]
        public WebKitOverlay(Uri uri, int windowWidth, int windowHeight, string overlayKey, string overlayName, float overlayWidth = 2f, bool isInGameOverlay = false) : this(uri, windowWidth, windowHeight, overlayKey, overlayName, isInGameOverlay ? OverlayType.InGame : OverlayType.Dashboard)
        {

        }

        public WebKitOverlay(Uri uri, int windowWidth, int windowHeight, string overlayKey, string overlayName, OverlayType overlayType)
        {
            if (!SteamVR_WebKit.Initialised)
                SteamVR_WebKit.Init();

            MessageHandler = new OverlayMessageHandler(this);

            _browserSettings = new BrowserSettings();
            _browserSettings.WindowlessFrameRate = 30;
            _uri = uri;
            _windowWidth = windowWidth;
            _windowHeight = windowHeight;
            _overlayKey = overlayKey;
            _overlayName = overlayName;

            AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;

            if (overlayType == OverlayType.Dashboard)
                CreateDashboardOverlay();
            else if (overlayType == OverlayType.InGame)
                CreateInGameOverlay();
            else
            {
                CreateDashboardOverlay(true);
                CreateInGameOverlay(true);
            }

            SteamVR_WebKit.Overlays.Add(this);

            FocusedNodeChanged += WebKitOverlay_FocusedNodeChanged;

            SetupTextures();
        }

        private void CurrentDomain_ProcessExit(object sender, EventArgs e)
        {
            if (_glFragmentShaderProgramId > 0)
                GL.DeleteProgram(_glFragmentShaderProgramId);
        }

        private void WebKitOverlay_FocusedNodeChanged(IWebBrowser browserControl, IBrowser browser, IFrame frame, IDomNode node)
        {
            if (IsKeyboardElement(node) && EnableKeyboard)
            {
                if (SteamVR_WebKit.ActiveKeyboardOverlay == this)
                    return;

                SteamVR_WebKit.OverlayManager.HideKeyboard();
                SteamVR_WebKit.ActiveKeyboardOverlay = null;

                if (CanDoUpdates())
                {
                    ShowKeyboard(GetNodeValue(node));
                }
            } else
            {
                if(SteamVR_WebKit.ActiveKeyboardOverlay == this)
                {
                    SteamVR_WebKit.OverlayManager.HideKeyboard();
                    SteamVR_WebKit.ActiveKeyboardOverlay = null;
                }
            }
        }

        public void ShowKeyboard(string value = "")
        {
            EVROverlayError err = EVROverlayError.None;

            if (DashboardOverlay == null && InGameOverlay != null)
            {
                err = SteamVR_WebKit.OverlayManager.ShowKeyboardForOverlay(InGameOverlay.Handle, 0, 0, "", 256, value, true, 0);
                SteamVR_WebKit.OverlayManager.SetKeyboardPositionForOverlay(InGameOverlay.Handle, new HmdRect2_t() { vTopLeft = new HmdVector2_t() { v0 = 0, v1 = _windowHeight }, vBottomRight = new HmdVector2_t() { v0 = _windowWidth, v1 = 0 } });
            }
            else if (DashboardOverlay != null && InGameOverlay == null)
            {
                err = SteamVR_WebKit.OverlayManager.ShowKeyboardForOverlay(DashboardOverlay.Handle, 0, 0, "", 256, value, true, 0);
                SteamVR_WebKit.OverlayManager.SetKeyboardPositionForOverlay(DashboardOverlay.Handle, new HmdRect2_t() { vTopLeft = new HmdVector2_t() { v0 = 0, v1 = _windowHeight }, vBottomRight = new HmdVector2_t() { v0 = _windowWidth, v1 = 0 } });
            }
            else if (DashboardOverlay != null && InGameOverlay != null)
            {
                // Maybe use last interacted?
            }
            else
            {
                err = SteamVR_WebKit.OverlayManager.ShowKeyboard(0, 0, "", 256,value, true, 0);
            }

            if (err == EVROverlayError.None)
                SteamVR_WebKit.ActiveKeyboardOverlay = this;

        }

        public void HideKeyboard()
        {
            SteamVR_WebKit.OverlayManager.HideKeyboard();
            SteamVR_WebKit.ActiveKeyboardOverlay = null;
        }

        bool IsKeyboardElement(IDomNode node)
        {
            if (node == null)
                return false;

            if (node.TagName.ToLower() == "input")
            {
                if (!node.HasAttribute("type") || node.HasAttribute("type") && (node["type"] != "checkbox" && node["type"] != "radio"))
                    return true;
                else
                    return false;
            }
            else if (node.TagName.ToLower() == "textarea")
                return true;
            else if (node.HasAttribute("contenteditable"))
                return true;

            return false;
        }

        public void UpdateInputSettings()
        {
            if (DashboardOverlay != null)
                DashboardOverlay.ToggleInput(true);

            if (InGameOverlay != null)
                InGameOverlay.ToggleInput(EnableNonDashboardInput);
        }

        public void SetBrowserSize(int width, int height)
        {
            _browser.Size = new Size(width, height);
            _dirtySize = true;
        }

        string GetNodeValue(IDomNode node)
        {
            if (node == null)
                return null;

            if (node.TagName.ToLower() == "input")
                return node.HasAttribute("value") ? node["value"] : "";
            else if (node.TagName.ToLower() == "textarea")
            {
                return "";
            }

            return null;
        }
        public void KeyboardInput(byte[] characters)
        {
            int len = 0;

            for (int i = 0; i < 8; i++)
            {
                if (characters[i] == 0)
                    continue;

                KeyEvent ev = KeyboardUtils.ConvertCharToVirtualKeyEvent(characters[i]);
                ev.FocusOnEditableField = false;

                SteamVR_WebKit.Log("[KEY] Key Code: " + ev.WindowsKeyCode + " | Modifiers: " + ev.Modifiers.ToString());

                Browser.GetBrowser().GetHost().SendKeyEvent(ev);
            }
        }

        public void ToggleAudio()
        {
            throw new NotImplementedException("I'll find the option to change the audio in CEF eventually.");
        }

        public void CreateDashboardOverlay(bool forcePrefix = false)
        {
            _dashboardOverlay = new Overlay((SteamVR_WebKit.PrefixOverlayType || forcePrefix ? "dashboard." : "") + _overlayKey, _overlayName, 2.0f, false);
            _dashboardOverlay.SetTextureSize(_windowWidth, _windowHeight);
            //_dashboardOverlay.Show();
        }

        public void CreateInGameOverlay(bool forcePrefix = false)
        {
            _inGameOverlay = new Overlay((SteamVR_WebKit.PrefixOverlayType || forcePrefix ? "ingame." : "") + _overlayKey, _overlayName, 2.0f, true);
            _inGameOverlay.SetTextureSize(_windowWidth, _windowHeight);
            _inGameOverlay.ToggleInput(EnableNonDashboardInput);
            _inGameOverlay.Show();
        }

        public void Destroy()
        {
            if (_inGameOverlay != null)
                DestroyInGameOverlay();

            if (_dashboardOverlay != null)
                DestroyDashboardOverlay();

            SteamVR_WebKit.Overlays.Remove(this);

            _browser.GetBrowser().CloseBrowser(true);
        }

        public void DestroyInGameOverlay()
        {
            _inGameOverlay.Destroy();
            _inGameOverlay = null;
        }

        public void DestroyDashboardOverlay()
        {
            _dashboardOverlay.Destroy();
            _dashboardOverlay = null;
        }

        public void StartBrowser(bool waitForAttachment = false)
        {
            //Allow the overlay to let us know when the controller showed up and we were able to attach to it
            if (waitForAttachment)
            {
                //Its possible that it happened before we got here if the controller was present at start
                if (_inGameOverlay.AttachmentSuccess)
                    AsyncBrowser();
                else
                    _inGameOverlay.OnAttachmentSuccess += AsyncBrowser;
            }
            else
                AsyncBrowser();
        }

        private Bitmap _bitmap;
        private object _bitmapLock = new object();
        protected virtual async void AsyncBrowser()
        {
            RequestContextSettings contextSettings = new RequestContextSettings()
            {
                CachePath = CachePath,
            };

            using (RequestContext context = new RequestContext(contextSettings, RequestContextHandler))
            {
                foreach(CefCustomScheme scheme in SchemeHandlers)
                {
                    context.RegisterSchemeHandlerFactory(scheme.SchemeName, scheme.DomainName, scheme.SchemeHandlerFactory);
                }

                SteamVR_WebKit.Log("Browser Initialising for " + _overlayKey);

                _browser = new ChromiumWebBrowser(Uri.ToString(), _browserSettings, context, false);
                Browser.RenderProcessMessageHandler = MessageHandler;
                BrowserPreInit?.Invoke(_browser, new EventArgs());
                _browser.Size = new Size((int)_windowWidth, (int)_windowHeight);
                //_browser.NewScreenshot += Browser_NewScreenshot;

                _browser.BrowserInitialized += _browser_BrowserInitialized;

                _browser.CreateBrowser();

                if (_zoomLevel > 1)
                {
                    _browser.FrameLoadStart += (s, argsi) =>
                    {
                        if (argsi.Frame.IsMain)
                        {
                            ((ChromiumWebBrowser)s).SetZoomLevel(_zoomLevel);
                        }
                    };
                }

                await LoadPageAsync(_browser);
            }

            //If while we waited any JS commands were queued, then run those now
            ExecQueuedJS();
        }

        //private void DoScreenshot()
        //{
        //    _browser.ScreenshotAsync().ContinueWith(x =>
        //    {
        //        _isRendering = true;

        //        _browserDidUpdate = true;

        //        lock (_bitmapLock)
        //        {
        //            _bitmap = x.Result;
        //        }

        //        BrowserRenderUpdate?.Invoke(this, new EventArgs());
        //        DoScreenshot();
        //    });
        //}

        private void _browser_BrowserInitialized(object sender, EventArgs e)
        {
            SteamVR_WebKit.Log("Browser Initialised for " + _overlayKey);
            BrowserReady?.Invoke(_browser, new EventArgs());
            // DoScreenshot();
            _isRendering = true;

            _browserDidUpdate = true;
        }

        public Task LoadPageAsync(ChromiumWebBrowser browser, string address = null)
        {
            //If using .Net 4.6 then use TaskCreationOptions.RunContinuationsAsynchronously
            //and switch to tcs.TrySetResult below - no need for the custom extension method
            var tcs = new TaskCompletionSource<bool>();

            EventHandler<LoadingStateChangedEventArgs> handler = null;
            handler = (sender, args) =>
            {
                //Wait for while page to finish loading not just the first frame
                if (!args.IsLoading)
                {
                    SteamVR_WebKit.Log("Page Loaded for " + _overlayKey);
                    PageReady?.Invoke(browser, new EventArgs());

                    browser.LoadingStateChanged -= handler;
                    //This is required when using a standard TaskCompletionSource
                    //Extension method found in the CefSharp.Internals namespace
                    tcs.TrySetResultAsync(true);
                }
            };

            browser.LoadingStateChanged += handler;

            if (!string.IsNullOrEmpty(address))
            {
                browser.Load(address);
            }
            return tcs.Task;
        }

        //private void Browser_NewScreenshot(object sender, EventArgs e)
        //{
        //    ChromiumWebBrowser browser = (ChromiumWebBrowser)sender;

        //    if (browser.Bitmap != null)
        //        _isRendering = true;

        //    _browserDidUpdate = true;

        //    BrowserRenderUpdate?.Invoke(sender, e);
        //}

        protected virtual void SetupTextures()
        {
            SteamVR_WebKit.Log("Setting up texture for " + _overlayKey);
            GL.BindTexture(TextureTarget.Texture2D, 0);

            if(SteamVR_WebKit.TraceLevel)
                SteamVR_WebKit.Log("BindTexture: " + GL.GetError());

            _glInputTextureId = GL.GenTexture();

            if (SteamVR_WebKit.TraceLevel)
                SteamVR_WebKit.Log("GenTexture: " + GL.GetError());

            GL.BindTexture(TextureTarget.Texture2D, _glInputTextureId);

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);

            if (SteamVR_WebKit.TraceLevel)
                SteamVR_WebKit.Log("TexParameter: " + GL.GetError());

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            if (SteamVR_WebKit.TraceLevel)
                SteamVR_WebKit.Log("TexParameter: " + GL.GetError());

            _textureData = new Texture_t();
            _textureData.eColorSpace = EColorSpace.Linear;
            _textureData.eType = ETextureType.OpenGL;
            _textureData.handle = (IntPtr)_glInputTextureId;

            if(SteamVR_WebKit.UseExperimentalOGL)
            {
                GL.MatrixMode(MatrixMode.Projection);
                GL.LoadIdentity();
                _glFrameBufferId = GL.GenFramebuffer();
                _glOutputTextureId = GL.GenTexture();
                _textureData.handle = (IntPtr)_glOutputTextureId;

                GL.BindTexture(TextureTarget.Texture2D, _glOutputTextureId);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, _windowWidth, _windowHeight, 0, OpenTK.Graphics.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
                GL.BindTexture(TextureTarget.Texture2D, 0);

                GL.BindFramebuffer(FramebufferTarget.Framebuffer, _glFrameBufferId);

                GL.FramebufferTexture(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, _glOutputTextureId, 0);
                GL.DrawBuffer(DrawBufferMode.ColorAttachment0);

                if(GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete)
                {
                        SteamVR_WebKit.Log("[OPENGL] Failed to setup frame buffer: " + GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer).ToString());
                }

                GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

                if(SteamVR_WebKit.DefaultFragmentShaderPath != null || FragmentShaderPath != null)
                {
                    SteamVR_WebKit.Log("[OPENGL] Using Fragment Shader");

                    string path = FragmentShaderPath != null ? FragmentShaderPath : SteamVR_WebKit.DefaultFragmentShaderPath;

                    CompileShader(path);
                }
            }

            SteamVR_WebKit.Log("Texture Setup complete for " + _overlayKey);
        }

        void CompileShader(string path)
        {
            if(!File.Exists(path))
            {
                SteamVR_WebKit.Log("[OPENGL] No Shader Found at " + path);
                return;
            }

            int fragShaderId = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragShaderId, File.ReadAllText(path));
            GL.CompileShader(fragShaderId);

            _glFragmentShaderProgramId = GL.CreateProgram();
            GL.AttachShader(_glFragmentShaderProgramId, fragShaderId);
            GL.LinkProgram(_glFragmentShaderProgramId);

            SteamVR_WebKit.Log("[OPENGL] Shader Result: " + GL.GetProgramInfoLog(_glFragmentShaderProgramId));

            GL.DetachShader(_glFragmentShaderProgramId, fragShaderId);
            GL.DeleteShader(fragShaderId);

            GL.Uniform1(GL.GetUniformLocation(_glFragmentShaderProgramId, "_MainTex"), 0);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, _glInputTextureId);
        }

        public virtual void UpdateTexture()
        {
            if (!_browserDidUpdate && !UpdateEveryFrame)
                return;

            _browserDidUpdate = false;

            if (_bitmap == null)
                return;

            if (AlphaMask != null)
            {
                if (AlphaMask.Width != _bitmap.Width || AlphaMask.Height != _bitmap.Height)
                {
                    AlphaMask = new Bitmap(AlphaMask, new Size(_bitmap.Width, _bitmap.Height));

                    BitmapData alphaMapData = AlphaMask.LockBits(
                        new Rectangle(0, 0, AlphaMask.Width, AlphaMask.Height),
                        ImageLockMode.ReadOnly,
                        System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                    _glAlphaMaskTextureId = GL.GenTexture();

                    GL.BindTexture(TextureTarget.Texture2D, _glOutputTextureId);
                    GL.BindTexture(TextureTarget.Texture2D, _glAlphaMaskTextureId);
                    GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, AlphaMask.Width, AlphaMask.Height, 0, OpenTK.Graphics.OpenGL.PixelFormat.Bgra, PixelType.UnsignedByte, alphaMapData.Scan0);
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
                    GL.BindTexture(TextureTarget.Texture2D, 0);
                    AlphaMask.UnlockBits(alphaMapData);

                    if(_glFragmentShaderProgramId > 0)
                    {
                        GL.ActiveTexture(TextureUnit.Texture1);
                        GL.BindTexture(TextureTarget.Texture2D, _glAlphaMaskTextureId);
                        GL.ActiveTexture(TextureUnit.Texture0); // Reset
                    }

                }
            }

            lock (_bitmapLock) {
                BitmapData bmpData = _bitmap.LockBits(
                    new Rectangle(0, 0, _bitmap.Width, _bitmap.Height),
                    ImageLockMode.ReadWrite,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb
                    );

                GL.BindTexture(TextureTarget.Texture2D, _glInputTextureId);

                if (_dirtySize)
                {
                    GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, _bitmap.Width, _bitmap.Height, 0, OpenTK.Graphics.OpenGL.PixelFormat.Bgra, PixelType.UnsignedByte, bmpData.Scan0);
                    _dirtySize = false;
                }
                else
                {
                    GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, _bitmap.Width, _bitmap.Height, OpenTK.Graphics.OpenGL.PixelFormat.Bgra, PixelType.UnsignedByte, bmpData.Scan0);
                }

                if (SteamVR_WebKit.UseExperimentalOGL)
                {
                    GL.BindFramebuffer(FramebufferTarget.Framebuffer, _glFrameBufferId);
                    GL.Viewport(0, 0, _bitmap.Width, _bitmap.Height);
                    GL.ClearColor(0,0,0,0);
                    GL.Clear(ClearBufferMask.ColorBufferBit);

                    if(_glFragmentShaderProgramId > 0)
                    {
                        GL.UseProgram(_glFragmentShaderProgramId);
                        GL.Uniform1(GL.GetUniformLocation(_glFragmentShaderProgramId, "_AlphaMap"), 1);
                        GL.Uniform2(GL.GetUniformLocation(_glFragmentShaderProgramId, "_MousePosition"), _lastMousePosition.X, _lastMousePosition.Y);
                        GL.Uniform2(GL.GetUniformLocation(_glFragmentShaderProgramId, "_OutputSize"), (float)_windowWidth, (float)_windowHeight);
                    }

                    GL.Enable(EnableCap.Blend);
                    GL.BlendFunc(BlendingFactor.One, BlendingFactor.Zero);

                    DrawQuad();

                    if (_glAlphaMaskTextureId > 0 && _glFragmentShaderProgramId == 0)
                    {
                        GL.Enable(EnableCap.Blend);
                        GL.BlendFuncSeparate(BlendingFactorSrc.Zero, BlendingFactorDest.One, BlendingFactorSrc.One, BlendingFactorDest.Zero);

                        GL.BindTexture(TextureTarget.Texture2D, _glAlphaMaskTextureId);

                        DrawQuad();

                        //GL.BlendFunc(BlendingFactorSrc.One, BlendingFactorDest.Zero
                    }

                    GL.Disable(EnableCap.Blend);
                }

                _bitmap.UnlockBits(bmpData);
                //copyBitmap.UnlockBits(bmpData);

                GL.BindTexture(TextureTarget.Texture2D, 0);
            }
        }

        void DrawQuad()
        {
            GL.Begin(PrimitiveType.Quads);
            GL.TexCoord2(0, 1); GL.Vertex2(-1, 1); // Top Left
            GL.TexCoord2(1, 1); GL.Vertex2(1, 1); // Top Right
            GL.TexCoord2(1, 0); GL.Vertex2(1, -1); // Bottom Right
            GL.TexCoord2(0, 0); GL.Vertex2(-1, -1); // Bottom Left
            GL.End();
        }

        MouseButtonType GetMouseButtonType(uint button)
        {
            switch ((EVRMouseButton)button)
            {
                case EVRMouseButton.Left:
                    return MouseButtonType.Left;

                case EVRMouseButton.Right:
                    return MouseButtonType.Right;

                case EVRMouseButton.Middle:
                    return MouseButtonType.Middle;
            }
            return MouseButtonType.Left;
        }

        void HandleMouseMoveEvent(VREvent_t ev)
        {
            //_browser.GetBrowser().GetHost().SendMouseMoveEvent((int)(_windowWidth * ev.data.mouse.x), (int)(_windowHeight * (1f - ev.data.mouse.y)), false, CefEventFlags.None);
            if(_isHolding && !brokeFromJitterThreshold)
            {
                if ((mouseClickPosition - new OpenTK.Vector2(ev.data.mouse.x, ev.data.mouse.y)).Length >= MouseDeltaTolerance)
                    brokeFromJitterThreshold = true;
                else
                    return;
            }

            _browser.GetBrowser().GetHost().SendMouseMoveEvent((int)ev.data.mouse.x, _windowHeight - (int)ev.data.mouse.y, false, _isHolding ? CefEventFlags.LeftMouseButton : CefEventFlags.None);

            _lastMousePosition = new Vector2(ev.data.mouse.x, _windowHeight - ev.data.mouse.y);

            if (SteamVR_WebKit.TraceLevel)
            {
                //SteamVR_WebKit.Log("[WEBKIT] Mouse Move Event Fired at " + ev.data.mouse.x + "," + ev.data.mouse.y);
            }
        }

        void HandleMouseButtonDownEvent(VREvent_t ev)
        {
            if (ev.data.mouse.button != (uint)EVRMouseButton.Left)
                return;

            _browser.GetBrowser().GetHost().SendMouseClickEvent((int)ev.data.mouse.x, _windowHeight - (int)ev.data.mouse.y, GetMouseButtonType(ev.data.mouse.button), false, 1, CefEventFlags.None);

            if((EVRMouseButton)ev.data.mouse.button == EVRMouseButton.Left)
            {
                _isHolding = true;
                brokeFromJitterThreshold = false;
                mouseClickPosition = new OpenTK.Vector2(ev.data.mouse.x, ev.data.mouse.y);

                if (SteamVR_WebKit.TraceLevel)
                {
                    SteamVR_WebKit.Log("[WEBKIT] Mouse Down Event Fired at " + ev.data.mouse.x + "," + ev.data.mouse.y);
                }
            }
        }

        void HandleMouseButtonUpEvent(VREvent_t ev)
        {
            if (ev.data.mouse.button != (uint)EVRMouseButton.Left)
                return;

            int xToSend = (int)ev.data.mouse.x;
            int yToSend = (int)ev.data.mouse.y;

            if (_isHolding && !brokeFromJitterThreshold)
            {
                if ((mouseClickPosition - new OpenTK.Vector2(ev.data.mouse.x, ev.data.mouse.y)).Length >= MouseDeltaTolerance)
                {
                    brokeFromJitterThreshold = true;
                }
                else
                {
                    xToSend = (int)mouseClickPosition.X;
                    yToSend = (int)mouseClickPosition.Y;
                }
            }

            _browser.GetBrowser().GetHost().SendMouseClickEvent((int)xToSend, _windowHeight - (int)yToSend, GetMouseButtonType(ev.data.mouse.button), true, 1, CefEventFlags.None);

            if ((EVRMouseButton)ev.data.mouse.button == EVRMouseButton.Left)
            {
                _isHolding = false;
                brokeFromJitterThreshold = false;

                if (SteamVR_WebKit.TraceLevel)
                {
                    SteamVR_WebKit.Log("[WEBKIT] Mouse Up Event Fired at " + xToSend + "," + yToSend);
                }
            }
        }

        void HandleMouseLeaveEvent()
        {
            _browser.GetBrowser().GetHost().SendMouseMoveEvent(0, 0, true, CefEventFlags.None);
        }
        
        void HandleMouseScrollEvent(VREvent_t ev)
        {
            _browser.GetBrowser().GetHost().SendMouseWheelEvent((int)_lastMousePosition.X, (int)_lastMousePosition.Y, (int)(ev.data.scroll.xdelta * SCROLL_AMOUNT_PER_SWIPE), (int)(ev.data.scroll.ydelta * SCROLL_AMOUNT_PER_SWIPE), CefEventFlags.None);
        }

        bool CanDoUpdates()
        {
            if (Browser == null)
                return false;

            if (DashboardOverlay == null && InGameOverlay == null)
                return false; // We can go no further.

            //This prevents Draw() from failing on get of bitmap when attachment is delayed for controllers
            if (InGameOverlay != null && !InGameOverlay.AttachmentSuccess)
                return false;

            if (DashboardOverlay != null && DashboardOverlay.IsVisible())
                return true;

            if (InGameOverlay != null && InGameOverlay.IsVisible())
                return true;

            return false;
        }

        public virtual void Update()
        {
            if (!_isRendering)
                return;

            PreUpdateCallback?.Invoke(this, new EventArgs());

            // Mouse inputs are for dashboards only right now.

            if (InGameOverlay != null)
            {
                while (InGameOverlay.PollEvent(ref eventData))
                {
                    EVREventType type = (EVREventType)eventData.eventType;

                    HandleEvent(type, eventData);
                }
            }

            if (DashboardOverlay != null)
            {
                while (DashboardOverlay.PollEvent(ref eventData))
                {
                    EVREventType type = (EVREventType)eventData.eventType;

                    HandleEvent(type, eventData);
                }
            }

            if ((!EnableNonDashboardInput || InGameOverlay == null) && DashboardOverlay != null && !DashboardOverlay.IsVisible())
            {
                if (_wasVisible)
                {
                    _wasVisible = false;
                    HandleMouseLeaveEvent();
                }
                return;
            }

            _wasVisible = true;

            PostUpdateCallback?.Invoke(this, new EventArgs());
        }

        void HandleEvent(EVREventType type, VREvent_t eventData)
        {
            switch(type)
            {
                case EVREventType.VREvent_KeyboardCharInput:
                    KeyboardInput(new byte[] {
                        eventData.data.keyboard.cNewInput0,
                        eventData.data.keyboard.cNewInput1,
                        eventData.data.keyboard.cNewInput2,
                        eventData.data.keyboard.cNewInput3,
                        eventData.data.keyboard.cNewInput4,
                        eventData.data.keyboard.cNewInput5,
                        eventData.data.keyboard.cNewInput6,
                        eventData.data.keyboard.cNewInput7,
                    });
                    break;

                case EVREventType.VREvent_KeyboardDone:
                    //StringBuilder text = new StringBuilder();
                    //SteamVR_WebKit.OverlayManager.GetKeyboardText(text, 1024);
                    //KeyboardInput(text.ToString().ToCharArray());
                    break;

                case EVREventType.VREvent_MouseMove:
                    HandleMouseMoveEvent(eventData);
                    break;

                case EVREventType.VREvent_MouseButtonDown:
                    HandleMouseButtonDownEvent(eventData);
                    break;

                case EVREventType.VREvent_MouseButtonUp:
                    HandleMouseButtonUpEvent(eventData);
                    break;

                case EVREventType.VREvent_ScrollSmooth:
                    if (_allowScrolling)
                        HandleMouseScrollEvent(eventData);
                    break;
            }
        }


        public virtual void Draw()
        {
            if (!CanDoUpdates())
                return;

            PreDrawCallback?.Invoke(this, new EventArgs());
            
            var newbitmap =_browser.ScreenshotAsync().Result;
            if (newbitmap != null)
            {
                if (_bitmap != null)
                {
                    _bitmap.Dispose();
                }
                _bitmap = newbitmap;
                _browserDidUpdate = true;
            }
            UpdateTexture();

            if (_bitmap != null)
            {
                if (DashboardOverlay != null && DashboardOverlay.IsVisible())
                {
                    DashboardOverlay.SetTexture(ref _textureData);
                    DashboardOverlay.Show();
                }

                if (InGameOverlay != null && InGameOverlay.IsVisible())
                {
                    InGameOverlay.SetTexture(ref _textureData);
                    InGameOverlay.Show();
                }
            }

            PostDrawCallback?.Invoke(this, new EventArgs());
        }
        
        Queue<string> JSCommandQueue = new Queue<string>();

        private void ExecAsyncJS(string js)
        {
            Browser.GetBrowser().FocusedFrame.ExecuteJavaScriptAsync(js);
        }

        public void TryExecAsyncJS(string js)
        {
            if ((_inGameOverlay == null || _inGameOverlay.AttachmentSuccess) && Browser.IsBrowserInitialized)
                ExecAsyncJS(js);
            else
                JSCommandQueue.Enqueue(js);
        }

        public void ExecQueuedJS()
        {
            foreach (string jsCmd in JSCommandQueue.ToList())
            {
                ExecAsyncJS(jsCmd);
                JSCommandQueue.Dequeue();
            }
        }

        public class OverlayMessageHandler : IRenderProcessMessageHandler
        {
            WebKitOverlay _parent;
            public bool DebugMode = false;

            public OverlayMessageHandler(WebKitOverlay parent)
            {
                _parent = parent;
            }

            void IRenderProcessMessageHandler.OnContextCreated(IWebBrowser browserControl, IBrowser browser, IFrame frame)
            {
                _parent.ContextCreated?.Invoke(browserControl, browser, frame);
            }

            void IRenderProcessMessageHandler.OnFocusedNodeChanged(IWebBrowser browserControl, IBrowser browser, IFrame frame, IDomNode node)
            {
                if(DebugMode)
                    SteamVR_WebKit.Log("Node Focus Change: " + (node != null ? node.ToString() : " none"));

                _parent.FocusedNodeChanged?.Invoke(browserControl, browser, frame, node);
            }

            public void OnContextReleased(IWebBrowser browserControl, IBrowser browser, IFrame frame)
            {
            }

            public void OnUncaughtException(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, JavascriptException exception)
            {
                throw new NotImplementedException();
            }
        }
    }
}