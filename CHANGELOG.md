# FiveM Toolbox - Changelog

Versioning: 0.x.y = pre-1.0; minor feature bumps on 0.X, patches on y.
Tags: NEW, CHANGED, FIXED.

---

## v0.8.1 - 2026-11-10 (latest)

**NEW** - **A build that Radeon Software does not know yet is added by the program (AMD).** *Before, the program said "Not in Radeon Software yet" and you had to add the build by hand in Radeon Software (Gaming > Games). Now Apply does it: it makes a profile for the build through AMD's own library (the library Radeon Software uses itself), copied from the profile of another FiveM build, with Radeon Chill on and your cap in both values. Radeon Software closes for a moment and opens again, like for every change, and the program checks that AMD shows the new value. The build also shows in the Radeon Software list of games (Gaming > Games): a build that you removed there shows again, and a build that is not in the list gets a new entry. Undo takes the build out of Radeon Software again and hides it in the list, like the Remove button does. There must be one FiveM build in Radeon Software with Radeon Chill values to copy from; if there is none, the program still says to add one by hand. Tested on an AMD Radeon RX 9070 XT.*

**CHANGED** - **The log file is called FiveMToolbox.log.** *Before, it was FiveMToolbox-log.txt. It still sits next to the exe, or in %LOCALAPPDATA%\FiveMToolbox when the folder of the exe can not be written. An old FiveMToolbox-log.txt is not touched; you can delete it.*

**CHANGED** - **The backups go in a folder called "backup" next to the exe.** *Before, they were hidden in %LOCALAPPDATA%\FiveMToolbox\backup. Now the program makes a folder "backup" in the folder where FiveMToolbox.exe is (next to the README and the changelog) and puts every backup there: Radeon's profile file, Radeon's list of games, a copy of the NVIDIA driver database, FiveM's settings files, and the data of a Windows list entry that was cleaned up. The Backups button opens that folder. It is the same rule as for the log file: when the folder of the exe can not be written (Program Files, a read-only place), the folder is %LOCALAPPDATA%\FiveMToolbox\backup, as before. Older backups stay where they were. The small memory files (Undo, the lists, the settings) stay in %LOCALAPPDATA%\FiveMToolbox.*

**FIXED** - **The window can not be closed while a job changes something.** *Before, closing the window in the middle of a change for AMD could leave Radeon Software closed and a file half changed. Now the window says that a job is still running and stays open. A search only reads, so the window can still be closed during a search.*

**FIXED** - **Several small problems in the AMD changes.** *A second Apply on a build that the program added keeps the note that the program added it, so Undo still takes the build out again (before, Undo only set the old number back and the profile stayed). If something fails half way, Radeon's profile file goes back as it was. When Radeon Software can not be closed, nothing is changed. When AMD's library is not ready for a moment (Radeon Software is starting), the program says so; before it said "not in Radeon Software yet", and Undo forgot the build. A failure of the list-of-games file of Radeon Software no longer undoes a cap that works, and that file is written through a side file, so it is never half written. Undo puts the graphics preference back only when the cap is back.*

**FIXED** - **"Clean up old entries" for NVIDIA is safer.** *It removes nothing when no copy of the driver profile database could be made. It leaves a profile alone when the profile holds a program that is not a FiveM build, when it is not a profile of this program, or when its build is on disk but the list of builds does not show it this time. When the driver ignores a profile, the Undo memory of that build stays now (it belongs to the profile the driver really uses). Undo with a value that was saved for the other kind of graphics driver is skipped.*

**FIXED** - **Small files are written safely.** *The Undo memory, the DNS backup, the lists of builds and the settings are written to a side file first, and then the side file takes the place of the old file. Before, a crash or a bad character could leave an empty file.*

**FIXED** - **The log.** *A message with line breaks (a stack trace) moved the click places of the fold lines [+] and [-], so a click could fold the wrong job. The log file lost lines when two parts of the program wrote at once. The log did not change its colours when Windows changed between light and dark. It is also faster now, because the severity of a line is worked out once.*

