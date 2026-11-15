; Polar Uninstaller NSIS Installer
; Version, output file and application EXE can be supplied on the command line:
;   makensis /DVERSION=1.0.129 /DOUTFILE=... /DAPPEXE=... PolarUninstaller.nsi
; The defaults match the current source version.

!ifndef VERSION
!define VERSION "1.0.144"
!endif
!ifndef OUTFILE
!define OUTFILE "PolarUninstaller-Setup-v${VERSION}.exe"
!endif
!ifndef APPEXE
!define APPEXE "..\Build\SingleFile\PolarUninstaller.exe"
!endif

!define APPNAME "Polar Uninstaller"
!define APPNAME_WPF "Polar Uninstaller"
!define COMPANY "Polar"
!define REGKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\PolarUninstallerWpfRust"
!define INSTALLDIR "$PROGRAMFILES64\Polar Uninstaller"

Name "${APPNAME} ${VERSION}"
OutFile "${OUTFILE}"
InstallDir "${INSTALLDIR}"
InstallDirRegKey HKLM "${REGKEY}" "InstallLocation"
RequestExecutionLevel admin
SetCompressor /SOLID lzma

VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "${APPNAME}"
VIAddVersionKey "CompanyName" "${COMPANY}"
VIAddVersionKey "FileVersion" "${VERSION}.0"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "FileDescription" "${APPNAME} Setup"
VIAddVersionKey "LegalCopyright" "Copyright Ac 2026 Polar"

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"

; Variables
Var WaitPid

; MUI settings
!define MUI_ABORTWARNING
!define MUI_ICON "..\Assets\Polar.ico"
!define MUI_UNICON "..\Assets\Polar.ico"
!define MUI_WELCOMEFINISHPAGE_BITMAP "..\Assets\InstallerWelcome.bmp"
!define MUI_HEADERIMAGE
!define MUI_HEADERIMAGE_BITMAP "..\Assets\InstallerHeader.bmp"
!define MUI_HEADERIMAGE_UNBITMAP "..\Assets\InstallerHeader.bmp"
!define MUI_FINISHPAGE_RUN "$INSTDIR\PolarUninstaller.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Launch ${APPNAME}"

; Pages
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

; --- Functions ---

Function .onInit
    StrCpy $WaitPid "0"
    ${GetParameters} $0
    ${GetOptions} $0 "--wait-pid" $1
    ${If} $1 != ""
        StrCpy $WaitPid $1
    ${EndIf}
    ${If} $WaitPid != "0"
        DetailPrint "Waiting for application to exit (PID $WaitPid)..."
        System::Call 'kernel32::OpenProcess(i 0x00100000, i 0, i $WaitPid) i .r0'
        ${If} $r0 != 0
            System::Call 'kernel32::WaitForSingleObject(i r0, i 30000) i .r1'
            System::Call 'kernel32::CloseHandle(i r0)'
        ${EndIf}
    ${EndIf}
    Sleep 1500
    ; close any lingering instance
    nsExec::ExecToLog 'taskkill /IM PolarUninstaller.exe /F'
    Sleep 1000
    ; Remove obsolete WPF-named shortcuts from previous installs (safe: Python never used this name)
    Delete "$SMPROGRAMS\Polar Uninstaller (WPF)\Polar Uninstaller.lnk"
    RMDir "$SMPROGRAMS\Polar Uninstaller (WPF)"
    Delete "$DESKTOP\Polar Uninstaller (WPF).lnk"
    ; Remove stale Polar shortcuts left by older Python/WPF layouts so they
    ; cannot keep showing an old icon next to the new one.
    Delete "$SMPROGRAMS\Polar Uninstaller.lnk"
    SetShellVarContext all
    Delete "$SMPROGRAMS\Polar Uninstaller.lnk"
    Delete "$SMPROGRAMS\Polar Uninstaller (WPF)\Polar Uninstaller.lnk"
    RMDir "$SMPROGRAMS\Polar Uninstaller (WPF)"
    Delete "$DESKTOP\Polar Uninstaller (WPF).lnk"
    SetShellVarContext current
FunctionEnd

; --- Sections ---

