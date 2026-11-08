# Rewired v0.7.0

Rewired v0.7.0 gives the desktop editor a new layout: a top bar with the tools you use most, a parts panel on the left, a details panel on the right, search for parts and commands, and a start screen with your recent projects and the examples. The browser (WebGL) version works again, and the example projects no longer say NOT SAVED as soon as they are opened.

## Desktop editor

- **Top bar**: *MENU*, the chip being edited and its project (with *NOT SAVED*, and *PAUSED, STEP n* or the breakpoint while the simulation is paused), undo and redo, *RUN*/*PAUSE* and *STEP*, *Search*, zoom with the zoom level, *FIT*, and *PARTS* and *DETAILS* to show or hide the side panels. Rest the mouse on a button to see what it does and its shortcut.
- **Parts panel** (left): every collection, opened and closed with a click, and *MY CHIPS* with the chips you have saved. Drag a part onto the board, or click it and then the board. Right-click a part for its menu; *FIND* searches by name.
- **Details panel** (right): with nothing selected, the chip being edited (its inputs, outputs, parts and wires) with *SAVE CHIP* and *NEW CHIP*. With a part selected, its pins and a button for everything in its right-click menu. With several parts selected: duplicate, copy, mirror and delete.
- **Search** (*Ctrl+K*): type part of a name to add a chip, open a saved chip for editing, or run any command. Up and Down choose, Enter runs, Esc closes.
- **Keyboard shortcuts** (*F1*, also in *MENU*): every shortcut and mouse gesture on one card.
- **Start screen**: the five projects you used last open with one click, and the examples (*Snake*, *CPU8*, *Core8*) are listed below them. An example you deleted is copied back when you open it.
- The bar along the bottom holds your starred chips and collections. *FIT* and opening a chip fit it between the bars and panels.
- New shortcuts: *Ctrl+K* search, *F1* shortcuts, *Ctrl+B* parts panel, *Ctrl+I* details panel. Settings saved by an older version get them, unless their keys are already used by another action.

## Browser (WebGL)

- **The browser build draws again.** It showed an empty grey screen, because shapes and text were drawn with indirect instancing from structured buffers, which WebGL does not have. Where the graphics card cannot do that, Rewired now builds the shapes and letters into ordinary meshes each frame. Graphics cards without compute support on other platforms use the same way.

## Fixes

- The example projects (*CPU8*, *Core8*, and chips inside *Snake*) no longer show *NOT SAVED* right after they are opened. Chips built by RHDL are saved at their smallest size, and the editor showed them larger to fit their pins, which counted as a change.

## Compatibility

- Projects from v0.6.0 open unchanged.
- **Android**: v0.6.0 installs this version from the app with *UPDATE*. The touch interface is unchanged.

## Downloads

- Windows x64: `DLSRewired-Windows-x64.zip`
- Linux x86_64: `DLSRewired-Linux-x86_64.zip`
- WebGL (browser): `DLSRewired-WebGL.zip`. Unzip it and serve the folder from any static web host.
- Android ARM64: `DLSRewired-Android-arm64.apk` (experimental)

On Android, allow installing apps from your browser or file manager, then open the APK.
