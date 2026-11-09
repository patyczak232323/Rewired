using System;
using System.Linq;
using DLS.Description;
using DLS.Game;
using DLS.Game.Updates;
using DLS.SaveSystem;
using DLS.Simulation;
using Seb.Helpers;
using Seb.Types;
using Seb.Vis;
using Seb.Vis.UI;
using UnityEngine;

namespace DLS.Graphics
{
	public static class MainMenu
	{
		public const int MaxProjectNameLength = 20;
		const bool capitalize = true;

		static MenuScreen activeMenuScreen = MenuScreen.Main;
		static PopupKind activePopup = PopupKind.None;
		static AppSettings EditedAppSettings;

		static readonly UIHandle ID_ProjectNameInput = new("MainMenu_ProjectNameInputField");
		static readonly UIHandle ID_DisplayResolutionWheel = new("MainMenu_DisplayResolutionWheel");
		static readonly UIHandle ID_FullscreenWheel = new("MainMenu_FullscreenWheel");
		static readonly UIHandle ID_ProjectsScrollView = new("MainMenu_ProjectsScrollView");
		static readonly UIHandle ID_KeyBindingsScrollView = new("MainMenu_KeyBindingsScrollView");

		static readonly string[] SettingsWheelFullScreenOptions = { "OFF", "MAXIMIZED", "BORDERLESS", "EXCLUSIVE" };
		static readonly FullScreenMode[] FullScreenModes = { FullScreenMode.Windowed, FullScreenMode.MaximizedWindow, FullScreenMode.FullScreenWindow, FullScreenMode.ExclusiveFullScreen };
		static readonly string[] SettingsWheelVSyncOptions = { "DISABLED", "ENABLED" };
		static readonly string[] SettingsWheelRenderingOptions = { "STANDARD", "GPU (BETA)" };

		static readonly Func<string, bool> projectNameValidator = ProjectNameValidator;
		static readonly UI.ScrollViewDrawContentFunc loadProjectScrollViewDrawer = DrawAllProjectsInScrollView;
		static readonly UI.ScrollViewDrawContentFunc keyBindingsScrollViewDrawer = DrawKeyBindingsScrollView;
		static readonly ShortcutAction[] configurableShortcutActions = (ShortcutAction[])Enum.GetValues(typeof(ShortcutAction));
		static int shortcutCaptureIndex = -1;


		static readonly string[] menuButtonNames =
		{
			FormatButtonString("New Project"),
			FormatButtonString("Open Project"),
			FormatButtonString("Settings"),
			FormatButtonString("About"),
			FormatButtonString("Quit")
		};

		static readonly string[] openProjectButtonNames =
		{
			FormatButtonString("Back"),
			FormatButtonString("Delete"),
			FormatButtonString("Duplicate"),
			FormatButtonString("Rename"),
			FormatButtonString("Open")
		};

		static readonly Vector2Int[] Resolutions =
		{
			new(960, 540),
			new(1280, 720),
			new(1920, 1080),
			new(2560, 1440)
		};

		static readonly string[] ResolutionNames = Resolutions.Select(r => ResolutionToString(r)).ToArray();
		static readonly string[] FullScreenResName = Resolutions.Select(r => ResolutionToString(Main.FullScreenResolution)).ToArray();
		static readonly string[] settingsButtonGroupNames = { "EXIT", "APPLY" };
		static readonly bool[] settingsButtonGroupStates = new bool[settingsButtonGroupNames.Length];

		static readonly bool[] openProjectButtonStates = new bool[openProjectButtonNames.Length];

		static ProjectDescription[] allProjectDescriptions;
		static string[] allProjectNames;
		static (bool compatible, string message)[] projectCompatibilities;
		static string[] projectWarnings;
		static string[] projectSummaries;
		static float lastProjectClickTime = -1;
		static bool openProjectRequested;

		static int selectedProjectIndex;

		static readonly string authorString = "Rewired by: @patyczak232323";
		static readonly string versionString = $"Rewired v{Main.RewiredVersion} ({Main.RewiredLastUpdatedString})";
		static string SelectedProjectName => allProjectDescriptions[selectedProjectIndex].ProjectName;

		static string FormatButtonString(string s) => capitalize ? s.ToUpper() : s;

		public static void Draw()
		{
			RewiredEngine.UpdatePaused();
			
			if (KeyboardShortcuts.CancelShortcutTriggered && activePopup == PopupKind.None)
			{
				if (activeMenuScreen == MenuScreen.KeyBindings && shortcutCaptureIndex >= 0)
				{
					shortcutCaptureIndex = -1;
				}
				else if (activeMenuScreen == MenuScreen.KeyBindings)
				{
					activeMenuScreen = MenuScreen.Settings;
				}
				else
				{
					BackToMain();
				}
			}

			UI.DrawFullscreenPanel(ColHelper.MakeCol255(29, 30, 34));

			// Branding belongs only to the landing screen. Submenus need the full
			// canvas so the large Rewired title cannot overlap their content.
			if (activeMenuScreen == MenuScreen.Main)
			{
				DrawBranding();
				DrawVersionInfo();
			}

			switch (activeMenuScreen)
			{
				case MenuScreen.Main:
					DrawMainScreen();
					break;
				case MenuScreen.LoadProject:
					DrawLoadProjectScreen();
					break;
				case MenuScreen.Settings:
					DrawSettingsScreen();
					break;
				case MenuScreen.KeyBindings:
					DrawKeyBindingsScreen();
					break;
				case MenuScreen.About:
					DrawAboutScreen();
					break;
			}

			switch (activePopup)
			{
				case PopupKind.DeleteConfirmation:
					DrawDeleteProjectConfirmationPopup();
					break;
				case PopupKind.NamePopup_RenameProject:
					DrawNamePopup();
					break;
				case PopupKind.NamePopup_DuplicateProject:
					DrawNamePopup();
					break;
				case PopupKind.NamePopup_NewProject:
					DrawNamePopup();
					break;
			}
		}

