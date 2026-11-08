using System.Collections.Generic;
using Seb.Helpers;
using UnityEngine;

namespace DLS.Simulation
{
	public static class SimKeyboardHelper
	{
		public static readonly KeyCode[] ValidInputKeys =
		{
			KeyCode.A, KeyCode.B, KeyCode.C, KeyCode.D, KeyCode.E, KeyCode.F, KeyCode.G,
			KeyCode.H, KeyCode.I, KeyCode.J, KeyCode.K, KeyCode.L, KeyCode.M, KeyCode.N,
			KeyCode.O, KeyCode.P, KeyCode.Q, KeyCode.R, KeyCode.S, KeyCode.T, KeyCode.U,
			KeyCode.V, KeyCode.W, KeyCode.X, KeyCode.Y, KeyCode.Z,

			KeyCode.Alpha0, KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4,
			KeyCode.Alpha5, KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9
		};

		static readonly HashSet<char> KeyLookup = new();
		static readonly HashSet<char> ReplayKeyLookup = new();
		static bool HasAnyInput;
		static bool ReplayOverrideActive;

		// What KeyIsHeld answers from. The sim thread asks for every KEY chip on every step,
		// thousands of times a second, while the main thread rebuilds the held keys once a
		// frame. Taking the lock for each question made the two threads fight over it, so
		// the main thread publishes an immutable copy instead and the sim thread only reads
		// the reference. A snapshot is never changed after it is published.
		sealed class Snapshot
		{
			public static readonly Snapshot None = new(new HashSet<char>(), false);

			public readonly HashSet<char> Keys;
			public readonly bool Replay;

			public Snapshot(HashSet<char> keys, bool replay)
			{
				Keys = keys;
				Replay = replay;
			}
		}

		static volatile Snapshot published = Snapshot.None;

		// Call with the lock held, after any change to the lookups above.
		static void Publish()
		{
			HashSet<char> source = ReplayOverrideActive ? ReplayKeyLookup : HasAnyInput ? KeyLookup : null;
			Snapshot current = published;
			if (current.Replay == ReplayOverrideActive && (source == null ? current.Keys.Count == 0 : current.Keys.SetEquals(source))) return;

			published = source == null || source.Count == 0 && !ReplayOverrideActive
				? Snapshot.None
				: new Snapshot(new HashSet<char>(source), ReplayOverrideActive);
		}

		// The key of a KEY chip held down with a finger, or '\0'. Set from the main thread
		// before RefreshInputState, so touch screens without a keyboard can press key chips.
		public static char TouchHeldKey;

		// Call from Main Thread
		public static void RefreshInputState()
		{
			lock (KeyLookup)
			{
				ReadHeldKeys();
				Publish();
			}
		}

		static void ReadHeldKeys()
		{
			KeyLookup.Clear();
			HasAnyInput = false;

			if (TouchHeldKey != '\0')
			{
				KeyLookup.Add(char.ToUpper(TouchHeldKey));
				HasAnyInput = true;
			}

			if (!InputHelper.AnyKeyOrMouseHeldThisFrame) return; // early exit if no key held
			if (InputHelper.CtrlIsHeld || InputHelper.ShiftIsHeld || InputHelper.AltIsHeld) return; // don't trigger key chips if modifier is held

			foreach (KeyCode key in ValidInputKeys)
			{
				if (InputHelper.IsKeyHeld(key))
				{
					char keyChar = char.ToUpper((char)key);
					KeyLookup.Add(keyChar);
					HasAnyInput = true;
				}
			}
		}

		// Call from Sim Thread
		public static bool KeyIsHeld(char key)
		{
			Snapshot snapshot = published;
			if (snapshot.Keys.Count == 0) return false;
			return snapshot.Keys.Contains(snapshot.Replay ? char.ToUpper(key) : key);
		}

		internal static char[] CaptureHeldKeys()
		{
			lock (KeyLookup)
			{
				HashSet<char> source = ReplayOverrideActive ? ReplayKeyLookup : KeyLookup;
				if (source.Count == 0) return System.Array.Empty<char>();

				char[] result = new char[source.Count];
				source.CopyTo(result);
				System.Array.Sort(result);
				return result;
			}
		}

		internal static void SetReplayInputState(char[] heldKeys)
		{
			lock (KeyLookup)
			{
				ReplayKeyLookup.Clear();
				if (heldKeys != null)
				{
					for (int i = 0; i < heldKeys.Length; i++)
					{
						ReplayKeyLookup.Add(char.ToUpper(heldKeys[i]));
					}
				}
				ReplayOverrideActive = true;
				Publish();
			}
		}

		internal static void ClearReplayInputState()
		{
			lock (KeyLookup)
			{
				ReplayKeyLookup.Clear();
				ReplayOverrideActive = false;
				Publish();
			}
		}
	}
}