using DLS.Game;
using Seb.Vis.UI;

namespace DLS.Graphics
{
	public static class UIDrawer
	{
		public enum MenuType
		{
			None,
			ChipSave,
			ChipLibrary,
			BottomBarMenuPopup,
			ChipCustomization,
			Preferences,
			SimulationDiagnostics,
			RhdlStudio,
			PinRename,
			MainMenu,
			RebindKeyChip,
			RomEdit,
			PulseEdit,
			UnsavedChanges,
			Search,
			ChipLabelPopup,
			TruthTable,
			Export,
			Breakpoint,
			MobileHelp,
			CommandPalette,
			ShortcutsHelp
		}

		static MenuType activeMenuOld;

		public static MenuType ActiveMenu { get; private set; }

		public static void Draw()
		{
			NotifyIfActiveMenuChanged();
			ExportMenu.Update();

			UI.TouchScrolling = MobileUI.IsActive;
			if (MobileUI.IsActive)
			{
				using (UI.CreateUIScope(MobileUI.UIWidth))
				{
					MobileUI.Draw();
				}
			}
			else
			{
				using (UI.CreateFixedAspectUIScope(drawLetterbox: true))
				{
					if (ActiveMenu is MenuType.MainMenu)
					{
						DrawAppMenus();
					}
					else
					{
						DrawProjectMenus(Project.ActiveProject);
					}
				}
			}

			InteractionState.MouseIsOverUI = UI.IsMouseOverUIThisFrame;
		}

		static void DrawAppMenus()
		{
			MainMenu.Draw();
		}

		static void DrawProjectMenus(Project project)
		{
			MenuType menuToDraw = ActiveMenu; // cache state in case it changes while drawing/updating the menus

			// The editor's bars and side panels; full-screen tools cover them.
			bool drawEditorChrome = menuToDraw is not MenuType.ChipCustomization and not MenuType.SimulationDiagnostics and not MenuType.RhdlStudio;
			if (drawEditorChrome)
			{
				DesktopPartsPanel.Draw(project);
				DesktopInspector.Draw(project);
				// Text is drawn above the shapes of its layer, so whatever can open over the side
				// panels (the bottom bar's collections, the menu, other menus) starts a new layer.
				UI.StartNewLayer();
				BottomBarUI.DrawUI(project);
				DesktopToolbar.Draw(project);
				UI.StartNewLayer();
				BottomBarUI.DrawMenuPopup();
				HandleDesktopShortcuts(project);
			}

			if (menuToDraw == MenuType.ChipSave) ChipSaveMenu.DrawMenu();
			else if (menuToDraw == MenuType.ChipLibrary) ChipLibraryMenu.DrawMenu();
			else if (menuToDraw == MenuType.ChipCustomization) ChipCustomizationMenu.DrawMenu();
			else if (menuToDraw == MenuType.Preferences) PreferencesMenu.DrawMenu(project);
			else if (menuToDraw == MenuType.SimulationDiagnostics) SimulationDiagnosticsMenu.DrawMenu();
			else if (menuToDraw == MenuType.RhdlStudio) RhdlStudioMenu.DrawMenu();
			else if (menuToDraw == MenuType.PinRename) PinEditMenu.DrawMenu();
			else if (menuToDraw == MenuType.RebindKeyChip) RebindKeyChipMenu.DrawMenu();
			else if (menuToDraw == MenuType.RomEdit) RomEditMenu.DrawMenu();
			else if (menuToDraw == MenuType.UnsavedChanges) UnsavedChangesPopup.DrawMenu();
			else if (menuToDraw == MenuType.Search) SearchPopup.DrawMenu();
			else if (menuToDraw == MenuType.ChipLabelPopup) ChipLabelMenu.DrawMenu();
			else if (menuToDraw == MenuType.PulseEdit) PulseEditMenu.DrawMenu();
			else if (menuToDraw == MenuType.TruthTable) TruthTableMenu.DrawMenu();
			else if (menuToDraw == MenuType.Export) ExportMenu.DrawMenu();
			else if (menuToDraw == MenuType.Breakpoint) BreakpointMenu.DrawMenu();
			else if (menuToDraw == MenuType.CommandPalette) CommandPalette.DrawMenu();
			else if (menuToDraw == MenuType.ShortcutsHelp) ShortcutsHelpMenu.DrawMenu();

			ContextMenu.Update();
			if (drawEditorChrome)
			{
				UI.StartNewLayer();
				DesktopTheme.DrawHint();
			}
		}

