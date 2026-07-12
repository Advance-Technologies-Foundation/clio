using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using ClioRing.Diagnostics;

namespace ClioRing.Interop;

/// <summary>
/// Registers a configurable Windows global hotkey against an existing HWND and hooks that window's
/// WndProc to observe WM_HOTKEY. The message hook uses an
/// <see cref="UnmanagedCallersOnlyAttribute"/> static thunk so it is fully NativeAOT compatible
/// (no runtime delegate marshalling). Registration failure is reported (not thrown) so the caller
/// can surface a loud, non-fatal notice.
/// </summary>
public sealed unsafe class WindowsHotkey : IDisposable {
	private const int GwlpWndProc = -4;
	private const uint WmHotkey = 0x0312;

	private const int HotkeyId = 0x4C43; // 'LC'

	private static readonly ConcurrentDictionary<IntPtr, WindowsHotkey> Instances = new();
	private static readonly IntPtr ThunkPtr =
		(IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, IntPtr>)&WndProcThunk;

	private IntPtr _hwnd;
	private IntPtr _originalWndProc;
	private Action? _onHotkey;
	private bool _hooked;
	private bool _registered;
	private bool _disposed;

	/// <summary>The modifiers+key description for display/logging (set on install).</summary>
	public string Gesture { get; private set; } = string.Empty;

	/// <summary>Win32 error code from the last failed registration (0 when none).</summary>
	public int LastError { get; private set; }

	/// <summary>Global count of actual WndProc subclass installs (diagnostic; must stay 1 per HWND lifetime).</summary>
	public static int InstallCount => _installCount;

	private static int _installCount;

	/// <summary>
	/// Installs the WndProc hook and registers <paramref name="gesture"/> on <paramref name="hwnd"/>.
	/// Returns true on success; on failure returns false and sets <see cref="LastError"/> (never throws
	/// for an in-use gesture — an optional feature must not crash the app).
	/// </summary>
	/// <param name="hwnd">Top-level window handle to hook.</param>
	/// <param name="onHotkey">Callback invoked (on the UI/message thread) when the hotkey fires.</param>
	/// <param name="gesture">The parsed gesture to register.</param>
	public bool Install(IntPtr hwnd, Action onHotkey, HotkeyGesture gesture) {
		if (hwnd == IntPtr.Zero) {
			throw new ArgumentException("Window handle is null.", nameof(hwnd));
		}

		_onHotkey = onHotkey;
		Gesture = gesture.Display;

		// IDEMPOTENT: hook a given HWND exactly once. A repeat call (e.g. OnOpened firing again on
		// a hide->show cycle) must NOT re-subclass — otherwise SetWindowLongPtr would return OUR
		// OWN ThunkPtr as the "previous" proc and the thunk would call itself forever (StackOverflow).
		if (_hooked && _hwnd == hwnd) {
			return _registered;
		}

		_hwnd = hwnd;
		Instances[hwnd] = this;

		if (!_hooked) {
			IntPtr previous = SetWindowLongPtr(hwnd, GwlpWndProc, ThunkPtr);

			// DEFENSIVE: never store our own thunk as the chain target. If we somehow got it back,
			// reject it and fall back to DefWindowProc (chain = Zero) so the thunk can never recurse.
			if (previous == ThunkPtr) {
				StartupLog.Log("hotkey hook: SetWindowLongPtr returned ThunkPtr (already hooked) — not storing");
				_originalWndProc = IntPtr.Zero;
			}
			else {
				_originalWndProc = previous; // preserved from the FIRST successful install only
			}

			_hooked = true;
			Interlocked.Increment(ref _installCount);
		}

		if (!_registered) {
			if (!RegisterHotKey(hwnd, HotkeyId, gesture.Modifiers, gesture.VirtualKey)) {
				LastError = Marshal.GetLastWin32Error();
				return false;
			}

			_registered = true;
		}

		return true;
	}

	/// <summary>
	/// Posts a synthetic WM_HOTKEY to the hooked window, exercising the exact native receipt
	/// path used by a real key press. Used by the warm-latency benchmark harness.
	/// </summary>
	public void PostSyntheticHotkey() {
		if (_hwnd != IntPtr.Zero) {
			PostMessage(_hwnd, WmHotkey, HotkeyId, IntPtr.Zero);
		}
	}

	/// <inheritdoc />
	public void Dispose() {
		if (_disposed) {
			return;
		}

		_disposed = true;
		if (_hwnd != IntPtr.Zero) {
			if (_registered) {
				UnregisterHotKey(_hwnd, HotkeyId);
			}

			// Restore the preserved original proc (only if we stored a genuine, non-self one).
			if (_hooked && _originalWndProc != IntPtr.Zero && _originalWndProc != ThunkPtr) {
				SetWindowLongPtr(_hwnd, GwlpWndProc, _originalWndProc);
			}

			Instances.TryRemove(_hwnd, out _);
			_hooked = false;
			_registered = false;
		}
	}

	[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
	private static IntPtr WndProcThunk(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam) {
		if (Instances.TryGetValue(hWnd, out WindowsHotkey? self)) {
			if (msg == WmHotkey) {
				// Stamp the native callback entry FIRST so it captures true receipt time.
				Metrics.HotkeyReceivedTicks = Stopwatch.GetTimestamp();
				try {
					self._onHotkey?.Invoke();
				}
				catch (Exception) {
					// Never let a managed exception escape into the native message pump.
				}
			}

			// Forward unhandled messages ONLY to a preserved, non-self original proc; otherwise
			// DefWindowProc. This makes self-recursion structurally impossible.
			IntPtr chain = self._originalWndProc;
			if (chain != IntPtr.Zero && chain != ThunkPtr) {
				return CallWindowProc(chain, hWnd, msg, wParam, lParam);
			}
		}

		return DefWindowProc(hWnd, msg, wParam, lParam);
	}

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

	[DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtrW")]
	private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

	[DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
	private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
	private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll", SetLastError = true, EntryPoint = "PostMessageW")]
	private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