**FIXED** - **A pasted number in the cap box.** *In Dutch Windows a pasted "45,5" was taken as a cap of 45. The box takes whole numbers only, in every language.*

**FIXED** - **A folder name with an "&" in it** (for example "Games & Mods") **showed without the "&"** in the folder list, on the activity card and in the log path.

**FIXED** - **The chip inside an old Ryzen or A-series processor** ("Radeon RX Vega 11 Graphics", "Radeon R7 Graphics") **was seen as a dedicated card.**

**CHANGED** - **The program loads every DLL (AMD, NVIDIA, Windows) from the Windows folder only.** *Before, a file with the same name next to the exe was tried first.*

**CHANGED** - **Smaller things.** *A failure while the program starts is written to the log now. The search for FiveM gives every drive its own time. A network drive that is gone no longer holds up the start. The 2 second timer rests while the window is minimised. Code that nothing used was removed.*

**CHANGED** - **A line in the log with a result has the colour as its background.** *Before, the text was coloured, and that was hard to see on the light theme. Now the line has a coloured background (green: good, yellow: a small problem, orange: a warning, red: an error, purple: something really broken) and the text keeps the normal colour. It is the same in the light and the dark theme, and the text is easy to read on every colour (checked by a self test).*

**CHANGED** - **The log keeps a finished job open while you stay on the tab.** *Before, a job (for example the search when the program starts) was folded into one line at once. Now it stays open while you are on the tab where it happened, and it folds up when you change to another tab. A job you open or fold by hand stays as you left it, a job with a problem stays open, and the pop-out log folds a finished job at once, as before.*

## v0.8.0 - 2026-11-10

**FIXED** - **The text was soft on a PC with screen scaling (125 percent, 150 percent).** *The program did not tell Windows that it knows about screen scaling, so Windows drew the window at 100 percent and stretched the picture. Now a normal start says it knows, and the whole window is scaled once when it opens: the letters, the buttons, the grids, the drawn parts, the small windows. Tested at 125 percent on a real screen with a self test that looks at every tab and every small window (blocks that overlap, parts that stick out, text that does not fit, grids with the wrong row height). The new switch --no-dpi starts the program the old way (stretched by Windows), for a PC where the scaling looks wrong. The new switch --dpi-test <file> runs the self test.*

**NEW** - **A button "Desktop icon: pure mode" on the FiveM cache tab.** *It makes an icon called "FiveM (pure mode)" on your desktop. The icon starts the FiveM.exe of the install that is chosen on the tab with -pure_1 (FiveM then ignores local file changes, for troubleshooting mods), with the FiveM icon. Press the button again and it says the icon is already there; for another install it updates the icon. It never makes a second icon. Tested in a scratch folder, never on your real desktop. New switch --pure-icon-test <file>.*

**FIXED** - **A wrong word in the Spanish texts.** *"Árralo" is now "Ciérralo" (close it).*

## v0.7.0 - 2026-11-10

**NEW** - **Frame cap for NVIDIA cards.** *The program uses the NVIDIA driver's own profile database (nvapi64.dll), the way NVIDIA Profile Inspector does. The setting "Frame Rate Limiter" is set for each FiveM build. A build that is already in a driver profile keeps using that profile, also the one FiveM makes itself ("Cfx.re: ..."). A build in no profile gets a profile of its own. After saving, the program opens the database again and checks that the driver kept the value. "Undo last change" puts the old limit back. A copy of the driver profile files is saved first. If saving needs administrator rights, the message says so.*

**NEW** - **A switch "Use the dedicated graphics card for FiveM".** *It shows only on a PC with an integrated chip and a dedicated card, and it is on by default. A frame cap only works when FiveM runs on the card of that driver. With the switch on, Apply also sets the Windows setting "Graphics performance preference" of every ticked build to high performance (Settings, System, Display, Graphics). The old value is remembered and Undo puts it back. Windows reads the entry when FiveM starts, so restart FiveM.*

