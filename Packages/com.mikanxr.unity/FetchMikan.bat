@echo off
setlocal
:: Copies the C# binding sources into Runtime\Bindings from an installed MikanXR client SDK
:: (dist\ClientSDK\Win64 of a MikanXR checkout, or an unpacked Mikan_<version>_Win64_ClientSDK.zip).
:: The SDK path comes from the first argument or from MIKAN_CLIENT_SDK. Nothing native is copied:
:: the Unity client runs on the managed websocket transport and writes its Spout senders itself.
set "SDK=%~1"
if "%SDK%"=="" set "SDK=%MIKAN_CLIENT_SDK%"
if "%SDK%"=="" (
  echo usage: FetchMikan.bat ^<path to ClientSDK\Win64^>  ^(or set MIKAN_CLIENT_SDK^)
  exit /b 1
)
if not exist "%SDK%\bindings\csharp\src\MikanAPI.cs" (
  echo No C# binding sources under "%SDK%\bindings\csharp\src"
  exit /b 1
)
set "DEST=%~dp0Runtime\Bindings"
if exist "%DEST%" rmdir /s /q "%DEST%"
robocopy "%SDK%\bindings\csharp\src" "%DEST%" *.cs /s /njh /njs /ndl /nfl >nul
if errorlevel 8 (
  echo Copy failed
  exit /b 1
)
copy /y "%SDK%\version.txt" "%DEST%\version.txt" >nul
echo Copied the C# bindings from "%SDK%"
exit /b 0