		public static void OnMenuOpened()
		{
			activeMenuScreen = MenuScreen.Main;
			activePopup = PopupKind.None;
			selectedProjectIndex = -1;
			RefreshLoadedProjects();
		}

		static void DrawBranding()
		{
			const string title = "REWIRED";
			const string subtitle = "DIGITAL LOGIC SIMULATOR";
			const float titleFontSize = 18.0f;
			const float subtitleFontSize = 3.5f;
			const float titleHeight = 24.5f;
			const float shaddowOffset = -0.33f;
			Color shadowCol = ColHelper.MakeCol255(87, 94, 230);
			Color subtitleCol = new(1, 1, 1, 0.55f);

			// Logo to the left of the title, the two centred together. The logo's square has room
			// round the chip for its pins, so it is sized and spaced by the chip as drawn.
			Vector2 titleSize = UI.CalculateTextSize(title, titleFontSize, FontType.Born2bSporty);
			float logoVisible = titleSize.y * 0.92f;
			float logoSize = logoVisible / RewiredLogo.DrawnFraction;
			const float logoGap = 2.4f;
			float titleShift = (logoVisible + logoGap) / 2;
			Vector2 titleTop = UI.Centre + new Vector2(titleShift, titleHeight);
			RewiredLogo.Draw(titleTop + new Vector2(-titleSize.x / 2 - logoGap - logoVisible / 2, -titleSize.y / 2), logoSize);

			UI.DrawText(title, FontType.Born2bSporty, titleFontSize, titleTop + Vector2.up * shaddowOffset, Anchor.CentreTop, shadowCol);
			UI.DrawText(title, FontType.Born2bSporty, titleFontSize, titleTop, Anchor.CentreTop, Color.white);

			// Under the title, clear of its shadow.
			Vector2 subtitleTop = titleTop + Vector2.down * (titleSize.y + 1.6f);
			UI.DrawText(subtitle, FontType.Born2bSporty, subtitleFontSize, subtitleTop, Anchor.CentreTop, subtitleCol);
		}

		// The examples that come with Rewired, with a line on what each one is.
		static readonly (string name, string about)[] examples =
		{
			("Snake", "a game on a pixel screen, played with W A S D"),
			("CPU8", "an 8-bit CPU with its own ROM and RAM"),
			("Core8", "an 8-bit processor core built from NAND gates")
		};

		const int RecentProjectCount = 5;

		static void DrawMainScreen()
		{
			if (activePopup != PopupKind.None) return;

			DrawSettings.UIThemeDLS theme = DrawSettings.ActiveUITheme;
			const float buttonWidth = 15;
			const float columnGap = 4;
			const float cardWidth = 40;

			// The buttons on the left, recent projects and the examples on the right; the two
			// centred together under the title.
			float groupLeft = UI.Centre.x - (buttonWidth + columnGap + cardWidth) / 2;
			Vector2 firstButtonCentre = new(groupLeft + buttonWidth / 2, UI.Centre.y + 6);
			int buttonIndex = UI.VerticalButtonGroup(menuButtonNames, theme.MainMenuButtonTheme, firstButtonCentre, new Vector2(buttonWidth, 0), false, true, 1);
			DrawStartCard(new Vector2(groupLeft + buttonWidth + columnGap, firstButtonCentre.y + 1.35f), cardWidth);

			if (buttonIndex == 0 || KeyboardShortcuts.MainMenu_NewProjectShortcutTriggered) // New project
			{
				RefreshLoadedProjects();
				activePopup = PopupKind.NamePopup_NewProject;
			}
			else if (buttonIndex == 1 || KeyboardShortcuts.MainMenu_OpenProjectShortcutTriggered) // Load project
			{
				RefreshLoadedProjects();
				selectedProjectIndex = -1;
				activeMenuScreen = MenuScreen.LoadProject;
			}
			else if (buttonIndex == 2 || KeyboardShortcuts.MainMenu_SettingsShortcutTriggered) // Settings
			{
				EditedAppSettings = Main.ActiveAppSettings;
				activeMenuScreen = MenuScreen.Settings;
				OnSettingsMenuOpened();
			}
			else if (buttonIndex == 3) // About
			{
				activeMenuScreen = MenuScreen.About;
			}
			else if (buttonIndex == 4 || KeyboardShortcuts.MainMenu_QuitShortcutTriggered) // Quit
			{
				Quit();
			}
		}

