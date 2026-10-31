using System;
using System.Linq;
using System.Text;
using DLS.Description;
using DLS.Game;
using DLS.RHDL;
using DLS.SaveSystem;
using DLS.Simulation;
using Seb.Helpers;
using Seb.Helpers.InputHandling;
using Seb.Types;
using Seb.Vis;
using Seb.Vis.UI;
using UnityEngine;

namespace DLS.Graphics
{
	public static class RhdlStudioMenu
	{
		const float WorkspaceWidth = 88f;
		const float LeftWidth = 56f;
		const float Gap = 1.2f;
		const float RightWidth = WorkspaceWidth - LeftWidth - Gap;
		const float RowHeight = 1.85f;

		static readonly RhdlCodeEditor CodeEditor = new();

		static RhdlCompileResult latestResult;
		static string statusText = "Ready.";
		static bool statusSuccess;
		static bool buildAttempted;
		static int lastBuildSubChipCount;
		static int lastBuildWireCount;
		static string requestedSourceChipName;
		static bool mobileInfoOpen;

		public static void OpenSource(string chipName)
		{
			requestedSourceChipName = chipName;
			UIDrawer.SetActiveMenu(UIDrawer.MenuType.RhdlStudio);
		}

		public static void OnMenuOpened()
		{
			Project project = Project.ActiveProject;
			if (project == null) return;

			string source = string.Empty;
			if (!string.IsNullOrWhiteSpace(requestedSourceChipName))
			{
				source = RhdlSourceStore.LoadChipSource(project.description.ProjectName, requestedSourceChipName);
			}
			if (string.IsNullOrWhiteSpace(source)) source = RhdlSourceStore.LoadDraft(project.description.ProjectName);
			bool loadedTemplate = string.IsNullOrWhiteSpace(source);
			if (loadedTemplate) source = StarterTemplate;

			// Sources saved with the RHDL 1.0 syntax open converted to RHDL 2.0.
			bool upgraded = !loadedTemplate && RhdlUpgrade.IsLegacySource(source) && RhdlUpgrade.TryUpgrade(source, out source, out _);
			requestedSourceChipName = null;
			SetEditorSource(source);
			if (loadedTemplate || upgraded) CodeEditor.MarkDirty();
			statusText = loadedTemplate
				? "New chip template. Rename it and start writing."
				: upgraded
					? "This source was written for RHDL 1.0 and has been converted to RHDL 2.0. BUILD saves the new version."
					: "Ready. Edit the source and press BUILD.";
			statusSuccess = false;
			buildAttempted = false;
			latestResult = null;
			CodeEditor.SetDiagnosticLines(Array.Empty<int>());
			mobileInfoOpen = false;
		}

