using DLS.Description;
using DLS.Game;
using Seb.Types;
using Seb.Vis;
using Seb.Vis.UI;
using UnityEngine;

namespace DLS.Graphics
{
	// The desktop editor's top bar: the menu, what is being edited, undo and redo, the
	// simulation controls, search, zoom and the side panels.
	public static class DesktopToolbar
	{
		const float Gap = 0.3f;
		const float GroupGap = 0.9f;
		const float Edge = 0.4f;
		const float IconWidth = 2.6f;
		const float MenuWidth = 7.2f;
		const float SearchWidth = 14f;
		const float ZoomLabelWidth = 4.4f;

		// The frame the menu was opened or closed from here, so the click does not also close it.
		public static int MenuToggleFrame { get; private set; } = -1;

		public static float MenuButtonLeft => Edge;

		static float ButtonTop => UI.Height - (DesktopLayout.TopBarHeight - DesktopTheme.ButtonHeight) / 2;

		public static void Draw(Project project)
		{
			float h = DesktopLayout.TopBarHeight;
			UI.DrawPanel(new Vector2(0, UI.Height), new Vector2(UI.Width, h), MobileTheme.BarCol, Anchor.TopLeft);
			UI.DrawLine(new Vector2(0, UI.Height - h), new Vector2(UI.Width, UI.Height - h), 0.06f, MobileTheme.LineCol);

			bool inOtherMenu = UIDrawer.ActiveMenu is not (UIDrawer.MenuType.None or UIDrawer.MenuType.BottomBarMenuPopup);
			using (UI.BeginDisabledScope(inOtherMenu))
			{
				float searchLeft = DrawRightGroup(project);
				float simLeft = DrawSimulationGroup(project, searchLeft - GroupGap * 2);
				DrawLeftGroup(project, simLeft - GroupGap * 2);
			}

			// Escape steps back out of a chip being looked inside.
			if (project.chipViewStack.Count > 1 && UIDrawer.ActiveMenu == UIDrawer.MenuType.None && !ContextMenu.IsOpen && KeyboardShortcuts.CancelShortcutTriggered)
			{
				project.ReturnToPreviousViewedChip();
			}
		}

		// ---- Left: menu (or back out of a chip being looked inside), then the chip's name ----
		static void DrawLeftGroup(Project project, float maxRight)
		{
			float y = ButtonTop;
			Vector2 buttonSize = new(MenuWidth, DesktopTheme.ButtonHeight);
			bool viewingInside = project.chipViewStack.Count > 1;

			if (viewingInside)
			{
				if (DesktopTheme.IconButton("BACK", MobileIcon.Back, DesktopTheme.Neutral, new Vector2(Edge, y), buttonSize))
				{
					project.ReturnToPreviousViewedChip();
				}

				DesktopTheme.Hint("Back to the chip outside", "Esc");
			}
			else
			{
				bool menuOpen = UIDrawer.ActiveMenu == UIDrawer.MenuType.BottomBarMenuPopup;
				if (DesktopTheme.IconButton("MENU", MobileIcon.Menu, DesktopTheme.Toggle(menuOpen), new Vector2(Edge, y), buttonSize))
				{
					UIDrawer.ToggleBottomPopupMenu();
					MenuToggleFrame = Time.frameCount;
				}
			}

			float x = Edge + MenuWidth + GroupGap;
			float width = maxRight - x;
			if (width < 4) return;

			float centreY = UI.Height - DesktopLayout.TopBarHeight / 2;
			string chipName = string.IsNullOrWhiteSpace(project.ActiveDevChipName) ? "NEW CHIP" : project.ActiveDevChipName.ToUpperInvariant();
			string title = viewingInside ? "INSIDE " + chipName : chipName;
			DesktopTheme.Text(DesktopTheme.Fit(title, width, 0.62f, true), new Vector2(x, centreY + 0.5f), 0.62f, Color.white, Anchor.TextCentreLeft, true);

			// Second line: the project, then what needs attention.
			const float lineScale = 0.48f;
			float lineY = centreY - 0.62f;
			float lineX = x;
			string first = viewingInside ? project.viewedChipsString + "  •  view only" : project.description.ProjectName;
			lineX = DrawSegment(first, lineX, x + width, lineY, lineScale, RewiredUI.DimText);

			if (!viewingInside && RewiredUI.ActiveChipHasUnsavedChanges(project))
			{
				lineX = DrawSegment("  •  NOT SAVED", lineX, x + width, lineY, lineScale, RewiredUI.UnsavedText);
			}

			if (project.simPaused)
			{
				string breakpoint = project.lastBreakpointHit;
				string paused = breakpoint == null
					? $"  •  PAUSED, STEP {project.simPausedSingleStepCounter}"
					: $"  •  BREAKPOINT: {breakpoint.ToUpperInvariant()}";
				DrawSegment(paused, lineX, x + width, lineY, lineScale, breakpoint == null ? new Color(0.62f, 0.68f, 0.95f) : Color.yellow);
			}
		}

