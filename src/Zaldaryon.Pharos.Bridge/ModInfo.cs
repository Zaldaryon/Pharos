using Vintagestory.API.Common;

// Lets the game load this assembly as a client-only code mod, so a headless client can stage it
// like any other mod. The server never needs it.
[assembly: ModInfo("Pharos Bridge", "pharosbridge",
    Version = "0.4.0",
    Side = "Client",
    RequiredOnClient = false,
    RequiredOnServer = false,
    Description = "Publishes client frame, chunk and GUI events to the Pharos test harness.",
    Authors = ["Zaldaryon"])]
