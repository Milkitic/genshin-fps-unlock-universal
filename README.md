# Genshin Impact FPS Unlocker

注：本fork已长期停更，原因是作者（我）早已不玩原神，没有自用价值失去维护动力
如果需要使用，请使用上游项目https://github.com/34736384/genshin-fps-unlock
如果需要在linux上跑，相信愿意折腾的你也绝对会自己合并上游的内存地址相关的修改！

A forked version which rewrites GUI and supports linux with WINE.

![image](https://github.com/Milkitic/genshin-fps-unlock-universal/assets/24785749/e92fe460-c045-46ef-bbf1-7f350e7eb179)

> Running in Windows 11 & Linux KDE

 - This tool helps you to unlock the 60 fps limit in the game
 - This is an external program which uses **WriteProcessMemory** to write the desired fps to the game
 - Handle protection bypass is already included
 - Does not require a driver for R/W access
 - Supports OS and CN version
 - Should work for future updates
 - If the source needs to be updated, I'll try to do it as soon as possible
 - You can download the compiled binary over at '[Release](https://github.com/34736384/genshin-fps-unlock/releases)' if you don't want to compile it yourself
 - Free code signing provided by [SignPath.io](https://signpath.io/)

 ## Compiling
 1. Install Visual Studio 2022 with Desktop C++ workload in Visual Studio Installer.
 2. Install .NET 8 SDK.
 3. Use `dotnet build ./unlockfps_gui` for regular compiling. Use `dotnet publish ./unlockfps_gui -c Release -r win-x64` for AOT publish.

 ### Cross Compiling on GNU/Linux
 You need [dotnet-8 SDK for Linux](https://dotnet.microsoft.com/en-us/download/dotnet) for .NET apps and x86_64-w64-mingw32 tool-chain for compiling the UnlockerStub to dll. Take Debian as an example, `apt install mingw-w64` installs the MinGW tool-chain. Finally, use `make` to build.

 ## Usage
 
 ### Running on Windows

 - Run the exe and click 'Launch'
 - If it is your first time running, unlocker will attempt to find your game through the registry. If it fails, then it will ask you to either browse or run the game.
 - Place the compiled exe anywhere you want (except for the game folder)
 - Make sure your game is closed—the unlocker will automatically start the game for you
 - Run the exe as administrator, and leave the exe running
 >It requires adminstrator because the game needs to be started by the unlocker and the game requires such permission
 - To load other third-party plugins, go to `Options->Settings->DLLs` and click add

 ### Running with wine

 #### Prerequisite
 
 ```
 WINEPREFIX=... winetricks dotnet45
 WINEPREFIX=... winecfg -v win7
 ```
 #### Running
 
 ```
 WINEPREFIX=... wine unlockfps.exe
 ```
 ![Screenshot_20240206_144503](https://github.com/Milkitic/genshin-fps-unlock-aot/assets/24785749/c1e0377a-89eb-49f6-958a-37b9229c5875)

 ## Version 3.0.0 Changes
 - Rewritten the project in .NET 8
 - Added a launch option to use mobile UI (for streaming from mobile devices or touchscreen laptops)
 ## Notes
 - HoYoverse (miHoYo) is well aware of this tool, and you will not get banned for using **ONLY** fps unlock.
 - If you are using other third-party plugins, you are doing it at your own risk.
 - Any artifacts from unlocking fps (e.g. stuttering) is **NOT** a bug of the unlocker

# 原神解锁FPS限制

 - 工作原理类似于外部辅助，通过**WriteProcessMemory**把FPS数值写进游戏
 - 不需要通过驱动进行读写操作
 - 支持国服和外服
 - 理论上支持后续版本，不需要更新源码
 - 如果需要更新我会尽快更新

## 编译
 - 用VS2022编译

### GNU/Linux 上交叉编译
见 [Cross Compiling on GNU/Linux](#cross-compiling-on-gnulinux)

## 食用指南
 - 运行前确保系统已安装[.NET Desktop Runtime 8.0.0](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-8.0.0-windows-x64-installer) (一般来说系统自带就有)
 - 第一次运行的话先以管理员运行，解锁器会尝试通过注册表寻找游戏路经，如果找不到的话会提示你浏览游戏位置或者开启游戏
 - 解锁器放哪都行
 - 运行之前确保游戏是关闭的
 - 用管理员运行解锁器
 - 解锁器不能关掉
>使用管理员运行是因为游戏必须由解锁器启动，游戏本身就需要管理员权限了，所以负责启动的也是需要的

## 3.0.0 版本更新
 - 用.NET 8重写了项目
 - 添加了一个启动项，可以以移动端UI启动

