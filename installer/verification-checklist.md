# Manual Verification Checklist — v1.0 Release

## 20.5 Fresh Windows User Account Verification

Run these steps on a clean Windows 10/11 user account with no prior Why Save install.

### MSI Install (no UAC)
- [ ] Run `WhySave.msi`
- [ ] No UAC prompt appears (per-user install)
- [ ] Files installed to `%LOCALAPPDATA%\Programs\WhySave\`

### Start with Windows
- [ ] Log out and log back in
- [ ] Why Save starts automatically (tray icon appears)
- [ ] `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\WhySave` registry entry exists

### Core Functionality
- [ ] Drop a PDF into Downloads → toast appears after ~1.5s quiet period
- [ ] Click "Add Context" on toast → Add Context form opens
- [ ] Fill reason + project → Save → row becomes `contexted`
- [ ] Inbox tab shows pending files; Library shows legacy + contexted
- [ ] Search tab: keyword finds file; `reason:term` finds by reason; empty state works
- [ ] Settings window: all sections (folders, junk rules, hotkey, encryption, updates, logging)

### Auto-Update (if enabled)
- [ ] Set auto-update to "stable" in Settings
- [ ] Stage a newer build at the feed URL with a higher version number
- [ ] On next launch (or within 24h), the update is detected
- [ ] Download completes and `WhySave.App.exe.new` appears in install dir
- [ ] On app exit, the updater stub swaps the exe
- [ ] On next launch, the new version is running

## 21.3 Manual UI Checklist

### Tray Menu
- [ ] Tray icon visible with "Y" icon
- [ ] Right-click shows: Search, Memory Inbox (with count), Library, Settings, Pause watching, Exit
- [ ] "🔴 N files need context" badge when N>0, no badge when N=0
- [ ] Left-click opens Search tab

### Search Tab
- [ ] Search box + Search button
- [ ] Results show filename, reason snippet, project, saved date, status badge
- [ ] "Open" button launches file with OS default handler
- [ ] "Edit context" button opens Add Context form
- [ ] No results → empty state message

### Inbox Tab
- [ ] Lists pending files with "Add why" button
- [ ] Bulk-select → "Dismiss to legacy" works
- [ ] Bulk-select → "Delete record" works
- [ ] Empty inbox → empty state message

### Library Tab
- [ ] Lists legacy + contexted files
- [ ] Browse filter works
- [ ] Status badges correct (Legacy=blue, Contexted=green)

### Settings Window
- [ ] Watched folders: add/remove, default Downloads shown
- [ ] Junk rules: block/allow globs, min-size slider, Apply + Reset
- [ ] Hotkey: format validation, conflict reporting
- [ ] Start with Windows toggle
- [ ] Encryption: status, Rotate key, Export data, Clear data
- [ ] Auto-update toggle (off by default)
- [ ] Verbose logging toggle

### Add Context Form
- [ ] Pre-filled with path, filename, ext, saved date
- [ ] Reason (multiline), Project (autocomplete), Source URL, Notes (multiline), Saved date
- [ ] Save → status becomes contexted, row leaves Inbox
- [ ] Cancel → no changes

### Toast Under Focus Assist
- [ ] With Focus Assist on: toast suppressed or routed to Action Center
- [ ] With Focus Assist off: toast appears, no focus steal
- [ ] Dismissed toast → file stays pending in Inbox