		static void DrawStartCard(Vector2 topLeft, float width)
		{
			DrawSettings.UIThemeDLS theme = DrawSettings.ActiveUITheme;
			const float rowHeight = 2.5f;
			const float textPad = 1.2f;
			if (allProjectDescriptions == null) RefreshLoadedProjects();

			Bounds2D header = RewiredUI.DrawSectionHeader("RECENT PROJECTS", topLeft, width, false);
			float y = header.Bottom - 0.4f;

			int[] recent = Enumerable.Range(0, allProjectDescriptions.Length)
				.OrderByDescending(i => LastUsed(allProjectDescriptions[i]))
				.Take(RecentProjectCount)
				.ToArray();

			if (recent.Length == 0)
			{
				RewiredUI.DrawLabel("No projects yet. Start one with NEW PROJECT, or open an example.", new Vector2(topLeft.x + textPad, y - rowHeight / 2), false, 0.55f, RewiredUI.DimText);
				y -= rowHeight;
			}

			foreach (int i in recent)
			{
				ProjectDescription desc = allProjectDescriptions[i];
				bool compatible = projectCompatibilities[i].compatible;
				ButtonTheme rowTheme = theme.ProjectSelectionButton;
				if (!compatible) rowTheme.textCols.normal.a = 0.5f;

				if (UI.Button(desc.ProjectName, rowTheme, new Vector2(topLeft.x, y), new Vector2(width, rowHeight), compatible, false, false, Anchor.TopLeft, true, textPad))
				{
					Main.CreateOrLoadProject(desc.ProjectName, string.Empty);
					return;
				}

				Bounds2D row = UI.PrevBounds;
				UI.DrawText(projectSummaries[i], theme.FontRegular, theme.FontSizeRegular * 0.62f, row.CentreRight + Vector2.left * textPad, Anchor.TextCentreRight, new Color(1, 1, 1, 0.45f));
				UI.OverridePreviousBounds(row);
				y -= rowHeight + 0.15f;
			}

			y -= 1.2f;
			header = RewiredUI.DrawSectionHeader("EXAMPLES", new Vector2(topLeft.x, y), width, false);
			y = header.Bottom - 0.4f;

			foreach ((string name, string about) in examples)
			{
				if (UI.Button(name, theme.ProjectSelectionButton, new Vector2(topLeft.x, y), new Vector2(width, rowHeight), true, false, false, Anchor.TopLeft, true, textPad))
				{
					OpenExample(name);
					return;
				}

				Bounds2D row = UI.PrevBounds;
				UI.DrawText(about, theme.FontRegular, theme.FontSizeRegular * 0.62f, row.CentreRight + Vector2.left * textPad, Anchor.TextCentreRight, new Color(1, 1, 1, 0.45f));
				UI.OverridePreviousBounds(row);
				y -= rowHeight + 0.15f;
			}
		}

		static DateTime LastUsed(ProjectDescription desc) => desc.LastSaveTime > desc.CreationTime ? desc.LastSaveTime : desc.CreationTime;

		// Opens an example; one that was deleted is copied back first.
		static void OpenExample(string name)
		{
			if (!Loader.ProjectExists(name) && !Main.InstallBundledProject(name)) return;
			Main.CreateOrLoadProject(name, string.Empty);
		}

		static void DrawLoadProjectScreen()
		{
			const int backButtonIndex = 0;
			const int deleteButtonIndex = 1;
			const int duplicateButtonIndex = 2;
			const int renameButtonIndex = 3;
			const int openButtonIndex = 4;
			DrawSettings.UIThemeDLS theme = DrawSettings.ActiveUITheme;

			Vector2 pos = UI.Centre + new Vector2(0, -1);
			Vector2 size = new(68, 32);
			Vector2 headerTopLeft = new(pos.x - size.x / 2f, pos.y + size.y / 2f + 3.2f);
			RewiredUI.DrawSectionHeader("LOAD PROJECT", headerTopLeft, size.x, true);

			UI.DrawScrollView(ID_ProjectsScrollView, pos, size, Anchor.Centre, theme.ScrollTheme, loadProjectScrollViewDrawer);
			ButtonTheme buttonTheme = DrawSettings.ActiveUITheme.MainMenuButtonTheme;

			bool projectSelected = selectedProjectIndex >= 0 && selectedProjectIndex < allProjectDescriptions.Length;
			bool compatibleProject = projectSelected && projectCompatibilities[selectedProjectIndex].compatible;

			for (int i = 0; i < openProjectButtonStates.Length; i++)
			{
				bool buttonEnabled = activePopup == PopupKind.None && (compatibleProject || i == backButtonIndex || (i == deleteButtonIndex && projectSelected));
				openProjectButtonStates[i] = buttonEnabled;
			}

			Vector2 buttonRegionPos = UI.PrevBounds.BottomLeft + Vector2.down * DrawSettings.VerticalButtonSpacing;
			int buttonIndex = UI.HorizontalButtonGroup(openProjectButtonNames, openProjectButtonStates, buttonTheme, buttonRegionPos, UI.PrevBounds.Width, UILayoutHelper.DefaultSpacing, 0, Anchor.TopLeft);

			if (projectSelected && !compatibleProject)
			{
				Vector2 errorMessagePos = UI.PrevBounds.BottomLeft + Vector2.down * (DrawSettings.DefaultButtonSpacing * 2);
				UI.DrawText(projectCompatibilities[selectedProjectIndex].message, buttonTheme.font, buttonTheme.fontSize, errorMessagePos, Anchor.TopLeft, Color.yellow);
			}
			else if (projectSelected && projectWarnings[selectedProjectIndex].Length > 0)
			{
				Vector2 warningMessagePos = UI.PrevBounds.BottomLeft + Vector2.down * (DrawSettings.DefaultButtonSpacing * 2);
				UI.DrawText(projectWarnings[selectedProjectIndex], buttonTheme.font, buttonTheme.fontSize, warningMessagePos, Anchor.TopLeft, Color.yellow);
			}

			// ---- Handle button input ----
			if (buttonIndex == backButtonIndex) BackToMain();
			else if (buttonIndex == deleteButtonIndex) activePopup = PopupKind.DeleteConfirmation;
			else if (buttonIndex == duplicateButtonIndex) activePopup = PopupKind.NamePopup_DuplicateProject;
			else if (buttonIndex == renameButtonIndex) activePopup = PopupKind.NamePopup_RenameProject;
			else if (buttonIndex == openButtonIndex || (compatibleProject && activePopup == PopupKind.None && (openProjectRequested || KeyboardShortcuts.ConfirmShortcutTriggered)))
			{
				openProjectRequested = false;
				Main.CreateOrLoadProject(SelectedProjectName, string.Empty);
			}

			openProjectRequested = false;
		}

		static bool ProjectNameValidator(string inputString) => inputString.Length <= 20 && !SaveUtils.NameContainsForbiddenChar(inputString);

