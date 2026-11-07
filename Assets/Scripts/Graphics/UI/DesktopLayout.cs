using DLS.Game;
using Seb.Helpers;
using Seb.Types;
using Seb.Vis;
using Seb.Vis.UI;
using UnityEngine;

namespace DLS.Graphics
{
	// Where the desktop editor's bars and side panels sit on the 16:9 UI canvas, and the part of
	// the screen left for the board.
	public static class DesktopLayout
	{
		public const float TopBarHeight = 3f;
		public const float PartsPanelWidth = 15f;
		public const float InspectorWidth = 16.5f;

		const string PrefPartsPanel = "Desktop_PartsPanelOpen";
		const string PrefInspector = "Desktop_InspectorOpen";

		const float CanvasWidth = UI.DefaultWidth;
		const float CanvasHeight = UI.DefaultWidth * 9 / 16f;

		static int partsPanelOpen = -1;
		static int inspectorOpen = -1;

		public static bool PartsPanelOpen
		{
			get
			{
				if (partsPanelOpen < 0) partsPanelOpen = PlayerPrefs.GetInt(PrefPartsPanel, 1);
				return partsPanelOpen == 1;
			}
			set
			{
				partsPanelOpen = value ? 1 : 0;
				PlayerPrefs.SetInt(PrefPartsPanel, partsPanelOpen);
				PlayerPrefs.Save();
			}
		}

		public static bool InspectorOpen
		{
			get
			{
				if (inspectorOpen < 0) inspectorOpen = PlayerPrefs.GetInt(PrefInspector, 1);
				return inspectorOpen == 1;
			}
			set
			{
				inspectorOpen = value ? 1 : 0;
				PlayerPrefs.SetInt(PrefInspector, inspectorOpen);
				PlayerPrefs.Save();
			}
		}

		// Nothing can be added while looking inside a chip, so the parts panel steps aside then.
		public static bool PartsPanelShown(Project project) => PartsPanelOpen && project != null && project.CanEditViewedChip;
		public static bool InspectorShown(Project project) => InspectorOpen && project != null;

		public static void TogglePartsPanel() => PartsPanelOpen = !PartsPanelOpen;
		public static void ToggleInspector() => InspectorOpen = !InspectorOpen;

		// The board between the bars and the open panels, in UI units.
		public static Rect BoardRectUI
		{
			get
			{
				Project project = Project.ActiveProject;
				float left = PartsPanelShown(project) ? PartsPanelWidth : 0;
				float right = CanvasWidth - (InspectorShown(project) ? InspectorWidth : 0);
				return Rect.MinMaxRect(left, BottomBarUI.barHeight, right, CanvasHeight - TopBarHeight);
			}
		}

		// The same area in screen pixels. The UI canvas keeps 16:9 inside the window (with black
		// bars on the other sides), the way UI.CreateFixedAspectUIScope places it.
		public static Rect BoardRectPixels
		{
			get
			{
				float screenW = Mathf.Max(1, Screen.width);
				float screenH = Mathf.Max(1, Screen.height);
				float canvasW = screenW;
				float canvasH = screenW * 9 / 16f;
				if (canvasH > screenH)
				{
					canvasH = screenH;
					canvasW = screenH * 16 / 9f;
				}

				Vector2 canvasMin = new((screenW - canvasW) / 2, (screenH - canvasH) / 2);
				float scale = canvasW / CanvasWidth;
				Rect board = BoardRectUI;
				return new Rect(canvasMin.x + board.xMin * scale, canvasMin.y + board.yMin * scale, board.width * scale, board.height * scale);
			}
		}
	}

	// Buttons and text of the desktop editor's bars and panels.
	public static class DesktopTheme
	{
		public const float ButtonHeight = 2.2f;
		public const float TextScale = 0.66f;
		public const float SmallTextScale = 0.5f;

		public static ButtonTheme Neutral => MobileTheme.WithFontScale(MobileTheme.Neutral, 0.74f);
		public static ButtonTheme Quiet => MobileTheme.WithFontScale(MobileTheme.Quiet, 0.74f);
		public static ButtonTheme Primary => MobileTheme.WithFontScale(MobileTheme.Primary, 0.74f);
		public static ButtonTheme Danger => MobileTheme.WithFontScale(MobileTheme.Danger, 0.74f);
		public static ButtonTheme Toggle(bool on) => on ? Primary : Neutral;

		// Clicks that land on an open context menu are the menu's.
		static bool IgnoreInputs => ContextMenu.HasFocus();

		public static bool Button(string label, ButtonTheme theme, Vector2 topLeft, Vector2 size, bool enabled = true) =>
			UI.Button(label, theme, topLeft, size, enabled, false, false, Anchor.TopLeft, false, 0, IgnoreInputs, false);

