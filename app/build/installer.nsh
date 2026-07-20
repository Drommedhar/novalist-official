; Custom NSIS include, auto-picked up by electron-builder (build/installer.nsh).
;
; Migration from the old Avalonia release: that build shipped as an Inno Setup
; installer with a different AppId, install location (C:\Program Files\Novalist)
; and uninstall registry key, so Windows treats it as a separate product. Without
; this, installing the new electron build leaves the old app behind as a second,
; orphaned "Novalist" install. Here we detect the old Inno entry and run its
; uninstaller silently before the new install proceeds.

!macro customInit
  ; Inno Setup appends "_is1" to the AppId when writing its uninstall key.
  !define OLD_INNO_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\{4F96BF95-1D05-4F60-93DD-F3C3F564A845}_is1"

  ; The old installer was per-machine (admin, Program Files). Inno may have
  ; recorded the entry in the 64-bit or 32-bit (WOW6432Node) HKLM view, or in
  ; HKCU for a per-user install. Check each until we find an uninstall string.
  StrCpy $R0 ""

  SetRegView 64
  ReadRegStr $R0 HKLM "${OLD_INNO_KEY}" "UninstallString"

  ${If} $R0 == ""
    SetRegView 32
    ReadRegStr $R0 HKLM "${OLD_INNO_KEY}" "UninstallString"
  ${EndIf}

  ${If} $R0 == ""
    ReadRegStr $R0 HKCU "${OLD_INNO_KEY}" "UninstallString"
  ${EndIf}

  SetRegView lastused

  ${If} $R0 != ""
    DetailPrint "Removing previous Novalist installation..."
    ; UninstallString is the quoted path to Inno's unins000.exe. The flags make
    ; it non-interactive; the old app itself elevates via its own manifest if the
    ; previous install was per-machine. ExecWait blocks until removal completes.
    ExecWait '$R0 /VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
  ${EndIf}

  !undef OLD_INNO_KEY
!macroend
