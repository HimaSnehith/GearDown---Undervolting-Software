# Gear Down

Gear Down is a lightweight, native Windows hardware governor designed to manage CPU and GPU thermals on modern gaming laptops and desktop systems. 

## The Architecture & Anti-Cheat Safety
Most aggressive thermal throttling and undervolting utilities rely on kernel-level drivers (such as `WinRing0.sys`) to write directly to motherboard SMU registers. While effective, these drivers operate in Ring 0 and are frequently flagged or blocked by modern kernel-level anti-cheat engines (e.g., Riot Vanguard, Easy Anti-Cheat, BattlEye). 

Gear Down was engineered to achieve significant temperature reduction entirely in User Mode (Ring 3). By utilizing native Windows APIs and official driver interfaces, it remains 100% safe for competitive multiplayer environments without risking account bans.

## Core Features
* **Native CPU Power Governance:** Modifies Windows Power Plan states via `powercfg` to cap maximum processor power draw. This limits thermal output natively without injecting code or running resource-heavy background loops.
* **Dual GPU Control Modes (Exclusive Toggle):**
  * **Fixed Frequency Cap Mode:** Establishes a strict clock ceiling via `nvidia-smi -lgc`, letting the hardware downclock naturally (210 MHz idle) while capping peak boost.
  * **Dynamic Temperature Lock Mode (Software Closed-Loop Governor):** Allows you to pick a target temperature (e.g. 70°C, 75°C). The real-time governor dynamically auto-tunes the GPU clock limits on the fly under load to hold the target temperature precisely without severe thermal throttling.
* **Zero Overhead:** The application relies on efficient event-driven UI updates and lightweight governor ticks.
* **State Persistence:** Automatically serializes user mode and limits to a local JSON configuration, reapplying target hardware states silently upon application boot.
* **Fail-Safe Restoration:** Integrates cleanly with the Windows System Tray. Instantly relinquishes control and restores all hardware to factory default settings the moment the process is terminated.

## ScreenShots
<img width="632" height="935" alt="image" src="https://github.com/user-attachments/assets/5492b802-7dd6-44d9-8f18-b6fbc0b13ae0" />


## How to Use (Finding Your Sweet Spot)

To get the best results, you need to find the optimal frequency limit for your specific GPU and the game you are playing.

1. **Check Your GPU's Range:** Look up your laptop GPU's maximum clock speed online to understand your hardware's limits.
2. **Observe Default Behavior:** Launch your game without Gear Down running and observe what clock frequency it naturally boosts to under load.
3. **Apply the Limit:** Open Gear Down and adjust the GPU Max Frequency slider to a value lower than the game's natural boost clock.
4. **Test and Tweak:** Check your temperatures and framerates. Adjust the slider until you find the perfect balance between heat reduction and performance. Since every game demands different power levels, your ideal limit will vary from game to game.

**Real-World Example (RTX 4050 playing GTA V):**
* My RTX 4050 operates between 210 MHz and 2700 MHz.
* At stock settings, GTA V naturally boosts to around **2300 - 2600 MHz**, causing temperatures to hit **75°C**.
* I use Gear Down to cap the frequency at **1800 MHz**.
* **The Result:** I only lose about 5-7% of my frames, but my temperatures drastically drop to **55°C - 60°C**.

*Note: It is recommended (though purely optional) to close the application or hit "Reset Everything" when you are done gaming, just to ensure your normal Windows desktop applications aren't unnecessarily capped.*


## Installation
1. Navigate to the **Releases** tab on this repository.
2. Download the latest `Setup.exe` (Highly compressed, framework-dependent build).
3. Run the installer. 
*Note: The application requires Administrator privileges to successfully modify system-level power plans and interface with the Nvidia driver.*

## System Requirements
* Windows 10 / Windows 11 (64-bit)
* .NET 8.0 Desktop Runtime
* Nvidia Dedicated GPU (GTX 10-Series or newer)

## Disclaimer
**Use at your own risk.** Gear Down modifies system power states and GPU frequencies using official Microsoft and Nvidia APIs. However, I am not responsible for any hardware degradation, system instability, system crashes, data loss, or thermal damage that may occur while using or misusing this software. By downloading and running this application, you accept full responsibility for your hardware. 

**NOTE**: this is purely made to undervolt/underclock your GPU, increasing the clock frequency can overclock your GPU and I am not responsible for any of the damages that are caused to your laptop and rest of the deatils shall follow already disclaimed as above.

## Development
Built with C# and WPF (Windows Presentation Foundation). The project is compiled as a framework-dependent executable to minimize deployment payload, handling dependencies via the host operating system.

## Support or Dontate [only if you want, no Pressure :) ]
UPIid: neerajajagadesh029-1@okicici
