using System.Collections.Generic;
using DLS.Description;
using DLS.Game;
using Seb.Helpers;
using Seb.Types;
using Seb.Vis;
using Seb.Vis.UI;
using UnityEngine;

namespace DLS.Graphics
{
	// Touch interface: large buttons for the KEY chips on the board, so a game such as the bundled
	// Snake can be played without a keyboard. W, A, S and D form a cross in the bottom right
	// corner of the board; other keys stand in a row in the bottom left. A button presses its
	// key while a finger holds it. The KEYS button among the view tools hides and shows the pad.
	public static class MobileKeyPad
	{
		const string PrefOpen = "rewired.mobile.keyPadOpen";
		const float Gap = 0.5f;
		const float Margin = 1f;

		static readonly List<char> keys = new();
		static readonly List<Bounds2D> drawnBounds = new();
		static char heldKey;
		static int heldFrame = -1;
		static int drawnFrame = -1;

		public static bool Open
		{
			get => PlayerPrefs.GetInt(PrefOpen, 1) != 0;
			set
			{
				PlayerPrefs.SetInt(PrefOpen, value ? 1 : 0);
				PlayerPrefs.Save();
			}
		}

		// The key held on the pad, or '\0'. Only from the last frame the pad was drawn, so it lets
		// go when a menu covers the pad.
		public static char HeldKey => heldFrame >= Time.frameCount - 1 ? heldKey : '\0';

		static float KeySize => MobileUI.Portrait ? 6.6f : 5.4f;

		// Whether the board has KEY chips: then the view tools offer the pad.
		public static bool BoardHasKeys(Project project)
		{
			CollectKeys(project);
			return keys.Count > 0;
		}

		// Room the pad takes at the side of the board (upright: at the bottom), for FIT to keep
		// the circuit clear of it. Zero when the pad is not shown.
		public static float ReservedSize(Project project) =>
			Open && BoardHasKeys(project) ? KeySize * 3 + Gap * 2 + Margin * 2 : 0;

		// Used before the world handles input, so holding a key does not also move the board.
		public static bool ContainsUIPoint(Vector2 point)
		{
			if (drawnFrame < Time.frameCount - 1) return false;
			foreach (Bounds2D bounds in drawnBounds)
			{
				if (Inside(bounds, point)) return true;
			}

			return false;
		}

		static bool Inside(Bounds2D bounds, Vector2 point) =>
			point.x >= bounds.Left && point.x <= bounds.Right && point.y >= bounds.Bottom && point.y <= bounds.Top;

		public static void Draw(Project project, Rect canvas)
		{
			drawnBounds.Clear();
			heldKey = '\0';
			if (!Open || !BoardHasKeys(project)) return;

			ChipInteractionController controller = project.controller;
			if (controller.SelectedElements.Count > 0 || controller.IsPlacingOrMovingElementOrCreatingWire || controller.IsEditingWire ||
			    controller.IsCreatingSelectionBox || MobileUI.AreaSelectMode || ContextMenu.IsOpen)
			{
				return;
			}

			drawnFrame = Time.frameCount;
			float size = KeySize;
			float step = size + Gap;
			bool cross = keys.Contains('W') && keys.Contains('A') && keys.Contains('S') && keys.Contains('D');
			// Exact bounds, not the enlarged touch area of buttons: the keys of the cross are close.
			Vector2? finger = InputHelper.IsMouseHeld(MouseButton.Left) ? (Vector2?)UI.ScreenToUISpace(InputHelper.MousePos) : null;

			// ---- W A S D as a cross in the bottom right corner ----
			float rowRight = canvas.xMax - Margin;
			if (cross)
			{
				Vector2 centre = new(canvas.xMax - Margin - size * 1.5f - Gap, canvas.yMin + Margin + size * 1.5f + Gap);
				DrawKey('W', centre + Vector2.up * step, size, finger);
				DrawKey('A', centre + Vector2.left * step, size, finger);
				DrawKey('S', centre + Vector2.down * step, size, finger);
				DrawKey('D', centre + Vector2.right * step, size, finger);
				rowRight = centre.x - step * 1.5f - Gap * 2;
			}

			// ---- Other keys in rows from the bottom left, up to the cross ----
			float x = canvas.xMin + Margin;
			float y = canvas.yMin + Margin + size / 2;
			foreach (char key in keys)
			{
				if (cross && key is 'W' or 'A' or 'S' or 'D') continue;
				if (x + size > rowRight && x > canvas.xMin + Margin)
				{
					x = canvas.xMin + Margin;
					y += step;
				}

				DrawKey(key, new Vector2(x + size / 2, y), size, finger);
				x += step;
			}

			heldFrame = Time.frameCount;
		}

		static void DrawKey(char key, Vector2 centre, float size, Vector2? finger)
		{
			Bounds2D bounds = Bounds2D.CreateFromCentreAndSize(centre, new Vector2(size, size));
			drawnBounds.Add(bounds);
			bool held = finger.HasValue && Inside(bounds, finger.Value);
			if (held) heldKey = key;

			// See-through, so the circuit under the pad stays readable.
			Color fill = held ? MobileTheme.Accent : new Color(0.16f, 0.17f, 0.2f, 0.82f);
			UI.DrawPanel(centre, bounds.Size, fill);
			Color edge = held ? MobileTheme.Accent : MobileTheme.LineCol;
			const float w = 0.08f;
			UI.DrawLine(bounds.BottomLeft, bounds.TopLeft, w, edge);
			UI.DrawLine(bounds.TopLeft, bounds.TopRight, w, edge);
			UI.DrawLine(bounds.TopRight, bounds.BottomRight, w, edge);
			UI.DrawLine(bounds.BottomRight, bounds.BottomLeft, w, edge);
			MobileTheme.Text(key.ToString(), centre, MobileUI.Portrait ? 1.25f : 1.05f, Color.white, Anchor.TextCentre, true);
			UI.OverridePreviousBounds(bounds);
		}

		// The keys of the KEY chips on the board, each once, in the order they were placed.
		static void CollectKeys(Project project)
		{
			keys.Clear();
			if (project?.ViewedChip == null) return;

			foreach (IMoveable element in project.ViewedChip.Elements)
			{
				if (element is not SubChipInstance { ChipType: ChipType.Key } subChip || subChip.InternalData == null || subChip.InternalData.Length == 0) continue;
				char key = char.ToUpperInvariant((char)subChip.InternalData[0]);
				if (key != '\0' && !keys.Contains(key)) keys.Add(key);
			}
		}
	}
}