		public static void DrawMenu()
		{
			Project project = Project.ActiveProject;
			if (project == null)
			{
				UIDrawer.SetActiveMenu(UIDrawer.MenuType.None);
				return;
			}

			DrawSettings.UIThemeDLS theme = DrawSettings.ActiveUITheme;
			MenuHelper.DrawBackgroundOverlay();

			if (MobileUI.IsActive)
			{
				DrawMobileMenu(theme, project);
				return;
			}

			Vector2 topLeft = UI.Centre + new Vector2(-WorkspaceWidth / 2f, 26.2f);
			Bounds2D mainHeader = RewiredUI.DrawSectionHeader(
				"REWIRED / RHDL STUDIO",
				topLeft,
				WorkspaceWidth,
				true,
				"RHDL 2.0  /  CHIPS IN PLAIN WORDS");

			Vector2 contentTop = mainHeader.BottomLeft + Vector2.down * 0.7f;
			Vector2 sourceTop = contentTop;
			Vector2 infoTop = contentTop + Vector2.right * (LeftWidth + Gap);

			string sourceInfo = $"Ln {CodeEditor.CaretLine}, Col {CodeEditor.CaretColumn}  /  {CodeEditor.LineCount} lines" +
			                    (CodeEditor.IsDirty ? "  /  MODIFIED" : string.Empty);
			Bounds2D sourceHeader = RewiredUI.DrawSectionHeader(
				CodeEditor.IsDirty ? "SOURCE *" : "SOURCE",
				sourceTop,
				LeftWidth,
				false,
				sourceInfo);
			Vector2 sourceBodyTop = sourceHeader.BottomLeft;
			Vector2 sourceSize = new(LeftWidth, 39.5f);
			RewiredUI.DrawCard(sourceBodyTop, sourceSize);
			Bounds2D sourceCard = UI.PrevBounds;

			Vector2 scrollTopLeft = sourceCard.TopLeft + new Vector2(0.55f, -0.55f);
			Vector2 scrollSize = new(sourceCard.Width - 1.1f, sourceCard.Height - 1.1f);
			InputFieldTheme editorTheme = theme.ChipNameInputField;
			editorTheme.font = theme.FontRegular;
			editorTheme.fontSize = theme.FontSizeRegular * 0.62f;
			editorTheme.bgCol = Color.clear;
			editorTheme.focusBorderCol = RewiredUI.Accent;

			RhdlEditorCommand editorCommand = CodeEditor.Draw(
				scrollTopLeft,
				scrollSize,
				theme.ScrollTheme,
				editorTheme);

			if ((editorCommand & RhdlEditorCommand.Save) != 0)
			{
				SaveDraft(project);
				statusText = "Draft saved.";
			}
			if ((editorCommand & RhdlEditorCommand.Build) != 0)
			{
				Build(project, false);
			}
			if ((editorCommand & RhdlEditorCommand.BuildAndOpen) != 0)
			{
				Build(project, true);
			}

			DrawRightPanel(theme, infoTop, sourceCard.Bottom);

			Vector2 actionsTopLeft = sourceCard.BottomLeft + Vector2.down * 0.7f;
			// One row under both columns, so the right column cannot run into it
			string[] actionNames = { "SAVE DRAFT", "BUILD", "BUILD & OPEN", "NEW CHIP", "LOAD EXAMPLE", "CLOSE" };
			int action = UI.HorizontalButtonGroup(
				actionNames,
				theme.MainMenuButtonTheme,
				actionsTopLeft,
				WorkspaceWidth,
				DrawSettings.DefaultButtonSpacing,
				0,
				Anchor.TopLeft);

			if (action == 0)
			{
				SaveDraft(project);
				statusText = "Draft saved.";
			}
			else if (action == 1)
			{
				Build(project, false);
			}
			else if (action == 2)
			{
				Build(project, true);
			}
			else if (action == 3)
			{
				LoadStarterTemplate();
			}
			else if (action == 4)
			{
				LoadExample();
			}
			else if (action == 5 || KeyboardShortcuts.CancelShortcutTriggered)
			{
				SaveDraft(project);
				UIDrawer.SetActiveMenu(UIDrawer.MenuType.None);
			}
		}

		static void LoadStarterTemplate()
		{
			SetEditorSource(StarterTemplate);
			CodeEditor.MarkDirty();
			CodeEditor.SetDiagnosticLines(Array.Empty<int>());
			latestResult = null;
			buildAttempted = false;
			statusSuccess = false;
			statusText = "New chip template loaded.";
		}

		static void LoadExample()
		{
			SetEditorSource(DefaultExample);
			CodeEditor.MarkDirty();
			CodeEditor.SetDiagnosticLines(Array.Empty<int>());
			latestResult = null;
			buildAttempted = false;
			statusSuccess = false;
			statusText = "Example loaded. Press BUILD & OPEN.";
		}

