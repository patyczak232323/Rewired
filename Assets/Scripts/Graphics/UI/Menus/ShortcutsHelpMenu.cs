using DLS.Description;
using DLS.Game;
using Seb.Helpers;
using Seb.Types;
using Seb.Vis;
using Seb.Vis.UI;
using UnityEngine;

namespace DLS.Graphics
{
	// Every keyboard shortcut and mouse gesture of the desktop editor on one card (F1).
	public static class ShortcutsHelpMenu
	{
		const float Width = 78f;
		const float ColumnGap = 3f;
		const float RowHeight = 1.32f;

		static readonly (string title, (ShortcutAction? action, string label, string fixedKeys)[] rows)[] leftColumn =
		{
			("EDITING", new (ShortcutAction?, string, string)[]
			{
				(ShortcutAction.Undo, "Undo", null),
				(ShortcutAction.Redo, "Redo", null),
				(ShortcutAction.RedoAlternative, "Redo", null),
				(ShortcutAction.SelectAll, "Select all", null),
				(ShortcutAction.Copy, "Copy", null),
				(ShortcutAction.Cut, "Cut", null),
				(ShortcutAction.Paste, "Paste", null),
				(ShortcutAction.Duplicate, "Duplicate", null),
				(ShortcutAction.MirrorHorizontal, "Mirror horizontally", null),
				(ShortcutAction.MirrorVertical, "Mirror vertically", null),
				(null, "Delete the selection", "Delete / Backspace"),
				(null, "Cancel what you are doing", "Esc")
			}),
			("CHIPS", new (ShortcutAction?, string, string)[]
			{
				(ShortcutAction.Save, "Save the chip", null),
				(ShortcutAction.CreateNewChip, "New chip", null),
				(ShortcutAction.ChipLibrary, "Chip library", null),
				(ShortcutAction.QuitToMainMenu, "Main menu", null)
			})
		};

		static readonly (string title, (ShortcutAction? action, string label, string fixedKeys)[] rows)[] rightColumn =
		{
			("FIND AND VIEW", new (ShortcutAction?, string, string)[]
			{
				(ShortcutAction.CommandPalette, "Search parts and commands", null),
				(ShortcutAction.Search, "Find a chip by name", null),
				(ShortcutAction.TogglePartsPanel, "Parts panel", null),
				(ShortcutAction.ToggleInspector, "Details panel", null),
				(ShortcutAction.ResetCamera, "Fit the chip to the screen", null),
				(ShortcutAction.ToggleGrid, "Grid", null),
				(ShortcutAction.Preferences, "Preferences", null),
				(null, "Pin names on and off", "Tab")
			}),
			("SIMULATION", new (ShortcutAction?, string, string)[]
			{
				(ShortcutAction.SimulationPause, "Pause or run", null),
				(ShortcutAction.SimulationNextStep, "One step while paused", null)
			}),
			("MOUSE", new (ShortcutAction?, string, string)[]
			{
				(null, "Move the view", "Middle drag / Alt+drag"),
				(null, "Zoom", "Mouse wheel"),
				(null, "Wire from pin to pin", "Drag from a pin"),
				(null, "Straight wires and moves", "Hold Shift"),
				(null, "Place several at once", "Hold Shift"),
				(null, "Snap to the grid", "Hold Ctrl"),
				(null, "Menu of a part or wire", "Right click")
			})
		};

		public static void DrawMenu()
		{
			MenuHelper.DrawBackgroundOverlay();
			Draw.ID panelID = UI.ReservePanel();
			float columnWidth = (Width - ColumnGap) / 2;
			Vector2 topLeft = new(UI.Centre.x - Width / 2, UI.Height - 4.5f);
			bool close;
			Bounds2D card;

			using (UI.BeginBoundsScope(true))
			{
				Bounds2D header = RewiredUI.DrawSectionHeader("KEYBOARD SHORTCUTS", topLeft, Width, true);
				float top = header.Bottom - 0.8f;

				float leftBottom = DrawColumn(leftColumn, new Vector2(topLeft.x, top), columnWidth);
				float rightBottom = DrawColumn(rightColumn, new Vector2(topLeft.x + columnWidth + ColumnGap, top), columnWidth);
				float bottom = Mathf.Min(leftBottom, rightBottom) - 0.6f;

				DesktopTheme.Text("Change any shortcut in SETTINGS > KEY BINDINGS on the main menu.", new Vector2(topLeft.x, bottom - 1f), 0.44f, RewiredUI.DimText);
				Vector2 closeSize = new(10f, 2.4f);
				close = DesktopTheme.Button("CLOSE", DesktopTheme.Neutral, new Vector2(topLeft.x + Width - closeSize.x, bottom), closeSize);

				card = UI.GetCurrentBoundsScope();
				MenuHelper.DrawReservedMenuPanel(panelID, card);
			}

			// The dimmed board behind counts as interface too, so look at the card itself.
			bool clickedOutside = InputHelper.IsMouseDownThisFrame(MouseButton.Left) && !UI.MouseInsideBounds(Bounds2D.Grow(card, 1f));
			if (close || clickedOutside || KeyboardShortcuts.CancelShortcutTriggered || KeyboardShortcuts.ShortcutsHelpShortcutTriggered)
			{
				UIDrawer.SetActiveMenu(UIDrawer.MenuType.None);
			}
		}

		// Returns the bottom of the column.
		static float DrawColumn((string title, (ShortcutAction? action, string label, string fixedKeys)[] rows)[] groups, Vector2 topLeft, float width)
		{
			float y = topLeft.y;
			foreach ((string title, (ShortcutAction? action, string label, string fixedKeys)[] rows) in groups)
			{
				DesktopTheme.Text(title, new Vector2(topLeft.x, y - RowHeight / 2), 0.44f, RewiredUI.Accent, Anchor.TextCentreLeft, true);
				y -= RowHeight + 0.15f;

				foreach ((ShortcutAction? action, string label, string fixedKeys) in rows)
				{
					string keys = fixedKeys;
					if (action.HasValue)
					{
						ShortcutBinding binding = KeyboardShortcuts.GetBinding(action.Value);
						if (!binding.IsBound) continue;
						keys = binding.ToDisplayString();
					}

					float centreY = y - RowHeight / 2;
					DesktopTheme.Text(label, new Vector2(topLeft.x, centreY), 0.46f, Color.white);
					DrawKeys(keys, new Vector2(topLeft.x + width, centreY));
					y -= RowHeight;
				}

				y -= 0.7f;
			}

			UI.OverridePreviousBounds(new Bounds2D(new Vector2(topLeft.x, y), new Vector2(topLeft.x + width, topLeft.y)));
			return y;
		}

		// The keys right-aligned on a small key-cap background.
		static void DrawKeys(string keys, Vector2 centreRight)
		{
			const float scale = 0.42f;
			float width = DesktopTheme.TextWidth(keys, scale) + 0.9f;
			UI.DrawPanel(centreRight, new Vector2(width, 1.1f), MobileTheme.CardCol, Anchor.CentreRight);
			DesktopTheme.Text(keys, centreRight + Vector2.left * 0.45f, scale, RewiredUI.SecondaryText, Anchor.TextCentreRight);
		}
	}
}
