/*namespace BotC_PDFer_3;

using Microsoft.AspNetCore.SignalR;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

public class AppHub : Hub
{

    // Hook 1: Receive JSON string, process it, and send real-time status updates
    public async Task ProcessDataAsync(string jsonPayload)
    {
        Console.WriteLine($"[Backend] Received data: {jsonPayload}");

        // Step A: Send instant acknowledgment
        await Clients.Caller.SendAsync("ReceiveStatus", "Processing started...");
        await Task.Delay(1000); // Simulate work

        // Step B: Send progress updates
        await Clients.Caller.SendAsync("ReceiveStatus", "Analyzing layout and settings...");
        await Task.Delay(1500); 

        await Clients.Caller.SendAsync("ReceiveStatus", "Finalizing task...");
        await Task.Delay(1000);

        // Step C: Tell the frontend the task is finished and give it a path to open
        string targetFolder = AppContext.BaseDirectory; 
        await Clients.Caller.SendAsync("TaskCompleted", targetFolder);
    }

    // Hook 2: Triggered by a button click on the web page to open a local folder
    public void OpenLocalFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;

        Console.WriteLine($"[Backend] Opening local folder: {path}");

        // Cross-platform command execution to open the native file manager
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start("explorer.exe", path);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Process.Start("xdg-open", path);
        }
    }
}*/