		// Draws text from x if any of it fits before maxX; returns where the next text starts.
		static float DrawSegment(string text, float x, float maxX, float y, float scale, Color col)
		{
			if (x >= maxX - 1) return x;
			string fitted = DesktopTheme.Fit(text, maxX - x, scale);
			DesktopTheme.Text(fitted, new Vector2(x, y), scale, col);
			return x + DesktopTheme.TextWidth(fitted, scale);
		}

		// ---- Middle: undo, redo, run or pause, step. Returns its left edge. ----
		static float DrawSimulationGroup(Project project, float right)
		{
			float y = ButtonTop;
			float h = DesktopTheme.ButtonHeight;
			ButtonTheme theme = DesktopTheme.Neutral;
			bool canEdit = project.CanEditViewedChip;
			bool paused = project.simPaused;
			UndoController undo = project.ViewedChip.UndoController;

			float stepWidth = DesktopTheme.LabelButtonWidth("STEP", theme, true);
			float runWidth = Mathf.Max(DesktopTheme.LabelButtonWidth("PAUSE", theme, true), DesktopTheme.LabelButtonWidth("RUN", theme, true));

			float x = right - stepWidth;
			if (DesktopTheme.IconButton("STEP", MobileIcon.Step, theme, new Vector2(x, y), new Vector2(stepWidth, h), paused))
			{
				project.advanceSingleSimStep = true;
			}

			DesktopTheme.Hint("One simulation step", Shortcut(ShortcutAction.SimulationNextStep));

			x -= Gap + runWidth;
			if (DesktopTheme.IconButton(paused ? "RUN" : "PAUSE", paused ? MobileIcon.Play : MobileIcon.Pause, DesktopTheme.Toggle(paused), new Vector2(x, y), new Vector2(runWidth, h)))
			{
				project.description.Prefs_SimPaused = !paused;
				project.SaveCurrentProjectDescription();
			}

			DesktopTheme.Hint(paused ? "Run the simulation" : "Pause the simulation", Shortcut(ShortcutAction.SimulationPause));

			x -= GroupGap + IconWidth;
			if (DesktopTheme.IconButton(null, MobileIcon.Redo, theme, new Vector2(x, y), new Vector2(IconWidth, h), canEdit && undo.CanRedo))
			{
				undo.TryRedo();
			}

			DesktopTheme.Hint("Redo", Shortcut(ShortcutAction.Redo));

			x -= Gap + IconWidth;
			if (DesktopTheme.IconButton(null, MobileIcon.Undo, theme, new Vector2(x, y), new Vector2(IconWidth, h), canEdit && undo.CanUndo))
			{
				undo.TryUndo();
			}

			DesktopTheme.Hint("Undo", Shortcut(ShortcutAction.Undo));
			return x;
		}