		// Each row: the name on the left, its chip count and last save on the right. A double click
		// opens the project.
		static void DrawAllProjectsInScrollView(Vector2 topLeft, float width, bool isLayoutPass)
		{
			const float spacing = 0;
			const float textPad = 1.2f;
			bool enabled = activePopup == PopupKind.None;
			DrawSettings.UIThemeDLS theme = DrawSettings.ActiveUITheme;

			for (int i = 0; i < allProjectDescriptions.Length; i++)
			{
				ProjectDescription desc = allProjectDescriptions[i];
				bool selected = i == selectedProjectIndex;
				ButtonTheme buttonTheme = selected ? theme.ProjectSelectionButtonSelected : theme.ProjectSelectionButton;
				if (!projectCompatibilities[i].compatible) buttonTheme.textCols.normal.a = 0.5f;

				bool pressed = UI.Button(desc.ProjectName, buttonTheme, topLeft, new Vector2(width, 0.6f), enabled, false, true, Anchor.TopLeft, true, textPad);
				Bounds2D row = UI.PrevBounds;
				UI.DrawText(projectSummaries[i], theme.FontRegular, theme.FontSizeRegular * 0.72f, row.CentreRight + Vector2.left * textPad, Anchor.TextCentreRight,
					new Color(1, 1, 1, selected ? 0.75f : 0.45f));
				UI.OverridePreviousBounds(row);

				if (pressed && !isLayoutPass)
				{
					bool doubleClick = selectedProjectIndex == i && Time.unscaledTime - lastProjectClickTime < 0.4f;
					selectedProjectIndex = i;
					lastProjectClickTime = Time.unscaledTime;
					if (doubleClick && projectCompatibilities[i].compatible) openProjectRequested = true;
				}

				topLeft = UI.PrevBounds.BottomLeft + Vector2.down * spacing;
			}
		}


		static void RefreshLoadedProjects()
		{
			allProjectDescriptions = Loader.LoadAllProjectDescriptions();
			allProjectNames = allProjectDescriptions.Select(d => d.ProjectName).ToArray();
			projectCompatibilities = allProjectDescriptions.Select(d => CanOpenProject(d)).ToArray();
			projectWarnings = allProjectDescriptions.Select(d => Loader.BuiltinNameConflictMessage(Loader.FindBuiltinNameConflicts(d))).ToArray();
			projectSummaries = allProjectDescriptions.Select(d => ProjectSummary.Describe(d, DateTime.Now)).ToArray();
		}

		static (bool canOpen, string failureReason) CanOpenProject(ProjectDescription projectDescription)
		{
			try
			{
				Main.Version earliestCompatible = Main.Version.Parse(projectDescription.DLSVersion_EarliestCompatible);
				Main.Version currentVersion = Main.DLSVersion;

				// In case project was made with a newer version of the sim, check if this version is able to open it
				bool canOpen = currentVersion.ToInt() >= earliestCompatible.ToInt();
				string failureReason = canOpen ? string.Empty : $"This project requires version {earliestCompatible} or later.";
				return (canOpen, failureReason);
			}
			catch
			{
				Debug.Log("Incompatible project: " + projectDescription.ProjectName);
				return (false, "Unrecognized project format");
			}
		}

		static void BackToMain()
		{
			UI.GetInputFieldState(ID_ProjectNameInput).ClearText();
			activeMenuScreen = MenuScreen.Main;
			activePopup = PopupKind.None;
			RefreshLoadedProjects();
		}


		static void OnSettingsMenuOpened()
		{
			// Automatically select whichever resolution option is closest to current window size
			WheelSelectorState resolutionWheelState = UI.GetWheelSelectorState(ID_DisplayResolutionWheel);
			int closestMatchError = int.MaxValue;
			for (int i = 0; i < Resolutions.Length; i++)
			{
				int matchError = Mathf.Min(Mathf.Abs(Screen.width - Resolutions[i].x), Mathf.Abs(Screen.height - Resolutions[i].y));
				if (matchError < closestMatchError)
				{
					closestMatchError = matchError;
					resolutionWheelState.index = i;
				}
			}

			// Automatically set curr fullscreen mode
			WheelSelectorState fullscreenWheelState = UI.GetWheelSelectorState(ID_FullscreenWheel);
			for (int i = 0; i < FullScreenModes.Length; i++)
			{
				if (Screen.fullScreenMode == FullScreenModes[i])
				{
					fullscreenWheelState.index = i;
					break;
				}
			}
		}

