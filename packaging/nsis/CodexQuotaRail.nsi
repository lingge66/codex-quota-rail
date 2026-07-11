Unicode True
RequestExecutionLevel user
SetCompressor /SOLID zlib

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "nsDialogs.nsh"

!ifndef VERSION
  !error "VERSION is required"
!endif
!ifndef PUBLISH_DIR
  !error "PUBLISH_DIR is required"
!endif
!ifndef ARTIFACT_DIR
  !error "ARTIFACT_DIR is required"
!endif

Name "Codex 可用额度边缘轨 ${VERSION}"
OutFile "${ARTIFACT_DIR}\CodexQuotaRail-Setup.exe"
InstallDir "$LOCALAPPDATA\Programs\CodexQuotaRail"
InstallDirRegKey HKCU "Software\CodexQuotaRail" "InstallDir"

Var StartMenuFolder
Var RemoveSettingsCheckbox
Var RemoveSettingsState

!define MUI_ABORTWARNING
!define MUI_ICON "${NSISDIR}\Contrib\Graphics\Icons\modern-install.ico"
!define MUI_UNICON "${NSISDIR}\Contrib\Graphics\Icons\modern-uninstall.ico"
!define MUI_STARTMENUPAGE_REGISTRY_ROOT "HKCU"
!define MUI_STARTMENUPAGE_REGISTRY_KEY "Software\CodexQuotaRail"
!define MUI_STARTMENUPAGE_REGISTRY_VALUENAME "StartMenuFolder"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_STARTMENU Application $StartMenuFolder
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_WELCOME
!insertmacro MUI_UNPAGE_CONFIRM
UninstPage custom un.RemoveSettingsPage un.RemoveSettingsPageLeave
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH

!insertmacro MUI_LANGUAGE "SimpChinese"
!insertmacro MUI_LANGUAGE "English"

Section "主程序（必需）" SEC_MAIN
  SectionIn RO
  SetOutPath "$INSTDIR"
  File /r "${PUBLISH_DIR}\*.*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\CodexQuotaRail" "InstallDir" "$INSTDIR"

  !insertmacro MUI_STARTMENU_WRITE_BEGIN Application
    CreateDirectory "$SMPROGRAMS\$StartMenuFolder"
    CreateShortcut "$SMPROGRAMS\$StartMenuFolder\Codex 可用额度边缘轨.lnk" "$INSTDIR\CodexQuotaRail.App.exe"
    CreateShortcut "$SMPROGRAMS\$StartMenuFolder\卸载.lnk" "$INSTDIR\Uninstall.exe"
  !insertmacro MUI_STARTMENU_WRITE_END

  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexQuotaRail" "DisplayName" "Codex 可用额度边缘轨"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexQuotaRail" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexQuotaRail" "Publisher" "LingGe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexQuotaRail" "DisplayIcon" "$INSTDIR\CodexQuotaRail.App.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexQuotaRail" "UninstallString" '$"$INSTDIR\Uninstall.exe$"'
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexQuotaRail" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexQuotaRail" "NoRepair" 1
SectionEnd

Section "开机时启动（推荐）" SEC_AUTOSTART
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "CodexQuotaRail" '$"$INSTDIR\CodexQuotaRail.App.exe$" --background'
SectionEnd

LangString DESC_SEC_MAIN ${LANG_SIMPCHINESE} "安装程序文件、开始菜单快捷方式和卸载器。"
LangString DESC_SEC_AUTOSTART ${LANG_SIMPCHINESE} "登录 Windows 后自动在托盘启动；安装后可随时从托盘关闭。"
LangString DESC_SEC_MAIN ${LANG_ENGLISH} "Install the application, Start Menu shortcuts, and uninstaller."
LangString DESC_SEC_AUTOSTART ${LANG_ENGLISH} "Start in the tray after Windows sign-in; this can be disabled from the tray."

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_MAIN} $(DESC_SEC_MAIN)
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_AUTOSTART} $(DESC_SEC_AUTOSTART)
!insertmacro MUI_FUNCTION_DESCRIPTION_END

Function un.RemoveSettingsPage
  nsDialogs::Create 1018
  Pop $0
  ${If} $0 == error
    Abort
  ${EndIf}
  ${NSD_CreateLabel} 0 0 100% 28u "默认保留本地设置和日志，便于以后重新安装。"
  Pop $0
  ${NSD_CreateCheckbox} 0 36u 100% 14u "同时删除本地设置"
  Pop $RemoveSettingsCheckbox
  ${NSD_Uncheck} $RemoveSettingsCheckbox
  nsDialogs::Show
FunctionEnd

Function un.RemoveSettingsPageLeave
  ${NSD_GetState} $RemoveSettingsCheckbox $RemoveSettingsState
FunctionEnd

Section "Uninstall"
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "CodexQuotaRail"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexQuotaRail"
  ReadRegStr $StartMenuFolder HKCU "Software\CodexQuotaRail" "StartMenuFolder"
  Delete "$SMPROGRAMS\$StartMenuFolder\Codex 可用额度边缘轨.lnk"
  Delete "$SMPROGRAMS\$StartMenuFolder\卸载.lnk"
  RMDir "$SMPROGRAMS\$StartMenuFolder"
  DeleteRegKey HKCU "Software\CodexQuotaRail"
  RMDir /r "$INSTDIR"
  ${If} $RemoveSettingsState == ${BST_CHECKED}
    RMDir /r "$LOCALAPPDATA\CodexQuotaRail"
  ${EndIf}
SectionEnd
