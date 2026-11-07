using System;
using System.Collections.Generic;
using System.Linq;
using DLS.Description;
using DLS.Game;
using Seb.Types;
using Seb.Vis;
using Seb.Vis.UI;
using UnityEngine;

namespace DLS.Graphics
{
	// The desktop editor's details panel, down the right of the board: what is selected, its pins
	// and everything that can be done with it. With nothing selected it describes the chip being edited.
	public static class DesktopInspector
	{
		const float HeaderHeight = 2.8f;
		const float Pad = 0.6f;
		const float ButtonHeight = 2.0f;
		const float ButtonGap = 0.3f;
		const float LineHeight = 1.25f;
		const int MaxPinsListed = 8;

		static readonly List<(string label, Action onPress, bool enabled, bool danger)> actions = new();

		static Rect PanelRect => Rect.MinMaxRect(UI.Width - DesktopLayout.InspectorWidth, BottomBarUI.barHeight, UI.Width, UI.Height - DesktopLayout.TopBarHeight);

		public static void Draw(Project project)
		{
			if (!DesktopLayout.InspectorShown(project)) return;

			Rect panel = PanelRect;
			UI.DrawPanel(new Vector2(panel.xMin, panel.yMax), panel.size, MobileTheme.PanelCol, Anchor.TopLeft);
			UI.DrawLine(new Vector2(panel.xMin, panel.yMin), new Vector2(panel.xMin, panel.yMax), 0.06f, MobileTheme.LineCol);
			DesktopTheme.Text("DETAILS", new Vector2(panel.xMin + 0.9f, panel.yMax - HeaderHeight / 2), 0.46f, RewiredUI.SecondaryText, Anchor.TextCentreLeft, true);

			bool inOtherMenu = UIDrawer.ActiveMenu is not (UIDrawer.MenuType.None or UIDrawer.MenuType.BottomBarMenuPopup);
			ChipInteractionController controller = project.controller;
			List<IMoveable> selection = controller.SelectedElements;
			float left = panel.xMin + Pad;
			float width = panel.width - Pad * 2;
			float top = panel.yMax - HeaderHeight - 0.4f;

			using (UI.BeginDisabledScope(inOtherMenu))
			using (UI.CreateMaskScopeMinMax(panel.min, panel.max))
			{
				if (controller.IsPlacingElements) DrawPlacing(project, left, top, width);
				else if (selection.Count == 1) DrawSingle(project, selection[0], left, top, width);
				else if (selection.Count > 1) DrawMultiple(project, selection, left, top, width);
				else DrawChipOverview(project, left, top, width);
			}
		}

		// ---- Nothing selected: the chip being edited ----

		static void DrawChipOverview(Project project, float left, float top, float width)
		{
			DevChipInstance chip = project.ViewedChip;
			bool canEdit = project.CanEditViewedChip;
			bool unsaved = canEdit && RewiredUI.ActiveChipHasUnsavedChanges(project);
			string name = string.IsNullOrWhiteSpace(project.ActiveDevChipName) ? "NEW CHIP" : project.ActiveDevChipName;

			float y = Title(name, left, top, width, null);
			y = Line(canEdit ? "the chip being edited" : "looking inside, view only", left, y, width, RewiredUI.DimText);
			if (unsaved) y = Line("NOT SAVED", left, y, width, RewiredUI.UnsavedText, true);

			y -= 0.5f;
			int inputs = chip.GetInputPins().Length;
			int outputs = chip.GetOutputPins().Count();
			int parts = chip.Elements.OfType<SubChipInstance>().Count();
			y = Fact("Inputs", inputs.ToString(), left, y, width);
			y = Fact("Outputs", outputs.ToString(), left, y, width);
			y = Fact("Parts", parts.ToString(), left, y, width);
			y = Fact("Wires", chip.Wires.Count.ToString(), left, y, width);

			if (canEdit)
			{
				actions.Clear();
				actions.Add(("SAVE CHIP", () => UIDrawer.SetActiveMenu(UIDrawer.MenuType.ChipSave), true, false));
				actions.Add(("NEW CHIP", () => MobileUI.CreateNewChip(project), true, false));
				y = DrawActions(left, y - 0.6f, width, unsaved ? 0 : -1);
			}

			y -= 0.8f;
			string tip = chip.Elements.Count == 0
				? "Drag parts in from the left, or press " + ShortcutText(ShortcutAction.CommandPalette) + " and type a name. Drag from pin to pin to wire them."
				: "Click a part to see its pins and what you can do with it. Drag a box to select several.";
			Paragraph(tip, left, y, width);
		}

		// ---- One element ----

