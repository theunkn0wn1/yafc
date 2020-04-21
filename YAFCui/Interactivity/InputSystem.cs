using System.Numerics;
using SDL2;

namespace YAFC.UI
{
    public readonly struct HitTestResult<T>
    {
        public readonly T target;
        public readonly UiBatch batch;
        public readonly Rect rect;

        public HitTestResult(T target, UiBatch batch, Rect rect)
        {
            this.target = target;
            this.batch = batch;
            this.rect = rect;
        }
    }
    public sealed class InputSystem
    {
        public static readonly InputSystem Instance = new InputSystem();
        
        private InputSystem() {}

        private Window mouseOverWindow;
        private HitTestResult<IMouseHandle> hoveringObject;
        private HitTestResult<IMouseHandle> mouseDownObject;
        private IMouseFocus activeMouseFocus;
        private IKeyboardFocus activeKeyboardFocus;
        private IKeyboardFocus defaultKeyboardFocus;
        private int mouseDownButton = -1;
        private Vector2 position;

        private IKeyboardFocus currentKeyboardFocus => activeKeyboardFocus ?? defaultKeyboardFocus;

        public Vector2 mousePosition => position;

        public void SetKeyboardFocus(IKeyboardFocus focus)
        {
            if (focus == activeKeyboardFocus)
                return;
            currentKeyboardFocus?.FocusChanged(false);
            activeKeyboardFocus = focus;
            currentKeyboardFocus?.FocusChanged(true);
        }
        
        public void SetMouseFocus(IMouseFocus mouseFocus)
        {
            if (mouseFocus == activeMouseFocus)
                return;
            activeMouseFocus?.FocusChanged(false);
            activeMouseFocus = mouseFocus;
            activeMouseFocus?.FocusChanged(true);
        }

        public void SetDefaultKeyboardFocus(IKeyboardFocus focus)
        {
            defaultKeyboardFocus = focus;
        }

        internal void KeyDown(SDL.SDL_Keysym key)
        {
            (activeKeyboardFocus ?? defaultKeyboardFocus)?.KeyDown(key);
        }

        internal void KeyUp(SDL.SDL_Keysym key)
        {
            (activeKeyboardFocus ?? defaultKeyboardFocus)?.KeyUp(key);
        }

        internal void TextInput(string input)
        {
            (activeKeyboardFocus ?? defaultKeyboardFocus)?.TextInput(input);
        }

        internal void MouseScroll(int delta)
        {
            if (HitTest<IMouseScrollHandle>(out var result))
                result.target.Scroll(delta, result.batch);
        }

        internal void MouseMove(int rawX, int rawY)
        {
            if (mouseOverWindow == null)
                return;
            position = new Vector2(rawX / mouseOverWindow.rootBatch.pixelsPerUnit, rawY / mouseOverWindow.rootBatch.pixelsPerUnit);
            if (mouseDownButton != -1 && mouseDownObject.target is IMouseDragHandle drag)
                drag.Drag(position, mouseDownButton, mouseDownObject.batch);
            else if (hoveringObject.target is IMouseMoveHandle move)
                move.MouseMove(position, hoveringObject.batch);
        }
        
        internal void MouseExitWindow(Window window)
        {
            if (mouseOverWindow == window)
                mouseOverWindow = null;
        }

        internal void MouseEnterWindow(Window window)
        {
            mouseOverWindow = window;
        }

        public bool HitTest<T>(out HitTestResult<T> result) where T : class, IMouseHandleBase
        {
            if (mouseOverWindow != null)
                return mouseOverWindow.HitTest(position, out result);
            result = default;
            return false;
        }

        internal void Update()
        {
            HitTest<IMouseHandle>(out var currentHovering);
            if (currentHovering.target != hoveringObject.target)
            {
                hoveringObject.target?.MouseExit(hoveringObject.batch);
                hoveringObject = currentHovering;
                hoveringObject.target?.MouseEnter(hoveringObject);
            }
            activeKeyboardFocus?.UpdateSelected();
        }

        internal void MouseDown(int button)
        {
            if (mouseDownButton != -1)
                return;
            if (button == SDL.SDL_BUTTON_LEFT)
            {
                if (activeKeyboardFocus != null)
                    SetKeyboardFocus(null);
                if (activeMouseFocus != null && (!HitTest<IMouseFocus>(out var result) || result.target != activeMouseFocus && !result.batch.HasParent(activeMouseFocus)))
                    SetMouseFocus(null);
            }
            mouseDownObject = hoveringObject;
            mouseDownButton = button;
            if (mouseDownObject.target is IMouseDragHandle drag)
                drag.BeginDrag(position, button, mouseDownObject.batch);
        }

        internal void MouseUp(int button)
        {
            if (button != mouseDownButton)
                return;
            if (mouseDownObject.target != null)
            {
                if (mouseDownObject.target is IMouseDragHandle drag)
                    drag.EndDrag(position, button, mouseDownObject.batch);
                if (mouseDownObject.target == hoveringObject.target)
                    mouseDownObject.target.MouseClick(mouseDownButton, mouseDownObject.batch);
            }

            mouseDownButton = -1;
            mouseDownObject = default;
        }
    }
}