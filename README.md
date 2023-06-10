# xi-host

XI Host is a set of .NET libraries and applications that support hosting components of an XI game server.

# State of the Code

XI Host is in the refinement, refactoring, and functional parity phases with [LandSandBoat server](https://github.com/LandSandBoat/server).  It is the intent of this software to provide an alternate solution to the login (and/or messaging and world) functionality and not a direct port.

Please familiarize yourself with the expectations indicated in [CONTRIBUTING.md](/CONTRIBUTING.md) and [CODE_OF_CONDUCT.md](/CODE_OF_CONDUCT.md).

# License

[GNU GPL v3](/LICENSE)

# Requirements

1. Visual Studio 2022 or MS Build Tools 2022
1. .NET 6.0
1. An OS capable of running the above stuff

# Building On Windows For

Debug mode will build assemblies with placeholder information.  Release mode will build assemblies with dynamic information.

## Windows

Nothing special.

## Linux

	dotnet publish XI.Host.sln -c Release --os linux --self-contained

The above command will not output some required files to the publish directory.  Use these commands from the Solution root.

	copy /Y host.json "Output\net6.0\linux-x64\publish\host.json"
	xcopy "clrzmq4\build\amd64" "Output\net6.0\linux-x64\publish" /Y

Use your favorite way to zip the published files for transport to Linux, or use the following PowerShell command.

	Compress-Archive 'Output\net6.0\linux-x64\publish' -DestinationPath 'Output\net6.0\linux-x64\XI.Host.zip'

# Building On Linux

Untested.  PowerShell may be necessary (or delete the PowerShell call in the pre-build step in the Common project).

# Running

## Overview

- Update the Solution Items' host.json with the location of the server settings (relative or absolute) path.
- Build or rebuild the solution (Release mode will version the assemblies with the current days' date).
- Make sure all game server applications are closed.
- From the output directory, run the World App.  World App may fail to bind if the map servers are already running.
- Run the xi_search and map applications.
- From the output directory, run the Login App.

## On Windows

All details in this section are optional.

In the case of a fresh Windows Server instance.  Update the admin account, so that the credentials do not expire.

	WMIC USERACCOUNT WHERE Name='admin' SET PasswordExpires=FALSE

Check expiration with:

	net user admin | findstr /C:expires

If Japanese Standard Time is needed, run the following command, and then reboot:

	tzutil /s "Tokyo Standard Time_dstoff"

Add in-bound rules to the firewall as necessary.  For example:

	netsh advfirewall firewall add rule name="XI Login 54230" dir=in action=allow protocol=TCP localport=54230

## On Linux

Make sure to set these files with execute permission.  For example:

	chmod +x XI.Host.Login.App
	chmod +x XI.Host.World.App

Update ServerSettingsDirectory host.json value with the relative or absolute path to the server settings.  For example:

	from
	"ServerSettingsDirectory": "../server/settings",
	to
	"ServerSettingsDirectory": "../../your-server/settings",

To run, always start the World App first, as it needs to bind the socket to work properly.  Also, do not close the World App and leave xi_map running, as one of the xi_map will attempt to bind to the same socket, which is typically only allowed once for all application running on the system.  Refer to the following example order:

	./XI.Host.World.App
	./xi_search
	./xi_map --ip 1.1.1.1 --port 54230
	./xi_map --ip 1.1.1.1 --port 54231
	./XI.Host.Login.App

Wait for all xi_map instances to report ready, check maintenance mode is correct (either on or off), and then start the Login App.  This will ensure players attempting to select a character after logging in can also zone-in without black screen waiting.

Note: Going between maintenance mode on and off requires restarting Login App.

# Debugging

## Visual Studio

1. If the client reports (for example) error 3308, search (Ctrl + F) for 308, and a result should be found in Common.Response project.
1. Left-click on an entry in the Find pane to navigate automatically to the entry.
1. Right-click on the constant name, and then select Find All References from the context menu.
1. In the references pane, cycle through each entry, and, for each, left-click the breakpoint bar on the left side of the code editing pane to set a breakpoint (indicated by a red dot).
1. Repeat the action that caused the initial client error, and then one of the breakpoints should get hit.

Waiting too long at any breakpoint will cause the client to timeout.

# Testing

## Checklist

1. Able to login
1. Able to logout from the title screen
1. Able to login again after logging out from the title screen
1. Able to create a character
1. Able to create a second character
1. Able to select a character and zone-in
1. TODO: Able to delete a character
1. Able to /say and others see it
1. Able to /shout and others see it
1. Able to /yell and others see it
1. Able to /tell and the expected player sees it
1. Not able to /party when not in a party
1. Able to join a party
1. Able to /party and other party members see it
1. Able to leave a party
1. Able to disband a party
1. Able to join an alliance
1. Able to /party and the alliance sees it
1. Not able to /linkshell when not in a linkshell
1. Able to join a linkshell by equiping a pearl
1. Able to /linkshell and linkshell members see it
1. Able to promote a linkshell member from pearl to sack
1. Able to see auction sales when they happen
1. Able to logout to the title screen, re-select a character, and zone-in
1. Able to login with multiple accounts from the same IP address
1. FIX: Not able to return to the title screen from a computer with multiple accounts logged in
1. Able to return to the title screen from a computer that had multiple accounts logged in
1. TODO: Able to logout to the title screen from a computer with multiple accounts logged in
1. GM characters are able to send system messages and others see it
1. GM characters can use various commands, such as !bring, !goto, and !jail as expected

# Behavioral Notes

- This code manages memory and network connections gracefully.  It should not require restarting.
- In combination with [xiloader updates made in branch xi-host](https://github.com/CarbyShop/xiloader/tree/xi-host), login and logout of multiple game clients from the same IP address is supported.
- If maintenance is on, only accounts with GM characters can login and any non-GM character on the account will fail to character select with a world server maintenance error message.  This reduces stress on the login during maintenance, so that accounts with GM characters can login with priority.

# Not Implemented

- Renaming of a character that has been marked for rename by the server administrator.

# Credits and History

This software was originally ported from [Darkstar Project](https://github.com/DarkstarProject) and re-designed by [Brian Kesecker](https://github.com/bkesecker) specifically for [Eden Server](https://github.com/EdenServer) in Feburary 2020.  See the State of the Code section for what is currently going on.