Section "Application (required)" SEC_APP
    SectionIn RO
    SetOutPath "$INSTDIR"
    SetOverwrite on
    File "${APPEXE}"
    ; Icons are taken from PolarUninstaller.exe itself. Remove standalone copies
    ; left by earlier releases so no shortcut can keep pointing at an old logo.
    Delete "$INSTDIR\PolarLogo*.ico"
    Delete "$INSTDIR\PolarIcon.png"
    WriteUninstaller "$INSTDIR\Uninstall.exe"

    CreateDirectory "$SMPROGRAMS\${APPNAME_WPF}"
    ; Shortcut icon: the installed EXE, index 0. Never a separate .ico file.
    CreateShortCut "$SMPROGRAMS\${APPNAME_WPF}\${APPNAME}.lnk" "$INSTDIR\PolarUninstaller.exe" "" "$INSTDIR\PolarUninstaller.exe" 0
    ; Refresh an existing desktop shortcut even if its optional section is unchecked.
    IfFileExists "$DESKTOP\${APPNAME_WPF}.lnk" 0 +2
    CreateShortCut "$DESKTOP\${APPNAME_WPF}.lnk" "$INSTDIR\PolarUninstaller.exe" "" "$INSTDIR\PolarUninstaller.exe" 0

    WriteRegStr HKLM "${REGKEY}" "DisplayName" "${APPNAME_WPF}"
    WriteRegStr HKLM "${REGKEY}" "Publisher" "${COMPANY}"
    WriteRegStr HKLM "${REGKEY}" "DisplayVersion" "${VERSION}"
    WriteRegStr HKLM "${REGKEY}" "InstallLocation" "$INSTDIR"
    WriteRegStr HKLM "${REGKEY}" "DisplayIcon" "$INSTDIR\PolarUninstaller.exe,0"
    WriteRegStr HKLM "${REGKEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
    WriteRegDWORD HKLM "${REGKEY}" "NoModify" 1
    WriteRegDWORD HKLM "${REGKEY}" "NoRepair" 1
    System::Call 'shell32::SHChangeNotify(i 0x2000, i 0x3005, w "$SMPROGRAMS\${APPNAME_WPF}\${APPNAME}.lnk", p 0)'
    System::Call 'shell32::SHChangeNotify(i 0x2000, i 0x3005, w "$INSTDIR\PolarUninstaller.exe", p 0)'
SectionEnd

Section "Desktop shortcut" SEC_DESKTOP
    CreateShortCut "$DESKTOP\${APPNAME_WPF}.lnk" "$INSTDIR\PolarUninstaller.exe" "" "$INSTDIR\PolarUninstaller.exe" 0
    System::Call 'shell32::SHChangeNotify(i 0x2000, i 0x3005, w "$DESKTOP\${APPNAME_WPF}.lnk", p 0)'
SectionEnd

; SHCNE_ASSOCCHANGED (0x08000000) with SHCNF_IDLIST: rebuild Explorer's icon
; cache after every install or in-place upgrade so the new EXE icon is shown.
Function .onInstSuccess
    System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'
FunctionEnd


; --- Uninstall ---

Section "Uninstall"
    nsExec::ExecToLog 'taskkill /IM PolarUninstaller.exe /F'
    Sleep 1000
    ; Remove obsolete WPF-named shortcuts from previous installs (safe: Python never used this name)
    Delete "$SMPROGRAMS\Polar Uninstaller (WPF)\Polar Uninstaller.lnk"
    RMDir "$SMPROGRAMS\Polar Uninstaller (WPF)"
    Delete "$DESKTOP\Polar Uninstaller (WPF).lnk"
    Delete "$SMPROGRAMS\${APPNAME_WPF}\${APPNAME}.lnk"
    RMDir "$SMPROGRAMS\${APPNAME_WPF}"
    Delete "$DESKTOP\${APPNAME_WPF}.lnk"
    ; Also clean obsolete WPF-named and stale Python-layout shortcuts.
    Delete "$SMPROGRAMS\Polar Uninstaller.lnk"
    SetShellVarContext all
    Delete "$SMPROGRAMS\Polar Uninstaller.lnk"
    Delete "$SMPROGRAMS\Polar Uninstaller (WPF)\Polar Uninstaller.lnk"
    RMDir "$SMPROGRAMS\Polar Uninstaller (WPF)"
    Delete "$DESKTOP\Polar Uninstaller (WPF).lnk"
    SetShellVarContext current
    Delete "$INSTDIR\PolarUninstaller.exe"
    Delete "$INSTDIR\Uninstall.exe"
    Delete "$INSTDIR\PolarLogo*.ico"
    Delete "$INSTDIR\PolarIcon.png"
    DeleteRegKey HKLM "${REGKEY}"
    RMDir "$INSTDIR"
SectionEnd

; Rebuild Explorer's icon cache after removing the shortcuts and EXE.
Function un.onUnInstSuccess
    System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'
FunctionEnd

