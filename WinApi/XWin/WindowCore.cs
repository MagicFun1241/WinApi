using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using WinApi.User32;

namespace WinApi.XWin
{
    public class WindowCore : NativeWindow, INativeConnectable, IDisposable
    {
        private IntPtr m_baseWindowProcPtr;
        private WindowProc m_instanceWindowProc;
        public WindowFactory Factory { get; private set; }
        public bool IsSourceOwner { get; protected set; }
        public bool IsDisposed { get; protected set; }

        public void Dispose()
        {
            Dispose(true);
        }

        void INativeAttachable.Attach(IntPtr handle)
        {
            ((INativeConnectable) this).Attach(handle, false);
        }

        void INativeConnectable.Attach(IntPtr handle, bool takeOwnership)
        {
            ThrowIfDisposed();
            Handle = handle;
            IsSourceOwner = takeOwnership;
        }

        IntPtr INativeAttachable.Detach()
        {
            ((INativeConnectable) this).DisconnectWindowProc();
            var h = Handle;
            Handle = IntPtr.Zero;
            IsSourceOwner = false;
            return h;
        }

        void INativeConnectable.ConnectWindowProc(IntPtr baseWindowProcPtr)
        {
            m_baseWindowProcPtr = baseWindowProcPtr == IntPtr.Zero
                ? GetItem(WindowLongFlags.GWLP_WNDPROC)
                : baseWindowProcPtr;
            m_instanceWindowProc = WindowProc;
            SetItem(WindowLongFlags.GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(m_instanceWindowProc));
            OnSourceConnected();
        }

        void INativeConnectable.ConnectWindowProc()
        {
            ((INativeConnectable) this).ConnectWindowProc(IntPtr.Zero);
        }

        void INativeConnectable.DisconnectWindowProc()
        {
            if (m_baseWindowProcPtr != IntPtr.Zero)
            {
                SetItem(WindowLongFlags.GWLP_WNDPROC, m_baseWindowProcPtr);
                m_baseWindowProcPtr = IntPtr.Zero;
            }
        }

        void INativeConnectable.SetFactory(WindowFactory factory)
        {
            Factory = factory;
        }

        protected void ThrowIfDisposed()
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(WindowCore));
        }

        ~WindowCore()
        {
            Dispose(false);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (IsDisposed) return;
            if (IsSourceOwner)
            {
                User32Methods.DestroyWindow(Handle);
            }
            else
            {
                ((INativeConnectable) this).Detach();
            }
            IsDisposed = true;
            GC.SuppressFinalize(this);
        }

        protected virtual void OnSourceConnected() {}

        // The WindowInstanceInitializerProc is only executed once, where it initializes the instance
        // with the appropriate values, and WndProc chain, after which the WindowProc method is 
        // directly called by Windows.
        internal IntPtr WindowInstanceInitializerProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            Debug.WriteLine("[WindowInstanceInitializerProc]: " + hwnd);
            var windowConnector = (INativeConnectable) this;
            windowConnector.Attach(hwnd, true);
            windowConnector.ConnectWindowProc(wParam);
            return WindowProc(hwnd, msg, IntPtr.Zero, lParam);
        }


        protected virtual void OnMessage(ref WindowMessage msg) {}

        protected virtual IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            var wmsg = new WindowMessage
            {
                Id = (WM) msg,
                WParam = wParam,
                LParam = lParam,
                Result = IntPtr.Zero,
                Handled = false
            };
            try
            {
                OnMessage(ref wmsg);
                return wmsg.Handled ? wmsg.Result : WindowBaseProc(hwnd, msg, wParam, lParam);
            }
            catch (Exception ex)
            {
                if (!HandleException(ex, this)) throw;
                return IntPtr.Zero;
            }
        }

        public event WindowExceptionHandler Exception;
        public static event WindowExceptionHandler UnhandledException;

        public static bool HandleException(Exception ex, WindowCore window)
        {
            var windowException = new WindowException(ex, window);
            window?.Exception?.Invoke(ref windowException);
            if (!windowException.IsHandled) UnhandledException?.Invoke(ref windowException);
            return windowException.IsHandled;
        }

        protected virtual IntPtr WindowBaseProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            return User32Methods.CallWindowProc(m_baseWindowProcPtr, hwnd, msg, wParam, lParam);
        }
    }

    public interface INativeConnectable : INativeAttachable
    {
        void Attach(IntPtr handle, bool takeOwnership);
        void ConnectWindowProc();
        void ConnectWindowProc(IntPtr baseWindowProcPtr);
        void DisconnectWindowProc();
        void SetFactory(WindowFactory factory);
    }

    public struct WindowMessage
    {
        public WM Id;
        public IntPtr WParam;
        public IntPtr LParam;
        public IntPtr Result;
        public bool Handled;

        public void SetHandled(bool handled = true)
        {
            Handled = handled;
        }

        public void SetHandledWithResult(IntPtr result)
        {
            Result = result;
            Handled = true;
        }

        public void SetHandledWithResult(int result)
        {
            SetHandledWithResult(new IntPtr(result));
        }
    }

    public delegate void WindowExceptionHandler(ref WindowException windowException);

    public struct WindowException
    {
        public bool IsHandled { get; set; }
        public WindowCore Window { get; set; }
        public Exception DispatchedException { get; set; }

        public WindowException(Exception ex) : this(ex, null) {}

        public WindowException(Exception ex, WindowCore window)
        {
            DispatchedException = ex;
            Window = window;
            IsHandled = false;
        }

        public void SetHandled(bool value = true)
        {
            IsHandled = value;
        }

        public override string ToString()
        {
            return DispatchedException.ToString();
        }
    }
}