# Manual checklist

`run.ps1` covers everything that can be checked without real input. A real mouse and keyboard cannot be sent
to another virtual desktop, so these are checked by hand, in an Explorer window on your own desktop, in a
folder with a panel (for example `examples/fastapi-react`).

## Mouse

- [ ] Hovering a button highlights it; the cursor becomes a hand.
- [ ] Hovering shows the tooltip after a short delay; it disappears when the cursor leaves.
- [ ] Clicking a file right next to the panel selects the file (the click is not eaten by the panel).
- [ ] Clicking a file *under the transparent area above the panel* selects the file.
- [ ] The calendar and the menu open above the panel, can be clicked, and close on a click elsewhere.
- [ ] Clicking a panel button while another program's window is in front runs the action and brings
      Explorer forward.

## Feel

- [ ] Drag the Explorer window quickly: the panel follows; any lag is a frame or two.
- [ ] Resize from the bottom-right corner: the panel stays in its corner.
- [ ] Minimize and restore, maximize and restore: the panel is in place each time.
- [ ] Switch tabs: the panel is only in the tab that shows a folder with a panel.
- [ ] Put another window (a browser) over Explorer: the panel does not show through it.
- [ ] Alt+Tab and the taskbar do not list the panel as a window.
- [ ] Move the Explorer window to another virtual desktop (Win+Tab): the panel goes with it.

## Display

- [ ] Display scale 150 %: the panel is in the corner and sharp.
- [ ] Second monitor with a different scale: drag the window across; the panel stays attached.
- [ ] Light theme: the panel is readable.
- [ ] Details pane on, navigation pane widened: the panel stays inside the file list area.

## System

- [ ] Sign out and in: the tray icon is there; panels appear.
- [ ] Restart Explorer (Task Manager → Windows Explorer → Restart): panels come back within a few seconds.
- [ ] Tray menu: pause hides all panels; allowed and blocked folders are listed; exit closes the program and
      leaves started servers running.
