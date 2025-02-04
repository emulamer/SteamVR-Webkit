﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SteamVR_WebKit;
using System.Drawing;
using OpenTK.Graphics.OpenGL;
using OpenTK.Graphics;
using OpenTK;
using CefSharp.OffScreen;

namespace SteamVR_WebKit_Test
{
    class Program
    {
        // Tests basic overlay
        static WebKitOverlay basicOverlay;

        // Tests video overlay in-game
        static WebKitOverlay videoOverlay;

        // Tests OpenVR.Applications proxy and using Angular.
        static WebKitOverlay applicationsOverlay;

        // Tests video overlay in-game attached to the left controller
        static WebKitOverlay controllerOverlay;

        static void Main(string[] args)
        {
            SteamVR_WebKit.SteamVR_WebKit.UseExperimentalOGL = true;
            SteamVR_WebKit.SteamVR_WebKit.DefaultFragmentShaderPath = Environment.CurrentDirectory + "\\Resources\\fragShader.frag";
            CefSharp.CefSharpSettings.FocusedNodeChangedEnabled = true;
            SteamVR_WebKit.SteamVR_WebKit.Init(new CefSettings() { WindowlessRenderingEnabled = true,  });
            SteamVR_WebKit.SteamVR_WebKit.FPS = 30;
            SteamVR_WebKit.SteamVR_WebKit.LogEvent += SteamVR_WebKit_LogEvent;

            //Notifications.RegisterIcon("default", new Bitmap(Environment.CurrentDirectory + "\\Resources\\alert.png"));
            basicOverlay = new WebKitOverlay(new Uri("https://whyyouare.web.app"), 1280, 800, "webkitTest", "WebKit", OverlayType.Dashboard);
            basicOverlay.DashboardOverlay.Width = 3.0f;
            basicOverlay.DashboardOverlay.SetThumbnail("Resources/webkit-logo.png");
            basicOverlay.BrowserPreInit += Overlay_BrowserPreInit;
            basicOverlay.BrowserReady += Overlay_BrowserReady;
            basicOverlay.StartBrowser();
            basicOverlay.EnableKeyboard = true;
            basicOverlay.MessageHandler.DebugMode = true;
            basicOverlay.MouseDeltaTolerance = 20;

            //basicOverlay.AlphaMask = new Bitmap(Environment.CurrentDirectory + "\\Resources\\alphamap.png");

            SteamVR_WebKit.SteamVR_WebKit.TraceLevel = true;

            videoOverlay = new WebKitOverlay(new Uri("https://www.youtube.com/embed/d7Co9PyueSk"), 1920, 1080, "videoTest", "Video", OverlayType.Both);
            videoOverlay.InGameOverlay.SetAttachment(AttachmentType.Overlay, new Vector3(2.5f, 0.6f, 1.5f), new Vector3(0, 45f, -35f), "system.vrdashboard");
            videoOverlay.EnableNonDashboardInput = true;
            videoOverlay.InGameOverlay.Width = 3f;
            videoOverlay.BrowserPreInit += VideoOverlay_BrowserPreInit;
            videoOverlay.BrowserReady += VideoOverlay_BrowserReady;
            videoOverlay.DashboardOverlay.Width = 3.0f;
            videoOverlay.StartBrowser();
            //videoOverlay.AlphaMask = new Bitmap(Environment.CurrentDirectory + "\\Resources\\alphamap.png");
            videoOverlay.UpdateEveryFrame = true;

            /*applicationsOverlay = new WebKitOverlay(new Uri("file://" + Environment.CurrentDirectory + "/Resources/applications.html"), 1024, 1024, "webkitTestApps", "WebKit-Apps", OverlayType.Dashboard);
            applicationsOverlay.DashboardOverlay.Width = 2.0f;
            applicationsOverlay.DashboardOverlay.SetThumbnail("Resources/webkit-logo.png");
            applicationsOverlay.BrowserPreInit += ApplicationsOverlay_BrowserPreInit;
            applicationsOverlay.BrowserReady += ApplicationsOverlay_BrowserReady;
            applicationsOverlay.StartBrowser();*/

            controllerOverlay = new WebKitOverlay(new Uri("https://www.youtube.com/embed/XOn5ckvIF3U?autoplay=1&start=27"), 550, 250, "controllerTest", "controllerVideo", OverlayType.InGame);
            controllerOverlay.InGameOverlay.SetDeviceAttachment(AttachmentType.RightController, new Vector3(0.0f, 0.0f, -0.1f), new Vector3(0.0f, 0.0f, 1.35f));
            controllerOverlay.InGameOverlay.Width = .25f;
            controllerOverlay.BrowserPreInit += ControllerOverlay_BrowserPreInit;
            controllerOverlay.BrowserReady += ControllerOverlay_BrowserReady;
            controllerOverlay.StartBrowser(true);

            //SteamVR_Application application = new SteamVR_Application();
            //application.InstallManifest(true);
            //application.SetAutoStartEnabled(false);
            //application.RemoveManifest();

            SteamVR_WebKit.SteamVR_WebKit.RunOverlays(); // Runs update/draw calls for all active overlays. And yes, it's blocking.
        }

        private static void SteamVR_WebKit_LogEvent(string line)
        {
            Console.WriteLine(line);
        }

        private static void ApplicationsOverlay_BrowserPreInit(object sender, EventArgs e)
        {
            applicationsOverlay.Browser.JavascriptObjectRepository.Register("applications", new SteamVR_WebKit.JsInterop.Applications());
        }

        private static void ApplicationsOverlay_BrowserReady(object sender, EventArgs e)
        {
            applicationsOverlay.Browser.GetBrowser().GetHost().ShowDevTools();
        }

        private static void VideoOverlay_BrowserReady(object sender, EventArgs e)
        {
            videoOverlay.Browser.GetBrowser().GetHost().ShowDevTools();
        }

        private static void VideoOverlay_BrowserPreInit(object sender, EventArgs e)
        {
            videoOverlay.Browser.JavascriptObjectRepository.Register("overlay", videoOverlay);
        }

        private static void ControllerOverlay_BrowserReady(object sender, EventArgs e)
        {
            //controllerOverlay.Browser.GetBrowser().GetHost().ShowDevTools();
        }

        private static void ControllerOverlay_BrowserPreInit(object sender, EventArgs e)
        {
            controllerOverlay.Browser.JavascriptObjectRepository.Register("overlay", controllerOverlay);
        }

        private static void Overlay_BrowserReady(object sender, EventArgs e)
        {
            basicOverlay.Browser.GetBrowser().GetHost().ShowDevTools();
        }

        //private static void Browser_ConsoleMessage(object sender, CefSharp.ConsoleMessageEventArgs e)
        //{
        //    string[] srcSplit = e.Source.Split('/'); // We only want the filename
        //    SteamVR_WebKit.SteamVR_WebKit.Log("[CONSOLE " + srcSplit[srcSplit.Length - 1] + ":" + e.Line + "] " + e.Message);
        //}

        private static void Overlay_BrowserPreInit(object sender, EventArgs e)
        {
            SteamVR_WebKit.SteamVR_WebKit.Log("Browser is ready.");

          //  basicOverlay.Browser.ConsoleMessage += Browser_ConsoleMessage;
            basicOverlay.Browser.JavascriptObjectRepository.Register("testObject", new JsCallbackTest());
            basicOverlay.Browser.JavascriptObjectRepository.Register("notifications", new SteamVR_WebKit.JsInterop.Notifications(basicOverlay.DashboardOverlay));
        }
    }
}