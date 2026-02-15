; COR Cleanup — NSIS Installer Script
; Produces a signed .exe installer that registers in Add/Remove Programs
; Build: makensis /DVERSION=x.y.z /DPUBLISH_DIR=path cor-cleanup.nsi

;---------------------------------------------------------------------------
; Build-time defines (passed from CI via /D flags)
;---------------------------------------------------------------------------
!ifndef VERSION
  !define VERSION "1.0.5"
!endif

!ifndef PUBLISH_DIR
  !define PUBLISH_DIR "..\publish"
!endif

!define APP_NAME        "COR Cleanup"
!define APP_EXE         "COR Cleanup.exe"
!define COMPANY         "COR Intelligence"
!define COMPANY_FULL    "COR Solutions Services Ltd"
!define WEBSITE         "https://corsolutions.co.uk"
!define UNINSTALL_KEY   "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}"
!define INSTALL_DIR     "$PROGRAMFILES64\${APP_NAME}"

;---------------------------------------------------------------------------
; Installer attributes
;---------------------------------------------------------------------------
Name "${APP_NAME} ${VERSION}"
OutFile "CORCleanup-Setup-${VERSION}.exe"
InstallDir "${INSTALL_DIR}"
InstallDirRegKey HKLM "${UNINSTALL_KEY}" "InstallLocation"
RequestExecutionLevel admin
Unicode True
SetCompressor /SOLID lzma
BrandingText "${COMPANY}"

;---------------------------------------------------------------------------
; Modern UI 2
;---------------------------------------------------------------------------
!include "MUI2.nsh"
!include "FileFunc.nsh"

; Installer icon (use the app icon)
!define MUI_ICON "..\CORCleanup\Assets\cor-cleanup.ico"
!define MUI_UNICON "..\CORCleanup\Assets\cor-cleanup.ico"

; Header and welcome images — use default MUI graphics
!define MUI_ABORTWARNING

; Welcome page
!define MUI_WELCOMEPAGE_TITLE "Welcome to ${APP_NAME} Setup"
!define MUI_WELCOMEPAGE_TEXT "This wizard will install ${APP_NAME} ${VERSION} on your computer.$\r$\n$\r$\n${APP_NAME} is an all-in-one Windows system administration toolkit by ${COMPANY}. It replaces CCleaner, Speccy, CrystalDiskInfo, Advanced IP Scanner, Revo Uninstaller, BlueScreenView and more.$\r$\n$\r$\nClick Next to continue."

; Finish page
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "Launch ${APP_NAME}"
!define MUI_FINISHPAGE_LINK "${COMPANY} Website"
!define MUI_FINISHPAGE_LINK_LOCATION "${WEBSITE}"

;---------------------------------------------------------------------------
; Pages
;---------------------------------------------------------------------------
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

;---------------------------------------------------------------------------
; Language
;---------------------------------------------------------------------------
!insertmacro MUI_LANGUAGE "English"

;---------------------------------------------------------------------------
; Installer Section
;---------------------------------------------------------------------------
Section "Install"
  SetOutPath "$INSTDIR"

  ; Close running instance if any
  nsExec::ExecToLog 'taskkill /F /IM "${APP_EXE}" /T'

  ; Copy main application
  File "${PUBLISH_DIR}\${APP_EXE}"

  ; Copy icon for Add/Remove Programs
  File "..\CORCleanup\Assets\cor-cleanup.ico"

  ; Create Start Menu shortcuts
  CreateDirectory "$SMPROGRAMS\${APP_NAME}"
  CreateShortcut "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\cor-cleanup.ico" 0
  CreateShortcut "$SMPROGRAMS\${APP_NAME}\Uninstall ${APP_NAME}.lnk" "$INSTDIR\Uninstall.exe"

  ; Create Desktop shortcut
  CreateShortcut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\cor-cleanup.ico" 0

  ; Write uninstaller
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  ; Calculate installed size (in KB)
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2

  ;-----------------------------------------------------------------------
  ; Register in Add/Remove Programs
  ;-----------------------------------------------------------------------
  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayName"      "${APP_NAME}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "UninstallString"   '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKLM "${UNINSTALL_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayIcon"       "$INSTDIR\cor-cleanup.ico"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "Publisher"         "${COMPANY_FULL}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayVersion"    "${VERSION}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "URLInfoAbout"      "${WEBSITE}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "InstallLocation"   "$INSTDIR"
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "EstimatedSize"   $0
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "NoModify"        1
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "NoRepair"        1

SectionEnd

;---------------------------------------------------------------------------
; Uninstaller Section
;---------------------------------------------------------------------------
Section "Uninstall"

  ; Close running instance
  nsExec::ExecToLog 'taskkill /F /IM "${APP_EXE}" /T'

  ; Remove application files
  Delete "$INSTDIR\${APP_EXE}"
  Delete "$INSTDIR\cor-cleanup.ico"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"

  ; Remove Start Menu shortcuts
  Delete "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk"
  Delete "$SMPROGRAMS\${APP_NAME}\Uninstall ${APP_NAME}.lnk"
  RMDir "$SMPROGRAMS\${APP_NAME}"

  ; Remove Desktop shortcut
  Delete "$DESKTOP\${APP_NAME}.lnk"

  ; Remove registry entries
  DeleteRegKey HKLM "${UNINSTALL_KEY}"

  ; Remove app data (optional — only log/config, never user backups)
  ; User is warned that registry backups in %APPDATA%\COR Cleanup\Backups are preserved
  MessageBox MB_YESNO "Remove application settings and logs?$\r$\n$\r$\n(Registry backups in %APPDATA%\COR Cleanup\Backups will be preserved)" IDNO skip_appdata
    RMDir /r "$APPDATA\${APP_NAME}\Logs"
    RMDir /r "$APPDATA\${APP_NAME}\Settings"
    ; Intentionally NOT removing Backups folder
  skip_appdata:

SectionEnd