		static void DrawSingle(Project project, IMoveable element, float left, float top, float width)
		{
			float y = top;

			if (element is SubChipInstance subchip)
			{
				string name = subchip.IsBus ? "BUS" : subchip.Description.Name;
				y = Title(name, left, y, width, subchip.Description.Colour);
				y = Line(MobilePalette.Describe(project, subchip.Description.Name), left, y, width, RewiredUI.DimText);
				if (!string.IsNullOrWhiteSpace(subchip.Label)) y = Line("Label: " + subchip.Label, left, y, width, RewiredUI.SecondaryText);
				if (subchip.ChipType == ChipType.Key && !string.IsNullOrEmpty(subchip.activationKeyString)) y = Line("Key: " + subchip.activationKeyString, left, y, width, RewiredUI.SecondaryText);

				y = PinList("INPUTS", subchip.InputPins, left, y - 0.4f, width);
				y = PinList("OUTPUTS", subchip.OutputPins, left, y - 0.2f, width);
			}
			else if (element is DevPinInstance devPin)
			{
				string name = string.IsNullOrWhiteSpace(devPin.Name) ? "PIN" : devPin.Name;
				y = Title(name, left, y, width, null);
				string kind = (devPin.IsInputPin ? "input of this chip" : "output of this chip") + ", " + BitText(devPin.BitCount);
				y = Line(kind, left, y, width, RewiredUI.DimText);
				if (devPin.IsInputPin) y = Line("Click its switch to change the value.", left, y, width, RewiredUI.DimText);
			}
			else
			{
				y = Title("SELECTED", left, y, width, null);
			}

			// Its actions, the same as its right-click menu. While that menu is open it owns them.
			if (ContextMenu.IsOpen || !ContextMenu.TryGetEntriesFor(element, out _, out ContextMenu.MenuEntry[] entries)) return;

			actions.Clear();
			bool canEdit = project.CanEditViewedChip;
			(string label, Action onPress, bool enabled, bool danger)? delete = null;
			bool hasMore = false;

			foreach (ContextMenu.MenuEntry entry in entries)
			{
				if (ContextMenu.IsDivider(entry) || ContextMenu.IsColourEntry(entry))
				{
					hasMore = true;
					continue;
				}

				string label = ActionLabel(entry.Text.Trim().ToUpperInvariant());
				if (label == "DELETE") delete = (label, entry.OnPress, entry.IsEnabled(), true);
				else actions.Add((label, entry.OnPress, entry.IsEnabled(), false));
			}

			actions.Add(("DUPLICATE", project.controller.DuplicateSelection, canEdit, false));
			if (hasMore)
			{
				Vector2 menuPos = new(left, y - 0.6f);
				actions.Add(("COLOUR AND MORE...", () => ContextMenu.OpenFor(element, menuPos), true, false));
			}

			if (delete.HasValue) actions.Add(delete.Value);
			DrawActions(left, y - 0.6f, width, -1);
		}

		static string ActionLabel(string menuText) => menuText switch
		{
			"VIEW" => "LOOK INSIDE",
			"OPEN" => "EDIT THIS CHIP",
			"MIRROR H" => "MIRROR HORIZONTALLY",
			"MIRROR V" => "MIRROR VERTICALLY",
			_ => menuText
		};

		// ---- Several elements ----

		static void DrawMultiple(Project project, List<IMoveable> selection, float left, float top, float width)
		{
			int chips = selection.OfType<SubChipInstance>().Count();
			int pins = selection.OfType<DevPinInstance>().Count();
			float y = Title(selection.Count + " SELECTED", left, top, width, null);
			if (chips > 0) y = Line(chips == 1 ? "1 part" : chips + " parts", left, y, width, RewiredUI.SecondaryText);
			if (pins > 0) y = Line(pins == 1 ? "1 pin" : pins + " pins", left, y, width, RewiredUI.SecondaryText);

			bool canEdit = project.CanEditViewedChip;
			ChipInteractionController controller = project.controller;
			SubChipInstance[] subchips = selection.OfType<SubChipInstance>().Where(s => !s.IsBus).ToArray();

			actions.Clear();
			actions.Add(("DUPLICATE", controller.DuplicateSelection, canEdit, false));
			actions.Add(("COPY", controller.CopySelection, controller.CanCopy, false));
			actions.Add(("MIRROR HORIZONTALLY", () => controller.MirrorSubChips(subchips, true), canEdit && subchips.Length > 0, false));
			actions.Add(("MIRROR VERTICALLY", () => controller.MirrorSubChips(subchips, false), canEdit && subchips.Length > 0, false));
			actions.Add(("DELETE", controller.DeleteSelection, canEdit, true));
			DrawActions(left, y - 0.6f, width, -1);
		}

		// ---- Placing ----

		static void DrawPlacing(Project project, float left, float top, float width)
		{
			List<IMoveable> placing = project.controller.SelectedElements;
			string name = placing.Count == 1 && placing[0] is SubChipInstance s ? s.Description.Name : placing.Count == 1 && placing[0] is DevPinInstance p ? p.Name : placing.Count + " PARTS";
			float y = Title("PLACING " + name, left, top, width, null);
			Paragraph("Click on the board to put it down. Hold Shift to place several. Esc cancels.", left, y, width);
		}