		static void DrawMobileMenu(DrawSettings.UIThemeDLS theme, Project project)
		{
			Rect safe = MobileUI.SafeRectUI;
			const float pad = 0.75f;
			const float gap = 0.45f;
			float left = safe.xMin + pad;
			float width = safe.width - pad * 2f;
			float top = safe.yMax - pad;

			// Dedicated mobile header. The left side is intentionally reserved for
			// MobileUI's persistent BACK button, which is drawn above this menu.
			UI.DrawPanel(new Vector2(left, top), new Vector2(width, 5.2f), RewiredUI.SurfaceRaised, Anchor.TopLeft);
			Bounds2D header = UI.PrevBounds;
			UI.DrawLine(header.BottomLeft, header.BottomRight, 0.07f, RewiredUI.Accent);
			UI.DrawText(
				"RHDL STUDIO",
				theme.FontBold,
				theme.FontSizeRegular * 0.98f,
				header.CentreLeft + Vector2.right * 12.8f,
				Anchor.TextCentreLeft,
				Color.white);

			string sourceInfo = $"LN {CodeEditor.CaretLine}  COL {CodeEditor.CaretColumn}  /  {CodeEditor.LineCount} LINES";
			if (CodeEditor.IsDirty) sourceInfo += "  /  MODIFIED";
			UI.DrawText(
				sourceInfo,
				theme.FontRegular,
				theme.FontSizeRegular * 0.62f,
				header.CentreRight + Vector2.left * 0.9f,
				Anchor.TextCentreRight,
				RewiredUI.SecondaryText);

			float actionTop = header.Bottom - gap;
			float actionH = 4.7f;
			float actionGap = 0.42f;
			float actionW = (width - actionGap * 3f) / 4f;
			string[] labels = { "SAVE", "BUILD", "BUILD + OPEN", mobileInfoOpen ? "EDITOR" : "INFO" };
			for (int i = 0; i < labels.Length; i++)
			{
				ButtonTheme bt = i == 3 && mobileInfoOpen
					? theme.ChipLibraryCollectionToggleOn
					: i == 1 || i == 2
						? theme.MainMenuButtonTheme
						: theme.MenuButtonTheme;

				if (!UI.Button(
					labels[i],
					bt,
					new Vector2(left + i * (actionW + actionGap), actionTop),
					new Vector2(actionW, actionH),
					true,
					false,
					false,
					Anchor.TopLeft,
					true,
					0.35f))
				{
					continue;
				}

				switch (i)
				{
					case 0:
						SaveDraft(project);
						statusText = "Draft saved.";
						break;
					case 1:
						Build(project, false);
						break;
					case 2:
						Build(project, true);
						break;
					case 3:
						mobileInfoOpen = !mobileInfoOpen;
						if (mobileInfoOpen) MobileInputBridge.DismissKeyboard();
						break;
				}
			}

			Vector2 statusTopLeft = new(left, actionTop - actionH - gap);
			RewiredUI.DrawCard(statusTopLeft, new Vector2(width, 3.15f), true);
			Bounds2D statusCard = UI.PrevBounds;
			Color statusCol = !buildAttempted
				? RewiredUI.SecondaryText
				: statusSuccess
					? new Color(0.62f, 0.9f, 0.68f)
					: new Color(1f, 0.48f, 0.48f);
			string statusLabel = !buildAttempted ? "READY" : statusSuccess ? "BUILD OK" : "BUILD ERROR";
			UI.DrawText(
				statusLabel,
				theme.FontBold,
				theme.FontSizeRegular * 0.66f,
				statusCard.CentreLeft + Vector2.right * 0.75f,
				Anchor.TextCentreLeft,
				statusCol);
			UI.DrawText(
				MobileStatusText(statusText),
				theme.FontRegular,
				theme.FontSizeRegular * 0.58f,
				statusCard.CentreRight + Vector2.left * 0.75f,
				Anchor.TextCentreRight,
				statusCol);

			float contentTop = statusCard.Bottom - gap;
			float contentBottom = safe.yMin + pad;
			if (MobileInputBridge.KeyboardVisible)
			{
				float pixelsPerUi = Screen.width / UI.Width;
				contentBottom = Mathf.Max(
					contentBottom,
					MobileInputBridge.KeyboardScreenRect.yMax / pixelsPerUi + 0.6f);
			}

			float contentHeight = Mathf.Max(5.5f, contentTop - contentBottom);
			Vector2 contentTopLeft = new(left, contentTop);

			if (mobileInfoOpen)
			{
				DrawMobileInfo(theme, contentTopLeft, new Vector2(width, contentHeight));
			}
			else
			{
				RewiredUI.DrawCard(contentTopLeft, new Vector2(width, contentHeight));
				Bounds2D editorCard = UI.PrevBounds;
				Vector2 scrollTopLeft = editorCard.TopLeft + new Vector2(0.45f, -0.45f);
				Vector2 scrollSize = new(editorCard.Width - 0.9f, editorCard.Height - 0.9f);

				InputFieldTheme editorTheme = theme.ChipNameInputField;
				editorTheme.font = theme.FontRegular;
				editorTheme.fontSize = theme.FontSizeRegular * 0.66f;
				editorTheme.bgCol = Color.clear;
				editorTheme.focusBorderCol = RewiredUI.Accent;

				RhdlEditorCommand editorCommand = CodeEditor.Draw(
					scrollTopLeft,
					scrollSize,
					theme.ScrollTheme,
					editorTheme);

				if ((editorCommand & RhdlEditorCommand.Save) != 0)
				{
					SaveDraft(project);
					statusText = "Draft saved.";
				}
				if ((editorCommand & RhdlEditorCommand.Build) != 0) Build(project, false);
				if ((editorCommand & RhdlEditorCommand.BuildAndOpen) != 0) Build(project, true);
			}

			if (KeyboardShortcuts.CancelShortcutTriggered)
			{
				SaveDraft(project);
				UIDrawer.SetActiveMenu(UIDrawer.MenuType.None);
			}
		}

