using System;
using System.Collections.Generic;
using System.Linq;
using DLS.Description;
using DLS.Game;
using Seb.Helpers;
using Seb.Types;
using Seb.Vis;
using Seb.Vis.UI;
using UnityEngine;

namespace DLS.Graphics
{
	// Search for everything in the desktop editor (Ctrl+K): type part of a name to add a chip,
	// open one for editing, or run any command. Up and Down choose, Enter runs, Esc closes.
	public static class CommandPalette
	{
		static readonly UIHandle ID_Input = new("CommandPalette_Input");
		const float Width = 50f;
		const float RowHeight = 2.3f;
		const int VisibleRows = 12;

		readonly struct Item
		{
			public readonly string Label;
			public readonly string Kind;
			public readonly string Detail;
			public readonly Action Run;
			public readonly bool Enabled;
			public readonly int Score;

			public Item(string label, string kind, string detail, Action run, bool enabled, int score)
			{
				Label = label;
				Kind = kind;
				Detail = detail;
				Run = run;
				Enabled = enabled;
				Score = score;
			}
		}

		const string CommandKind = "";
		static readonly List<Item> items = new();
		static string query = string.Empty;
		static int selected;
		static int firstVisible;
		static int openedFrame;
		static Vector2 lastMousePos;

		public static void Open()
		{
			Project project = Project.ActiveProject;
			if (project == null) return;
			project.controller.CancelEverything();
			UIDrawer.SetActiveMenu(UIDrawer.MenuType.CommandPalette);
		}

		public static void OnMenuOpened()
		{
			openedFrame = Time.frameCount;
			query = string.Empty;
			selected = 0;
			firstVisible = 0;
			UI.GetInputFieldState(ID_Input).ClearText();
			lastMousePos = InputHelper.MousePos;
			Rebuild();
		}

		public static void DrawMenu()
		{
			MenuHelper.DrawBackgroundOverlay();
			InputFieldTheme inputTheme = MenuHelper.Theme.ChipNameInputField;
			inputTheme.font = MenuHelper.Theme.FontRegular;
			inputTheme.fontSize = 1.25f;
			Vector2 topLeft = new(UI.Centre.x - Width / 2, UI.Height - 6f);

			Draw.ID panelID = UI.ReservePanel();
			Item? toRun = null;
			Bounds2D card;

			using (UI.BeginBoundsScope(true))
			{
				UI.InputField(ID_Input, inputTheme, topLeft, new Vector2(Width, 3f), "Type a part or a command", Anchor.TopLeft, 1.2f, OnTextChanged, true);
				float y = UI.PrevBounds.Bottom - 0.6f;

				HandleKeys();

				if (items.Count == 0)
				{
					DesktopTheme.Text("Nothing matches \"" + query + "\".", new Vector2(topLeft.x + 1f, y - RowHeight / 2), 0.5f, RewiredUI.DimText);
					UI.OverridePreviousBounds(Bounds2D.CreateFromTopLeftAndSize(new Vector2(topLeft.x, y), new Vector2(Width, RowHeight)));
				}

				// The mouse picks a row only once it moves, so a resting pointer does not fight the arrow keys.
				bool mouseMoved = (InputHelper.MousePos - lastMousePos).sqrMagnitude > 1f;
				lastMousePos = InputHelper.MousePos;

				int last = Mathf.Min(items.Count, firstVisible + VisibleRows);
				for (int i = firstVisible; i < last; i++)
				{
					Bounds2D row = Bounds2D.CreateFromTopLeftAndSize(new Vector2(topLeft.x, y), new Vector2(Width, RowHeight));
					bool hovered = UI.MouseInsideBounds(row);
					if (hovered && mouseMoved) selected = i;

					if (DrawRow(items[i], row, i == selected) && Time.frameCount != openedFrame) toRun = items[i];
					y -= RowHeight;
				}

				if (items.Count > last)
				{
					DesktopTheme.Text($"{items.Count - last} more: keep typing or use the arrow keys", new Vector2(topLeft.x + 1f, y - 0.9f), 0.4f, RewiredUI.DimText);
					UI.OverridePreviousBounds(Bounds2D.CreateFromTopLeftAndSize(new Vector2(topLeft.x, y), new Vector2(Width, 1.8f)));
				}

				card = UI.GetCurrentBoundsScope();
				MenuHelper.DrawReservedMenuPanel(panelID, card);
			}

			if (KeyboardShortcuts.ConfirmShortcutTriggered && selected >= 0 && selected < items.Count) toRun = items[selected];

			if (toRun.HasValue && toRun.Value.Enabled)
			{
				UIDrawer.SetActiveMenu(UIDrawer.MenuType.None);
				toRun.Value.Run();
				return;
			}

			// The dimmed board behind counts as interface too, so look at the card itself.
			bool clickedOutside = InputHelper.IsMouseDownThisFrame(MouseButton.Left) && !UI.MouseInsideBounds(Bounds2D.Grow(card, 1f)) && Time.frameCount != openedFrame;
			if (KeyboardShortcuts.CancelShortcutTriggered || clickedOutside || (KeyboardShortcuts.CommandPaletteShortcutTriggered && Time.frameCount != openedFrame))
			{
				UIDrawer.SetActiveMenu(UIDrawer.MenuType.None);
			}
		}

