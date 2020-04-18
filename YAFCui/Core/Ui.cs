using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using SDL2;

namespace YAFC.UI
{
    public static class Ui
    {
        public static bool quit { get; private set; }

        private static Dictionary<uint, Window> windows = new Dictionary<uint, Window>();
        internal static void RegisterWindow(uint id, Window window)
        {
            windows[id] = window;
        }

        [DllImport("SHCore.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwareness(int awareness);
        public static void Start()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                SetProcessDpiAwareness(2);
            
            SDL.SDL_Init(SDL.SDL_INIT_VIDEO);
            SDL.SDL_SetHint(SDL.SDL_HINT_RENDER_SCALE_QUALITY, "linear");
            SDL_ttf.TTF_Init();
            SDL_image.IMG_Init(SDL_image.IMG_InitFlags.IMG_INIT_PNG | SDL_image.IMG_InitFlags.IMG_INIT_JPG);
            asyncCallbacksAdded = SDL.SDL_RegisterEvents(1);
            SynchronizationContext.SetSynchronizationContext(new UiSyncronizationContext());
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }
        
        public static long time { get; private set; }
        private static readonly Stopwatch timeWatch = Stopwatch.StartNew();

        public static bool IsMainThread() => Thread.CurrentThread.ManagedThreadId == mainThreadId;

        private static int mainThreadId;
        private static uint asyncCallbacksAdded;
        private static Queue<(SendOrPostCallback, object)> callbacksQueued = new Queue<(SendOrPostCallback, object)>();

        public static void MainLoop()
        {
            while (!quit)
            {
                ProcessEvents();
                Render();
            }
        }

        public static void ProcessEvents()
        {
            try
            {
                var inputSystem = InputSystem.Instance;
                var minNextEvent = long.MaxValue - 1;
                foreach (var (_, window) in windows)
                    minNextEvent = Math.Min(minNextEvent, window.nextRepaintTime);
                var delta = Math.Min(1 + (minNextEvent - timeWatch.ElapsedMilliseconds), int.MaxValue);
                var hasEvents = SDL.SDL_WaitEventTimeout(out var evt, (int) delta) != 0;
                time = timeWatch.ElapsedMilliseconds;
                if (!hasEvents && time < minNextEvent)
                    time = minNextEvent;
                while (hasEvents)
                {
                    switch (evt.type)
                    {
                        case SDL.SDL_EventType.SDL_QUIT:
                            quit = true;
                            break;
                        case SDL.SDL_EventType.SDL_MOUSEBUTTONUP:
                            inputSystem.MouseUp(evt.button.button);
                            break;
                        case SDL.SDL_EventType.SDL_MOUSEBUTTONDOWN:
                            inputSystem.MouseDown(evt.button.button);
                            break;
                        case SDL.SDL_EventType.SDL_MOUSEWHEEL:
                            var y = -evt.wheel.y;
                            if (evt.wheel.direction == (uint) SDL.SDL_MouseWheelDirection.SDL_MOUSEWHEEL_FLIPPED)
                                y = -y;
                            inputSystem.MouseScroll(y);
                            break;
                        case SDL.SDL_EventType.SDL_MOUSEMOTION:
                            inputSystem.MouseMove(evt.motion.x, evt.motion.y);
                            break;
                        case SDL.SDL_EventType.SDL_KEYDOWN:
                            inputSystem.KeyDown(evt.key.keysym);
                            break;
                        case SDL.SDL_EventType.SDL_KEYUP:
                            inputSystem.KeyUp(evt.key.keysym);
                            break;
                        case SDL.SDL_EventType.SDL_TEXTINPUT:
                            unsafe
                            {
                                var term = 0;
                                while (evt.text.text[term] != 0)
                                    ++term;
                                var inputString = new string((sbyte*) evt.text.text, 0, term, Encoding.UTF8);
                                inputSystem.TextInput(inputString);
                            }

                            break;
                        case SDL.SDL_EventType.SDL_WINDOWEVENT:
                            var window = windows[evt.window.windowID];
                            switch (evt.window.windowEvent)
                            {
                                case SDL.SDL_WindowEventID.SDL_WINDOWEVENT_ENTER:
                                    inputSystem.MouseEnterWindow(window);
                                    break;
                                case SDL.SDL_WindowEventID.SDL_WINDOWEVENT_LEAVE:
                                    inputSystem.MouseExitWindow(window);
                                    break;
                                case SDL.SDL_WindowEventID.SDL_WINDOWEVENT_CLOSE:
                                    window.Close();
                                    break;
                                case SDL.SDL_WindowEventID.SDL_WINDOWEVENT_FOCUS_LOST:
                                    inputSystem.SetKeyboardFocus(null);
                                    window.FocusLost();
                                    break;
                                case SDL.SDL_WindowEventID.SDL_WINDOWEVENT_MOVED:
                                    window.WindowMoved();
                                    break;
                                case SDL.SDL_WindowEventID.SDL_WINDOWEVENT_RESIZED:
                                    window.WindowResize();
                                    break;
                                default:
                                    Console.WriteLine("Window event of type " + evt.window.windowEvent);
                                    break;
                            }

                            break;
                        case SDL.SDL_EventType.SDL_RENDER_TARGETS_RESET: 
                            break;
                        case SDL.SDL_EventType.SDL_USEREVENT:
                            if (evt.user.type == asyncCallbacksAdded)
                                ProcessAsyncCallbackQueue(); 
                            break;
                        default:
                            Console.WriteLine("Event of type " + evt.type);
                            break;
                    }

                    hasEvents = SDL.SDL_PollEvent(out evt) != 0;
                }
                inputSystem.Update();
            }
            catch (Exception ex)
            {
                ExceptionScreen.ShowException(ex);
            }
        }

        public static void Render()
        {
            foreach (var (_, window) in windows)
            {
                try
                {
                    window.Render();
                }
                catch (Exception ex)
                {
                    ExceptionScreen.ShowException(ex);
                }
            }
        }

        public static EnterThreadPoolAwaitable ExitMainThread() => default;
        public static EnterMainThreadAwaitable EnterMainThread() => default;
        
        
        public static void Quit()
        {
            quit = true;
            SDL_ttf.TTF_Quit();
            SDL_image.IMG_Quit();
            SDL.SDL_Quit();
        }

        private static void ProcessAsyncCallbackQueue()
        {
            var hasCustomCallbacks = true;
            while (hasCustomCallbacks)
            {
                (SendOrPostCallback, object) next;
                lock (callbacksQueued)
                {
                    if (callbacksQueued.Count == 0)
                        break;
                    next = callbacksQueued.Dequeue();
                    hasCustomCallbacks = callbacksQueued.Count > 0;
                }

                try
                {
                    next.Item1(next.Item2);
                }
                catch (Exception ex)
                {
                    ExceptionScreen.ShowException(ex);
                }
            }
        }
        
        public static void ExecuteInMainThread(SendOrPostCallback callback, object data)
        {
            var shouldSendEvent = false;
            lock (callbacksQueued)
            {
                if (callbacksQueued.Count == 0)
                    shouldSendEvent = true;
                callbacksQueued.Enqueue((callback, data));
            }

            if (shouldSendEvent)
            {
                var evt = new SDL.SDL_Event
                {
                    type = SDL.SDL_EventType.SDL_USEREVENT,
                    user = new SDL.SDL_UserEvent
                    {
                        type = asyncCallbacksAdded
                    }
                };
                SDL.SDL_PushEvent(ref evt);
            }
        }
    }
}