		static void DrawSettingsScreen()
		{
			DrawSettings.UIThemeDLS theme = DrawSettings.ActiveUITheme;

			// Keep enough separation between setting labels and controls, especially
			// for the longer "Keyboard shortcuts" row.
			float regionWidth = 38;
			float labelOriginLeft = UI.Centre.x - regionWidth / 2;
			float elementOriginRight = UI.Centre.x + regionWidth / 2;
			Vector2 wheelSize = new(16, 2.5f);
			Vector2 pos = new(labelOriginLeft, UI.Centre.y + 4);
			using (UI.BeginBoundsScope(true))
			{
				Draw.ID backgroundPanelID = UI.ReservePanel();
				Bounds2D settingsHeader = RewiredUI.DrawSectionHeader("SETTINGS", new Vector2(labelOriginLeft, pos.y + 6f), regionWidth, true);

				// -- Resolution --
				bool resEnabled = EditedAppSettings.fullscreenMode == FullScreenMode.Windowed;
				UI.DrawText("Resolution", theme.FontRegular, theme.FontSizeRegular, pos, Anchor.CentreLeft, Color.white);
				string[] resNames = resEnabled ? ResolutionNames : FullScreenResName;
				int resIndex = UI.WheelSelector(ID_DisplayResolutionWheel, resNames, new Vector2(elementOriginRight, pos.y), wheelSize, theme.OptionsWheel, Anchor.CentreRight, enabled: resEnabled);
				EditedAppSettings.ResolutionX = Resolutions[resIndex].x;
				EditedAppSettings.ResolutionY = Resolutions[resIndex].y;

				// -- Full screen --
				pos += Vector2.down * 4;
				UI.DrawText("Fullscreen", theme.FontRegular, theme.FontSizeRegular, pos, Anchor.CentreLeft, Color.white);
				int fullScreenSettingIndex = UI.WheelSelector(ID_FullscreenWheel, SettingsWheelFullScreenOptions, new Vector2(elementOriginRight, pos.y), wheelSize, theme.OptionsWheel, Anchor.CentreRight);
				EditedAppSettings.fullscreenMode = FullScreenModes[fullScreenSettingIndex];
				pos += Vector2.down * 4;

				// -- Vsync --
				UI.DrawText("VSync", theme.FontRegular, theme.FontSizeRegular, pos, Anchor.CentreLeft, Color.white);
				int vsyncSetting = UI.WheelSelector(EditedAppSettings.VSyncEnabled ? 1 : 0, SettingsWheelVSyncOptions, new Vector2(elementOriginRight, pos.y), wheelSize, theme.OptionsWheel, Anchor.CentreRight);
				EditedAppSettings.VSyncEnabled = vsyncSetting == 1;

				// -- Rendering (experimental GPU cache; not available where shapes are drawn from meshes, e.g. the browser) --
				pos += Vector2.down * 4;
				bool gpuAvailable = Draw.SupportsCachedShapes;
				UI.DrawText("Rendering", theme.FontRegular, theme.FontSizeRegular, pos, Anchor.CentreLeft, Color.white);
				int renderingSetting = UI.WheelSelector(EditedAppSettings.GpuRendering && gpuAvailable ? 1 : 0, SettingsWheelRenderingOptions, new Vector2(elementOriginRight, pos.y), wheelSize, theme.OptionsWheel, Anchor.CentreRight, enabled: gpuAvailable);
				if (gpuAvailable) EditedAppSettings.GpuRendering = renderingSetting == 1;

				// -- Updates --
				pos += Vector2.down * 4;
				UI.DrawText("Check for updates at start", theme.FontRegular, theme.FontSizeRegular, pos, Anchor.CentreLeft, Color.white);
				int updateSetting = UI.WheelSelector(EditedAppSettings.DisableUpdateCheck ? 0 : 1, SettingsWheelVSyncOptions, new Vector2(elementOriginRight, pos.y), wheelSize, theme.OptionsWheel, Anchor.CentreRight);
				EditedAppSettings.DisableUpdateCheck = updateSetting == 0;

				// -- Key bindings --
				pos += Vector2.down * 4;
				UI.DrawText("Keyboard shortcuts", theme.FontRegular, theme.FontSizeRegular, pos, Anchor.CentreLeft, Color.white);
				Vector2 keyBindingsButtonSize = new(14, wheelSize.y);
				if (UI.Button("KEYBINDINGS", theme.MainMenuButtonTheme, new Vector2(elementOriginRight, pos.y), keyBindingsButtonSize, true, false, false, Anchor.CentreRight))
				{
					shortcutCaptureIndex = -1;
					activeMenuScreen = MenuScreen.KeyBindings;
				}

				// Background panel
				UI.ModifyPanel(backgroundPanelID, UI.GetCurrentBoundsScope().Centre, UI.GetCurrentBoundsScope().Size + Vector2.one * 3, RewiredUI.SurfaceRaised);
			}

			Vector2 buttonPos = UI.PrevBounds.BottomLeft + Vector2.down * DrawSettings.VerticalButtonSpacing;
			settingsButtonGroupStates[0] = true;
			settingsButtonGroupStates[1] = true;

			int buttonIndex = UI.HorizontalButtonGroup(settingsButtonGroupNames, settingsButtonGroupStates, theme.MainMenuButtonTheme, buttonPos, UI.PrevBounds.Width, UILayoutHelper.DefaultSpacing, 0, Anchor.TopLeft);

			if (buttonIndex == 0)
			{
				BackToMain();
			}
			else if (buttonIndex == 1)
			{
				Main.SaveAndApplyAppSettings(EditedAppSettings);
			}
		}

		static void DrawKeyBindingsScreen()
		{
			DrawSettings.UIThemeDLS theme = DrawSettings.ActiveUITheme;

			RewiredUI.DrawSectionHeader("KEYBINDINGS", UI.CentreTop + new Vector2(-36f, -3.5f), 72f, true);
			UI.DrawText(
				"Click a binding, then press a key combination. Backspace/Delete clears it.",
				theme.FontRegular,
				theme.FontSizeRegular * 0.72f,
				UI.CentreTop + Vector2.down * 8,
				Anchor.Centre,
				new Color(1, 1, 1, 0.62f));

			Vector2 scrollSize = new(72, 37);
			Vector2 scrollPos = UI.Centre + Vector2.up * 1.5f;
			UI.DrawScrollView(
				ID_KeyBindingsScrollView,
				scrollPos,
				scrollSize,
				Anchor.Centre,
				theme.ScrollTheme,
				keyBindingsScrollViewDrawer);

			Vector2 buttonPos = UI.PrevBounds.BottomLeft + Vector2.down * DrawSettings.VerticalButtonSpacing;
			string[] buttonNames = { "BACK", "RESET DEFAULTS" };
			int buttonIndex = UI.HorizontalButtonGroup(
				buttonNames,
				theme.MainMenuButtonTheme,
				buttonPos,
				UI.PrevBounds.Width,
				UILayoutHelper.DefaultSpacing,
				0,
				Anchor.TopLeft);

			if (buttonIndex == 0)
			{
				shortcutCaptureIndex = -1;
				activeMenuScreen = MenuScreen.Settings;
			}
			else if (buttonIndex == 1)
			{
				EditedAppSettings.ResetKeyBindings();
				shortcutCaptureIndex = -1;
			}

			UI.DrawText(
				"Changes are stored when you press APPLY in Settings.",
				theme.FontRegular,
				theme.FontSizeRegular * 0.66f,
				UI.CentreBottom + Vector2.up * 2.4f,
				Anchor.Centre,
				new Color(1, 1, 1, 0.5f));

			if (shortcutCaptureIndex >= 0) CaptureShortcutInput();
		}

