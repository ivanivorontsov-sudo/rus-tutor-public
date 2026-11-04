Unicode true
!include "MUI2.nsh"
Name "Репетитор по русскому языку"
OutFile "..\dist\RusTutor-Setup-1.0.0.exe"
InstallDir "$LOCALAPPDATA\Programs\RusTutor"
RequestExecutionLevel user
SetCompressor /SOLID lzma
!define MUI_ICON "..\src\RusTutor\app.ico"
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\RusTutor.exe"
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "Russian"
Section "RusTutor"
  SetOutPath "$INSTDIR"
  File /r "..\out\RusTutor\*.*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateDirectory "$SMPROGRAMS\Репетитор по русскому"
  CreateShortcut "$SMPROGRAMS\Репетитор по русскому\Репетитор по русскому.lnk" "$INSTDIR\RusTutor.exe"
  CreateShortcut "$SMPROGRAMS\Репетитор по русскому\Удалить.lnk" "$INSTDIR\Uninstall.exe"
  CreateShortcut "$DESKTOP\Репетитор по русскому.lnk" "$INSTDIR\RusTutor.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\RusTutor" "DisplayName" "Репетитор по русскому языку (ОГЭ/ЕГЭ)"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\RusTutor" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\RusTutor" "DisplayIcon" "$INSTDIR\RusTutor.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\RusTutor" "DisplayVersion" "1.0.0"
SectionEnd
Section "Uninstall"
  ExecWait 'taskkill /f /im RusTutor.exe'
  RMDir /r "$INSTDIR"
  RMDir /r "$SMPROGRAMS\Репетитор по русскому"
  Delete "$DESKTOP\Репетитор по русскому.lnk"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\RusTutor"
SectionEnd