		// ---- Right: search, zoom, the side panels. Returns its left edge. ----
		static float DrawRightGroup(Project project)
		{
			float y = ButtonTop;
			float h = DesktopTheme.ButtonHeight;
			ButtonTheme theme = DesktopTheme.Neutral;

			float x = UI.Width - Edge;
			float detailsWidth = DesktopTheme.LabelButtonWidth("DETAILS", theme, false);
			x -= detailsWidth;
			if (DesktopTheme.Button("DETAILS", DesktopTheme.Toggle(DesktopLayout.InspectorOpen), new Vector2(x, y), new Vector2(detailsWidth, h)))
			{
				DesktopLayout.ToggleInspector();
			}

			DesktopTheme.Hint("Show or hide the details panel", Shortcut(ShortcutAction.ToggleInspector));

			float partsWidth = DesktopTheme.LabelButtonWidth("PARTS", theme, false);
			x -= Gap + partsWidth;
			if (DesktopTheme.Button("PARTS", DesktopTheme.Toggle(DesktopLayout.PartsPanelOpen && project.CanEditViewedChip), new Vector2(x, y), new Vector2(partsWidth, h), project.CanEditViewedChip))
			{
				DesktopLayout.TogglePartsPanel();
			}

			DesktopTheme.Hint("Show or hide the parts panel", Shortcut(ShortcutAction.TogglePartsPanel));

			// Zoom: out, the current zoom, in, fit.
			x -= GroupGap + IconWidth;
			if (DesktopTheme.IconButton(null, MobileIcon.Fit, theme, new Vector2(x, y), new Vector2(IconWidth, h)))
			{
				CameraController.FitViewToChip();
			}

			DesktopTheme.Hint("Fit the chip to the screen", Shortcut(ShortcutAction.ResetCamera));

			x -= Gap + IconWidth;
			if (DesktopTheme.IconButton(null, MobileIcon.Plus, theme, new Vector2(x, y), new Vector2(IconWidth, h)))
			{
				CameraController.ZoomAroundBoardCentre(0.8f);
			}

			DesktopTheme.Hint("Zoom in", "Mouse wheel");

			x -= ZoomLabelWidth;
			string zoom = Mathf.RoundToInt(CameraController.ZoomPercent) + "%";
			DesktopTheme.Text(zoom, new Vector2(x + ZoomLabelWidth / 2, UI.Height - DesktopLayout.TopBarHeight / 2), DesktopTheme.SmallTextScale * 1.1f, RewiredUI.SecondaryText, Anchor.TextCentre);

			x -= IconWidth;
			if (DesktopTheme.IconButton(null, MobileIcon.Minus, theme, new Vector2(x, y), new Vector2(IconWidth, h)))
			{
				CameraController.ZoomAroundBoardCentre(1.25f);
			}

			DesktopTheme.Hint("Zoom out", "Mouse wheel");

			// Search: chips to add and every command, by name.
			x -= GroupGap + SearchWidth;
			if (DesktopTheme.Button(string.Empty, DesktopTheme.Quiet, new Vector2(x, y), new Vector2(SearchWidth, h)))
			{
				CommandPalette.Open();
			}

			Bounds2D search = UI.PrevBounds;
			bool enabled = UIDrawer.ActiveMenu is UIDrawer.MenuType.None or UIDrawer.MenuType.BottomBarMenuPopup;
			Color textCol = enabled ? RewiredUI.DimText : new Color(1, 1, 1, 0.3f);
			MobileTheme.DrawIcon(MobileIcon.Search, search.CentreLeft + Vector2.right * 1.1f, 0.95f, textCol);
			DesktopTheme.Text("Search", search.CentreLeft + Vector2.right * 2.1f, DesktopTheme.SmallTextScale * 1.1f, textCol);
			DesktopTheme.Text(Shortcut(ShortcutAction.CommandPalette), search.CentreRight + Vector2.left * 0.7f, DesktopTheme.SmallTextScale, new Color(1, 1, 1, 0.38f), Anchor.TextCentreRight);
			UI.OverridePreviousBounds(search);
			DesktopTheme.Hint("Find a part or a command");
			return x;
		}

		static string Shortcut(ShortcutAction action)
		{
			ShortcutBinding binding = KeyboardShortcuts.GetBinding(action);
			return binding.IsBound ? binding.ToDisplayString() : string.Empty;
		}
	}
}
