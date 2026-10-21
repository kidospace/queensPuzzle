using Common;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace qp {

    /// <summary>What the board hears from <see cref="BoardTouchInput"/>. Only things that happen —
    /// nothing is called every frame. The touch carries the screen point and its world point.</summary>
    public interface IBoardTouchListener {
        /// <summary>A finger pressed. <paramref name="doubleTap"/>: it came within the double-tap
        /// window of the last tap's release (real time) — the listener decides whether it really is
        /// one (the same cell) and acts on it NOW, on the press.</summary>
        void TouchDown(MBTouches.TouchData touch, bool doubleTap);
        /// <summary>The pressed finger moved past the tap distance (or held past the tap time and moved) — a drag.</summary>
        void TouchDrag(MBTouches.TouchData touch);
        /// <summary>The finger lifted, or the touch was cancelled.</summary>
        void TouchUp(MBTouches.TouchData touch);
    }

    /// <summary>
    /// The board's touch source: the Input System (EnhancedTouch), turned into the calls of
    /// <see cref="IBoardTouchListener"/>. The game's rules stay with the listener (MBGameplay).
    ///
    /// Every finger event carries the time it REALLY happened (the OS stamp, not the frame), so a
    /// slow frame can't turn a double-tap into two taps, and a tap that starts and ends inside one
    /// frame is not lost. A double-tap is decided — and delivered — on the second PRESS (Android's
    /// rule): how long that press is held changes nothing, and nothing lands on a late release.
    /// Events arrive in order at the start of the frame (after a long stall they all arrive at
    /// once — see ShouldUse). In the editor the mouse is a finger too (TouchSimulation).
    /// OnFinger is the one door.
    /// </summary>
    public sealed class BoardTouchInput {

        /// <summary>A double-tap: the second press comes within this long after the first
        /// release, in real time.</summary>
        public const float DoubleTapDelay = 0.3f;

        const float TapMaxSeconds = 1f;   // a press held longer than this turns into a drag on any move
        // No distance rule for the second tap: the listener requires the SAME cell, which is the
        // real test — a distance cap only wrongly refused double-taps on big cells (a tablet).

        readonly IBoardTouchListener _listener;
        readonly float _tapRadius;        // px: moving further makes a press a drag (~0.25 cm)
        int _finger = -1;                 // the finger that started the gesture; others are ignored
        bool _dragging;
        double _downTime, _lastUpTime = double.NegativeInfinity;
        Vector2 _downPos;
        MBTouches.TouchData _touch;       // reused: screen point → world point

        /// <summary>Start feeding <paramref name="listener"/>. Call <see cref="Detach"/> when done.</summary>
        public static BoardTouchInput Attach(IBoardTouchListener listener) => new BoardTouchInput(listener);

        BoardTouchInput(IBoardTouchListener listener) {
            _listener = listener;
            _tapRadius = 0.0984252f * (Screen.dpi > 0 ? Screen.dpi : 160f);   // 0.25 cm in pixels
            EnhancedTouchSupport.Enable();
#if UNITY_EDITOR || UNITY_STANDALONE
            TouchSimulation.Enable();
#endif
            ETouch.onFingerDown += OnFinger;
            ETouch.onFingerMove += OnFinger;
            ETouch.onFingerUp += OnFinger;
        }

        public void Detach() {
            ETouch.onFingerDown -= OnFinger;
            ETouch.onFingerMove -= OnFinger;
            ETouch.onFingerUp -= OnFinger;
            _finger = -1;
        }

        // Whether to act on this finger event at all. Every event, for now; a rule about events
        // that come in late after a long stall would go here.
        static bool ShouldUse(ETouch t) => true;

        // Release → next press under the delay, in real time.
        static bool IsDoubleTap(double lastUpTime, double downTime) => downTime - lastUpTime < DoubleTapDelay;

        void OnFinger(Finger f) {
            var t = f.currentTouch.valid ? f.currentTouch : f.lastTouch;
            if (!t.valid || !ShouldUse(t)) return;
            var phase = t.phase;
            Vector2 pos = t.screenPosition;

            if (phase == TouchPhase.Began) {
                if (_finger >= 0) return;   // a second finger must not hijack a paint drag
                _finger = f.index;
                _dragging = false;
                _downTime = t.time;
                _downPos = pos;
                _touch.ScreenPoint = pos;
                _listener.TouchDown(_touch, IsDoubleTap(_lastUpTime, _downTime));
                return;
            }
            if (f.index != _finger) return;

            if (phase == TouchPhase.Moved) {
                if (!_dragging) {
                    // still a tap while it stays close and short
                    if (t.time - _downTime < TapMaxSeconds && Vector2.Distance(_downPos, pos) < _tapRadius) return;
                    _dragging = true;
                }
                _touch.ScreenPoint = pos;
                _listener.TouchDrag(_touch);
                return;
            }
            if (phase == TouchPhase.Ended || phase == TouchPhase.Canceled) {
                _finger = -1;
                // the tap history: a tap's release may start a double-tap; a drag or a cancel breaks
                // it (the next press must not pair with one from before it)
                _lastUpTime = _dragging || phase == TouchPhase.Canceled ? double.NegativeInfinity : t.time;
                _listener.TouchUp(_touch);
            }
        }
    }
}
