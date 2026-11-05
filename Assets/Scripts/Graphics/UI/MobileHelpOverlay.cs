using System.Collections.Generic;
using DLS.Game;
using Seb.Types;
using Seb.Vis;
using Seb.Vis.UI;
using UnityEngine;

namespace DLS.Graphics
{
	// "How it works": the touch interface in six steps. Shown once the first time a project is
	// opened, and from MENU > HOW IT WORKS and the empty board.
	public static class MobileHelpOverlay
	{
		static readonly (string title, string text)[] Steps =
		{
			("ADD PARTS", "Drag a part from PARTS onto the board, or tap it and then tap the board."),
			("CONNECT", "Drag from one pin to another, or tap both pins. Tap empty space on the way to bend."),
			("TRY IT", "Tap the round button of an input to switch it on and off."),
			("CHANGE", "Tap a part to select it: its actions appear below. Drag a part to move it."),
			("LOOK AROUND", "Drag empty space to move, pinch or + and - to zoom. FIT shows everything."),
			("SAVE", "SAVE turns the board into a chip. Find it under MY CHIPS and use it again.")
		};

		public static void Draw()
		{
			MenuHelper.DrawBackgroundOverlay();
			Rect safe = MobileUI.SafeRectUI;

			// Upright the screen's units are smaller, so the text is larger and wraps.
			bool portrait = MobileUI.Portrait;
			float textScale = portrait ? 0.74f : 0.6f;
			float titleScale = portrait ? 0.72f : 0.62f;
			float lineHeight = DrawSettings.ActiveUITheme.FontSizeRegular * textScale * 1.45f;
			const float titleToText = 1.9f;
			const float stepGap = 1.3f;

			float width = Mathf.Min(safe.width - (portrait ? 2f : 4f), 70f);
			const float textLeft = 4.6f;
			float textWidth = width - textLeft - 1.2f;

			List<string>[] wrapped = new List<string>[Steps.Length];
			float stepsHeight = 0;
			for (int i = 0; i < Steps.Length; i++)
			{
				wrapped[i] = MobileTheme.Wrap(Steps[i].text, textWidth, textScale);
				stepsHeight += titleToText + (wrapped[i].Count - 1) * lineHeight + stepGap + 1f;
			}

			string hold = "Hold a finger on anything to see all its options.";
			List<string> holdLines = MobileTheme.Wrap(hold, portrait ? width - 3f : width - 20f, 0.58f);
			float footerHeight = portrait ? 4.4f + 1.2f + holdLines.Count * 1.4f + 1f : 5.6f;
			float height = Mathf.Min(4.6f + stepsHeight + footerHeight + 0.6f, safe.height - 1f);
			Bounds2D panel = MobileTheme.Card(new Vector2(safe.center.x, safe.center.y), new Vector2(width, height), Anchor.Centre);

			MobileTheme.Text("HOW IT WORKS", panel.TopLeft + new Vector2(1.5f, -2.2f), 0.86f, Color.white, Anchor.TextCentreLeft, true);

			float y = panel.Top - 5f;
			float textX = panel.Left + textLeft;
			for (int i = 0; i < Steps.Length; i++)
			{
				string title = Steps[i].title;
				UI.DrawCircle(new Vector2(panel.Left + 2.4f, y - titleToText / 2), 1.3f, MobileTheme.Accent);
				MobileTheme.Text((i + 1).ToString(), new Vector2(panel.Left + 2.4f, y - titleToText / 2), 0.62f, Color.white, Anchor.TextCentre, true);
				MobileTheme.Text(title, new Vector2(textX, y), titleScale, MobileTheme.Accent, Anchor.TextCentreLeft, true);
				y -= titleToText;
				foreach (string line in wrapped[i])
				{
					MobileTheme.Text(line, new Vector2(textX, y), textScale, Color.white);
					y -= lineHeight;
				}

				y -= stepGap + 1f - lineHeight;
			}

			// The tip sits beside GOT IT when there is room; upright, above it.
			float tipY = portrait ? panel.Bottom + 4.4f + 1.9f + (holdLines.Count - 1) * 1.4f : panel.Bottom + 3f;
			foreach (string line in holdLines)
			{
				MobileTheme.Text(line, new Vector2(panel.Left + 1.5f, tipY), 0.58f, MobileTheme.SecondaryTextCol);
				tipY -= 1.4f;
			}

			Vector2 buttonSize = portrait ? new Vector2(width - 2f, 4.4f) : new Vector2(15f, 4.4f);
			if (MobileTheme.Button("GOT IT", MobileTheme.Primary, panel.BottomRight + new Vector2(-1f, 0.9f), buttonSize, true, Anchor.BottomRight) ||
			    KeyboardShortcuts.CancelShortcutTriggered)
			{
				UIDrawer.SetActiveMenu(UIDrawer.MenuType.None);
			}
		}
	}
}
