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
	// The desktop editor's parts panel, down the left of the board: every chip that can be added,
	// grouped by collection. Click a chip and then the board to place it, or drag it onto the board.
	public static class DesktopPartsPanel
	{
		const float HeaderHeight = 2.8f;
		const float SectionHeight = 2.1f;
		const float RowHeight = 2.6f;
		const float RowGap = 0.15f;
		const float Pad = 0.35f;
		const float DragThreshold = 0.8f;
		const float ScrollSpeed = 2.6f;

		// How long a section takes to open or close, in seconds.
		const float OpenSeconds = 0.2f;

		const string OtherCollectionName = "OTHER";
		// A key no collection can have, shown as MY CHIPS.
		const string MyChipsSection = "\u0001MY CHIPS";

		enum RowKind
		{
			Section,
			Chip
		}

		readonly struct Row
		{
			public readonly RowKind Kind;
			public readonly string Name;
			public readonly int Count;
			// For a chip: the section it is listed in.
			public readonly string Section;

			public Row(RowKind kind, string name, int count = 0, string section = null)
			{
				Kind = kind;
				Name = name;
				Count = count;
				Section = section;
			}
		}

		// Where a row is this frame. A section that is opening or closing shows only part of its
		// chips: they slide out from under the header and are cut off at ClipBottom.
		readonly struct Placed
		{
			public readonly Row Row;
			public readonly float Top;
			public readonly float ClipTop;
			public readonly float ClipBottom;

			public Placed(Row row, float top, float clipTop, float clipBottom)
			{
				Row = row;
				Top = top;
				ClipTop = clipTop;
				ClipBottom = clipBottom;
			}
		}

		static readonly List<Row> rows = new();
		static readonly List<Placed> placed = new();
		// How far each section is open, 0 to 1, moving towards IsOpen over OpenSeconds.
		static readonly Dictionary<string, float> openness = new();
		static readonly List<string> sectionChips = new();
		// Sections the user closed; IN/OUT, BASIC and MY CHIPS start open, the others closed.
		static readonly HashSet<string> toggledSections = new();
		static readonly string[] sectionsOpenAtStart = { "BASIC", "IN/OUT", MyChipsSection };

		static float scroll;
		static string pressedChip;
		static string pressedSection;
		static Vector2 pressStart;
		static bool dragPlacing;
		static string armedChip;

		public static void Reset()
		{
			scroll = 0;
			openness.Clear();
			pressedChip = null;
			pressedSection = null;
			dragPlacing = false;
			armedChip = null;
		}

		static Rect PanelRect => Rect.MinMaxRect(0, BottomBarUI.barHeight, DesktopLayout.PartsPanelWidth, UI.Height - DesktopLayout.TopBarHeight);

		public static void Draw(Project project)
		{
			if (!DesktopLayout.PartsPanelShown(project))
			{
				dragPlacing = false;
				pressedChip = pressedSection = null;
				return;
			}

			Rect panel = PanelRect;
			UI.DrawPanel(new Vector2(panel.xMin, panel.yMax), panel.size, MobileTheme.PanelCol, Anchor.TopLeft);
			UI.DrawLine(new Vector2(panel.xMax, panel.yMin), new Vector2(panel.xMax, panel.yMax), 0.06f, MobileTheme.LineCol);

			bool inOtherMenu = UIDrawer.ActiveMenu is not (UIDrawer.MenuType.None or UIDrawer.MenuType.BottomBarMenuPopup);
			if (!project.controller.IsPlacingElements && !dragPlacing) armedChip = null;

			using (UI.BeginDisabledScope(inOtherMenu))
			{
				DrawHeader(panel);

				Rect list = Rect.MinMaxRect(panel.xMin + Pad, panel.yMin + Pad, panel.xMax - Pad, panel.yMax - HeaderHeight);
				CollectRows(project);
				LayOut(list);
				if (!inOtherMenu && !ContextMenu.HasFocus()) HandleInput(project, list);
				DrawRows(project, list);
			}
		}

		static void DrawHeader(Rect panel)
		{
			float centreY = panel.yMax - HeaderHeight / 2;
			DesktopTheme.Text("PARTS", new Vector2(panel.xMin + 0.9f, centreY), 0.46f, RewiredUI.SecondaryText, Anchor.TextCentreLeft, true);

			Vector2 findSize = new(DesktopTheme.LabelButtonWidth("FIND", DesktopTheme.Quiet, true), 1.9f);
			Vector2 findPos = new(panel.xMax - Pad - findSize.x, centreY + findSize.y / 2);
			if (DesktopTheme.IconButton("FIND", MobileIcon.Search, DesktopTheme.Quiet, findPos, findSize))
			{
				Project.ActiveProject.controller.CancelEverything();
				UIDrawer.SetActiveMenu(UIDrawer.MenuType.Search);
			}

			string shortcut = KeyboardShortcuts.GetBinding(ShortcutAction.Search).IsBound ? KeyboardShortcuts.GetBindingDisplayString(ShortcutAction.Search) : null;
			DesktopTheme.Hint("Find a chip by name", shortcut);
		}

		// ---- Contents ----

		static bool IsOpen(string section)
		{
			bool openAtStart = System.Array.IndexOf(sectionsOpenAtStart, section) >= 0;
			return openAtStart != toggledSections.Contains(section);
		}

		static void ToggleSection(string section)
		{
			if (!toggledSections.Remove(section)) toggledSections.Add(section);
		}

		// 0 closed, 1 open; starts where the section is, so nothing animates on the first frame.
		static float Openness(string section) => openness.TryGetValue(section, out float amount) ? amount : IsOpen(section) ? 1 : 0;

		// Fast at first, gently into place.
		static float Ease(float t) => 1 - (1 - t) * (1 - t) * (1 - t);

		static void AnimateSections()
		{
			float step = Mathf.Min(Time.unscaledDeltaTime, 0.1f) / OpenSeconds;
			foreach (Row row in rows)
			{
				if (row.Kind != RowKind.Section) continue;
				openness[row.Name] = Mathf.MoveTowards(Openness(row.Name), IsOpen(row.Name) ? 1 : 0, step);
			}
		}

		// The project's collections, then MY CHIPS with every chip the user has made. OTHER, where
		// saved chips go by default, is only listed if it holds built-in chips as well.
		static void CollectRows(Project project)
		{
			rows.Clear();
			bool otherHasBuiltins = false;

			foreach (ChipCollection collection in project.description.ChipCollections)
			{
				if (ChipDescription.NameMatch(collection.Name, OtherCollectionName))
				{
					otherHasBuiltins = collection.Chips.Exists(c => project.chipLibrary.IsBuiltinChip(c));
					continue;
				}

				AddSection(project, collection.Name, collection.Chips);
			}

			sectionChips.Clear();
			foreach (ChipDescription chip in project.chipLibrary.allChips)
			{
				if (chip.ChipType == ChipType.Custom) sectionChips.Add(chip.Name);
			}

			sectionChips.Sort(System.StringComparer.OrdinalIgnoreCase);
			AddSection(project, MyChipsSection, new List<string>(sectionChips));

			if (otherHasBuiltins)
			{
				foreach (ChipCollection collection in project.description.ChipCollections)
				{
					if (ChipDescription.NameMatch(collection.Name, OtherCollectionName)) AddSection(project, collection.Name, collection.Chips);
				}
			}
		}

		static void AddSection(Project project, string name, List<string> chips)
		{
			int count = 0;
			foreach (string chip in chips)
			{
				if (project.chipLibrary.HasChip(chip)) count++;
			}

			rows.Add(new Row(RowKind.Section, name, count));
			// A closing section keeps its chips until it has closed.
			if (!IsOpen(name) && Openness(name) <= 0) return;

			foreach (string chip in chips)
			{
				if (project.chipLibrary.HasChip(chip)) rows.Add(new Row(RowKind.Chip, chip, section: name));
			}
		}

		static float RowSize(Row row) => (row.Kind == RowKind.Section ? SectionHeight : RowHeight) + RowGap;

		// Places every row for this frame, with list.yMax at the top of the content (before
		// scrolling). Each section's chips form a block that is shown to the eased openness:
		// the block slides down from under its header and is cut off below what is shown.
		static void LayOut(Rect list)
		{
			AnimateSections();
			placed.Clear();
			float y = 0;
			for (int i = 0; i < rows.Count;)
			{
				Row section = rows[i];
				placed.Add(new Placed(section, y, y, y - SectionHeight));
				y -= RowSize(section);
				i++;

				int first = i;
				float blockHeight = 0;
				for (; i < rows.Count && rows[i].Kind == RowKind.Chip; i++) blockHeight += RowSize(rows[i]);
				if (i == first) continue;

				float shown = blockHeight * Ease(Openness(section.Name));
				float rowTop = y + (blockHeight - shown);
				for (int k = first; k < i; k++)
				{
					placed.Add(new Placed(rows[k], rowTop, y, y - shown));
					rowTop -= RowSize(rows[k]);
				}

				y -= shown;
			}

			laidOutHeight = -y;
		}

		static float laidOutHeight;

		static float ContentHeight() => laidOutHeight;

		// The row at a point of the list, or -1: only the part of a row that is shown counts.
		static int RowAt(Rect list, Vector2 point)
		{
			if (!list.Contains(point)) return -1;
			float offset = list.yMax + scroll;
			for (int i = 0; i < placed.Count; i++)
			{
				Placed p = placed[i];
				float height = RowSize(p.Row) - RowGap;
				float top = Mathf.Min(p.Top, p.ClipTop) + offset;
				float bottom = Mathf.Max(p.Top - height, p.ClipBottom) + offset;
				if (point.y <= top && point.y >= bottom) return i;
			}

			return -1;
		}

		// ---- Input ----

		static void HandleInput(Project project, Rect list)
		{
			Vector2 mouse = UI.ScreenToUISpace(InputHelper.MousePos);
			ChipInteractionController controller = project.controller;

			if (list.Contains(mouse) && InputHelper.MouseScrollDelta.y != 0)
			{
				scroll -= InputHelper.MouseScrollDelta.y * ScrollSpeed;
			}

			if (InputHelper.IsMouseDownThisFrame(MouseButton.Left))
			{
				pressedChip = pressedSection = null;
				int index = RowAt(list, mouse);
				if (index >= 0)
				{
					Row row = placed[index].Row;
					if (row.Kind == RowKind.Section) pressedSection = row.Name;
					else pressedChip = row.Name;
					pressStart = mouse;
				}
			}
			else if (InputHelper.IsMouseDownThisFrame(MouseButton.Right))
			{
				int index = RowAt(list, mouse);
				if (index >= 0 && placed[index].Row.Kind == RowKind.Chip)
				{
					ContextMenu.OpenBottomBarContextMenu(placed[index].Row.Name, false, true);
				}
			}

			// Dragging a chip out of the panel picks it up; letting go over the board places it there.
			if (pressedChip != null && !dragPlacing && InputHelper.IsMouseHeld(MouseButton.Left) && (mouse - pressStart).magnitude > DragThreshold)
			{
				if (project.ViewedChip.CanAddSubchip(pressedChip))
				{
					controller.CancelEverything();
					controller.StartPlacing(pressedChip);
					SearchPopup.AddRecentChip(pressedChip);
					armedChip = pressedChip;
					dragPlacing = true;
				}
				else pressedChip = null;
			}

			if (!InputHelper.IsMouseUpThisFrame(MouseButton.Left)) return;

			if (dragPlacing)
			{
				bool overBoard = DesktopLayout.BoardRectUI.Contains(mouse);
				if (!overBoard) controller.CancelEverything();
				else controller.TryFinishPlacing(); // overlapping something: it stays on the mouse to be placed with a click
			}
			else
			{
				int index = RowAt(list, mouse);
				Row? released = index >= 0 ? placed[index].Row : null;

				if (pressedSection != null && released is { Kind: RowKind.Section } section && section.Name == pressedSection)
				{
					ToggleSection(pressedSection);
				}
				else if (pressedChip != null && released is { Kind: RowKind.Chip } chipRow && chipRow.Name == pressedChip && project.ViewedChip.CanAddSubchip(pressedChip))
				{
					// A click picks the chip up to be placed with a click on the board; clicking it again puts it back.
					bool wasArmed = armedChip == pressedChip && controller.IsPlacingElements;
					controller.CancelEverything();
					armedChip = null;
					if (!wasArmed)
					{
						controller.StartPlacing(pressedChip);
						SearchPopup.AddRecentChip(pressedChip);
						armedChip = pressedChip;
					}
				}
			}

			dragPlacing = false;
			pressedChip = pressedSection = null;
		}

		// ---- Drawing ----

		static void DrawRows(Project project, Rect list)
		{
			float contentHeight = ContentHeight();
			scroll = Mathf.Clamp(scroll, 0, Mathf.Max(0, contentHeight - list.height));
			Vector2 mouse = UI.ScreenToUISpace(InputHelper.MousePos);
			int hovered = UIDrawer.ActiveMenu is UIDrawer.MenuType.None or UIDrawer.MenuType.BottomBarMenuPopup ? RowAt(list, mouse) : -1;

			float offset = list.yMax + scroll;

			// Rows of sections that are open or closed share the list's mask, as always.
			using (UI.CreateMaskScopeMinMax(list.min, list.max))
			{
				for (int i = 0; i < placed.Count; i++)
				{
					if (!IsMoving(placed[i])) DrawPlaced(project, placed[i], list, offset, i == hovered);
				}
			}

			// The chips of a section that is opening or closing are cut off where its block
			// ends, one mask per section.
			for (int i = 0; i < placed.Count;)
			{
				if (!IsMoving(placed[i]))
				{
					i++;
					continue;
				}

				float clipTop = Mathf.Min(list.yMax, placed[i].ClipTop + offset);
				float clipBottom = Mathf.Max(list.yMin, placed[i].ClipBottom + offset);
				int end = i;
				while (end < placed.Count && IsMoving(placed[end]) && placed[end].Row.Section == placed[i].Row.Section) end++;

				if (clipTop > clipBottom)
				{
					using (UI.CreateMaskScopeMinMax(new Vector2(list.xMin, clipBottom), new Vector2(list.xMax, clipTop)))
					{
						for (int k = i; k < end; k++) DrawPlaced(project, placed[k], list, offset, k == hovered);
					}
				}

				i = end;
			}

			// A hint that the list continues below.
			if (contentHeight - scroll > list.height + 0.1f)
			{
				UI.DrawPanel(new Vector2(list.center.x, list.yMin), new Vector2(list.width, 0.18f), RewiredUI.Accent * 0.8f, Anchor.CentreBottom);
			}
		}

		// A chip of a section that is neither fully open nor closed.
		static bool IsMoving(Placed p) => p.Row.Kind == RowKind.Chip && Openness(p.Row.Section) is > 0 and < 1;

		static void DrawPlaced(Project project, Placed p, Rect list, float offset, bool hovered)
		{
			float top = p.Top + offset;
			if (top - (RowSize(p.Row) - RowGap) >= list.yMax || top <= list.yMin) return;
			if (p.Row.Kind == RowKind.Section) DrawSection(p.Row, new Vector2(list.xMin, top), list.width, hovered);
			else DrawChip(project, p.Row.Name, new Vector2(list.xMin, top), list.width, hovered);
		}

		static void DrawSection(Row row, Vector2 topLeft, float width, bool hovered)
		{
			Bounds2D bounds = Bounds2D.CreateFromTopLeftAndSize(topLeft, new Vector2(width, SectionHeight));
			if (hovered) UI.DrawPanel(bounds, MobileTheme.CardCol);

			// The arrow turns from pointing right (closed) to pointing down (open) as it opens.
			Vector2 arrowCentre = bounds.CentreLeft + Vector2.right * 0.75f;
			const float s = 0.32f;
			float turn = (1 - Ease(Openness(row.Name))) * Mathf.PI / 2;
			Vector2 Arrow(Vector2 point) => arrowCentre + new Vector2(
				point.x * Mathf.Cos(turn) - point.y * Mathf.Sin(turn),
				point.x * Mathf.Sin(turn) + point.y * Mathf.Cos(turn));
			UI.DrawTriangle(Arrow(new Vector2(-s, s * 0.6f)), Arrow(new Vector2(s, s * 0.6f)), Arrow(new Vector2(0, -s * 0.7f)), RewiredUI.SecondaryText);

			string name = row.Name == MyChipsSection ? "MY CHIPS" : row.Name;
			DesktopTheme.Text(DesktopTheme.Fit(name, width - 4f, 0.46f, true), bounds.CentreLeft + Vector2.right * 1.5f, 0.46f, Color.white, Anchor.TextCentreLeft, true);
			DesktopTheme.Text(row.Count.ToString(), bounds.CentreRight + Vector2.left * 0.5f, 0.42f, RewiredUI.DimText, Anchor.TextCentreRight);
		}

		static void DrawChip(Project project, string name, Vector2 topLeft, float width, bool hovered)
		{
			bool armed = name == armedChip && (project.controller.IsPlacingElements || dragPlacing);
			bool canAdd = project.ViewedChip.CanAddSubchip(name);
			bool pressed = name == pressedChip && InputHelper.IsMouseHeld(MouseButton.Left);

			Color bg = armed ? RewiredUI.Accent : pressed ? ColHelper.MakeCol255(58, 61, 70) : hovered && canAdd ? ColHelper.MakeCol255(44, 46, 53) : MobileTheme.CardCol;
			UI.DrawPanel(topLeft, new Vector2(width, RowHeight), bg, Anchor.TopLeft);
			Bounds2D card = UI.PrevBounds;

			if (project.chipLibrary.TryGetChipDescription(name, out ChipDescription description))
			{
				UI.DrawPanel(card.TopLeft, new Vector2(0.28f, RowHeight), description.Colour, Anchor.TopLeft);
			}

			Color nameCol = canAdd ? Color.white : RewiredUI.DimText;
			string about = canAdd ? MobilePalette.Describe(project, name) : ChipDescription.NameMatch(name, project.ActiveDevChipName) ? "open now" : "uses the open chip";
			DesktopTheme.Text(DesktopTheme.Fit(name, width - 1.4f, 0.52f, true), new Vector2(card.Left + 0.85f, card.Centre.y + 0.45f), 0.52f, nameCol, Anchor.TextCentreLeft, true);
			DesktopTheme.Text(DesktopTheme.Fit(about, width - 1.4f, 0.4f), new Vector2(card.Left + 0.85f, card.Centre.y - 0.6f), 0.4f, armed ? Color.white : RewiredUI.DimText);
		}
	}
}
