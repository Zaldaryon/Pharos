# Whitelist, bans and player data

The server decides who may join, and it keeps what it knows of its players. Both live in the
server's own store, so Pharos gives a test a way in.

## The whitelist

`Server.Admin.Whitelist` lets a player join, whatever the whitelist says, and
`Server.Admin.Unwhitelist` takes the player off it again. `Server.Admin.IsWhitelisted` says
whether the player is on it.

```csharp
Server.Admin.Whitelist("Bob", "a friend");

Assert.True(Server.Admin.IsWhitelisted("Bob"));
```

`Server.Admin.WhitelistMode` says whether the server lets only the whitelisted players in.

## Bans

`Server.Admin.Ban` bans a player, so the server refuses the join, and `Server.Admin.Unban` takes
the ban off again. `Server.Admin.BanOf` reads the ban of a player.

```csharp
Server.Admin.Ban("Mallory", reason: "griefing");

Assert.NotNull(Server.Admin.BanOf("Mallory"));
```

## Kicks

`Server.Admin.Kick` kicks a player off the server, with the reason the client is sent.

```csharp
Server.Admin.Kick(player, "a test kick");

await SucceedWhenAsync(() => Assert.False(player.IsConnected), maxTicks: 200);
```

## Refused joins

`Server.Admin.TryJoinAsync` tries to let a player in and says what became of it, instead of
throwing.

```csharp
JoinOutcome outcome = await Server.Admin.TryJoinAsync("Mallory");

Assert.False(outcome.Accepted);
```

## Player data

`Server.PlayerData.Of` reads the data the server keeps of a player: the role, the privileges, the
groups and whatever a mod stored with the player.

```csharp
Server.PlayerData.Of(player).CustomPlayerData["mymod:rank"] = "elder";

await player.RejoinAsync();
Assert.Equal("elder", Server.PlayerData.Of(player).CustomPlayerData["mymod:rank"]);
```

## Limits

- The whitelist and the bans are the server's own. A headless player connects locally, so the
  whitelist never refuses it; the bans still do.
- A player that is already connected cannot be let in again under the same name.
- The player data is the server's store, so it lives as long as the server does, and a player
  that rejoins keeps it.
- The order of the join and leave events, and the anti-abuse thresholds, are not recorded here.