		static bool DrawRow(Item item, Bounds2D row, bool isSelected)
		{
			bool pressed = UI.Button(string.Empty, isSelected ? DesktopTheme.Primary : DesktopTheme.Quiet, row.TopLeft, row.Size, item.Enabled, false, false, Anchor.TopLeft, false, 0, false, false);
			Color textCol = item.Enabled ? Color.white : RewiredUI.DimText;
			Color detailCol = isSelected ? new Color(1, 1, 1, 0.8f) : RewiredUI.DimText;

			// Chips are tagged ADD or EDIT; commands are not tagged.
			const float kindWidth = 4.2f;
			if (item.Kind != CommandKind) DesktopTheme.Text(item.Kind, row.CentreLeft + Vector2.right * 1f, 0.38f, detailCol, Anchor.TextCentreLeft, true);
			float labelLeft = row.Left + 1f + kindWidth;
			float detailWidth = string.IsNullOrEmpty(item.Detail) ? 0 : Mathf.Min(DesktopTheme.TextWidth(item.Detail, 0.44f) + 1f, Width * 0.4f);
			DesktopTheme.Text(DesktopTheme.Fit(item.Label, row.Right - labelLeft - detailWidth - 1f, 0.54f, true), new Vector2(labelLeft, row.Centre.y), 0.54f, textCol, Anchor.TextCentreLeft, true);
			if (detailWidth > 0)
			{
				DesktopTheme.Text(DesktopTheme.Fit(item.Detail, detailWidth - 0.5f, 0.44f), row.CentreRight + Vector2.left * 1f, 0.44f, detailCol, Anchor.TextCentreRight);
			}

			UI.OverridePreviousBounds(row);
			return pressed;
		}

		static void HandleKeys()
		{
			if (items.Count == 0) return;
			if (InputHelper.IsKeyDownThisFrame(KeyCode.DownArrow)) selected++;
			if (InputHelper.IsKeyDownThisFrame(KeyCode.UpArrow)) selected--;
			if (InputHelper.IsKeyDownThisFrame(KeyCode.PageDown)) selected += VisibleRows;
			if (InputHelper.IsKeyDownThisFrame(KeyCode.PageUp)) selected -= VisibleRows;
			selected = Mathf.Clamp(selected, 0, items.Count - 1);

			if (selected < firstVisible) firstVisible = selected;
			if (selected >= firstVisible + VisibleRows) firstVisible = selected - VisibleRows + 1;
			firstVisible = Mathf.Clamp(firstVisible, 0, Mathf.Max(0, items.Count - VisibleRows));
		}

		static bool OnTextChanged(string text)
		{
			// The key that opened the palette is not typed into it.
			if (Time.frameCount == openedFrame) return false;
			if (text.Length > 40) return false;

			query = text;
			selected = 0;
			firstVisible = 0;
			Rebuild();
			return true;
		}

		// ---- Contents ----