		static void DrawMobileInfo(
			DrawSettings.UIThemeDLS theme,
			Vector2 topLeft,
			Vector2 size)
		{
			RewiredUI.DrawCard(topLeft, size, true);
			Bounds2D card = UI.PrevBounds;
			float x = card.Left + 0.8f;
			float y = card.Top - 1.0f;
			float innerW = card.Width - 1.6f;

			UI.DrawText(
				"COMPILER / QUICK REFERENCE",
				theme.FontBold,
				theme.FontSizeRegular * 0.78f,
				new Vector2(x, y),
				Anchor.TextCentreLeft,
				Color.white);
			y -= 2.1f;

			string buildInfo;
			if (statusSuccess && latestResult?.Description != null)
			{
				buildInfo =
					$"{latestResult.Description.Name}  /  {lastBuildSubChipCount} components  /  {lastBuildWireCount} connections";
			}
			else
			{
				buildInfo = MobileStatusText(statusText, 120);
			}

			UI.DrawText(
				buildInfo,
				theme.FontRegular,
				theme.FontSizeRegular * 0.60f,
				new Vector2(x, y),
				Anchor.TextCentreLeft,
				statusSuccess ? new Color(0.62f, 0.9f, 0.68f) : RewiredUI.SecondaryText);
			y -= 2.4f;

			string help =
				"chip Adder\n" +
				"    in A: 8, B: 8\n" +
				"    out Y: 8\n" +
				"    Y = A + B\n\n" +
				"and  or  xor  not  nand  nor  xnor\n" +
				"Y = if S then A else B    wire t: 9 = A + B\n" +
				"A[3]  A[0..3]  join(high, low)    const N = 8\n" +
				"y = NAND(a, b)    q, nq = DffBit(d, clk)\n" +
				"for i in 0..7 ... end    # comment\n\n" +
				"UNDO/REDO on keyboard    BUILD above";
			UI.DrawText(
				help,
				theme.FontRegular,
				theme.FontSizeRegular * 0.56f,
				new Vector2(x, y),
				Anchor.TopLeft,
				RewiredUI.SecondaryText);

			float buttonH = 4.7f;
			float buttonGap = 0.45f;
			float buttonW = (innerW - buttonGap) / 2f;
			Vector2 buttons = new(card.Left + 0.8f, card.Bottom + 0.8f);
			if (UI.Button(
				"NEW CHIP",
				theme.MenuButtonTheme,
				buttons,
				new Vector2(buttonW, buttonH),
				true,
				false,
				false,
				Anchor.BottomLeft))
			{
				SetEditorSource(StarterTemplate);
				CodeEditor.MarkDirty();
				CodeEditor.SetDiagnosticLines(Array.Empty<int>());
				latestResult = null;
				buildAttempted = false;
				statusSuccess = false;
				statusText = "New chip template loaded.";
				mobileInfoOpen = false;
			}
			if (UI.Button(
				"LOAD EXAMPLE",
				theme.MainMenuButtonTheme,
				buttons + Vector2.right * (buttonW + buttonGap),
				new Vector2(buttonW, buttonH),
				true,
				false,
				false,
				Anchor.BottomLeft))
			{
				SetEditorSource(DefaultExample);
				CodeEditor.MarkDirty();
				CodeEditor.SetDiagnosticLines(Array.Empty<int>());
				latestResult = null;
				buildAttempted = false;
				statusSuccess = false;
				statusText = "Example loaded. Press BUILD + OPEN.";
				mobileInfoOpen = false;
			}
		}

