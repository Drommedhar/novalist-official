using Android.Content;
using Android.Views;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace Novalist.Mobile.Pages;

public sealed class AndroidNavigationScrollView : ScrollView
{
    internal Action? DragStarted { get; set; }
    internal Action<double>? DragChanged { get; set; }
    internal Action<double, bool>? DragEnded { get; set; }
}

public sealed class AndroidNavigationScrollHandler : ScrollViewHandler
{
    protected override MauiScrollView CreatePlatformView() => new SidebarScrollView(Context)
    {
        ClipToOutline = true,
        FillViewport = true
    };

    protected override void ConnectHandler(MauiScrollView platformView)
    {
        base.ConnectHandler(platformView);
        var sidebar = (AndroidNavigationScrollView)VirtualView;
        var native = (SidebarScrollView)platformView;
        native.DragStarted = () => sidebar.DragStarted?.Invoke();
        native.DragChanged = translation => sidebar.DragChanged?.Invoke(translation);
        native.DragEnded = (velocity, cancelled) => sidebar.DragEnded?.Invoke(velocity, cancelled);
    }

    protected override void DisconnectHandler(MauiScrollView platformView)
    {
        var native = (SidebarScrollView)platformView;
        native.DragStarted = null;
        native.DragChanged = null;
        native.DragEnded = null;
        base.DisconnectHandler(platformView);
    }

    private sealed class SidebarScrollView : MauiScrollView
    {
        private readonly float _density;
        private readonly int _touchSlop;
        private readonly VelocityTracker _velocity;
        private float _startX;
        private float _startY;
        private bool _dragging;
        private bool _scrolling;

        public SidebarScrollView(Context context) : base(context)
        {
            _density = context.Resources?.DisplayMetrics?.Density
                ?? throw new InvalidOperationException("Android display density is unavailable.");
            _touchSlop = ViewConfiguration.Get(context)?.ScaledTouchSlop
                ?? throw new InvalidOperationException("Android touch configuration is unavailable.");
            _velocity = VelocityTracker.Obtain()
                ?? throw new InvalidOperationException("Android velocity tracking is unavailable.");
        }

        public Action? DragStarted { get; set; }
        public Action<double>? DragChanged { get; set; }
        public Action<double, bool>? DragEnded { get; set; }

        public override bool DispatchTouchEvent(MotionEvent? motion)
        {
            if (motion == null) return base.DispatchTouchEvent(motion);
            if (motion.ActionMasked == MotionEventActions.Down)
            {
                _startX = motion.RawX;
                _startY = motion.RawY;
                _dragging = false;
                _scrolling = false;
                _velocity.Clear();
            }
            _velocity.AddMovement(motion);
            if (motion.ActionMasked == MotionEventActions.Move) UpdateDrag(motion);
            if (!_dragging) return base.DispatchTouchEvent(motion);

            if (motion.ActionMasked == MotionEventActions.Up || motion.ActionMasked == MotionEventActions.Cancel
                || motion.ActionMasked == MotionEventActions.PointerDown)
            {
                _velocity.ComputeCurrentVelocity(1000);
                DragEnded?.Invoke(_velocity.XVelocity / _density, motion.ActionMasked != MotionEventActions.Up);
                _dragging = false;
                _scrolling = true;
                Parent?.RequestDisallowInterceptTouchEvent(false);
            }
            return true;
        }

        private void UpdateDrag(MotionEvent motion)
        {
            var horizontal = motion.RawX - _startX;
            var vertical = motion.RawY - _startY;
            if (!_dragging && !_scrolling && Math.Max(Math.Abs(horizontal), Math.Abs(vertical)) > _touchSlop)
            {
                if (Math.Abs(vertical) >= Math.Abs(horizontal)) { _scrolling = true; return; }
                _dragging = true;
                // Cancel the button's pending click before owning the horizontal drag.
                using var cancel = MotionEvent.Obtain(motion)
                    ?? throw new InvalidOperationException("Android could not cancel the navigation touch.");
                cancel.Action = MotionEventActions.Cancel;
                base.DispatchTouchEvent(cancel);
                Parent?.RequestDisallowInterceptTouchEvent(true);
                DragStarted?.Invoke();
            }
            if (_dragging) DragChanged?.Invoke(horizontal / _density);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _velocity.Recycle();
                _velocity.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