		static void Rebuild()
		{
			items.Clear();
			Project project = Project.ActiveProject;
			if (project == null) return;

			bool canEdit = project.CanEditViewedChip;
			ChipInteractionController controller = project.controller;
			UndoController undo = project.ViewedChip.UndoController;
			bool searching = !string.IsNullOrWhiteSpace(query);

			// Commands
			AddCommand("Add a part: find a chip by name", ShortcutAction.Search, () => UIDrawer.SetActiveMenu(UIDrawer.MenuType.Search), canEdit);
			AddCommand("New chip", ShortcutAction.CreateNewChip, BottomBarUI.CreateNewChip, canEdit);
			AddCommand("Save chip", ShortcutAction.Save, () => UIDrawer.SetActiveMenu(UIDrawer.MenuType.ChipSave), canEdit);
			AddCommand(project.simPaused ? "Run the simulation" : "Pause the simulation", ShortcutAction.SimulationPause, () =>
			{
				project.description.Prefs_SimPaused = !project.simPaused;
				project.SaveCurrentProjectDescription();
			}, true);
			AddCommand("Simulation step", ShortcutAction.SimulationNextStep, () => project.advanceSingleSimStep = true, project.simPaused);
			AddCommand("Undo", ShortcutAction.Undo, () => undo.TryUndo(), canEdit && undo.CanUndo);
			AddCommand("Redo", ShortcutAction.Redo, () => undo.TryRedo(), canEdit && undo.CanRedo);
			AddCommand("Select all", ShortcutAction.SelectAll, controller.SelectAll, canEdit);
			AddCommand("Copy selection", ShortcutAction.Copy, controller.CopySelection, controller.CanCopy);
			AddCommand("Paste", ShortcutAction.Paste, controller.Paste, canEdit && ChipInteractionController.ClipboardHasContent);
			AddCommand("Duplicate selection", ShortcutAction.Duplicate, controller.DuplicateSelection, canEdit && controller.SelectedElements.Count > 0);
			AddCommandWithDetail("Delete selection", null, "Delete", controller.DeleteSelection, canEdit && controller.SelectedElements.Count > 0);
			AddCommand("Fit the chip to the screen", ShortcutAction.ResetCamera, CameraController.FitViewToChip, true);
			AddCommand("Show or hide the grid", ShortcutAction.ToggleGrid, () =>
			{
				project.ToggleGridDisplay();
				project.SaveCurrentProjectDescription();
			}, true);
			AddCommand(DesktopLayout.PartsPanelOpen ? "Hide the parts panel" : "Show the parts panel", ShortcutAction.TogglePartsPanel, DesktopLayout.TogglePartsPanel, canEdit);
			AddCommand(DesktopLayout.InspectorOpen ? "Hide the details panel" : "Show the details panel", ShortcutAction.ToggleInspector, DesktopLayout.ToggleInspector, true);
			AddCommand("Chip library", ShortcutAction.ChipLibrary, () => UIDrawer.SetActiveMenu(UIDrawer.MenuType.ChipLibrary), canEdit);
			AddCommandWithDetail("RHDL Studio: describe a chip in words", null, null, () => UIDrawer.SetActiveMenu(UIDrawer.MenuType.RhdlStudio), true);
			AddCommandWithDetail("Simulation diagnostics", null, null, () => UIDrawer.SetActiveMenu(UIDrawer.MenuType.SimulationDiagnostics), true);
			AddCommandWithDetail("Export this chip", null, null, () => ExportMenu.Open(project.ActiveDevChipName), true);
			AddCommand("Preferences", ShortcutAction.Preferences, () => UIDrawer.SetActiveMenu(UIDrawer.MenuType.Preferences), true);
			AddCommand("Keyboard shortcuts", ShortcutAction.ShortcutsHelp, () => UIDrawer.SetActiveMenu(UIDrawer.MenuType.ShortcutsHelp), true);
			AddCommand("Back to the main menu", ShortcutAction.QuitToMainMenu, BottomBarUI.ExitToMainMenu, true);

			// Chips: added when typed, and the ones used lately before anything is typed.
			foreach (ChipDescription chip in project.chipLibrary.allChips)
			{
				int score = SearchRanking.Score(chip.Name, query);
				if (score == SearchRanking.NoMatch) continue;
				if (!searching && !SearchPopup.IsRecentChip(chip.Name)) continue;

				string name = chip.Name;
				bool canAdd = canEdit && project.ViewedChip.CanAddSubchip(name);
				if (!searching && !canAdd) continue;
				items.Add(new Item(name, "ADD", MobilePalette.Describe(project, name), () => SearchPopup.UseChip(name), canAdd, score + 50));

				// Saved chips can also be opened for editing.
				if (searching && chip.ChipType == ChipType.Custom)
				{
					items.Add(new Item(name, "EDIT", "open it for editing", () => SearchPopup.OpenChip(name), true, score - 1));
				}
			}

			// Best matches first, what cannot be done now last; before anything is typed, commands
			// keep their order after the recent chips.
			if (searching)
			{
				List<Item> sorted = items.OrderByDescending(i => i.Enabled).ThenByDescending(i => i.Score).ToList();
				items.Clear();
				items.AddRange(sorted);
			}
			else
			{
				List<Item> chips = items.FindAll(i => i.Kind == "ADD");
				items.RemoveAll(i => i.Kind == "ADD");
				items.InsertRange(0, chips);
			}

			void AddCommand(string label, ShortcutAction? shortcut, Action run, bool enabled) =>
				AddCommandWithDetail(label, shortcut, null, run, enabled);

			void AddCommandWithDetail(string label, ShortcutAction? shortcut, string detail, Action run, bool enabled)
			{
				int score = SearchRanking.Score(label, query);
				if (score == SearchRanking.NoMatch) return;
				if (shortcut.HasValue)
				{
					ShortcutBinding binding = KeyboardShortcuts.GetBinding(shortcut.Value);
					if (binding.IsBound) detail = binding.ToDisplayString();
				}

				// Before anything is typed, only what can be done now is listed.
				if (!searching && !enabled) return;
				items.Add(new Item(label, CommandKind, detail, run, enabled, score));
			}
		}
	}
}