		static void DrawKeyBindingsScrollView(Vector2 topLeft, float width, bool isLayoutPass)
		{
			DrawSettings.UIThemeDLS theme = DrawSettings.ActiveUITheme;
			const float rowHeight = 3.3f;
			const float rowSpacing = 0.45f;
			const float horizontalPad = 0.8f;
			const float bindingWidth = 23f;

			for (int i = 0; i < configurableShortcutActions.Length; i++)
			{
				ShortcutAction action = configurableShortcutActions[i];
				Color rowCol = i % 2 == 0 ? ColHelper.MakeCol255(38) : ColHelper.MakeCol255(43);
				UI.DrawPanel(topLeft, new Vector2(width, rowHeight), RewiredUI.Surface, Anchor.TopLeft);
				Bounds2D rowBounds = UI.PrevBounds;

				UI.DrawText(
					KeyboardShortcuts.GetActionDisplayName(action),
					theme.FontRegular,
					theme.FontSizeRegular * 0.78f,
					rowBounds.CentreLeft + Vector2.right * horizontalPad,
					Anchor.TextCentreLeft,
					Color.white);

				string bindingText = shortcutCaptureIndex == i
					? "PRESS KEYS..."
					: EditedAppSettings.GetBinding(action).ToDisplayString();

				ButtonTheme bindingTheme = theme.MainMenuButtonTheme;
				if (shortcutCaptureIndex == i)
				{
					bindingTheme.buttonCols.normal = bindingTheme.buttonCols.hover;
				}

				if (UI.Button(
					bindingText,
					bindingTheme,
					rowBounds.CentreRight - Vector2.right * horizontalPad,
					new Vector2(bindingWidth, 2.35f),
					true,
					false,
					false,
					Anchor.CentreRight))
				{
					shortcutCaptureIndex = i;
				}

				topLeft += Vector2.down * (rowHeight + rowSpacing);
			}
		}

		static void CaptureShortcutInput()
		{
			if (shortcutCaptureIndex < 0 || shortcutCaptureIndex >= configurableShortcutActions.Length) return;

			ShortcutAction action = configurableShortcutActions[shortcutCaptureIndex];

			if (InputHelper.IsKeyDownThisFrame(KeyCode.Backspace) || InputHelper.IsKeyDownThisFrame(KeyCode.Delete))
			{
				EditedAppSettings.SetBinding(action, new ShortcutBinding(action, KeyCode.None));
				shortcutCaptureIndex = -1;
				return;
			}

			foreach (KeyCode key in (KeyCode[])Enum.GetValues(typeof(KeyCode)))
			{
				if (key == KeyCode.None || IsModifierKey(key) || key.ToString().StartsWith("Mouse", StringComparison.Ordinal)) continue;
				if (!InputHelper.IsKeyDownThisFrame(key)) continue;

				ShortcutBinding binding = new(
					action,
					key,
					InputHelper.CtrlIsHeld,
					InputHelper.ShiftIsHeld,
					InputHelper.AltIsHeld);

				EditedAppSettings.SetBinding(action, binding);
				shortcutCaptureIndex = -1;
				return;
			}
		}

		static bool IsModifierKey(KeyCode key) =>
			key is KeyCode.LeftControl or KeyCode.RightControl or
				KeyCode.LeftShift or KeyCode.RightShift or
				KeyCode.LeftAlt or KeyCode.RightAlt or
				KeyCode.LeftCommand or KeyCode.RightCommand;

		static void DrawNamePopup()
		{
			DrawSettings.UIThemeDLS theme = DrawSettings.ActiveUITheme;

			UI.StartNewLayer();
			UI.DrawFullscreenPanel(theme.MenuBackgroundOverlayCol);

			using (UI.BeginBoundsScope(true))
			{
				Draw.ID panelID = UI.ReservePanel();

				InputFieldTheme inputTheme = theme.ChipNameInputField;

				Vector2 charSize = UI.CalculateTextSize("M", inputTheme.fontSize, inputTheme.font);
				Vector2 padding = new(2, 2);
				Vector2 inputFieldSize = new Vector2(charSize.x * MaxProjectNameLength, charSize.y) + padding * 2;

				string popupTitle = activePopup switch
				{
					PopupKind.NamePopup_NewProject => "NEW PROJECT",
					PopupKind.NamePopup_RenameProject => "RENAME PROJECT",
					PopupKind.NamePopup_DuplicateProject => "DUPLICATE PROJECT",
					_ => "PROJECT"
				};
				Vector2 headerTopLeft = new(UI.Centre.x - inputFieldSize.x / 2f, UI.Centre.y + inputFieldSize.y / 2f + 3.2f);
				RewiredUI.DrawSectionHeader(popupTitle, headerTopLeft, inputFieldSize.x, true);

				InputFieldState state = UI.InputField(ID_ProjectNameInput, inputTheme, UI.Centre, inputFieldSize, "", Anchor.Centre, padding.x, projectNameValidator, true);

				string projectName = state.text;
				bool validProjectName = !string.IsNullOrWhiteSpace(projectName) && SaveUtils.ValidFileName(projectName);
				bool projectNameAlreadyExists = false;
				foreach (string existingProjectName in allProjectNames)
				{
					bool isCurrentProjectCaseOnlyRename =
						activePopup == PopupKind.NamePopup_RenameProject &&
						string.Equals(existingProjectName, SelectedProjectName, StringComparison.Ordinal) &&
						string.Equals(projectName, SelectedProjectName, StringComparison.OrdinalIgnoreCase);

					if (!isCurrentProjectCaseOnlyRename)
					{
						projectNameAlreadyExists |= string.Equals(projectName, existingProjectName, StringComparison.OrdinalIgnoreCase);
					}
				}

				bool canCreateProject = validProjectName && !projectNameAlreadyExists;

				Vector2 buttonsRegionSize = new(inputFieldSize.x, 5);
				Vector2 buttonsRegionCentre = UILayoutHelper.CalculateCentre(UI.PrevBounds.BottomLeft, buttonsRegionSize, Anchor.TopLeft);
				(Vector2 size, Vector2 centre) layoutCancel = UILayoutHelper.HorizontalLayout(2, 0, buttonsRegionCentre, buttonsRegionSize);
				(Vector2 size, Vector2 centre) layoutConfirm = UILayoutHelper.HorizontalLayout(2, 1, buttonsRegionCentre, buttonsRegionSize);

				bool cancelButton = UI.Button("CANCEL", theme.MainMenuButtonTheme, layoutCancel.centre, new Vector2(layoutCancel.size.x, 0), true, false, true);
				bool confirmButton = UI.Button("CONFIRM", theme.MainMenuButtonTheme, layoutConfirm.centre, new Vector2(layoutConfirm.size.x, 0), canCreateProject, false, true);

				if (cancelButton || KeyboardShortcuts.CancelShortcutTriggered)
				{
					state.ClearText();
					activePopup = PopupKind.None;
				}

				if (confirmButton || (canCreateProject && KeyboardShortcuts.ConfirmShortcutTriggered))
				{
					state.ClearText();
					PopupKind kind = activePopup;
					activePopup = PopupKind.None;
					OnNamePopupConfirmed(kind, projectName);
				}

				MenuHelper.DrawReservedMenuPanel(panelID, UI.GetCurrentBoundsScope());
			}
		}

