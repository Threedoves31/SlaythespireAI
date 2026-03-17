@echo off
chcp 65001 >nul
setlocal EnableDelayedExpansion

echo ====================================================
echo      STS2 AI Bot - 快速编译安装脚本
echo ====================================================
echo.

:: 配置路径
set "PROJECT_DIR=%~dp0src\mod\STS2AIBot"
set "MOD_DIR=%~dp0src\mod"
set "INSTALL_DIR=D:\Steam\steamapps\common\Slay the Spire 2\mods\STS2AIBot"

:: 步骤 1: 编译
echo [1/3] 编译 Mod...
cd /d "%PROJECT_DIR%"
dotnet build -c Debug
if errorlevel 1 (
    echo.
    echo [错误] 编译失败！请检查错误信息。
    pause
    exit /b 1
)
echo [OK] 编译成功！
echo.

:: 步骤 2: 创建安装目录
echo [2/3] 创建安装目录...
if not exist "%INSTALL_DIR%" (
    mkdir "%INSTALL_DIR%"
    echo    创建目录: %INSTALL_DIR%
)
echo [OK] 目录已准备
echo.

:: 步骤 3: 复制文件
echo [3/3] 复制文件到 Steam Mods 目录...

:: 复制 DLL
set "DLL_SRC=%PROJECT_DIR%\bin\Debug\net9.0\STS2AIBot.dll"
if exist "%DLL_SRC%" (
    copy /Y "%DLL_SRC%" "%INSTALL_DIR%\" >nul
    echo    [OK] STS2AIBot.dll
) else (
    echo    [错误] 找不到 STS2AIBot.dll
)

:: 复制 PDB (调试符号，可选)
set "PDB_SRC=%PROJECT_DIR%\bin\Debug\net9.0\STS2AIBot.pdb"
if exist "%PDB_SRC%" (
    copy /Y "%PDB_SRC%" "%INSTALL_DIR%\" >nul
    echo    [OK] STS2AIBot.pdb (调试符号)
)

:: 复制 PCK (如果存在)
set "PCK_SRC=%MOD_DIR%\STS2AIBot.pck"
if exist "%PCK_SRC%" (
    copy /Y "%PCK_SRC%" "%INSTALL_DIR%\" >nul
    echo    [OK] STS2AIBot.pck
) else (
    echo    [跳过] 无 PCK 文件 (可选)
)

:: 复制 modinfo.json
set "INFO_SRC=%MOD_DIR%\modinfo.json"
if exist "%INFO_SRC%" (
    copy /Y "%INFO_SRC%" "%INSTALL_DIR%\" >nul
    echo    [OK] modinfo.json
) else (
    echo    [错误] 找不到 modinfo.json
)

:: 复制 mod_manifest.json (可选)
set "MANIFEST_SRC=%MOD_DIR%\mod_manifest.json"
if exist "%MANIFEST_SRC%" (
    copy /Y "%MANIFEST_SRC%" "%INSTALL_DIR%\" >nul
    echo    [OK] mod_manifest.json
)

echo.
echo ====================================================
echo              安装完成！
echo ====================================================
echo.
echo   安装位置: %INSTALL_DIR%
echo.
echo   游戏内热键:
echo     F2 - 切换 AI 策略 (Heuristic/Simulation/Random)
echo     F3 - 暂停/继续
echo     F4 - 手动模式
echo.
echo ====================================================
echo.

:: 询问是否打开安装目录
set /p OPEN_DIR="是否打开安装目录? (Y/N): "
if /i "%OPEN_DIR%"=="Y" (
    explorer "%INSTALL_DIR%"
)

pause