		// ---- Building blocks ----

		// The name in large letters, with a swatch of the chip's colour before it. Returns the next line's top.
		static float Title(string text, float left, float top, float width, Color? swatch)
		{
			const float scale = 0.6f;
			float x = left;
			float centreY = top - 0.8f;
			if (swatch.HasValue)
			{
				UI.DrawPanel(new Vector2(left, centreY), new Vector2(0.9f, 0.9f), swatch.Value, Anchor.CentreLeft);
				x += 1.4f;
			}

			DesktopTheme.Text(DesktopTheme.Fit(text.ToUpperInvariant(), left + width - x, scale, true), new Vector2(x, centreY), scale, Color.white, Anchor.TextCentreLeft, true);
			return top - 1.9f;
		}

		static float Line(string text, float left, float top, float width, Color col, bool bold = false)
		{
			if (string.IsNullOrEmpty(text)) return top;
			const float scale = 0.44f;
			DesktopTheme.Text(DesktopTheme.Fit(text, width, scale, bold), new Vector2(left, top - LineHeight / 2), scale, col, Anchor.TextCentreLeft, bold);
			return top - LineHeight;
		}

		static float Fact(string label, string value, float left, float top, float width)
		{
			const float scale = 0.46f;
			float centreY = top - LineHeight / 2;
			DesktopTheme.Text(label, new Vector2(left, centreY), scale, RewiredUI.DimText);
			DesktopTheme.Text(value, new Vector2(left + width, centreY), scale, Color.white, Anchor.TextCentreRight);
			return top - LineHeight;
		}

		static void Paragraph(string text, float left, float top, float width)
		{
			const float scale = 0.42f;
			foreach (string line in MobileTheme.Wrap(text, width, scale))
			{
				DesktopTheme.Text(line, new Vector2(left, top - LineHeight / 2), scale, RewiredUI.DimText);
				top -= LineHeight * 0.95f;
			}
		}

		static float PinList(string heading, PinInstance[] pins, float left, float top, float width)
		{
			if (pins == null || pins.Length == 0) return top;

			DesktopTheme.Text(heading, new Vector2(left, top - LineHeight / 2), 0.4f, RewiredUI.SecondaryText, Anchor.TextCentreLeft, true);
			DesktopTheme.Text(pins.Length.ToString(), new Vector2(left + width, top - LineHeight / 2), 0.4f, RewiredUI.DimText, Anchor.TextCentreRight);
			top -= LineHeight;

			int listed = Mathf.Min(pins.Length, MaxPinsListed);
			for (int i = 0; i < listed; i++)
			{
				PinInstance pin = pins[i];
				string name = string.IsNullOrWhiteSpace(pin.Name) ? "(unnamed)" : pin.Name;
				float centreY = top - LineHeight / 2;
				UI.DrawPanel(new Vector2(left + 0.15f, centreY), new Vector2(0.4f, 0.4f), pin.GetColHigh(), Anchor.CentreLeft);
				DesktopTheme.Text(DesktopTheme.Fit(name, width - 4.5f, 0.42f), new Vector2(left + 0.9f, centreY), 0.42f, Color.white);
				DesktopTheme.Text(BitText(pin.bitCount), new Vector2(left + width, centreY), 0.4f, RewiredUI.DimText, Anchor.TextCentreRight);
				top -= LineHeight;
			}

			if (pins.Length > listed)
			{
				DesktopTheme.Text($"+{pins.Length - listed} more", new Vector2(left + 0.9f, top - LineHeight / 2), 0.4f, RewiredUI.DimText);
				top -= LineHeight;
			}

			return top;
		}

		static string BitText(PinBitCount bits) => (int)bits == 1 ? "1 bit" : (int)bits + " bits";

		// Full-width buttons for the actions collected above. Returns the bottom of the last one.
		static float DrawActions(float left, float top, float width, int primaryIndex)
		{
			Action pressed = null;
			for (int i = 0; i < actions.Count; i++)
			{
				(string label, Action onPress, bool enabled, bool danger) = actions[i];
				ButtonTheme theme = danger ? DesktopTheme.Danger : i == primaryIndex ? DesktopTheme.Primary : DesktopTheme.Neutral;

				// Keep the delete button apart from the others.
				if (danger) top -= 0.5f;
				if (DesktopTheme.Button(label, theme, new Vector2(left, top), new Vector2(width, ButtonHeight), enabled)) pressed = onPress;
				top -= ButtonHeight + ButtonGap;
			}

			pressed?.Invoke();
			return top;
		}

		static string ShortcutText(ShortcutAction action)
		{
			ShortcutBinding binding = KeyboardShortcuts.GetBinding(action);
			return binding.IsBound ? binding.ToDisplayString() : "the search button";
		}
	}
}
