# Traders and villagers

Traders sell what they have and want what they lack, and villagers talk. Both live in code the
server keeps to itself, so Pharos gives a test a way in.

## What a trader offers

`Server.Trade.Of` reads what a trader sells, what it wants to buy, the price of each, what the
player has, what the trader has and how many days are left until the trader restocks.

```csharp
Entity trader = null!;
TraderView view = Server.Trade.Of(trader);

Assert.True(view.NextRefreshDays > 0);
```

## Trading

`Server.Trade.TradeAsync` trades with a trader, the way the dialog does: the goods go into the
carts and the trader hands them over. It returns the result of the game's own trade, the codes the
player bought and the codes the player sold.

```csharp
Entity trader = null!;
ServerTestPlayer player = null!;
string code = "game:storagevessel-earthen";
TradeResult result = await Server.Trade.TradeAsync(player, trader, [code]);

Assert.Equal("Success", result.Result);
```

The result carries the name of the game's own outcome, such as `Success` or
`PlayerNotEnoughAssets`.

## Restocking

`Server.Trade.RestockAsync` moves the clock past the day the trader restocks and lets the trader
restock.

```csharp
Entity trader = null!;
await Server.Trade.RestockAsync(trader);
```

## Conversations

`Server.Dialogue.StartAsync` starts a conversation with someone, the way the player does, and
returns the lines and the answers. `Server.Dialogue.ChooseAsync` picks an answer and returns what
is said next.

```csharp
Entity villager = null!;
ServerTestPlayer player = null!;
DialogueLines lines = await Server.Dialogue.StartAsync(player, villager);

Assert.True(lines.Lines.Count > 0);
```

## Limits

- The stock, the prices and the demand are the game's own, so the trader, the climate and the
  season decide what it offers.
- The trader only trades with the player it is talking to, so a trade needs a player.
- The conversations need a dialogue that the one who talks carries.