**NEW** - **A Graphics tab.** *It reads FiveM's two settings files (in AppData\Roaming\CitizenFX) and shows what is set now. Extended Texture Budget (fivem.cfg, vid_budgetScale) can be set to the highest value, 100, with one button, so fewer MLOs pop in and out when many are close together. Post FX (gta5_settings.xml, PostFX) can be set to Normal, for MLOs and cars that look far too bright. Each file gets a backup first, FiveM has to be closed (it writes both files again when it closes), and only that one line changes: your key binds in fivem.cfg stay as they are.*

**NEW** - **"Recommended (N %)" for the Extended Texture Budget.** *The game's own memory bar shows about 3056 MB at the lowest budget and 8056 MB at the highest, so about 50 MB for every percent. The button picks the highest value that leaves about 20 percent of your video memory free: 10 GB and more gives 100, 8 GB gives 70, 6 GB gives 35, 4 GB gives 5. The line under the number is green up to the advice, orange above it, and red when less than 10 percent of the video memory stays free. It is an estimate: the base of 3056 MB depends on your other graphics settings. Nothing is forced, the slider still goes to 100.*

**NEW** - **A live meter of the video memory FiveM really uses.** *While the Graphics tab is open, the program reads every 2 seconds how much video memory the FiveM game process (FiveM_b####_GTAProcess) holds on your dedicated card. It uses the Windows counter "GPU Process Memory" (Dedicated Usage), with English counter names, so it works on a Windows in any language and needs no administrator rights. The bar is green up to 80 percent used, orange up to 90 percent, red above. When FiveM is closed, the line shows the highest value of the last game. Needs Windows 10 version 1709 or newer.*

**NEW** - **Two game options on the Graphics tab: "Fix interface lag" and "Fix stutter at high frame rates".** *These are two options of FiveM's own Settings menu that you can set before you load into a city. "Fix interface lag" is nui_useInProcessGpu: FiveM's own text says it fixes UI lag at high GPU usage, but it may cause GPU crashes. "Fix stutter at high frame rates" turns the Audio frame limiter off (game_useAudioFrameLimiter = false). Both are lines in fivem.cfg: FiveM must be closed, a backup is made first, and only that one line changes.*

**NEW** - **A "Reset" on the FiveM cache tab: the most extreme clean.** *It removes what the normal and the deep clean remove, and also the content of your settings folder: key binds, graphics settings, saved server data, camera and microphone permissions, camera views and the places of FiveM's own windows. FiveM starts like a new install afterwards. A big warning window comes first, and the red Reset button stays off until you tick "I understand that I lose my key binds and my graphics settings". The block is behind a switch, "Reset mode", that deletes nothing itself. A full copy of the settings folder and of the licence check files is saved first, and when a copy cannot be made, nothing is deleted. It only touches six named items in %APPDATA%\CitizenFX (fivem.cfg, gta5_settings.xml, kvs, media_access.json, camera_save_structure.xml, imgui.ini) and two folders in %LOCALAPPDATA%: CEF (browser engine files) and DigitalEntitlements (the saved GTA V licence check, FiveM makes it again). The whole Rockstar Games folder (launcher, Social Club, GTA V settings, music) stays, so no save game is touched. The log shows a check before and after.*

**NEW** - **A button "Clean up old entries" (Graphics card block).** *It looks for what this program left behind: profiles in the NVIDIA driver that it made and that are old (none of their programs is a build on disk, they hold no settings, or the driver ignores them because FiveM made its own profile), and entries of the Windows graphics list for FiveM programs whose file is gone. The list is shown with the reason for each, and nothing is removed before you say yes. A copy of the driver profile database is saved first, and every removed profile is checked again. It never touches the profiles of FiveM itself, of other programs or of the NVIDIA app.*

**NEW** - **Each graphics card says if it is integrated or dedicated, and how much memory is its own.** *The program first asks Windows which chip is the dedicated one (the adapter type bits behind "Power saving" and "High performance" in Windows Settings, System, Display, Graphics). The card name is only the fallback, for a PC where Windows gives no clear answer. Virtual display adapters (Parsec, Hyper-V, Remote) are left out of the list.*

**NEW** - **FiveM.exe is a row in the frame cap list, and there is a "Reset the list" button.** *The launcher has its own profile in Radeon Software, and NVIDIA's driver needs it too. "Reset the list" takes the builds that are not on disk out of the list, searches again and adds what it finds. A build that is not on disk and is in no NVIDIA profile gets no new profile.*

**NEW** - **A switch "Start as administrator" in the start window.** *If it is on, the program restarts with administrator rights at once (Windows asks once) and does so at every start. Turn it off in the start window to stop.*

**NEW** - **Command line switches for diagnostics and tests.** *For example --nv-probe, --nv-list, --nv-settings and --vram-probe only read. --pretend-vram <MB> shows the advice for a card with less video memory. The --...-test switches run the self-tests. They are meant for bug reports and development.*

**NEW** - **MIT license.** *The project is open source under the MIT license, see the file LICENSE.*

**CHANGED** - **There are four tabs: FiveM cache, Frame cap, DNS tools, Graphics.** *The log has a Graphics filter. The program opens on the FiveM cache tab by default.*

**CHANGED** - **A dedicated Intel Arc card is not supported at the moment.** *The author could not test it. The window says so, and Apply cap and Undo are off. An Intel chip in the processor is not affected.*

**CHANGED** - **The deep clean says what it really does.** *It only deletes the two folders nui-storage (web data and logins) and game-storage (saved game files and accounts). Key binds and graphics settings live in another folder and are never touched by the deep clean.*

**CHANGED** - **The window starts taller (985 px, or as high as the screen allows)**, so the Graphics tab fits.

**FIXED** - **The icons at the top could be missing on some PCs.** *The shield and the theme buttons were drawn only from the Windows icon font, and the app icon could not be read when the program was started from a network share. Now the program draws them itself when the font is missing, and reads the icon in another way from a network path.*

**FIXED** - **The NVIDIA frame limit did nothing for a new FiveM build that FiveM had already started once.** *FiveM makes its own driver profile "Cfx.re: <full path>" for a new build, and the driver gives a running program the profile found by its FULL path. The program now looks the profile up by the full path of the exe, and writes the limit into the profile that comes back. FiveM's own settings in that profile are kept, and Undo takes only the limit out.*

**FIXED** - **The backup of fivem.cfg kept the wrong copy when one click changed it more than once.** *The backup name is the second, so a second copy in the same second overwrote the first one, which was the original. Now the first copy of a second is kept.*

**FIXED** - **The cache folders were measured while another tab was open.** *Now they are measured only when the FiveM cache tab is open.*

## v0.6.0

**NEW** - **The builds are remembered.** *The tool writes down the FiveM builds it finds. After a cache clean has deleted the build files, the list still shows them, marked "(not on disk now)", and you can still set the cap. If nothing was written down yet, Radeon Software's own list of FiveM builds is used. FiveM downloads the files again when you start it.*

**NEW** - **An "Undo last change" button on the frame cap tab.** *Before every change the tool saves the two cap values the build had. The button puts them back in the same careful way: Radeon Software closes and opens again, the settings file is backed up first, and AMD has to confirm the result. It goes one step back per build.*

**NEW** - **A report for support.** *The first icon on the right of the log copies the whole log of this run, with the debug lines, to the clipboard, ready to paste into a message. It has file paths and the name of the PC in it, and the card says so.*

**CHANGED** - **The cap number box is in the program's colours.** *The Windows one stayed white in dark mode. The new one has two small arrow buttons, and it also works with the mouse wheel, the Up and Down keys, and typing.*

**NEW** - **The graphics card box shows the driver version and its date for every card.** *It is the Windows driver version, in its own column.*

**CHANGED** - **Undo says what it put back.** *The row says "Cap put back to 140 / 140." and the card says how many builds went back to how many FPS.*

**NEW** - **A shield button restarts the program as administrator.** *It sits in the title bar, left of the three theme icons. Windows asks once. After that the DNS tools never ask again, the title says "administrator" and the shield is blue. FiveM and Radeon Software are still started with normal rights, so they do not run as administrator.*

**NEW** - **The DNS tab shows how many names are in the DNS cache.** *Windows only lets an administrator see the cache. In administrator mode the number is live on the card and counts up or down. Without administrator rights the program counts after the permission window, shows the number and asks "Clear them?" before it clears anything, all with one permission window.*

**CHANGED** - **The welcome window explains simply that some things need administrator rights.** *The DNS tools need them. The frame cap and the FiveM cache do not.*

**NEW** - **Only one copy of the program can be open.** *Starting it again brings the open window to the front instead of opening a second one, so two copies can never change Radeon's settings file or the DNS at the same moment. The Windows permission step for DNS and the restart after a language change still work.*

**NEW** - **A new FiveM build is announced.** *At start-up and at every search, a build that was not there last time gets a card with its name. If Radeon Software does not know it yet, the card says to add it there once (Gaming > Games) and press Search again. The new build is ticked like the others, so Apply covers it.*

**CHANGED** - **Builds that Radeon Software still has stay in the list**, marked "(not on disk now)", also when another build is on disk. A cache clean deletes the files, not the Radeon profiles, so the cap of those builds can still be changed.

**CHANGED** - **The DNS list shows the network card and its speed.** *The "Type" column only repeated what the connection name said (Ethernet or Wi-Fi). Now there is a "Network card" column and a "Speed" column.*

**FIXED** - **The table headers line up with the text in the rows**, and the tick column is centred under its header.

**CHANGED** - **The Build column is wider**, so "(not on disk now)" fits.

**CHANGED** - **The log is a rounded block like the other blocks, and it takes the space the tab leaves free.** *A card that opens above the log now sits right under the last block of the tab, with the same gap as between the other blocks.*

**FIXED** - **The cap number can be selected and typed over.** *The number box has a real text box inside: click it and the whole number is selected, then type, copy or paste.*

**FIXED** - **Opening the FiveM cache tab no longer measures the folders and fills the log every time.** *It measures the first time, when the folder changes, and after 2 minutes. "Clean cache" and "Search again" still measure at once.*

## v0.5.0

**NEW** - **An activity card shows what a job is doing, and how it ended.** *While the frame cap, a DNS change, a cache clean or a search runs, a card opens at the top of the log area. It pushes the log down a little and covers nothing on the tab. It has a spinner, the step in words (for example "Closing Radeon Software ..."), a bar that fills as the job moves on, and the seconds that passed. When the job ends it turns into a green tick (done), a blue "i" (nothing to change) or a red cross (it did not work), with one clear sentence. Click it to close it. The green and blue cards close by themselves after 14 seconds, and a red one stays until you click it. Before this, nothing on screen said that both builds already had the cap and that Radeon Software was left alone.*

**NEW** - **The frame cap rows light up.** *The build being worked on says "Working ...", then its row glows green or red for a moment.*

**NEW** - **Colours in the log.** *Green is good, yellow is a light problem, orange is a warning, red is an error, purple is something really broken. A folded job takes the colour of its worst line. The DNS tag is teal now, so purple only ever means "really broken".*

**NEW** - **Click the program icon (top left) to pick the language.** *A menu lists "Follow the Windows setting", English, Nederlands, Deutsch, Français and Español. The program restarts in that language, on the same tab.*

**NEW** - **Everything fades a little.** *Buttons, tabs, filter pills and the theme icons fade when you point at them, the active tab fades in, and the small windows fade in.*

**CHANGED** - **The FiveM cache tab keeps its buttons in a card, like the other tabs.**

**CHANGED** - **The folder bar is a drop-down in the program's colours.** *The Windows one had a bright white frame in dark mode.*

**CHANGED** - **The grid headers have no white lines in dark mode.**

**CHANGED** - **Cleaning the cache is quicker.** *It uses the sizes it measured before, instead of measuring every folder a second time.*

**FIXED** - **The resize cursor no longer sticks.** *The window set its own cursor on the thin edge, and every control inside inherited it and kept it. Now only the edge shows it.*

**FIXED** - **The "Why?" button had no left line.** *The hint text above it was 8 pixels too wide and covered the edge.*

**FIXED** - **Clearing the DNS cache said "did not finish" when it had worked.** *Windows keeps a few names (for example the ones from the hosts file) or puts them back at once, so the count never reaches zero. The result now follows what Windows reports, the card shows the entries before and after, and the log lists the names that stay.*

**FIXED** - **The pop-out log opened scrolled to the wrong place.**

## v0.4.0

**NEW** - **Five languages: English, Dutch, German, French and Spanish.** *The window text follows the Windows display language. English is the default, and it shows when Windows uses any other language. To pick one yourself, put a line like language=nl (or de, fr, es, en) in settings.txt. The log and the log file stay in English on purpose, so a log you send is always readable. The translations are not checked by native speakers yet.*

**NEW** - **The tabs sit in the title bar, like a browser.** *Every window has the same dark or light title bar with its own minimise, maximise and close buttons. The window edges still resize.*

**NEW** - **A card layout on every tab.** *Each part has a card with an icon and a title. The grid, the DNS choices and the three cache cards sit on them.*

**NEW** - **Three small icons for the look: follow Windows, light, dark.** *They replace the Appearance drop-down.*

**NEW** - **A "Why?" button next to the cap box.** *The hint is one short line now. The button opens a small window that explains why 140 is the highest you can pick.*

**NEW** - **A tidier log.** *Long lines wrap and line up under the message, so the time and the tool stay in a column. A finished job folds into one line: click [+] to open it and [-] to fold it again. A job with a problem stays open. There is no sideways scroll bar.*

**NEW** - **A new icon.** *A blue gauge in a flat, angular black shape, closer to FiveM's own look.*

**CHANGED** - **"Open log file" uses the program Windows has for .txt files.** *It no longer forces Notepad.*

**CHANGED** - **The window is wider and taller (about 1000 by 800) and cannot be made narrower than 960.** *No page needs a scroll bar at the normal size, on a 1440p screen at 125% and on 1080p.*

**CHANGED** - **The source file is UTF-8, and build.ps1 tells the compiler so.** *The translations have accented letters.*

## v0.3.0

**NEW** - **A welcome window and a small window to pick a tool.** *At the start you read a short notice (as-is, testing, what each tool does) and tick "do not show again". Then a small window asks what you want to do, and opens that tab. A link at the bottom of the main window brings the start window back.*

**NEW** - **Icons on the tabs and buttons.** *The Windows icon font gives every button a small picture. The frame cap tab has a gauge drawn by the program.*

**NEW** - **A nicer FiveM cache tab.** *Three cards (temporary files, logs and crash reports, and the deep clean in a red frame) each with an icon, the size in big letters, a short text and an include box. A coloured dot shows if FiveM is open, and a button next to it closes FiveM.*

**NEW** - **"Clean cache" and "Start FiveM" sit next to each other.** *The old "start when finished" box is a button now. A "pure mode" box sits beside it.*

**NEW** - **The log says what the cache tab found.** *Opening the tab, or changing the install, writes the size of every folder and a total to the log.*

**NEW** - **DNS: clearing the cache is checked and repeated.** *The program counts the entries Windows keeps, runs the flush, runs Windows' own flush call as the second try, and tries a third time if the cache is still not empty. An extra box also clears the NetBIOS and ARP caches. The log shows the entries before and after.*

**NEW** - **Dark scroll bars.** *Windows paints scroll bars dark when the theme is dark.*

**CHANGED** - **The copyright and version are at the bottom left.** *The bar under the log shows the copyright and the version, with the link to the start window at the right.*

**CHANGED** - **The Appearance box is wider.** *"System (follow Windows)" fits now.*

## v0.2.0

**NEW** - **Three tools in one program.** *Frame cap, DNS tools and a FiveM cache cleaner, each on its own tab.*

**NEW** - **Dark mode, light mode, and follow Windows.** *"Appearance" at the top right. The default follows the Windows setting and changes live when Windows changes. The title bar follows too.*

**NEW** - **DNS tools: clear the DNS cache.** *One button. Windows asks for permission first, because clearing the cache needs it.*

**NEW** - **DNS tools: change DNS, with an Undo.** *Cloudflare (default), Google, or both. Only the ticked connections change (Ethernet and Wi-Fi are ticked by default, VPN and virtual ones are not). The old setting (usually Automatic) is saved first where only an administrator can change it, so Undo puts it back exactly.*

**NEW** - **FiveM cache cleaner, using the same FiveM search as the frame cap.** *Temporary files and logs are ticked. The deep clean (login and local settings) is off and asks again before it is ticked. Each part shows its size. It refuses to run while FiveM is open (a button can close FiveM), and it only touches the named folders inside a folder called FiveM.app. It can start FiveM when finished, also in pure mode.*

**NEW** - **The log is in the main window.** *It sits at the bottom of every tab, with a filter per tool, a debug lines switch, Copy, Open log file, In folder and Pop out. Every line says which tool wrote it.*

**NEW** - **An icon.** *A blue speed gauge on a black rounded square, in the colour #1177b5 with FiveM's black and sharp look.*

**NEW** - **Copyright and version in the file.** *Right-click the exe, Properties, Details: product name, version, company and "Copyright (c) 2026 Pepreal". The window shows the same at the top right.*

**CHANGED** - **The program is now called FiveM Toolbox.** *The exe is FiveMToolbox.exe. Old backups and settings move to the new folder by themselves.*

**CHANGED** - **The accent colour is #1177b5.** *Buttons and the active tab use the icon's blue.*

## v0.1.1

**NEW** - **A log window.** *Shows every step the program takes, live, with a Copy button and an "Open log file" button.*

**NEW** - **A debug log file.** *Every run writes a log next to the exe (or in %LOCALAPPDATA% if that folder is read-only). It lists the Windows version, the graphics cards and drivers, which AMD/NVIDIA driver files exist, Radeon Software's state, every place it searched for FiveM, what AMD reported for each build, and any error with details. Send this file when something does not work on another PC.*

**NEW** - **The program has a name, a description and a version in its file properties.** *Right-click the exe, then Properties, then Details.*

**FIXED** - **Shortcut search no longer stops at a folder Windows will not let it read.** *Found by the new debug log: the Start Menu has a protected "Programma's" link, and the search gave up there.*

**FIXED** - **The program no longer lists itself as a running FiveM.** *Its own name starts with "FiveM", so the "is FiveM running" check counted it.*

**CHANGED** - **The log tells more.** *What AMD reports before and after, how many profiles look the same as the build, which one it tries, when it starts and stops Radeon Software, and every check while it waits for AMD.*

## v0.1.0

**NEW** - **A window program.** *It needs no install: double-click it.*

**NEW** - **It checks the graphics card first.** *It shows your cards (AMD, NVIDIA, or both like a laptop) before it does anything.*

**NEW** - **AMD: one frame cap per FiveM build.** *It writes the cap through Radeon Software's own profile file. Only the cap changes (Radeon Chill min and max FPS). No other setting is touched.*

**NEW** - **It finds FiveM by itself.** *It looks in the default folder, in a running FiveM, in shortcuts, in Radeon Software's list, and on your drives. A "Pick a folder" button covers unusual places.*

**NEW** - **Every build gets the cap.** *Every FiveM_b####_GTAProcess.exe it finds is capped, not only the newest one.*

**NEW** - **The cap box starts at 140 and stops at 140.** *The window explains why: the GTA V engine is from 2013 and was never designed for very high frame rates, and players report micro-stutter above about 140 FPS. A cap keeps FiveM below that range. A lower cap is fine.*

**NEW** - **A backup before every change.** *A button opens the backup folder.*

**NEW** - **It checks its own work.** *After a change it asks AMD's library if the new cap is really active for that build. If not, it undoes the change.*

Known limits of this first version: for AMD the build must already be in Radeon Software with Radeon Chill switched on once, and Radeon Software closes and opens again during the change. (NVIDIA came in v0.7.0.)
