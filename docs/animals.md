# Animals

Animals breed, grow, lay eggs and can be butchered. The game keeps that state in behaviors that
only the animals themselves know, so Pharos gives a test a way in.

## The state of an animal

`Server.Animals.Of` returns what an animal is about to do: whether it is pregnant, how long the
pregnancy lasts, when it may breed again, how much it has to eat to breed, how long it needs to
grow and what it grows into.

```csharp
Entity hen = null!;
AnimalState state = Server.Animals.Of(hen);

Assert.False(state.Breeding!.IsPregnant);
```

An animal that does not breed has no breeding state, and an animal that does not grow has no
growth state.

## Feeding

`Server.Animals.Feed` gives an animal food, the way the game counts the food it eats.

```csharp
Entity hen = null!;
Server.Animals.Feed(hen, portions: 3);
```

## Breeding

`Server.Animals.BreedAsync` breeds two animals, the way the game does: the female has to have
eaten enough, the male has to be near her, and the pregnancy lasts a few days of game time. It
returns the offspring.

```csharp
Entity hen = null!;
Entity rooster = null!;
Entity[] chicks = await Server.Animals.BreedAsync(rooster, hen, maxDays: 10);

Assert.NotEmpty(chicks);
```

## Growing

`Server.Animals.GrowAsync` grows a young animal into its adult, the way the game does, and returns
the adult.

```csharp
Entity chick = null!;
Entity? adult = await Server.Animals.GrowAsync(chick, maxDays: 30);

Assert.NotNull(adult);
```

## The hen box

`Server.Animals.HenBox` reads what a hen box holds: the eggs in it and whether an animal sits in
it.

```csharp
HenBoxState box = Server.Animals.HenBox(pos);

Assert.Equal(0, box.Eggs);
```

## Limits

- The breeding and the growth are the game's own, so the weather, the season and the animal
  decide how they go.
- The game asks every few seconds whether an animal may breed, and the driver asks in its place,
  with the game's own random. A female that has not eaten enough, or a male that is not near
  her, does not breed.
- The growth replaces the young animal with the adult, so the adult is a new entity.