		static string MobileStatusText(string text, int max = 86)
		{
			if (string.IsNullOrWhiteSpace(text)) return string.Empty;
			string singleLine = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
			while (singleLine.Contains("  ")) singleLine = singleLine.Replace("  ", " ");
			if (singleLine.Length <= max) return singleLine;
			return singleLine.Substring(0, Mathf.Max(0, max - 3)) + "...";
		}

		// The right column ends level with the source editor (bottom at y = bottom)
		static void DrawRightPanel(DrawSettings.UIThemeDLS theme, Vector2 topLeft, float bottom)
		{
			Bounds2D compilerHeader = RewiredUI.DrawSectionHeader("COMPILER", topLeft, RightWidth);
			Vector2 cursor = compilerHeader.BottomLeft;

			RewiredUI.DrawCard(cursor, new Vector2(RightWidth, 10.4f), true);
			Bounds2D statusCard = UI.PrevBounds;
			Color statusCol = !buildAttempted
				? RewiredUI.SecondaryText
				: statusSuccess
					? new Color(0.62f, 0.9f, 0.68f)
					: new Color(1f, 0.48f, 0.48f);
			string statusLabel = !buildAttempted ? "STATUS" : statusSuccess ? "BUILD: OK" : "BUILD: ERRORS";

			RewiredUI.DrawLabel(
				statusLabel,
				statusCard.TopLeft + new Vector2(0.8f, -1.0f),
				true,
				0.66f,
				statusCol);

			string summary = statusText;
			if (statusSuccess && latestResult?.Description != null)
			{
				summary += $"\n\nchip: {latestResult.Description.Name}" +
				           $"\ncomponents: {lastBuildSubChipCount}" +
				           $"\nconnections: {lastBuildWireCount}";
			}

			UI.DrawText(
				summary,
				theme.FontRegular,
				theme.FontSizeRegular * 0.58f,
				statusCard.TopLeft + new Vector2(0.8f, -2.4f),
				Anchor.TopLeft,
				statusCol);

			cursor = statusCard.BottomLeft + Vector2.down * 0.6f;
			Bounds2D syntaxHeader = RewiredUI.DrawSectionHeader("RHDL 2.0 QUICK REFERENCE", cursor, RightWidth);
			cursor = syntaxHeader.BottomLeft;

			RewiredUI.DrawCard(cursor, new Vector2(RightWidth, Mathf.Max(4f, cursor.y - bottom)));
			Bounds2D syntaxCard = UI.PrevBounds;
			string help =
				"chip Adder\n" +
				"    in A: 8, B: 8\n" +
				"    out Y: 8\n" +
				"    Y = A + B\n\n" +
				"Wire: t = A and B   or   wire t: 9 = A + B\n" +
				"Logic: and  or  xor  not  nand  nor  xnor\n" +
				"Choice: Y = if S then A else B\n" +
				"Bits: A[3], A[0..3], join(high, low)\n" +
				"Repeat: for i in 0..7  ...  end\n" +
				"Constant: const WIDTH = 8\n" +
				"Chip: y = NAND(a, b)   s, c = HalfAdder(a, b)\n" +
				"Feedback: use a name above the line that sets it\n" +
				"Comments start with #\n\n" +
				"ENTER after chip or for indents automatically\n" +
				"TAB / SHIFT+TAB indent   ALT+UP/DOWN move lines\n" +
				"CTRL+BACKSPACE/DELETE delete word\n" +
				"CTRL+Z/Y undo/redo   CTRL+/ comment\n" +
				"CTRL+SHIFT+F format document   F8 next error\n" +
				"CTRL+D duplicate line   HOME smart-home\n" +
				"CTRL+S save   CTRL+B / CTRL+ENTER build   F5 build+open";

			UI.DrawText(
				help,
				theme.FontRegular,
				theme.FontSizeRegular * 0.56f,
				syntaxCard.TopLeft + new Vector2(0.8f, -0.8f),
				Anchor.TopLeft,
				RewiredUI.SecondaryText);
		}