		static void OnNamePopupConfirmed(PopupKind kind, string name)
		{
			if (kind is PopupKind.NamePopup_RenameProject or PopupKind.NamePopup_DuplicateProject)
			{
				if (kind is PopupKind.NamePopup_RenameProject) Saver.RenameProject(SelectedProjectName, name);
				if (kind is PopupKind.NamePopup_DuplicateProject) Saver.DuplicateProject(SelectedProjectName, name);

				RefreshLoadedProjects();
				selectedProjectIndex = 0; // the modified project will now be at top of list
				UI.GetScrollbarState(ID_ProjectsScrollView).scrollY = 0; // scroll to top so selection is visible
			}
			else if (kind is PopupKind.NamePopup_NewProject)
			{
				Main.CreateOrLoadProject(name);
			}
		}

		static void DrawDeleteProjectConfirmationPopup()
		{
			DrawSettings.UIThemeDLS theme = DrawSettings.ActiveUITheme;

			UI.StartNewLayer();
			UI.DrawFullscreenPanel(theme.MenuBackgroundOverlayCol);

			using (UI.BeginBoundsScope(true))
			{
				Draw.ID panelID = UI.ReservePanel();
				const float deleteWidth = 42f;
				RewiredUI.DrawSectionHeader("DELETE PROJECT", UI.Centre + new Vector2(-deleteWidth / 2f, 6.2f), deleteWidth, true);
				UI.DrawText("Are you sure you want to delete this project?", theme.FontRegular, theme.FontSizeRegular * 0.82f, UI.Centre, Anchor.Centre, Color.yellow);

				Vector2 buttonRegionTopLeft = UI.PrevBounds.BottomLeft + Vector2.down * DrawSettings.VerticalButtonSpacing;
				float buttonRegionWidth = UI.PrevBounds.Width;
				int buttonIndex = UI.HorizontalButtonGroup(new[] { "CANCEL", "DELETE" }, theme.MainMenuButtonTheme, buttonRegionTopLeft, buttonRegionWidth, DrawSettings.HorizontalButtonSpacing, 0, Anchor.TopLeft);
				UI.ModifyPanel(panelID, UI.GetCurrentBoundsScope().Centre, UI.GetCurrentBoundsScope().Size + Vector2.one * 2, ColHelper.MakeCol255(37, 37, 43));

				if (buttonIndex == 0) // Cancel
				{
					activePopup = PopupKind.None;
				}
				else if (buttonIndex == 1) // Delete
				{
					Saver.DeleteProject(SelectedProjectName);
					selectedProjectIndex = -1;
					RefreshLoadedProjects();
					activePopup = PopupKind.None;
				}
			}
		}