		// A button with a vector icon and an optional label; without a label the icon is centred.
		public static bool IconButton(string label, MobileIcon icon, ButtonTheme theme, Vector2 topLeft, Vector2 size, bool enabled = true)
		{
			bool hasLabel = !string.IsNullOrEmpty(label);
			float iconSize = Mathf.Min(size.y * 0.42f, 1.2f);
			const float iconGap = 0.55f;
			float labelOffset = hasLabel ? (iconSize + iconGap) / 2 : 0;
			bool pressed = UI.Button(label ?? string.Empty, theme, topLeft, size, enabled, false, false, Anchor.TopLeft, false, labelOffset, IgnoreInputs, false);
			Bounds2D bounds = UI.PrevBounds;

			float labelWidth = hasLabel ? UI.CalculateTextSize(label, theme.fontSize, theme.font).x : 0;
			float groupWidth = hasLabel ? iconSize + iconGap + labelWidth : iconSize;
			Vector2 iconCentre = new(bounds.Centre.x - groupWidth / 2 + iconSize / 2, bounds.Centre.y);
			MobileTheme.DrawIcon(icon, iconCentre, iconSize, enabled ? theme.textCols.normal : theme.textCols.inactive);

			UI.OverridePreviousBounds(bounds);
			return pressed;
		}

		public static float LabelButtonWidth(string label, ButtonTheme theme, bool withIcon) =>
			UI.CalculateTextSize(label, theme.fontSize, theme.font).x + (withIcon ? 2.9f : 1.8f);

		public static void Text(string text, Vector2 pos, float scale, Color col, Anchor anchor = Anchor.TextCentreLeft, bool bold = false) =>
			MobileTheme.Text(text, pos, scale, col, anchor, bold);

		public static float TextWidth(string text, float scale, bool bold = false) => MobileTheme.TextWidth(text, scale, bold);

		public static string Fit(string text, float width, float scale, bool bold = false) => MobileTheme.Fit(text, width, scale, bold);

		// ---- Hover hints ----
		// A short label under a button the mouse rests on, with its shortcut. Buttons call Hint
		// right after they are drawn; DrawHint draws the one under the mouse on top of everything.

		const float HintDelay = 0.45f;
		static string hintText;
		static string hintShortcut;
		static Bounds2D hintAnchor;
		static int hintFrame = -1;
		static string hoverKey;
		static float hoverStartTime;

		public static void Hint(string text, string shortcut = null)
		{
			Bounds2D bounds = UI.PrevBounds;
			if (ContextMenu.IsOpen || !UI.MouseInsideBounds(bounds)) return;

			if (hoverKey != text)
			{
				hoverKey = text;
				hoverStartTime = Time.unscaledTime;
			}

			hintText = text;
			hintShortcut = shortcut;
			hintAnchor = bounds;
			hintFrame = Time.frameCount;
		}

		public static void DrawHint()
		{
			if (hintFrame != Time.frameCount)
			{
				hoverKey = null;
				return;
			}

			if (Time.unscaledTime - hoverStartTime < HintDelay || InputHelper.IsMouseHeld(MouseButton.Left)) return;

			const float scale = 0.5f;
			const float padX = 0.7f;
			const float height = 1.9f;
			bool hasShortcut = !string.IsNullOrEmpty(hintShortcut);
			float textWidth = TextWidth(hintText, scale);
			float shortcutWidth = hasShortcut ? TextWidth(hintShortcut, scale) + 1.2f : 0;
			float width = textWidth + shortcutWidth + padX * 2;

			// Below the button, or above it when the button is at the bottom of the screen.
			bool below = hintAnchor.Bottom - height - 0.4f > 0;
			float top = below ? hintAnchor.Bottom - 0.35f : hintAnchor.Top + 0.35f + height;
			float left = Mathf.Clamp(hintAnchor.Centre.x - width / 2, 0.3f, UI.Width - width - 0.3f);

			Bounds2D card = MobileTheme.Card(new Vector2(left, top), new Vector2(width, height), Anchor.TopLeft);
			Text(hintText, card.CentreLeft + Vector2.right * padX, scale, Color.white);
			if (hasShortcut) Text(hintShortcut, card.CentreRight + Vector2.left * padX, scale, RewiredUI.DimText, Anchor.TextCentreRight);
		}

		public static void Outline(Bounds2D bounds, Color col, float width = 0.06f)
		{
			UI.DrawLine(bounds.BottomLeft, bounds.TopLeft, width, col);
			UI.DrawLine(bounds.TopLeft, bounds.TopRight, width, col);
			UI.DrawLine(bounds.TopRight, bounds.BottomRight, width, col);
			UI.DrawLine(bounds.BottomRight, bounds.BottomLeft, width, col);
			UI.OverridePreviousBounds(bounds);
		}
	}
}