		// Shortcuts of the desktop editor's own bars and panels.
		static void HandleDesktopShortcuts(Project project)
		{
			if (ActiveMenu is not (MenuType.None or MenuType.BottomBarMenuPopup)) return;

			if (KeyboardShortcuts.CommandPaletteShortcutTriggered) CommandPalette.Open();
			else if (KeyboardShortcuts.ShortcutsHelpShortcutTriggered) SetActiveMenu(MenuType.ShortcutsHelp);
			else if (KeyboardShortcuts.TogglePartsPanelShortcutTriggered && project.CanEditViewedChip) DesktopLayout.TogglePartsPanel();
			else if (KeyboardShortcuts.ToggleInspectorShortcutTriggered) DesktopLayout.ToggleInspector();
		}

		public static bool InInputBlockingMenu() => !(ActiveMenu is MenuType.None or MenuType.BottomBarMenuPopup or MenuType.ChipCustomization);

		static void NotifyIfActiveMenuChanged()
		{
			// UI Changed -- notify opened
			if (ActiveMenu != activeMenuOld)
			{
				if (activeMenuOld == MenuType.ChipCustomization) CustomizationSceneDrawer.OnCustomizationMenuClosed();

				if (ActiveMenu == MenuType.ChipSave) ChipSaveMenu.OnMenuOpened();
				else if (ActiveMenu == MenuType.ChipLibrary) ChipLibraryMenu.OnMenuOpened();
				else if (ActiveMenu == MenuType.ChipCustomization) ChipCustomizationMenu.OnMenuOpened();
				else if (ActiveMenu == MenuType.PinRename) PinEditMenu.OnMenuOpened();
				else if (ActiveMenu == MenuType.Preferences) PreferencesMenu.OnMenuOpened();
				else if (ActiveMenu == MenuType.SimulationDiagnostics) SimulationDiagnosticsMenu.OnMenuOpened();
				else if (ActiveMenu == MenuType.RhdlStudio) RhdlStudioMenu.OnMenuOpened();
				else if (ActiveMenu == MenuType.MainMenu)
				{
					MainMenu.OnMenuOpened();
					if (MobileUI.IsActive) MobileUI.OnMainMenuOpened();
				}
				else if (ActiveMenu == MenuType.RebindKeyChip) RebindKeyChipMenu.OnMenuOpened();
				else if (ActiveMenu == MenuType.RomEdit) RomEditMenu.OnMenuOpened();
				else if (ActiveMenu == MenuType.Search) SearchPopup.OnMenuOpened();
				else if (ActiveMenu == MenuType.ChipLabelPopup) ChipLabelMenu.OnMenuOpened();
				else if (ActiveMenu == MenuType.PulseEdit) PulseEditMenu.OnMenuOpened();
				else if (ActiveMenu == MenuType.TruthTable) TruthTableMenu.OnMenuOpened();
				else if (ActiveMenu == MenuType.Export) ExportMenu.OnMenuOpened();
				else if (ActiveMenu == MenuType.Breakpoint) BreakpointMenu.OnMenuOpened();
				else if (ActiveMenu == MenuType.CommandPalette) CommandPalette.OnMenuOpened();

				if (InInputBlockingMenu() && Project.ActiveProject != null && Project.ActiveProject.controller != null)
				{
					Project.ActiveProject.controller.CancelEverything();
				}

				activeMenuOld = ActiveMenu;
			}
		}

		public static void ToggleBottomPopupMenu()
		{
			SetActiveMenu(ActiveMenu is MenuType.None ? MenuType.BottomBarMenuPopup : MenuType.None);
		}

		public static void SetActiveMenu(MenuType type)
		{
			ActiveMenu = type;
		}

		public static void Reset()
		{
			SetActiveMenu(MenuType.None);
			activeMenuOld = MenuType.None;
			ContextMenu.Reset();
			UI.ResetAllStates();
			BottomBarUI.Reset();
			ChipSaveMenu.Reset();
			RomEditMenu.Reset();
			ChipLibraryMenu.Reset();
			SearchPopup.Reset();
			DesktopPartsPanel.Reset();
			MobileUI.Reset();
		}
	}
}
