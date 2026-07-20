# VanillaInventory

Plugin opt-in de inventário do jogador (hotbar, inventário, cursor, ItemStackRequest).

## Build

```bash
dotnet build plugins/VanillaInventory/VanillaInventory.csproj
```

## API

```csharp
if (context.Services.TryGet(out IVanillaInventoryApi? api) && api is not null)
{
    _ = api.Inventory.TryGive(player, stack, out _);
}
```

Events (core): `PlayerOpenInventorySignal` — cancelável antes de abrir a UI (tecla E).

## Provides

- `orion:inventory`