		static void Build(Project project, bool openAfterBuild)
		{
			string source = GetEditorSource();
			SaveDraft(project);
			buildAttempted = true;

			latestResult = RhdlCompiler.Compile(source, project.chipLibrary);
			if (!latestResult.Success)
			{
				statusSuccess = false;
				CodeEditor.SetDiagnosticLines(latestResult.Diagnostics.Select(d => d.Line));
				RhdlDiagnostic first = latestResult.Diagnostics.FirstOrDefault(d => d.Line > 0);
				if (first != null) CodeEditor.GoTo(first.Line, first.Column > 0 ? first.Column : 1);

				StringBuilder message = new();
				RhdlDiagnostic[] diagnostics = latestResult.Diagnostics.Take(3).ToArray();
				for (int i = 0; i < diagnostics.Length; i++)
				{
					if (i > 0) message.Append('\n');
					message.Append(CompactDiagnostic(diagnostics[i]));
				}
				if (latestResult.Diagnostics.Length > diagnostics.Length)
					message.Append($"\n+ {latestResult.Diagnostics.Length - diagnostics.Length} more error(s)");
				statusText = message.ToString();
				return;
			}

			ChipDescription description = latestResult.Description;
			if (project.chipLibrary.IsBuiltinChip(description.Name))
			{
				statusSuccess = false;
				statusText = $"Cannot overwrite builtin component '{description.Name}'.";
				return;
			}

			bool existed = project.chipLibrary.HasChip(description.Name);
			bool isGlobal = existed && project.chipLibrary.IsGlobalChip(description.Name);
			if (isGlobal && !GlobalChipManager.ValidateGlobalDependencies(description, project.chipLibrary, out string globalError))
			{
				statusSuccess = false;
				statusText = globalError;
				return;
			}

			Saver.SaveChip(description, project.description.ProjectName, isGlobal);
			project.chipLibrary.NotifyChipSaved(description, isGlobal);
			RhdlSourceStore.SaveChipSource(project.description.ProjectName, description.Name, source);

			if (!existed)
			{
				project.SetStarred(description.Name, true, false, autoSave: false);
			}
			project.UpdateAndSaveProjectDescription();

			CombinationalChipCacheManager.RefreshPersistentCachesAfterSave(
				description.Name,
				project.chipLibrary,
				project.description.ProjectName);

			lastBuildSubChipCount = description.SubChips?.Length ?? 0;
			lastBuildWireCount = description.Wires?.Length ?? 0;
			statusText = existed ? "Rebuilt the existing chip." : "Built and saved a new chip.";
			statusSuccess = true;
			CodeEditor.SetDiagnosticLines(Array.Empty<int>());
			CodeEditor.MarkSaved();

			if (openAfterBuild)
			{
				project.LoadDevChipOrCreateNewIfDoesntExist(description.Name);
				UIDrawer.SetActiveMenu(UIDrawer.MenuType.None);
			}
		}

		static void SaveDraft(Project project)
		{
			RhdlSourceStore.SaveDraft(project.description.ProjectName, GetEditorSource());
			CodeEditor.MarkSaved();
		}

		static string CompactDiagnostic(RhdlDiagnostic diagnostic)
		{
			string location = diagnostic.Line > 0
				? diagnostic.Column > 0 ? $"L{diagnostic.Line}:{diagnostic.Column}" : $"L{diagnostic.Line}"
				: "RHDL";
			string message = diagnostic.Message ?? string.Empty;
			const int max = 58;
			if (message.Length > max) message = message.Substring(0, max - 3) + "...";
			return location + "  " + message;
		}

		static string GetEditorSource() => CodeEditor.Text;

		static void SetEditorSource(string source) => CodeEditor.SetText(source);

		public const string StarterTemplate =
@"# A small chip: name it, list its pins, give each output a value.
chip Adder
    in A: 8, B: 8
    out Y: 8

    Y = A + B
";

		public const string DefaultExample =
@"# Every line is hardware, and all of it works at the same time.
chip AluMini
    in A: 8, B: 8, select
    out Y: 8, equal, carry

    wire total: 9 = A + B
    mixed = join(A[4..7], B[0..3])

    Y = if select then total[0..7] else mixed
    equal = A == B
    carry = total[8]
";

	}
}
