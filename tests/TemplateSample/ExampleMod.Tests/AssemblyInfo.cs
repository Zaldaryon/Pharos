using Zaldaryon.Pharos.XUnit;

// Every test boots with the mod under test: the server loads it, and so does each client that joins.
[assembly: ServerMods(ModUnderTest.Path)]