		static void DrawAboutScreen()
		{
			DrawSettings.UIThemeDLS uiTheme = DrawSettings.ActiveUITheme;
			ButtonTheme buttonTheme = uiTheme.MainMenuButtonTheme;
			Color secondaryTextCol = new(1, 1, 1, 0.65f);

			Vector2 panelCentre = UI.Centre + Vector2.up * 1;
			Vector2 panelSize = new(76, 36);
			UI.DrawPanel(panelCentre, panelSize, RewiredUI.SurfaceRaised, Anchor.Centre);
			RewiredUI.DrawFrame(UI.PrevBounds);

			Vector2 pos = panelCentre + new Vector2(0, 13);
			UI.DrawText("REWIRED", FontType.Born2bSporty, 8f, pos, Anchor.Centre, Color.white);

			pos += Vector2.down * 5;
			UI.DrawText("Independent digital logic simulator", uiTheme.FontRegular, uiTheme.FontSizeRegular, pos, Anchor.Centre, Color.white);
			pos += Vector2.down * 3;
			UI.DrawText($"Version {Main.RewiredVersion} ({Main.RewiredLastUpdatedString})", uiTheme.FontRegular, uiTheme.FontSizeRegular, pos, Anchor.Centre, new Color(1, 1, 1, 0.7f));

			pos += Vector2.down * 4.5f;
			UI.DrawText("Rewired project and simulation engine: @patyczak232323", uiTheme.FontRegular, uiTheme.FontSizeRegular, pos, Anchor.Centre, Color.white);
			pos += Vector2.down * 3;
			UI.DrawText("Based on Digital Logic Sim by Sebastian Lague", uiTheme.FontRegular, uiTheme.FontSizeRegular, pos, Anchor.Centre, Color.white);

			pos += Vector2.down * 4;
			UI.DrawText("Original editor, project format and UI foundation retained", uiTheme.FontRegular, uiTheme.FontSizeRegular, pos, Anchor.Centre, secondaryTextCol);
			pos += Vector2.down * 2.2f;
			UI.DrawText("under the MIT License.", uiTheme.FontRegular, uiTheme.FontSizeRegular, pos, Anchor.Centre, secondaryTextCol);

			pos += Vector2.down * 4;
			UI.DrawText("Rewired uses its own simulation semantics;", uiTheme.FontRegular, uiTheme.FontSizeRegular, pos, Anchor.Centre, secondaryTextCol);
			pos += Vector2.down * 2.2f;
			UI.DrawText("exact DLS behaviour is not guaranteed.", uiTheme.FontRegular, uiTheme.FontSizeRegular, pos, Anchor.Centre, secondaryTextCol);

			if (UI.Button("BACK", buttonTheme, UI.CentreBottom + Vector2.up * 8, new Vector2(15, 0), true, true, true))
			{
				BackToMain();
			}
		}

		static void DrawVersionInfo()
		{
			DrawSettings.UIThemeDLS theme = DrawSettings.ActiveUITheme;
			UI.DrawPanel(UI.BottomLeft, new Vector2(UI.Width, 4), RewiredUI.SurfaceRaised, Anchor.BottomLeft);

			const float pad = 1;
			const float gap = 2;
			Color col = new(1, 1, 1, 0.5f);
			Bounds2D bar = UI.PrevBounds;
			float fontSize = theme.FontSizeRegular;

			// The update status sits between the author (left) and the version (right). On a narrow
			// window the parts would overlap, so the status text goes first, then the author.
			float authorWidth = UI.CalculateTextSize(authorString, fontSize, theme.FontRegular).x;
			float versionWidth = UI.CalculateTextSize(versionString, fontSize, theme.FontRegular).x;
			float versionLeft = bar.Right - pad - versionWidth;

			bool hasUpdater = AppUpdater.Supported;
			string label = hasUpdater ? AppUpdater.ActionLabel : null;
			string status = hasUpdater ? AppUpdater.StatusText ?? string.Empty : string.Empty;
			ButtonTheme buttonTheme = theme.MainMenuButtonTheme;
			const float buttonStatusGap = 1;
			float buttonWidth = label == null ? 0 : ButtonWidth(label, buttonTheme);
			float statusWidth = status.Length == 0 ? 0 : UI.CalculateTextSize(status, fontSize, theme.FontRegular).x;

			bool showAuthor = true;
			bool showStatus = statusWidth > 0;
			float GroupWidth() => buttonWidth + (showStatus ? statusWidth + (buttonWidth > 0 ? buttonStatusGap : 0) : 0);
			float FreeLeft() => showAuthor ? bar.Left + pad + authorWidth + gap : bar.Left + pad;
			float FreeWidth() => versionLeft - gap - FreeLeft();

			if (hasUpdater)
			{
				if (GroupWidth() > FreeWidth() && buttonWidth > 0) showStatus = false;
				if (GroupWidth() > FreeWidth()) showAuthor = false;
			}

			if (showAuthor) UI.DrawText(authorString, theme.FontRegular, fontSize, bar.CentreLeft + Vector2.right * pad, Anchor.TextCentreLeft, col);
			UI.DrawText(versionString, theme.FontRegular, fontSize, bar.CentreRight + Vector2.left * pad, Anchor.TextCentreRight, col);
			if (!hasUpdater || GroupWidth() <= 0) return;

			// Centre the group on the bar when it fits there, otherwise in the free space
			float groupWidth = GroupWidth();
			float groupLeft = bar.Centre.x - groupWidth / 2;
			groupLeft = Mathf.Clamp(groupLeft, FreeLeft(), Mathf.Max(FreeLeft(), versionLeft - gap - groupWidth));
			float y = bar.Centre.y;

			bool available = AppUpdater.Current == AppUpdater.State.Available;
			Color statusCol = available ? RewiredUI.Accent : new Color(1, 1, 1, 0.5f);
			if (showStatus)
			{
				UI.DrawText(status, theme.FontRegular, fontSize, new Vector2(groupLeft, y), Anchor.TextCentreLeft, statusCol);
				groupLeft += statusWidth + (buttonWidth > 0 ? buttonStatusGap : 0);
			}

			if (label != null && UI.Button(label, buttonTheme, new Vector2(groupLeft, y), new Vector2(0, 3f), true, true, false, Anchor.CentreLeft))
			{
				AppUpdater.RunAction();
			}
		}

		// Width of a button sized to its text, as UI.Button computes it
		static float ButtonWidth(string text, ButtonTheme buttonTheme)
		{
			float textWidth = UI.CalculateTextSize(text, buttonTheme.fontSize, buttonTheme.font).x;
			float lineHeight = UI.CalculateTextSize("M", buttonTheme.fontSize, buttonTheme.font).y;
			return textWidth + lineHeight * buttonTheme.paddingScale.x;
		}

		static string ResolutionToString(Vector2Int r) => $"{r.x} x {r.y}";

		static void Quit()
		{
			Application.Quit();
		}

		enum MenuScreen
		{
			Main,
			LoadProject,
			Settings,
			KeyBindings,
			About
		}

		enum PopupKind
		{
			None,
			DeleteConfirmation,
			NamePopup_RenameProject,
			NamePopup_DuplicateProject,
			NamePopup_NewProject
		}
	}
}
